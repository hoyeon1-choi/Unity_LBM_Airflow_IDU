using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public class FmuRealParameterOverride
{
    public bool enabled = true;
    public string variableName = string.Empty;
    public double value = 0.0;
    [ReadOnly] public string status = "Not applied.";
}

public class FmuCoSimulationModel : MonoBehaviour, ICoSimulationModel
{
    [Header("FMU Model")]
    [SerializeField] private string modelId = "Simple_CFMU";
    [SerializeField] private string fmuFileName = "Simple_CFMU.fmu";

    [Header("Runtime")]
    [SerializeField] private bool useMockRuntime = false;
    [SerializeField] private bool fallbackToMockOnNativeFailure = true;
    [SerializeField] private bool logging = true;

    [Header("Experiment")]
    [SerializeField] private double startTime = 0.0;
    [SerializeField] private double stopTime = 0.0;
    [SerializeField] private double defaultStepSize = 2.0;

    [Header("FMU Real Parameters")]
    [SerializeField] private bool applyParameterOverridesOnInitialize = true;
    [SerializeField] private bool applyTunableParameterOverridesBeforeEachStep = true;
    [SerializeField] private List<FmuRealParameterOverride> realParameterOverrides =
        new List<FmuRealParameterOverride>();

    [Header("Read-Only Status")]
    [SerializeField, ReadOnly] private bool isInitialized = false;
    [SerializeField, ReadOnly] private bool nativeFallbackActive = false;
    [SerializeField, ReadOnly] private string runtimeMode = "Not initialized";
    [SerializeField, ReadOnly] private string resolvedSourcePath = string.Empty;
    [SerializeField, ReadOnly] private string resolvedUnzipDirectory = string.Empty;
    [SerializeField, ReadOnly] private string parsedModelName = string.Empty;
    [SerializeField, ReadOnly] private int parsedVariableCount = 0;
    [SerializeField, ReadOnly] private int appliedParameterCount = 0;
    [SerializeField, ReadOnly] private string parameterStatus = "No parameters applied.";
    [SerializeField, ReadOnly] private string lastStatus = "Not initialized.";

    private IFmi2Runtime runtime;
    private FmuModelDescription modelDescription;
    private double latestSimTimeSeconds;

    public string ModelId => string.IsNullOrEmpty(modelId) ? name : modelId;
    public bool IsInitialized => isInitialized;
    public string RuntimeMode => runtimeMode;
    public string LastStatus => lastStatus;
    public bool NativeFallbackActive => nativeFallbackActive;
    public FmuModelDescription ModelDescription => modelDescription;
    public IReadOnlyList<FmuRealParameterOverride> RealParameterOverrides => realParameterOverrides;

    public void Initialize(double startTime, double stopTime, double stepSize)
    {
        if (isInitialized)
            return;

        this.startTime = startTime;
        this.stopTime = stopTime;
        this.defaultStepSize = stepSize;

        string root = Path.Combine(Application.streamingAssetsPath, "FMU");
        string resolveStatus;
        if (!FmuModelDescriptionParser.TryResolveFmuSourcePath(
                root,
                string.IsNullOrEmpty(fmuFileName) ? $"{ModelId}.fmu" : fmuFileName,
                ModelId,
                out resolvedSourcePath,
                out resolveStatus))
        {
            lastStatus = resolveStatus;
            throw new FileNotFoundException(resolveStatus);
        }

        string cacheRoot = Path.Combine(Application.persistentDataPath, "FMUCache");
        resolvedUnzipDirectory = FmuModelDescriptionParser.PrepareUnzipDirectory(
            resolvedSourcePath,
            cacheRoot,
            ModelId);

        modelDescription = FmuModelDescriptionParser.ParseFromDirectory(resolvedUnzipDirectory);
        parsedModelName = modelDescription.modelName;
        parsedVariableCount = modelDescription.variables.Count;

        nativeFallbackActive = false;

        if (useMockRuntime)
        {
            InitializeRuntime(new MockFmi2Runtime(), "Mock");
            return;
        }

        try
        {
            InitializeRuntime(new NativeFmi2Runtime(), "Native");
        }
        catch (Exception ex)
        {
            TerminateOrDispose();

            if (!fallbackToMockOnNativeFailure)
            {
                lastStatus = $"Native FMU initialization failed and fallback is disabled: {ex.Message}";
                throw;
            }

            nativeFallbackActive = true;
            Debug.LogWarning(
                $"[CoSimulation][{ModelId}] Native FMU initialization failed. Falling back to mock runtime. " +
                $"Reason: {ex.Message}");

            InitializeRuntime(new MockFmi2Runtime(), "MockFallback");
        }
    }

    public void SetInput(string variableName, CoSimSignalValue value)
    {
        EnsureInitialized();

        double realValue;
        if (!value.TryGetReal(out realValue))
            throw new InvalidOperationException($"Only Real inputs are currently supported. {ModelId}.{variableName}");

        uint valueReference = ResolveValueReference(variableName);
        runtime.SetReal(valueReference, realValue);
        latestSimTimeSeconds = value.simTimeSeconds;
    }

    public CoSimSignalValue GetOutput(string variableName)
    {
        EnsureInitialized();

        uint valueReference = ResolveValueReference(variableName);
        double value = runtime.GetReal(valueReference);
        return CoSimSignalValue.FromReal(value, latestSimTimeSeconds);
    }

    public bool TryGetRealValue(string variableName, out double value)
    {
        value = double.NaN;

        try
        {
            if (!isInitialized || runtime == null)
                return false;

            uint valueReference = ResolveValueReference(variableName);
            value = runtime.GetReal(valueReference);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void DoStep(double currentTime, double stepSize)
    {
        EnsureInitialized();

        if (applyTunableParameterOverridesBeforeEachStep)
            ApplyRealParameterOverrides(runtime, false, false);

        runtime.DoStep(currentTime, stepSize);
        latestSimTimeSeconds = currentTime + stepSize;
    }

    public void TerminateOrDispose()
    {
        if (runtime != null)
        {
            runtime.Terminate();
            runtime.Dispose();
            runtime = null;
        }

        isInitialized = false;
        runtimeMode = "Not initialized";
    }

    [ContextMenu("Load Real Parameter Defaults From FMU")]
    public void LoadRealParameterDefaultsFromFmu()
    {
        PopulateRealParameterOverridesFromModelDescription(false);
    }

    [ContextMenu("Reset Real Parameters To FMU Defaults")]
    public void ResetRealParametersToFmuDefaults()
    {
        PopulateRealParameterOverridesFromModelDescription(true);
    }

    [ContextMenu("Apply Real Parameters Now")]
    public void ApplyRealParametersFromInspector()
    {
        if (!isInitialized || runtime == null)
        {
            parameterStatus = "FMU is not initialized; parameters will apply on next initialization.";
            Debug.LogWarning($"[CoSimulation][{ModelId}] {parameterStatus}");
            return;
        }

        int applied = ApplyRealParameterOverrides(runtime, false, true);
        appliedParameterCount = applied;
        parameterStatus = $"Applied {applied} Real parameter override(s) to initialized runtime.";
        lastStatus = parameterStatus;
        Debug.Log($"[CoSimulation][{ModelId}] {parameterStatus}");
    }

    public int PopulateRealParameterOverridesFromModelDescription(bool resetExistingValues)
    {
        try
        {
            FmuModelDescription description = LoadModelDescriptionForInspector();
            if (resetExistingValues)
                realParameterOverrides.Clear();

            int added = 0;
            int updated = 0;

            for (int i = 0; i < description.variables.Count; i++)
            {
                FmuVariableInfo variable = description.variables[i];
                if (!IsRealParameter(variable))
                    continue;

                FmuRealParameterOverride parameter = FindParameterOverride(variable.name);
                if (parameter == null)
                {
                    parameter = new FmuRealParameterOverride
                    {
                        enabled = true,
                        variableName = variable.name,
                        value = variable.hasStartReal ? variable.startReal : 0.0
                    };
                    realParameterOverrides.Add(parameter);
                    added++;
                }
                else if (resetExistingValues)
                {
                    parameter.value = variable.hasStartReal ? variable.startReal : 0.0;
                    updated++;
                }

                parameter.status = BuildVariableStatus(variable, "Loaded default");
            }

            parameterStatus =
                $"Loaded Real parameter defaults from {description.modelName}. added={added}, updated={updated}.";
            lastStatus = parameterStatus;
            Debug.Log($"[CoSimulation][{ModelId}] {parameterStatus}");
            return added + updated;
        }
        catch (Exception ex)
        {
            parameterStatus = $"Could not load FMU parameter defaults: {ex.Message}";
            lastStatus = parameterStatus;
            Debug.LogWarning($"[CoSimulation][{ModelId}] {parameterStatus}");
            return 0;
        }
    }

    [ContextMenu("Initialize FMU")]
    public void InitializeFromInspector()
    {
        try
        {
            Initialize(startTime, stopTime, defaultStepSize);
            Debug.Log($"[CoSimulation][{ModelId}] {lastStatus}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CoSimulation][{ModelId}] Initialize failed: {ex.Message}");
        }
    }

    [ContextMenu("Terminate FMU")]
    public void TerminateFromInspector()
    {
        TerminateOrDispose();
        lastStatus = "Terminated by inspector command.";
    }

    private void OnDestroy()
    {
        TerminateOrDispose();
    }

    private void InitializeRuntime(IFmi2Runtime newRuntime, string mode)
    {
        try
        {
            newRuntime.Load(resolvedSourcePath, resolvedUnzipDirectory, ModelId, logging);
            appliedParameterCount = applyParameterOverridesOnInitialize
                ? ApplyRealParameterOverrides(newRuntime, true, true)
                : 0;
            newRuntime.SetupExperiment(startTime, stopTime, 0.0);
            newRuntime.EnterInitializationMode();
            newRuntime.ExitInitializationMode();
        }
        catch
        {
            newRuntime.Dispose();
            throw;
        }

        runtime = newRuntime;
        isInitialized = true;
        runtimeMode = mode;
        lastStatus =
            $"{mode} runtime initialized. source={resolvedSourcePath}, unzip={resolvedUnzipDirectory}, " +
            $"modelName={parsedModelName}, variables={parsedVariableCount}, parameters={appliedParameterCount}";

        if (logging)
            Debug.Log($"[CoSimulation][{ModelId}] {lastStatus}");
    }

    private void EnsureInitialized()
    {
        if (!isInitialized)
            Initialize(startTime, stopTime, defaultStepSize);
    }

    private uint ResolveValueReference(string variableName)
    {
        if (modelDescription == null)
            throw new InvalidOperationException($"FMU modelDescription is not loaded for {ModelId}.");

        FmuVariableInfo variable;
        if (!modelDescription.TryGetVariable(variableName, out variable))
            throw new KeyNotFoundException($"FMU variable not found: {ModelId}.{variableName}");

        if (variable.valueType != SignalValueType.Real)
            throw new InvalidOperationException($"Only Real FMU variables are currently supported: {ModelId}.{variableName}");

        return variable.valueReference;
    }

    private int ApplyRealParameterOverrides(
        IFmi2Runtime targetRuntime,
        bool registerAsInitialValues,
        bool logWarnings)
    {
        if (targetRuntime == null || realParameterOverrides == null || realParameterOverrides.Count == 0)
        {
            parameterStatus = "No Real parameter overrides configured.";
            return 0;
        }

        int applied = 0;
        int skipped = 0;
        StringBuilder status = new StringBuilder(256);

        for (int i = 0; i < realParameterOverrides.Count; i++)
        {
            FmuRealParameterOverride parameter = realParameterOverrides[i];
            if (parameter == null || !parameter.enabled)
            {
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(parameter.variableName))
            {
                parameter.status = "Skipped: variable name is empty.";
                skipped++;
                continue;
            }

            FmuVariableInfo variable;
            if (modelDescription == null || !modelDescription.TryGetVariable(parameter.variableName, out variable))
            {
                parameter.status = "Skipped: variable not found in modelDescription.";
                if (logWarnings)
                    Debug.LogWarning($"[CoSimulation][{ModelId}] {parameter.status} variable={parameter.variableName}");
                skipped++;
                continue;
            }

            if (variable.valueType != SignalValueType.Real)
            {
                parameter.status = $"Skipped: {variable.valueType} variables are not supported.";
                if (logWarnings)
                    Debug.LogWarning($"[CoSimulation][{ModelId}] {parameter.status} variable={parameter.variableName}");
                skipped++;
                continue;
            }

            if (!registerAsInitialValues && IsFixedParameter(variable))
            {
                parameter.status = BuildVariableStatus(variable, "Skipped runtime apply for fixed parameter");
                skipped++;
                continue;
            }

            try
            {
                if (registerAsInitialValues)
                    targetRuntime.RegisterInitialReal(variable.valueReference, parameter.value);
                else
                    targetRuntime.SetReal(variable.valueReference, parameter.value);

                parameter.status = BuildVariableStatus(
                    variable,
                    registerAsInitialValues ? "Registered initial value" : "Applied runtime value");
                applied++;
            }
            catch (Exception ex)
            {
                parameter.status = $"Failed: {ex.Message}";
                if (logWarnings)
                {
                    Debug.LogWarning(
                        $"[CoSimulation][{ModelId}] Failed to apply parameter {parameter.variableName}: {ex.Message}");
                }
                skipped++;
            }
        }

        status.Append($"Applied Real parameter overrides: applied={applied}, skipped={skipped}.");
        parameterStatus = status.ToString();
        return applied;
    }

    private FmuModelDescription LoadModelDescriptionForInspector()
    {
        string root = Path.Combine(Application.streamingAssetsPath, "FMU");
        string sourcePath;
        string resolveStatus;
        if (!FmuModelDescriptionParser.TryResolveFmuSourcePath(
                root,
                string.IsNullOrEmpty(fmuFileName) ? $"{ModelId}.fmu" : fmuFileName,
                ModelId,
                out sourcePath,
                out resolveStatus))
        {
            throw new FileNotFoundException(resolveStatus);
        }

        string cacheRoot = Path.Combine(Application.persistentDataPath, "FMUCache");
        string unzipDirectory = FmuModelDescriptionParser.PrepareUnzipDirectory(sourcePath, cacheRoot, ModelId);
        FmuModelDescription description = FmuModelDescriptionParser.ParseFromDirectory(unzipDirectory);

        resolvedSourcePath = sourcePath;
        resolvedUnzipDirectory = unzipDirectory;
        parsedModelName = description.modelName;
        parsedVariableCount = description.variables.Count;
        modelDescription = description;

        return description;
    }

    private FmuRealParameterOverride FindParameterOverride(string variableName)
    {
        if (realParameterOverrides == null)
            realParameterOverrides = new List<FmuRealParameterOverride>();

        for (int i = 0; i < realParameterOverrides.Count; i++)
        {
            FmuRealParameterOverride parameter = realParameterOverrides[i];
            if (parameter != null && string.Equals(parameter.variableName, variableName, StringComparison.Ordinal))
                return parameter;
        }

        return null;
    }

    private static bool IsRealParameter(FmuVariableInfo variable)
    {
        return variable != null &&
               variable.valueType == SignalValueType.Real &&
               variable.causality == SignalDirection.Parameter;
    }

    private static bool IsFixedParameter(FmuVariableInfo variable)
    {
        return variable != null &&
               string.Equals(variable.variability, "fixed", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildVariableStatus(FmuVariableInfo variable, string prefix)
    {
        if (variable == null)
            return prefix;

        string start = variable.hasStartReal ? $", start={variable.startReal:G6}" : string.Empty;
        return $"{prefix}: vr={variable.valueReference}, causality={variable.causality}, " +
               $"variability={variable.variability}{start}";
    }
}
