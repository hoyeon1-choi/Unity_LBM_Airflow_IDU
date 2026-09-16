using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEngine;

[Serializable]
public class FmuRealParameterOverride
{
    public bool enabled = true;
    public string variableName = string.Empty;
    public double value = 0.0;
    [ReadOnly] public string status = "Not applied.";
}

[Serializable]
public class FmuIntegerParameterOverride
{
    public bool enabled = true;
    public string variableName = string.Empty;
    public int value = 0;
    [ReadOnly] public string status = "Not applied.";
}

[Serializable]
public class FmuStringParameterOverride
{
    public bool enabled = true;
    public string variableName = string.Empty;
    public string value = string.Empty;
    public bool rewriteModelDescriptionStart = true;
    [ReadOnly] public string status = "Not applied.";
}

public class FmuCoSimulationModel : MonoBehaviour, ICoSimulationModel
{
    [Header("FMU Model")]
    [SerializeField] private string modelId = "Simple_CFMU";
    [SerializeField] private string fmuFileName = "Simple_CFMU.fmu";

    [Header("Runtime")]
    [SerializeField] private bool useMockRuntime = false;
    [SerializeField] private bool useExternalRuntime = false;
    [SerializeField] private bool launchBundledServer = false;
    [SerializeField] private bool fallbackToMockOnNativeFailure = true;
    [SerializeField] private int externalCommandTimeoutMs = 30000;
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
    [SerializeField] private List<FmuIntegerParameterOverride> integerParameterOverrides =
        new List<FmuIntegerParameterOverride>();
    [SerializeField] private List<FmuStringParameterOverride> stringParameterOverrides =
        new List<FmuStringParameterOverride>();

    [Header("FMU Initial Real Inputs")]
    [SerializeField] private List<FmuRealParameterOverride> initialRealInputValues =
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
    [SerializeField, ReadOnly] private int appliedStringParameterCount = 0;
    [SerializeField, ReadOnly] private int appliedInitialInputCount = 0;
    [SerializeField, ReadOnly] private string parameterStatus = "No parameters applied.";
    [SerializeField, ReadOnly] private string lastStatus = "Not initialized.";

    private IFmi2Runtime runtime;
    private IFmi2Runtime initializationRuntime;
    private FmuModelDescription modelDescription;
    private double latestSimTimeSeconds;
    private Task pendingInitializationTask;
    private Task pendingStepTask;
    private double pendingStepEndTime;
    private string initializationStreamingAssetsPath = string.Empty;
    private volatile bool initializationCancellationRequested;

    public string ModelId => string.IsNullOrEmpty(modelId) ? name : modelId;
    public bool IsInitialized => isInitialized;
    public bool IsInitializationPending => pendingInitializationTask != null;
    public string RuntimeMode => runtimeMode;
    public string LastStatus => lastStatus;
    public bool NativeFallbackActive => nativeFallbackActive;
    public FmuModelDescription ModelDescription => modelDescription;
    public IReadOnlyList<FmuRealParameterOverride> RealParameterOverrides => realParameterOverrides;
    public IReadOnlyList<FmuIntegerParameterOverride> IntegerParameterOverrides => integerParameterOverrides;
    public IReadOnlyList<FmuStringParameterOverride> StringParameterOverrides => stringParameterOverrides;
    public IReadOnlyList<FmuRealParameterOverride> InitialRealInputValues => initialRealInputValues;

    public void ConfigureModel(CoSimulationFmuModelConfig config)
    {
        if (config == null)
            return;

        ConfigureModel(
            config.modelId,
            config.fmuFileName,
            config.useMockRuntime,
            config.useExternalRuntime,
            config.fallbackToMockOnNativeFailure,
            config.externalCommandTimeoutMs,
            config.logging,
            config.defaultStepSize);
        launchBundledServer = config.launchBundledServer;
        ConfigureParameterOverrides(config);
    }

    public void ConfigureModel(
        string modelId,
        string fmuFileName,
        bool useMockRuntime,
        bool useExternalRuntime,
        bool fallbackToMockOnNativeFailure,
        int externalCommandTimeoutMs,
        bool logging,
        double defaultStepSize)
    {
        if (isInitialized)
            TerminateOrDispose();

        this.modelId = string.IsNullOrWhiteSpace(modelId) ? name : modelId.Trim();
        this.fmuFileName = string.IsNullOrWhiteSpace(fmuFileName) ? $"{this.modelId}.fmu" : fmuFileName.Trim();
        this.useMockRuntime = useMockRuntime;
        this.useExternalRuntime = useExternalRuntime;
        this.fallbackToMockOnNativeFailure = fallbackToMockOnNativeFailure;
        this.externalCommandTimeoutMs = Math.Max(1000, externalCommandTimeoutMs);
        this.logging = logging;
        this.defaultStepSize = Math.Max(defaultStepSize, 1.0e-6);
        lastStatus = $"Configured FMU model. modelId={this.modelId}, fmuFileName={this.fmuFileName}";
    }

    private void ConfigureParameterOverrides(CoSimulationFmuModelConfig config)
    {
        if (config == null)
            return;

        if (realParameterOverrides == null)
            realParameterOverrides = new List<FmuRealParameterOverride>();
        if (stringParameterOverrides == null)
            stringParameterOverrides = new List<FmuStringParameterOverride>();
        if (integerParameterOverrides == null)
            integerParameterOverrides = new List<FmuIntegerParameterOverride>();
        if (initialRealInputValues == null)
            initialRealInputValues = new List<FmuRealParameterOverride>();

        realParameterOverrides.Clear();
        integerParameterOverrides.Clear();
        stringParameterOverrides.Clear();
        initialRealInputValues.Clear();

        if (config.realParameterOverrides != null)
        {
            for (int i = 0; i < config.realParameterOverrides.Count; i++)
            {
                CoSimulationRealParameterPreset preset = config.realParameterOverrides[i];
                if (preset == null)
                    continue;

                realParameterOverrides.Add(new FmuRealParameterOverride
                {
                    enabled = preset.enabled,
                    variableName = preset.variableName,
                    value = preset.value,
                    status = "Configured from co-sim profile."
                });
            }
        }

        if (config.integerParameterOverrides != null)
        {
            for (int i = 0; i < config.integerParameterOverrides.Count; i++)
            {
                CoSimulationIntegerParameterPreset preset = config.integerParameterOverrides[i];
                if (preset == null)
                    continue;

                integerParameterOverrides.Add(new FmuIntegerParameterOverride
                {
                    enabled = preset.enabled,
                    variableName = preset.variableName,
                    value = preset.value,
                    status = "Configured from co-sim profile."
                });
            }
        }

        if (config.stringParameterOverrides != null)
        {
            for (int i = 0; i < config.stringParameterOverrides.Count; i++)
            {
                CoSimulationStringParameterPreset preset = config.stringParameterOverrides[i];
                if (preset == null)
                    continue;

                stringParameterOverrides.Add(new FmuStringParameterOverride
                {
                    enabled = preset.enabled,
                    variableName = preset.variableName,
                    value = preset.value,
                    rewriteModelDescriptionStart = preset.rewriteModelDescriptionStart,
                    status = "Configured from co-sim profile."
                });
            }
        }

        if (config.initialRealInputValues != null)
        {
            for (int i = 0; i < config.initialRealInputValues.Count; i++)
            {
                CoSimulationRealParameterPreset preset = config.initialRealInputValues[i];
                if (preset == null)
                    continue;

                initialRealInputValues.Add(new FmuRealParameterOverride
                {
                    enabled = preset.enabled,
                    variableName = preset.variableName,
                    value = preset.value,
                    status = "Configured as an FMU initialization input."
                });
            }
        }
    }
    public void Initialize(double startTime, double stopTime, double stepSize)
    {
        if (isInitialized)
            return;

        if (pendingInitializationTask != null)
            throw new InvalidOperationException($"FMU initialization is already pending for {ModelId}.");

        initializationCancellationRequested = false;
        InitializeInternal(
            startTime,
            stopTime,
            stepSize,
            Application.dataPath,
            Application.streamingAssetsPath,
            Application.persistentDataPath);
    }

    public void BeginInitialize(double startTime, double stopTime, double stepSize)
    {
        if (isInitialized || pendingInitializationTask != null)
            return;

        initializationCancellationRequested = false;
        string applicationDataPath = Application.dataPath;
        string streamingAssetsPath = Application.streamingAssetsPath;
        string persistentDataPath = Application.persistentDataPath;

        pendingInitializationTask = Task.Run(() => InitializeInternal(
            startTime,
            stopTime,
            stepSize,
            applicationDataPath,
            streamingAssetsPath,
            persistentDataPath));
    }

    public bool TryCompleteInitialization()
    {
        if (pendingInitializationTask == null)
            return isInitialized;
        if (!pendingInitializationTask.IsCompleted)
            return false;

        Task completedTask = pendingInitializationTask;
        pendingInitializationTask = null;
        completedTask.GetAwaiter().GetResult();
        return isInitialized;
    }

    public bool TryGetInitialRealInputValue(string variableName, out double value)
    {
        value = 0.0;
        if (initialRealInputValues == null || string.IsNullOrWhiteSpace(variableName))
            return false;

        for (int i = 0; i < initialRealInputValues.Count; i++)
        {
            FmuRealParameterOverride input = initialRealInputValues[i];
            if (input == null || !input.enabled ||
                !string.Equals(input.variableName, variableName, StringComparison.Ordinal))
            {
                continue;
            }

            value = input.value;
            return true;
        }

        return false;
    }

    private void InitializeInternal(
        double startTime,
        double stopTime,
        double stepSize,
        string applicationDataPath,
        string streamingAssetsPath,
        string persistentDataPath)
    {
        if (isInitialized)
            return;

        this.startTime = startTime;
        this.stopTime = stopTime;
        this.defaultStepSize = stepSize;
        initializationStreamingAssetsPath = streamingAssetsPath;

        string root = Path.Combine(streamingAssetsPath, "FMU");
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

        string cacheRoot = Path.Combine(persistentDataPath, "FMUCache");
        resolvedUnzipDirectory = FmuModelDescriptionParser.PrepareUnzipDirectory(
            resolvedSourcePath,
            cacheRoot,
            ModelId);

        appliedStringParameterCount = ApplyStringParameterOverridesToModelDescription(resolvedUnzipDirectory, true);
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
            if (useExternalRuntime)
                InitializeRuntime(
                    new ExternalFmi2Runtime(
                        externalCommandTimeoutMs,
                        launchBundledServer,
                        applicationDataPath,
                        streamingAssetsPath,
                        persistentDataPath),
                    "External");
            else
                InitializeRuntime(new NativeFmi2Runtime(), "Native");
        }
        catch (OperationCanceledException)
        {
            runtime = null;
            isInitialized = false;
            runtimeMode = "Not initialized";
            lastStatus = "FMU initialization was cancelled.";
            throw;
        }
        catch (Exception ex)
        {
            runtime = null;
            isInitialized = false;
            runtimeMode = "Not initialized";

            if (!fallbackToMockOnNativeFailure)
            {
                lastStatus = $"FMU runtime initialization failed and fallback is disabled: {ex.Message}";
                throw;
            }

            nativeFallbackActive = true;
            Debug.LogWarning(
                $"[CoSimulation][{ModelId}] FMU runtime initialization failed. Falling back to mock runtime. " +
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

        RunStepSequence(currentTime, stepSize);
        latestSimTimeSeconds = currentTime + stepSize;
    }

    public void BeginStep(double currentTime, double stepSize)
    {
        EnsureInitialized();
        if (pendingStepTask != null)
            throw new InvalidOperationException($"An FMU step is already pending for {ModelId}.");

        if (applyTunableParameterOverridesBeforeEachStep)
            ApplyRealParameterOverrides(runtime, false, false);

        pendingStepEndTime = currentTime + stepSize;
        if (runtime is ExternalFmi2Runtime)
            pendingStepTask = Task.Run(() => RunStepSequence(currentTime, stepSize));
        else
        {
            RunStepSequence(currentTime, stepSize);
            latestSimTimeSeconds = pendingStepEndTime;
        }
    }

    private void RunStepSequence(double currentTime, double communicationStepSize)
    {
        double remaining = communicationStepSize;
        double substepTime = currentTime;
        double maxSubstep = Math.Max(defaultStepSize, 1.0e-6);

        while (remaining > 1.0e-9)
        {
            double substep = Math.Min(remaining, maxSubstep);
            runtime.DoStep(substepTime, substep);
            substepTime += substep;
            remaining -= substep;
        }
    }

    public bool TryCompleteStep()
    {
        if (pendingStepTask == null)
            return true;
        if (!pendingStepTask.IsCompleted)
            return false;

        Task completedTask = pendingStepTask;
        pendingStepTask = null;
        completedTask.GetAwaiter().GetResult();
        latestSimTimeSeconds = pendingStepEndTime;
        return true;
    }

    public void TerminateOrDispose()
    {
        initializationCancellationRequested = true;
        if (pendingInitializationTask != null && !pendingInitializationTask.IsCompleted &&
            initializationRuntime is ExternalFmi2Runtime initializingExternalRuntime)
        {
            initializingExternalRuntime.AbortPendingCommand($"cancelling initialization of {ModelId}");
        }

        if (pendingInitializationTask != null)
        {
            pendingInitializationTask.ContinueWith(
                task => { _ = task.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        }

        if (pendingStepTask != null && !pendingStepTask.IsCompleted && runtime is ExternalFmi2Runtime externalRuntime)
            externalRuntime.AbortPendingCommand($"disposing {ModelId}");

        pendingStepTask = null;
        pendingInitializationTask = null;
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

    [ContextMenu("Load String Parameter Defaults From FMU")]
    public void LoadStringParameterDefaultsFromFmu()
    {
        PopulateStringParameterOverridesFromModelDescription(false);
    }

    [ContextMenu("Reset String Parameters To FMU Defaults")]
    public void ResetStringParametersToFmuDefaults()
    {
        PopulateStringParameterOverridesFromModelDescription(true);
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

    public int PopulateStringParameterOverridesFromModelDescription(bool resetExistingValues)
    {
        try
        {
            FmuModelDescription description = LoadModelDescriptionForInspector();
            if (resetExistingValues)
                stringParameterOverrides.Clear();

            int added = 0;
            int updated = 0;

            for (int i = 0; i < description.variables.Count; i++)
            {
                FmuVariableInfo variable = description.variables[i];
                if (!IsStringParameter(variable))
                    continue;

                FmuStringParameterOverride parameter = FindStringParameterOverride(variable.name);
                if (parameter == null)
                {
                    parameter = new FmuStringParameterOverride
                    {
                        enabled = true,
                        variableName = variable.name,
                        value = variable.hasStartString ? variable.startString : string.Empty,
                        rewriteModelDescriptionStart = true
                    };
                    stringParameterOverrides.Add(parameter);
                    added++;
                }
                else if (resetExistingValues)
                {
                    parameter.value = variable.hasStartString ? variable.startString : string.Empty;
                    updated++;
                }

                parameter.status = BuildVariableStatus(variable, "Loaded default");
            }

            parameterStatus =
                $"Loaded String parameter defaults from {description.modelName}. added={added}, updated={updated}.";
            lastStatus = parameterStatus;
            Debug.Log($"[CoSimulation][{ModelId}] {parameterStatus}");
            return added + updated;
        }
        catch (Exception ex)
        {
            parameterStatus = $"Could not load FMU String parameter defaults: {ex.Message}";
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
        initializationRuntime = newRuntime;
        try
        {
            newRuntime.Load(resolvedSourcePath, resolvedUnzipDirectory, ModelId, logging);
            appliedParameterCount = applyParameterOverridesOnInitialize
                ? ApplyRealParameterOverrides(newRuntime, true, true) + ApplyIntegerParameterOverrides(newRuntime, true)
                : 0;
            appliedStringParameterCount = applyParameterOverridesOnInitialize
                ? RegisterInitialStringParameterOverrides(newRuntime, true)
                : 0;
            double tolerance = modelDescription != null && modelDescription.hasDefaultExperimentTolerance
                ? modelDescription.defaultExperimentTolerance
                : 0.0;
            newRuntime.SetupExperiment(startTime, stopTime, tolerance);
            newRuntime.EnterInitializationMode();
            appliedInitialInputCount = ApplyInitialRealInputs(newRuntime, true);
            newRuntime.ExitInitializationMode();

            if (initializationCancellationRequested)
                throw new OperationCanceledException($"FMU initialization was cancelled for {ModelId}.");

            runtime = newRuntime;
            isInitialized = true;
            runtimeMode = mode;
        }
        catch
        {
            newRuntime.Dispose();
            throw;
        }
        finally
        {
            initializationRuntime = null;
        }

        lastStatus =
            $"{mode} runtime initialized. source={resolvedSourcePath}, unzip={resolvedUnzipDirectory}, " +
            $"modelName={parsedModelName}, variables={parsedVariableCount}, parameters={appliedParameterCount}, " +
            $"stringParameters={appliedStringParameterCount}, initialInputs={appliedInitialInputCount}";

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

    private int ApplyInitialRealInputs(IFmi2Runtime targetRuntime, bool logWarnings)
    {
        if (targetRuntime == null || initialRealInputValues == null)
            return 0;

        int applied = 0;
        for (int i = 0; i < initialRealInputValues.Count; i++)
        {
            FmuRealParameterOverride input = initialRealInputValues[i];
            if (input == null || !input.enabled || string.IsNullOrWhiteSpace(input.variableName))
                continue;

            if (modelDescription == null ||
                !modelDescription.TryGetVariable(input.variableName, out FmuVariableInfo variable) ||
                variable.valueType != SignalValueType.Real ||
                variable.causality != SignalDirection.Input)
            {
                input.status = "Skipped: Real input variable not found in modelDescription.";
                if (logWarnings)
                    Debug.LogWarning($"[CoSimulation][{ModelId}] {input.status} variable={input.variableName}");
                continue;
            }

            targetRuntime.SetReal(variable.valueReference, input.value);
            input.status = BuildVariableStatus(variable, "Applied during initialization");
            applied++;
        }

        return applied;
    }

    private int ApplyIntegerParameterOverrides(IFmi2Runtime targetRuntime, bool logWarnings)
    {
        if (targetRuntime == null || integerParameterOverrides == null)
            return 0;

        int applied = 0;
        for (int i = 0; i < integerParameterOverrides.Count; i++)
        {
            FmuIntegerParameterOverride parameter = integerParameterOverrides[i];
            if (parameter == null || !parameter.enabled || string.IsNullOrWhiteSpace(parameter.variableName))
                continue;

            if (modelDescription == null || !modelDescription.TryGetVariable(parameter.variableName, out FmuVariableInfo variable) ||
                variable.valueType != SignalValueType.Integer)
            {
                parameter.status = "Skipped: Integer variable not found in modelDescription.";
                if (logWarnings)
                    Debug.LogWarning($"[CoSimulation][{ModelId}] {parameter.status} variable={parameter.variableName}");
                continue;
            }

            try
            {
                targetRuntime.RegisterInitialInteger(variable.valueReference, parameter.value);
                parameter.status = BuildVariableStatus(variable, "Registered initial Integer value");
                applied++;
            }
            catch (Exception ex)
            {
                parameter.status = $"Failed: {ex.Message}";
                if (logWarnings)
                    Debug.LogWarning($"[CoSimulation][{ModelId}] Failed to apply Integer parameter {parameter.variableName}: {ex.Message}");
            }
        }

        return applied;
    }

    private int RegisterInitialStringParameterOverrides(IFmi2Runtime targetRuntime, bool logWarnings)
    {
        if (targetRuntime == null || stringParameterOverrides == null)
            return 0;

        int applied = 0;
        for (int i = 0; i < stringParameterOverrides.Count; i++)
        {
            FmuStringParameterOverride parameter = stringParameterOverrides[i];
            if (parameter == null || !parameter.enabled || string.IsNullOrWhiteSpace(parameter.variableName))
                continue;

            if (modelDescription == null || !modelDescription.TryGetVariable(parameter.variableName, out FmuVariableInfo variable) ||
                variable.valueType != SignalValueType.String)
                continue;

            try
            {
                targetRuntime.RegisterInitialString(variable.valueReference, ResolveStringParameterValue(parameter.value));
                parameter.status = BuildVariableStatus(variable, "Registered initial String value");
                applied++;
            }
            catch (Exception ex)
            {
                parameter.status = $"Failed: {ex.Message}";
                if (logWarnings)
                    Debug.LogWarning($"[CoSimulation][{ModelId}] Failed to apply String parameter {parameter.variableName}: {ex.Message}");
            }
        }
        return applied;
    }

    private int ApplyStringParameterOverridesToModelDescription(string unzipDirectory, bool logWarnings)
    {
        if (stringParameterOverrides == null || stringParameterOverrides.Count == 0)
            return 0;

        string xmlPath = Path.Combine(unzipDirectory, "modelDescription.xml");
        if (!File.Exists(xmlPath))
            throw new FileNotFoundException("modelDescription.xml was not found for String parameter override.", xmlPath);

        XDocument document = XDocument.Load(xmlPath);
        int applied = 0;
        int skipped = 0;

        for (int i = 0; i < stringParameterOverrides.Count; i++)
        {
            FmuStringParameterOverride parameter = stringParameterOverrides[i];
            if (parameter == null || !parameter.enabled)
            {
                skipped++;
                continue;
            }

            if (!parameter.rewriteModelDescriptionStart)
            {
                parameter.status = "Skipped: modelDescription rewrite is disabled.";
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(parameter.variableName))
            {
                parameter.status = "Skipped: variable name is empty.";
                skipped++;
                continue;
            }

            XElement scalar = FindScalarVariable(document, parameter.variableName);
            XElement stringElement = scalar != null ? FindValueTypeElement(scalar, "String") : null;
            if (scalar == null || stringElement == null)
            {
                parameter.status = "Skipped: String parameter not found in modelDescription.";
                if (logWarnings)
                    Debug.LogWarning($"[CoSimulation][{ModelId}] {parameter.status} variable={parameter.variableName}");
                skipped++;
                continue;
            }

            string resolvedValue = ResolveStringParameterValue(parameter.value);
            stringElement.SetAttributeValue("start", resolvedValue);
            parameter.status = $"Rewrote modelDescription start: {resolvedValue}";
            applied++;
        }

        if (applied > 0)
            document.Save(xmlPath);

        if (applied > 0 || skipped > 0)
            parameterStatus = $"String parameter modelDescription rewrite: applied={applied}, skipped={skipped}.";

        return applied;
    }

    private string ResolveStringParameterValue(string value)
    {
        string resolved = value ?? string.Empty;
        string streamingAssetsPath = string.IsNullOrEmpty(initializationStreamingAssetsPath)
            ? Application.streamingAssetsPath
            : initializationStreamingAssetsPath;
        string streamingAssets = streamingAssetsPath.Replace('\\', '/');
        string fmuRoot = Path.Combine(streamingAssetsPath, "FMU").Replace('\\', '/');

        resolved = resolved.Replace("{StreamingAssets}", streamingAssets)
                           .Replace("{STREAMING_ASSETS}", streamingAssets)
                           .Replace("{FMU_ROOT}", fmuRoot);

        if (!string.IsNullOrWhiteSpace(resolved) && !Path.IsPathRooted(resolved))
            resolved = Path.Combine(fmuRoot, resolved);

        return resolved.Replace('\\', '/');
    }

    private static XElement FindScalarVariable(XDocument document, string variableName)
    {
        if (document == null)
            return null;

        foreach (XElement element in document.Descendants())
        {
            if (element.Name.LocalName != "ScalarVariable")
                continue;

            XAttribute name = element.Attribute("name");
            if (name != null && string.Equals(name.Value, variableName, StringComparison.Ordinal))
                return element;
        }

        return null;
    }

    private static XElement FindValueTypeElement(XElement scalar, string localName)
    {
        if (scalar == null)
            return null;

        foreach (XElement child in scalar.Elements())
        {
            if (child.Name.LocalName == localName)
                return child;
        }

        return null;
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

    private FmuStringParameterOverride FindStringParameterOverride(string variableName)
    {
        if (stringParameterOverrides == null)
            stringParameterOverrides = new List<FmuStringParameterOverride>();

        for (int i = 0; i < stringParameterOverrides.Count; i++)
        {
            FmuStringParameterOverride parameter = stringParameterOverrides[i];
            if (parameter != null && string.Equals(parameter.variableName, variableName, StringComparison.Ordinal))
                return parameter;
        }

        return null;
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

    private static bool IsStringParameter(FmuVariableInfo variable)
    {
        return variable != null &&
               variable.valueType == SignalValueType.String &&
               variable.causality == SignalDirection.Parameter;
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

        string start = variable.hasStartReal
            ? $", start={variable.startReal:G6}"
            : (variable.hasStartString ? $", start={variable.startString}" : string.Empty);
        return $"{prefix}: vr={variable.valueReference}, causality={variable.causality}, " +
               $"variability={variable.variability}{start}";
    }
}


