using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class HostOptions
{
    public string PipeName { get; private set; } = string.Empty;
    public string PluginPath { get; private set; } = string.Empty;
    public string LogPath { get; private set; } = string.Empty;

    public static HostOptions Parse(string[] args)
    {
        var options = new HostOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string value = i + 1 < args.Length ? args[i + 1] : string.Empty;
            if (string.Equals(arg, "--pipe", StringComparison.OrdinalIgnoreCase))
            {
                options.PipeName = value;
                i++;
            }
            else if (string.Equals(arg, "--plugin", StringComparison.OrdinalIgnoreCase))
            {
                options.PluginPath = value;
                i++;
            }
            else if (string.Equals(arg, "--log", StringComparison.OrdinalIgnoreCase))
            {
                options.LogPath = value;
                i++;
            }
        }

        if (string.IsNullOrWhiteSpace(options.PipeName))
            throw new ArgumentException("--pipe is required.");
        if (string.IsNullOrWhiteSpace(options.PluginPath))
            throw new ArgumentException("--plugin is required.");

        options.PluginPath = Path.GetFullPath(options.PluginPath);
        if (!File.Exists(options.PluginPath))
            throw new FileNotFoundException("FmuNativePlugin.dll was not found.", options.PluginPath);

        if (!string.IsNullOrWhiteSpace(options.LogPath))
        {
            options.LogPath = Path.GetFullPath(options.LogPath);
            string? logDir = Path.GetDirectoryName(options.LogPath);
            if (!string.IsNullOrWhiteSpace(logDir))
                Directory.CreateDirectory(logDir);
        }

        return options;
    }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            HostOptions options = HostOptions.Parse(args);
            NativePluginResolver.Configure(options.PluginPath);
            using var server = new FmuHostServer(options);
            return server.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}

internal sealed class FmuHostServer : IDisposable
{
    private readonly HostOptions options;
    private readonly Dictionary<string, IntPtr> instances = new(StringComparer.Ordinal);
    private bool shutdownRequested;

    public FmuHostServer(HostOptions options)
    {
        this.options = options;
    }

    public int Run()
    {
        Log("FmuHost started. pipe=" + options.PipeName + ", plugin=" + options.PluginPath);
        while (!shutdownRequested)
        {
            using var pipe = new NamedPipeServerStream(
                options.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.None);
            pipe.WaitForConnection();

            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true
            };

            string? requestLine = reader.ReadLine();
            string responseLine = HandleRequest(requestLine);
            writer.WriteLine(responseLine);
        }

        Dispose();
        Log("FmuHost stopped.");
        return 0;
    }

    public void Dispose()
    {
        foreach (IntPtr handle in instances.Values)
        {
            if (handle != IntPtr.Zero)
                FmuNative.Unload(handle);
        }

        instances.Clear();
    }

    private string HandleRequest(string? requestLine)
    {
        try
        {
            Request request = Request.Parse(requestLine);
            switch (request.Command)
            {
                case "ping":
                    return Protocol.Ok("pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                case "load":
                    return Load(request);
                case "setup":
                    return Setup(request);
                case "enter":
                    return Enter(request);
                case "exit":
                    return Exit(request);
                case "register":
                    return RegisterInitialReal(request);
                case "set":
                    return SetReal(request);
                case "get":
                    return GetReal(request);
                case "step":
                    return DoStep(request);
                case "unload":
                    return Unload(request);
                case "shutdown":
                    shutdownRequested = true;
                    return Protocol.Ok();
                default:
                    return Protocol.Fail("Unknown command: " + request.Command);
            }
        }
        catch (Exception ex)
        {
            Log("Request failed: " + ex.Message);
            return Protocol.Fail(ex.Message);
        }
    }

    private string Load(Request request)
    {
        string instance = request.Require("instance");
        string unzip = request.Require("unzip");
        bool logging = request.GetBool("logging", true);
        string logPath = request.Get("log", string.Empty);

        if (instances.TryGetValue(instance, out IntPtr previous) && previous != IntPtr.Zero)
        {
            FmuNative.Unload(previous);
            instances.Remove(instance);
        }

        if (!string.IsNullOrWhiteSpace(logPath))
            FmuNative.SetDebugLogPath(logPath);

        IntPtr handle = FmuNative.Load(unzip, instance, logging ? 1 : 0);
        if (handle == IntPtr.Zero)
            return Protocol.Fail("Fmu_Load failed: " + FmuNative.GetLastErrorText());

        instances[instance] = handle;
        Log("Loaded FMU instance=" + instance + ", unzip=" + unzip);
        return Protocol.Ok();
    }

    private string Setup(Request request)
    {
        IntPtr handle = GetHandle(request);
        double start = request.GetDouble("start", 0.0);
        double stop = request.GetDouble("stop", 0.0);
        bool hasStop = request.GetBool("hasStop", stop > start);
        double tolerance = request.GetDouble("tolerance", 0.0);
        bool toleranceDefined = request.GetBool("toleranceDefined", tolerance > 0.0);

        int ok = FmuNative.SetupExperiment(handle, start, stop, hasStop ? 1 : 0, tolerance, toleranceDefined ? 1 : 0);
        return ok != 0 ? Protocol.Ok() : Protocol.Fail("Fmu_SetupExperiment failed: " + FmuNative.GetLastErrorText());
    }

    private string Enter(Request request)
    {
        int ok = FmuNative.EnterInitializationMode(GetHandle(request));
        return ok != 0 ? Protocol.Ok() : Protocol.Fail("Fmu_EnterInitializationMode failed: " + FmuNative.GetLastErrorText());
    }

    private string Exit(Request request)
    {
        int ok = FmuNative.ExitInitializationMode(GetHandle(request));
        return ok != 0 ? Protocol.Ok() : Protocol.Fail("Fmu_ExitInitializationMode failed: " + FmuNative.GetLastErrorText());
    }

    private string RegisterInitialReal(Request request)
    {
        int ok = FmuNative.RegisterInitialReal(
            GetHandle(request),
            request.Require("name"),
            request.GetDouble("value", 0.0));
        return ok != 0 ? Protocol.Ok() : Protocol.Fail("Fmu_RegisterInitialReal failed: " + FmuNative.GetLastErrorText());
    }

    private string SetReal(Request request)
    {
        int ok = FmuNative.SetReal(
            GetHandle(request),
            request.Require("name"),
            request.GetDouble("value", 0.0));
        return ok != 0 ? Protocol.Ok() : Protocol.Fail("Fmu_SetReal failed: " + FmuNative.GetLastErrorText());
    }

    private string GetReal(Request request)
    {
        int ok = FmuNative.GetReal(GetHandle(request), request.Require("name"), out double value);
        return ok != 0
            ? Protocol.Ok("value", value.ToString("R", CultureInfo.InvariantCulture))
            : Protocol.Fail("Fmu_GetReal failed: " + FmuNative.GetLastErrorText());
    }

    private string DoStep(Request request)
    {
        IntPtr handle = GetHandle(request);
        double current = request.GetDouble("current", 0.0);
        double step = request.GetDouble("step", 0.0);
        int ok = FmuNative.DoStep(handle, current, step);
        return ok != 0 ? Protocol.Ok() : Protocol.Fail("Fmu_DoStep failed: " + FmuNative.GetLastErrorText());
    }

    private string Unload(Request request)
    {
        string instance = request.Require("instance");
        if (instances.TryGetValue(instance, out IntPtr handle) && handle != IntPtr.Zero)
        {
            FmuNative.Unload(handle);
            instances.Remove(instance);
            Log("Unloaded FMU instance=" + instance);
        }

        return Protocol.Ok();
    }

    private IntPtr GetHandle(Request request)
    {
        string instance = request.Require("instance");
        if (!instances.TryGetValue(instance, out IntPtr handle) || handle == IntPtr.Zero)
            throw new InvalidOperationException("FMU instance is not loaded: " + instance);

        return handle;
    }

    private void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(options.LogPath))
            return;

        File.AppendAllText(
            options.LogPath,
            DateTime.Now.ToString("O", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine,
            Encoding.UTF8);
    }
}

internal sealed class Request
{
    private readonly Dictionary<string, string> values;

    private Request(string command, Dictionary<string, string> values)
    {
        Command = command;
        this.values = values;
    }

    public string Command { get; }

    public static Request Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            throw new ArgumentException("Empty request.");

        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new ArgumentException("Empty request.");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < parts.Length; i++)
        {
            int equals = parts[i].IndexOf('=');
            if (equals <= 0)
                continue;

            string key = parts[i].Substring(0, equals);
            string value = Uri.UnescapeDataString(parts[i].Substring(equals + 1));
            values[key] = value;
        }

        return new Request(parts[0], values);
    }

    public string Require(string key)
    {
        if (!values.TryGetValue(key, out string? value) || string.IsNullOrEmpty(value))
            throw new ArgumentException("Missing request value: " + key);

        return value;
    }

    public string Get(string key, string fallback)
    {
        return values.TryGetValue(key, out string? value) ? value : fallback;
    }

    public bool GetBool(string key, bool fallback)
    {
        if (!values.TryGetValue(key, out string? value))
            return fallback;

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    public double GetDouble(string key, double fallback)
    {
        if (!values.TryGetValue(key, out string? value))
            return fallback;

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : fallback;
    }
}

internal static class Protocol
{
    public static string Ok(params string[] keyValues)
    {
        return Build(true, keyValues);
    }

    public static string Fail(string error)
    {
        return Build(false, "error", error ?? string.Empty);
    }

    private static string Build(bool ok, params string[] keyValues)
    {
        var sb = new StringBuilder(ok ? "ok=1" : "ok=0");
        for (int i = 0; i + 1 < keyValues.Length; i += 2)
        {
            sb.Append(' ')
              .Append(keyValues[i])
              .Append('=')
              .Append(Uri.EscapeDataString(keyValues[i + 1] ?? string.Empty));
        }

        return sb.ToString();
    }
}

internal static class NativePluginResolver
{
    private static string pluginPath = string.Empty;
    private static IntPtr pluginHandle = IntPtr.Zero;
    private static bool configured;

    public static void Configure(string path)
    {
        if (configured)
            return;

        pluginPath = Path.GetFullPath(path);
        NativeLibrary.SetDllImportResolver(typeof(FmuNative).Assembly, Resolve);
        configured = true;
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "FmuNativePlugin", StringComparison.Ordinal) &&
            !string.Equals(libraryName, "FmuNativePlugin.dll", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        if (pluginHandle == IntPtr.Zero)
            pluginHandle = NativeLibrary.Load(pluginPath);

        return pluginHandle;
    }
}

internal static class FmuNative
{
    private const string DllName = "FmuNativePlugin";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "Fmu_Load")]
    public static extern IntPtr Load(string unzipDir, string instanceName, int loggingOn);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "Fmu_SetDebugLogPath")]
    public static extern int SetDebugLogPath(string path);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fmu_SetupExperiment")]
    public static extern int SetupExperiment(
        IntPtr handle,
        double startTime,
        double stopTime,
        int hasStopTime,
        double tolerance,
        int toleranceDefined);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fmu_EnterInitializationMode")]
    public static extern int EnterInitializationMode(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fmu_ExitInitializationMode")]
    public static extern int ExitInitializationMode(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "Fmu_SetReal")]
    public static extern int SetReal(IntPtr handle, string variableName, double value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "Fmu_GetReal")]
    public static extern int GetReal(IntPtr handle, string variableName, out double value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "Fmu_RegisterInitialReal")]
    public static extern int RegisterInitialReal(IntPtr handle, string variableName, double value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fmu_DoStep")]
    public static extern int DoStep(IntPtr handle, double currentTime, double stepSize);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fmu_Unload")]
    public static extern void Unload(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fmu_GetLastError")]
    private static extern IntPtr GetLastErrorPtr();

    public static string GetLastErrorText()
    {
        IntPtr ptr = GetLastErrorPtr();
        return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? string.Empty : string.Empty;
    }
}
