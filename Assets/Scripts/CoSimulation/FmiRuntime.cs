using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

public interface IFmi2Runtime : IDisposable
{
    void Load(string fmuPath, string unzipDirectory, string instanceName, bool logging);
    void SetupExperiment(double startTime, double stopTime, double tolerance);
    void EnterInitializationMode();
    void ExitInitializationMode();
    void RegisterInitialReal(uint valueReference, double value);
    void SetReal(uint valueReference, double value);
    double GetReal(uint valueReference);
    void DoStep(double currentTime, double stepSize);
    void Terminate();
}

internal static class FmuNative
{
    private const string DllName = "FmuNativePlugin";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "Fmu_Load")]
    public static extern IntPtr Load(string unzipDir, string instanceName, int loggingOn);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fmu_Initialize")]
    public static extern int Initialize(IntPtr handle, double startTime, double stopTime, int hasStopTime);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "Fmu_SetReal")]
    public static extern int SetReal(IntPtr handle, string variableName, double value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "Fmu_GetReal")]
    public static extern int GetReal(IntPtr handle, string variableName, out double value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "Fmu_RegisterInitialReal")]
    public static extern int RegisterInitialReal(IntPtr handle, string variableName, double value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fmu_DoStep")]
    public static extern int DoStep(IntPtr handle, double currentTime, double stepSize);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fmu_Reset")]
    public static extern int Reset(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fmu_Unload")]
    public static extern void Unload(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fmu_GetLastError")]
    private static extern IntPtr GetLastErrorPtr();

    public static string GetLastErrorText()
    {
        IntPtr ptr = GetLastErrorPtr();
        return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : string.Empty;
    }
}

public class NativeFmi2Runtime : IFmi2Runtime
{
    private IntPtr handle = IntPtr.Zero;
    private FmuModelDescription modelDescription;
    private readonly Dictionary<uint, string> variableNameByValueReference = new Dictionary<uint, string>();
    private double startTime;
    private double stopTime;
    private bool hasStopTime;
    private bool initialized;

    public void Load(string fmuPath, string unzipDirectory, string instanceName, bool logging)
    {
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

        handle = FmuNative.Load(unzipDirectory, instanceName, logging ? 1 : 0);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException($"Fmu_Load failed: {ReadNativeError()}");
    }

    public void SetupExperiment(double startTime, double stopTime, double tolerance)
    {
        this.startTime = startTime;
        this.stopTime = stopTime;
        hasStopTime = stopTime > startTime;
    }

    public void EnterInitializationMode()
    {
        // The current native plugin wraps FMI setup, enter, and exit in Fmu_Initialize.
    }

    public void ExitInitializationMode()
    {
        EnsureLoaded();
        if (initialized)
            return;

        int ok = FmuNative.Initialize(handle, startTime, stopTime, hasStopTime ? 1 : 0);
        if (ok == 0)
            throw new InvalidOperationException($"Fmu_Initialize failed: {ReadNativeError()}");

        initialized = true;
    }

    public void RegisterInitialReal(uint valueReference, double value)
    {
        EnsureLoaded();

        string variableName = ResolveVariableName(valueReference);
        int ok = FmuNative.RegisterInitialReal(handle, variableName, value);
        if (ok == 0)
            throw new InvalidOperationException($"Fmu_RegisterInitialReal({variableName}) failed: {ReadNativeError()}");
    }

    public void SetReal(uint valueReference, double value)
    {
        EnsureLoaded();

        string variableName = ResolveVariableName(valueReference);
        int ok = FmuNative.SetReal(handle, variableName, value);
        if (ok == 0)
            throw new InvalidOperationException($"Fmu_SetReal({variableName}) failed: {ReadNativeError()}");
    }

    public double GetReal(uint valueReference)
    {
        EnsureLoaded();

        string variableName = ResolveVariableName(valueReference);
        double value;
        int ok = FmuNative.GetReal(handle, variableName, out value);
        if (ok == 0)
            throw new InvalidOperationException($"Fmu_GetReal({variableName}) failed: {ReadNativeError()}");

        return value;
    }

    public void DoStep(double currentTime, double stepSize)
    {
        EnsureLoaded();

        int ok = FmuNative.DoStep(handle, currentTime, stepSize);
        if (ok == 0)
            throw new InvalidOperationException($"Fmu_DoStep failed: {ReadNativeError()}");
    }

    public void Terminate()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            FmuNative.Unload(handle);
            handle = IntPtr.Zero;
        }

        initialized = false;
    }

    private void EnsureLoaded()
    {
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Native FMU runtime is not loaded.");
    }

    private string ResolveVariableName(uint valueReference)
    {
        string variableName;
        if (!variableNameByValueReference.TryGetValue(valueReference, out variableName))
            throw new KeyNotFoundException($"ValueReference not found in FMU modelDescription: {valueReference}");

        return variableName;
    }

    private static string ReadNativeError()
    {
        try
        {
            string error = FmuNative.GetLastErrorText();
            return string.IsNullOrEmpty(error) ? "No native error text." : error;
        }
        catch (Exception ex)
        {
            return $"Could not read native error text: {ex.Message}";
        }
    }
}

public class MockFmi2Runtime : IFmi2Runtime
{
    private FmuModelDescription modelDescription;
    private readonly Dictionary<uint, double> realValues = new Dictionary<uint, double>();
    private readonly Dictionary<string, uint> valueReferenceByName =
        new Dictionary<string, uint>(StringComparer.Ordinal);
    private string modelName = string.Empty;
    private bool loaded;

    public void Load(string fmuPath, string unzipDirectory, string instanceName, bool logging)
    {
        if (string.IsNullOrEmpty(unzipDirectory) || !Directory.Exists(unzipDirectory))
            throw new DirectoryNotFoundException($"FMU unzip directory not found: {unzipDirectory}");

        modelDescription = FmuModelDescriptionParser.ParseFromDirectory(unzipDirectory);
        modelName = string.IsNullOrEmpty(modelDescription.modelName)
            ? instanceName
            : modelDescription.modelName;

        realValues.Clear();
        valueReferenceByName.Clear();

        for (int i = 0; i < modelDescription.variables.Count; i++)
        {
            FmuVariableInfo variable = modelDescription.variables[i];
            if (variable == null || variable.valueType != SignalValueType.Real)
                continue;

            valueReferenceByName[variable.name] = variable.valueReference;
            if (!realValues.ContainsKey(variable.valueReference))
                realValues.Add(variable.valueReference, variable.hasStartReal ? variable.startReal : 0.0);
        }

        loaded = true;
    }

    public void SetupExperiment(double startTime, double stopTime, double tolerance)
    {
    }

    public void EnterInitializationMode()
    {
    }

    public void ExitInitializationMode()
    {
        EnsureLoaded();
    }

    public void RegisterInitialReal(uint valueReference, double value)
    {
        SetReal(valueReference, value);
    }

    public void SetReal(uint valueReference, double value)
    {
        EnsureLoaded();
        realValues[valueReference] = value;
    }

    public double GetReal(uint valueReference)
    {
        EnsureLoaded();

        double value;
        if (!realValues.TryGetValue(valueReference, out value))
            throw new KeyNotFoundException($"Mock valueReference not found: {valueReference}");

        return value;
    }

    public void DoStep(double currentTime, double stepSize)
    {
        EnsureLoaded();

        if (string.Equals(modelName, "Simple_CFMU", StringComparison.Ordinal))
        {
            double sensor = GetRealByName("T_sensor", 0.0);
            double setPoint = GetRealByName("T_set", 0.0);
            double hzMin = GetRealByName("Hz_min", 0.0);
            double hzMax = GetRealByName("Hz_max", 100.0);
            double hz = Clamp((sensor - setPoint) * 10.0, hzMin, hzMax);
            SetRealByName("Hz", hz);
        }
        else if (string.Equals(modelName, "Simple_Plant", StringComparison.Ordinal))
        {
            double hz = Math.Max(0.0, GetRealByName("hz_Plant", 0.0));
            double sensor = GetRealByName("T_sensor_Plant", GetRealByName("T_sensor_start", 30.0));
            double coolingGain = GetRealByName("coolingGain", 0.12);
            double deltaTMin = GetRealByName("deltaT_min", 0.0);
            double deltaTMax = GetRealByName("deltaT_max", 15.0);
            double tau = Math.Max(1.0e-6, GetRealByName("tau", 30.0));
            double absoluteDischargeMin = GetRealByName("T_dis_abs_min", 5.0);
            double absoluteDischargeMax = GetRealByName("T_dis_abs_max", 60.0);
            double coolingDeltaMin = Math.Min(deltaTMin, deltaTMax);
            double coolingDeltaMax = Math.Max(deltaTMin, deltaTMax);
            double absoluteMin = Math.Min(absoluteDischargeMin, absoluteDischargeMax);
            double absoluteMax = Math.Max(absoluteDischargeMin, absoluteDischargeMax);

            double deltaTTarget = Clamp(coolingGain * hz, coolingDeltaMin, coolingDeltaMax);
            double dischargeTarget = sensor - deltaTTarget;
            double rawPrevious = GetRealByName("T_dis_raw", sensor);
            double lagFactor = stepSize > 0.0 ? 1.0 - Math.Exp(-stepSize / tau) : 0.0;
            double rawDischarge = rawPrevious + lagFactor * (dischargeTarget - rawPrevious);

            double sensorRelativeMin = sensor - coolingDeltaMax;
            double sensorRelativeMax = sensor - coolingDeltaMin;
            double discharge = Clamp(rawDischarge, sensorRelativeMin, sensorRelativeMax);
            discharge = Clamp(discharge, absoluteMin, absoluteMax);

            SetRealByName("hz_eff", hz, false);
            SetRealByName("deltaT_target", deltaTTarget, false);
            SetRealByName("T_dis_target", dischargeTarget, false);
            SetRealByName("T_dis_min", sensorRelativeMin, false);
            SetRealByName("T_dis_max", sensorRelativeMax, false);
            SetRealByName("T_dis_raw", rawDischarge, false);
            SetRealByName("der(T_dis_raw)", (dischargeTarget - rawPrevious) / tau, false);
            SetRealByName("T_dis_Plant", discharge);
        }
    }

    public void Terminate()
    {
        Dispose();
    }

    public void Dispose()
    {
        loaded = false;
        realValues.Clear();
        valueReferenceByName.Clear();
    }

    private void EnsureLoaded()
    {
        if (!loaded)
            throw new InvalidOperationException("Mock FMU runtime is not loaded.");
    }

    private double GetRealByName(string variableName, double fallback)
    {
        uint valueReference;
        if (!valueReferenceByName.TryGetValue(variableName, out valueReference))
            return fallback;

        double value;
        return realValues.TryGetValue(valueReference, out value) ? value : fallback;
    }

    private void SetRealByName(string variableName, double value, bool requireVariable = true)
    {
        uint valueReference;
        if (!valueReferenceByName.TryGetValue(variableName, out valueReference))
        {
            if (requireVariable)
                throw new KeyNotFoundException($"Mock variable not found: {variableName}");

            return;
        }

        realValues[valueReference] = value;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (min > max)
        {
            double swap = min;
            min = max;
            max = swap;
        }

        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
