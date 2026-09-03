using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class ExternalFmi2Runtime : IFmi2Runtime
{
    private readonly int commandTimeoutMs;
    private readonly Dictionary<uint, string> variableNameByValueReference = new Dictionary<uint, string>();
    private readonly ExternalFmuHostManager hostManager = new ExternalFmuHostManager();
    private FmuModelDescription modelDescription;
    private string instanceName = string.Empty;
    private bool loaded;
    private bool acquiredHost;
    private bool logging;
    private double startTime;
    private double stopTime;
    private double tolerance;
    private bool hasStopTime;

    public ExternalFmi2Runtime(int commandTimeoutMs = 30000)
    {
        this.commandTimeoutMs = Math.Max(1000, commandTimeoutMs);
    }

    public void Load(string fmuPath, string unzipDirectory, string instanceName, bool logging)
    {
        this.instanceName = string.IsNullOrWhiteSpace(instanceName) ? Path.GetFileNameWithoutExtension(fmuPath) : instanceName;
        this.logging = logging;

        if (string.IsNullOrEmpty(unzipDirectory) || !Directory.Exists(unzipDirectory))
            throw new DirectoryNotFoundException($"FMU unzip directory not found: {unzipDirectory}");

        modelDescription = FmuModelDescriptionParser.ParseFromDirectory(unzipDirectory);
        variableNameByValueReference.Clear();

        for (int i = 0; i < modelDescription.variables.Count; i++)
        {
            FmuVariableInfo variable = modelDescription.variables[i];
            if (variable != null && !variableNameByValueReference.ContainsKey(variable.valueReference))
                variableNameByValueReference.Add(variable.valueReference, variable.name);
        }

        hostManager.Acquire(this.instanceName);
        acquiredHost = true;

        try
        {
            Execute(
                "load",
                new Dictionary<string, string>
                {
                    { "unzip", unzipDirectory },
                    { "logging", logging ? "1" : "0" },
                    { "log", Path.Combine(unzipDirectory, "FmuNativePlugin.log") }
                });
            loaded = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SetupExperiment(double startTime, double stopTime, double tolerance)
    {
        EnsureLoaded();
        this.startTime = startTime;
        this.stopTime = stopTime;
        this.tolerance = tolerance;
        hasStopTime = stopTime > startTime;

        Execute(
            "setup",
            new Dictionary<string, string>
            {
                { "start", startTime.ToString("R", CultureInfo.InvariantCulture) },
                { "stop", stopTime.ToString("R", CultureInfo.InvariantCulture) },
                { "hasStop", hasStopTime ? "1" : "0" },
                { "tolerance", tolerance.ToString("R", CultureInfo.InvariantCulture) },
                { "toleranceDefined", tolerance > 0.0 ? "1" : "0" }
            });
    }

    public void EnterInitializationMode()
    {
        EnsureLoaded();
        Execute("enter");
    }

    public void ExitInitializationMode()
    {
        EnsureLoaded();
        Execute("exit");
    }

    public void RegisterInitialReal(uint valueReference, double value)
    {
        EnsureLoaded();
        ExecuteRealCommand("register", valueReference, value);
    }

    public void SetReal(uint valueReference, double value)
    {
        EnsureLoaded();
        ExecuteRealCommand("set", valueReference, value);
    }

    public double GetReal(uint valueReference)
    {
        EnsureLoaded();
        Dictionary<string, string> response = Execute(
            "get",
            new Dictionary<string, string>
            {
                { "name", ResolveVariableName(valueReference) }
            });

        string valueText;
        if (!response.TryGetValue("value", out valueText) ||
            !double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new InvalidOperationException($"External FMU host returned invalid Real value for {instanceName}.{ResolveVariableName(valueReference)}");
        }

        return value;
    }

    public void DoStep(double currentTime, double stepSize)
    {
        EnsureLoaded();
        long startTimestamp = Stopwatch.GetTimestamp();
        if (logging)
            Debug.Log($"[CoSimulation][{instanceName}] External DoStep begin. t={currentTime:F3}s, h={stepSize:F3}s");

        Execute(
            "step",
            new Dictionary<string, string>
            {
                { "current", currentTime.ToString("R", CultureInfo.InvariantCulture) },
                { "step", stepSize.ToString("R", CultureInfo.InvariantCulture) }
            });

        if (logging)
        {
            double elapsedMs = 1000.0 * (Stopwatch.GetTimestamp() - startTimestamp) / Stopwatch.Frequency;
            Debug.Log($"[CoSimulation][{instanceName}] External DoStep end. t={currentTime:F3}s, h={stepSize:F3}s, elapsed={elapsedMs:F1}ms");
        }
    }

    public void Terminate()
    {
        Dispose();
    }

    public void AbortPendingCommand(string reason)
    {
        hostManager.Abort(reason);
    }

    public void Dispose()
    {
        if (loaded)
        {
            try
            {
                hostManager.ExecuteIfRunning(
                    "unload",
                    new Dictionary<string, string> { { "instance", instanceName } },
                    Math.Min(commandTimeoutMs, 5000));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CoSimulation][{instanceName}] External FMU unload failed: {ex.Message}");
            }

            loaded = false;
        }

        if (acquiredHost)
        {
            hostManager.Release();
            acquiredHost = false;
        }
    }

    private void ExecuteRealCommand(string command, uint valueReference, double value)
    {
        Execute(
            command,
            new Dictionary<string, string>
            {
                { "name", ResolveVariableName(valueReference) },
                { "value", value.ToString("R", CultureInfo.InvariantCulture) }
            });
    }

    private Dictionary<string, string> Execute(string command)
    {
        return Execute(command, null);
    }

    private Dictionary<string, string> Execute(string command, Dictionary<string, string> args)
    {
        if (args == null)
            args = new Dictionary<string, string>();

        args["instance"] = instanceName;
        return hostManager.Execute(command, args, commandTimeoutMs);
    }

    private void EnsureLoaded()
    {
        if (!loaded)
            throw new InvalidOperationException("External FMU runtime is not loaded.");
    }

    private string ResolveVariableName(uint valueReference)
    {
        string variableName;
        if (!variableNameByValueReference.TryGetValue(valueReference, out variableName))
            throw new KeyNotFoundException($"ValueReference not found in FMU modelDescription: {valueReference}");

        return variableName;
    }
}

internal sealed class ExternalFmuHostManager
{
    private readonly object syncRoot = new object();
    private Process process;
    private string pipeName = string.Empty;
    private string hostLogPath = string.Empty;
    private string hostLabel = "FMU";
    private int referenceCount;

    public void Acquire(string label)
    {
        lock (syncRoot)
        {
            hostLabel = SanitizeFileName(label);
            EnsureStarted();
            referenceCount++;
        }
    }

    public void Release()
    {
        lock (syncRoot)
        {
            if (referenceCount > 0)
                referenceCount--;

            if (referenceCount == 0)
                ShutdownHost();
        }
    }

    public Dictionary<string, string> Execute(string command, Dictionary<string, string> args, int timeoutMs)
    {
        lock (syncRoot)
        {
            EnsureStarted();
            try
            {
                Dictionary<string, string> response = SendRaw(BuildRequest(command, args), timeoutMs);
                bool ok = response.TryGetValue("ok", out string okText) && okText == "1";
                if (!ok)
                {
                    string error;
                    response.TryGetValue("error", out error);
                    throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "External FMU host command failed." : error);
                }

                return response;
            }
            catch (Exception ex)
            {
                KillHost("command failed: " + ex.Message);
                throw;
            }
        }
    }

    public bool ExecuteIfRunning(string command, Dictionary<string, string> args, int timeoutMs)
    {
        lock (syncRoot)
        {
            if (process == null || process.HasExited || string.IsNullOrEmpty(pipeName))
                return false;

            try
            {
                Dictionary<string, string> response = SendRaw(BuildRequest(command, args), timeoutMs);
                bool ok = response.TryGetValue("ok", out string okText) && okText == "1";
                if (!ok)
                {
                    string error;
                    response.TryGetValue("error", out error);
                    throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "External FMU host command failed." : error);
                }

                return true;
            }
            catch (Exception ex)
            {
                KillHost("command failed: " + ex.Message);
                throw;
            }
        }
    }
    public void Abort(string reason)
    {
        Process processToKill = System.Threading.Interlocked.Exchange(ref process, null);
        pipeName = string.Empty;
        if (processToKill == null)
            return;

        try
        {
            if (!processToKill.HasExited)
            {
                Debug.LogWarning($"[CoSimulation][{hostLabel}] Aborting external FMU host: {reason}. log={hostLogPath}");
                processToKill.Kill();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CoSimulation][{hostLabel}] Could not abort external FMU host: {ex.Message}");
        }
        finally
        {
            processToKill.Dispose();
        }
    }
    private void EnsureStarted()
    {
        if (process != null && !process.HasExited)
            return;

        string hostExePath = ResolveHostExecutablePath();
        string pluginPath = ResolveNativePluginPath();
        string logDirectory = Path.Combine(Application.persistentDataPath, "FmuHost");
        Directory.CreateDirectory(logDirectory);
        hostLogPath = Path.Combine(logDirectory, $"FmuHost_{hostLabel}_{Guid.NewGuid():N}.log");
        pipeName = "lbm_fmu_host_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N");

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = hostExePath,
            Arguments = "--pipe " + Quote(pipeName) + " --plugin " + Quote(pluginPath) + " --log " + Quote(hostLogPath),
            WorkingDirectory = Path.GetDirectoryName(hostExePath),
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException("Could not start FmuHost process.");

        try
        {
            WaitUntilReady(10000);
        }
        catch
        {
            KillHost("startup failed");
            throw;
        }

        Debug.Log($"[CoSimulation][{hostLabel}] Started dedicated external FMU host. pid={process.Id}, pipe={pipeName}, log={hostLogPath}");
    }

    private void WaitUntilReady(int timeoutMs)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * (timeoutMs / 1000.0));
        Exception lastException = null;

        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (process == null || process.HasExited)
                throw new InvalidOperationException("FmuHost exited before accepting connections.");

            try
            {
                Dictionary<string, string> response = SendRaw("ping", 1000);
                if (response.TryGetValue("ok", out string okText) && okText == "1")
                    return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                System.Threading.Thread.Sleep(100);
            }
        }

        throw new TimeoutException("FmuHost did not become ready. " + (lastException != null ? lastException.Message : string.Empty));
    }

    private Dictionary<string, string> SendRaw(string requestLine, int timeoutMs)
    {
        int safeTimeoutMs = Math.Max(1000, timeoutMs);
        string activePipeName = pipeName;
        System.Threading.Tasks.Task<Dictionary<string, string>> task =
            System.Threading.Tasks.Task.Run(() => SendRawBlocking(activePipeName, requestLine, safeTimeoutMs));

        if (!task.Wait(safeTimeoutMs))
            throw new TimeoutException($"External FMU host command timed out after {safeTimeoutMs} ms. request={requestLine}");

        return task.GetAwaiter().GetResult();
    }

    private static Dictionary<string, string> SendRawBlocking(string activePipeName, string requestLine, int timeoutMs)
    {
        using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", activePipeName, PipeDirection.InOut))
        {
            pipe.Connect(Math.Max(1000, timeoutMs));

            using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true))
            using (StreamReader reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
            {
                writer.AutoFlush = true;
                writer.WriteLine(requestLine);
                string responseLine = reader.ReadLine();
                if (responseLine == null)
                    throw new IOException("External FMU host closed the pipe without a response.");

                return ParseResponse(responseLine);
            }
        }
    }

    private void ShutdownHost()
    {
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
            {
                SendRaw("shutdown", 2000);
                if (!process.WaitForExit(3000))
                    KillHost("shutdown timeout");
            }
        }
        catch
        {
            KillHost("shutdown failed");
        }
        finally
        {
            if (process != null)
            {
                process.Dispose();
                process = null;
            }
        }
    }

    private void KillHost(string reason)
    {
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
            {
                Debug.LogWarning($"[CoSimulation][{hostLabel}] Killing external FMU host: {reason}. log={hostLogPath}");
                process.Kill();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CoSimulation][{hostLabel}] Could not kill external FMU host: {ex.Message}");
        }
        finally
        {
            process.Dispose();
            process = null;
            pipeName = string.Empty;
        }
    }

    private static string ResolveHostExecutablePath()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string[] candidates =
        {
            Path.Combine(projectRoot, "FmuHost", "bin", "Debug", "net8.0", "FmuHost.exe"),
            Path.Combine(projectRoot, "FmuHost", "bin", "Release", "net8.0", "FmuHost.exe"),
            Path.Combine(Application.streamingAssetsPath, "FmuHost", "FmuHost.exe")
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            if (File.Exists(candidates[i]))
                return candidates[i];
        }

        throw new FileNotFoundException("FmuHost.exe was not found. Build FmuHost/FmuHost.csproj first.", candidates[0]);
    }

    private static string ResolveNativePluginPath()
    {
        string[] candidates =
        {
            Path.Combine(Application.dataPath, "Plugins", "x86_64", "FmuNativePlugin.dll"),
            Path.Combine(Application.streamingAssetsPath, "FmuHost", "FmuNativePlugin.dll")
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            if (File.Exists(candidates[i]))
                return candidates[i];
        }

        throw new FileNotFoundException("FmuNativePlugin.dll was not found for external FMU host.", candidates[0]);
    }

    private static string BuildRequest(string command, Dictionary<string, string> args)
    {
        StringBuilder sb = new StringBuilder(command ?? string.Empty);
        if (args != null)
        {
            foreach (KeyValuePair<string, string> pair in args)
            {
                sb.Append(' ')
                  .Append(pair.Key)
                  .Append('=')
                  .Append(Uri.EscapeDataString(pair.Value ?? string.Empty));
            }
        }

        return sb.ToString();
    }

    private static Dictionary<string, string> ParseResponse(string line)
    {
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(line))
            return values;

        string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            int equals = parts[i].IndexOf('=');
            if (equals <= 0)
                continue;

            string key = parts[i].Substring(0, equals);
            string value = Uri.UnescapeDataString(parts[i].Substring(equals + 1));
            values[key] = value;
        }

        return values;
    }

    private static string Quote(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }

    private static string SanitizeFileName(string value)
    {
        string safeValue = string.IsNullOrWhiteSpace(value) ? "FMU" : value;
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidChars.Length; i++)
            safeValue = safeValue.Replace(invalidChars[i], '_');

        return safeValue;
    }
}




