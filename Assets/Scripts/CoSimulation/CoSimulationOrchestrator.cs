using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class CoSimulationOrchestrator : MonoBehaviour
{
    [Header("Co-Simulation")]
    [SerializeField] private CoSimulationProfile coSimulationProfile;
    [SerializeField] private bool enableCoSimulation = true;
    [SerializeField] private double coSimStepSizeSeconds = 2.0;
    [SerializeField] private bool useLbmSimulatedTime = true;
    [SerializeField] private bool runFmuBeforeLbmStep = false;
    [SerializeField] private bool logEveryCoSimStep = true;

    [Header("Connections")]
    [SerializeField] private CoSimConnectionMap connectionMap;
    [SerializeField] private AirflowLbmSignalAdapter airflowAdapter;
    [SerializeField] private List<FmuCoSimulationModel> fmuModels = new List<FmuCoSimulationModel>();
    [SerializeField] private CoSimulationCsvLogger csvLogger;

    [Header("Debug Signal Names")]
    [SerializeField] private string debugHzModelId = "Simple_CFMU";
    [SerializeField] private string debugHzVariableName = "Hz";
    [SerializeField] private string debugControllerSetpointModelId = "Simple_CFMU";
    [SerializeField] private string debugControllerSetpointVariableName = "T_set";
    [SerializeField] private string debugPlantHzInputModelId = "Simple_Plant";
    [SerializeField] private string debugPlantHzInputVariableName = "hz_Plant";
    [SerializeField] private string debugDischargeModelId = "Simple_Plant";
    [SerializeField] private string debugDischargeVariableName = "T_dis_Plant";

    [Header("Read-Only Status")]
    [SerializeField, ReadOnly] private string activeProfileName = "Simple_ControllerPlant";
    [SerializeField, ReadOnly] private string activeFmuModelSummary = string.Empty;
    [SerializeField, ReadOnly] private string latestDebugSignalSummary = string.Empty;
    [SerializeField, ReadOnly] private double currentCoSimTime = 0.0;
    [SerializeField, ReadOnly] private double nextCoSimTime = 0.0;
    [SerializeField, ReadOnly] private ulong coSimStepIndex = 0;
    [SerializeField, ReadOnly] private ulong completedCoSimStepCount = 0;
    [SerializeField, ReadOnly] private bool coSimFailureObserved = false;
    [SerializeField, ReadOnly] private string lastCoSimFailure = string.Empty;
    [SerializeField, ReadOnly] private double latestSensorTemperatureDegC = 0.0;
    [SerializeField, ReadOnly] private double latestControllerSetpointDegC = double.NaN;
    [SerializeField, ReadOnly] private double latestHz = double.NaN;
    [SerializeField, ReadOnly] private double latestPlantHzInput = double.NaN;
    [SerializeField, ReadOnly] private double latestDischargeTemperatureDegC = double.NaN;
    [SerializeField, ReadOnly] private double latestAppliedInletTemperatureDegC = double.NaN;
    [SerializeField, ReadOnly] private int latestTargetInletCount = 0;
    [SerializeField, ReadOnly] private bool nativeFallbackActive = false;
    [SerializeField, ReadOnly] private string runtimeModeSummary = "Not initialized.";
    [TextArea(3, 8)]
    [SerializeField, ReadOnly] private string lastStatus = "Not initialized.";

    private readonly CoSimSignalBus signalBus = new CoSimSignalBus();
    private readonly Dictionary<CoSimSignalKey, CoSimSignalValue> previousStepSignals =
        new Dictionary<CoSimSignalKey, CoSimSignalValue>();
    private readonly HashSet<string> fanModeSignalsWithValidControllerOutput =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private CoSimConnectionMap runtimeDefaultConnectionMap;
    private CoSimConnectionMap runtimeProfileConnectionMap;
    private SimulationController simulationController;
    private bool scheduleInitialized;
    private bool coSimStepInProgress;
    private CoSimConnectionMap activeStepMap;
    private StringBuilder activeStepStatus;
    private int activeStepModelIndex;
    private int activeInitializationModelIndex;
    private double activeStepTime;
    private bool modelsInitializationCompleted;
    private FmuCoSimulationModel pendingInitializationModel;
    private FmuCoSimulationModel pendingStepModel;

    public ulong CoSimStepIndex => coSimStepIndex;
    public ulong CompletedCoSimStepCount => completedCoSimStepCount;
    public bool IsCoSimStepInProgress => coSimStepInProgress;
    public bool CoSimFailureObserved => coSimFailureObserved;
    public string LastCoSimFailure => lastCoSimFailure;
    public double CurrentCoSimTime => currentCoSimTime;
    public double LatestSensorTemperatureDegC => latestSensorTemperatureDegC;
    public double LatestControllerSetpointDegC => latestControllerSetpointDegC;
    public double LatestHz => latestHz;
    public double LatestPlantHzInput => latestPlantHzInput;
    public double LatestDischargeTemperatureDegC => latestDischargeTemperatureDegC;
    public double LatestAppliedInletTemperatureDegC => latestAppliedInletTemperatureDegC;
    public string RuntimeModeSummary => runtimeModeSummary;
    public string ProfileName => activeProfileName;
    public string ActiveFmuModelSummary => activeFmuModelSummary;
    public string LatestDebugSignalSummary => latestDebugSignalSummary;
    public string LastStatus => lastStatus;

    private void Awake()
    {
        if (coSimulationProfile != null)
            ApplyProfile(coSimulationProfile);

        ResolveReferences();
    }

    private void Update()
    {
        if (runFmuBeforeLbmStep)
            TickIfDue();
    }

    private void LateUpdate()
    {
        if (!runFmuBeforeLbmStep)
            TickIfDue();
    }

    public void ApplyProfile(CoSimulationProfile profile)
    {
        if (profile == null)
            profile = CoSimulationProfile.CreateDefaultSimpleProfile();

        coSimulationProfile = profile;
        activeProfileName = profile.ProfileName;
        coSimStepSizeSeconds = Math.Max(profile.CoSimStepSizeSeconds, 1.0e-6);
        useLbmSimulatedTime = profile.UseLbmSimulatedTime;
        runFmuBeforeLbmStep = profile.RunFmuBeforeLbmStep;
        logEveryCoSimStep = profile.LogEveryCoSimStep;

        ApplyPrimaryDebugSignal(profile.ControllerSetpointSignal, ref debugControllerSetpointModelId, ref debugControllerSetpointVariableName);
        ApplyPrimaryDebugSignal(profile.ControllerOutputSignal, ref debugHzModelId, ref debugHzVariableName);
        ApplyPrimaryDebugSignal(profile.PlantInputSignal, ref debugPlantHzInputModelId, ref debugPlantHzInputVariableName);
        ApplyPrimaryDebugSignal(profile.DischargeOutputSignal, ref debugDischargeModelId, ref debugDischargeVariableName);

        runtimeProfileConnectionMap = profile.CreateRuntimeConnectionMap();
        runtimeDefaultConnectionMap = null;
        ResetSchedule();
        lastStatus = $"Applied co-sim profile '{activeProfileName}'.";
    }

    [ContextMenu("Apply Selected Profile")]
    public void ApplySelectedProfileFromInspector()
    {
        ApplyProfile(coSimulationProfile);
    }
    [ContextMenu("Run One Co-Sim Step Now")]
    public void RunOneStepFromInspector()
    {
        ResolveReferences();
        double time = GetCurrentTime();
        scheduleInitialized = true;
        nextCoSimTime = time;
        DoCoSimulationStep(time);
    }

    [ContextMenu("Auto Find References")]
    public void AutoFindReferences()
    {
        ResolveReferences(true);
    }

    [ContextMenu("Reset Co-Sim Schedule")]
    public void ResetSchedule()
    {
        CancelActiveStep();
        scheduleInitialized = false;
        nextCoSimTime = 0.0;
        coSimStepIndex = 0;
        completedCoSimStepCount = 0;
        coSimFailureObserved = false;
        lastCoSimFailure = string.Empty;
        fanModeSignalsWithValidControllerOutput.Clear();
        lastStatus = "Co-sim schedule reset.";
    }

    private void TickIfDue()
    {
        if (!enableCoSimulation || coSimFailureObserved)
            return;

        if (coSimStepInProgress)
        {
            ContinueCoSimulationStep();
            return;
        }

        ResolveReferences();

        if (airflowAdapter == null)
        {
            lastStatus = "AirflowLbmSignalAdapter is missing.";
            return;
        }

        double currentTime = GetCurrentTime();
        if (!scheduleInitialized)
        {
            nextCoSimTime = currentTime;
            scheduleInitialized = true;
        }

        if (currentTime + 1e-9 < nextCoSimTime)
            return;

        // LBM time only decides when the communication point is due. The FMU
        // must receive the exact scheduled time reached by its previous step.
        DoCoSimulationStep(nextCoSimTime);
    }

    private void DoCoSimulationStep(double currentTime)
    {
        try
        {
            activeStepMap = GetActiveConnectionMap();
            EnsureFmuModels();
            SortFmuModelsForMap(activeStepMap);
            signalBus.Clear();
            currentCoSimTime = currentTime;
            coSimStepIndex++;
            activeStepTime = currentTime;
            activeStepModelIndex = 0;
            activeInitializationModelIndex = 0;
            modelsInitializationCompleted = false;
            activeStepStatus = new StringBuilder(512);
            coSimStepInProgress = true;
            simulationController?.SetExternalStepPause(true, "Waiting for sequential FMU initialization/step.");

            SeedBusWithPreviousStepSignals(activeStepStatus);
            PublishProfileConstantSignals(activeStepStatus);
            PublishProviderSources(activeStepMap, airflowAdapter.ModelId, airflowAdapter, activeStepStatus);
            ContinueCoSimulationStep();
        }
        catch (Exception ex)
        {
            FailActiveStep(ex);
        }
    }

    private void ContinueCoSimulationStep()
    {
        try
        {
            if (!modelsInitializationCompleted)
            {
                if (!ContinueSequentialModelInitialization())
                    return;

                modelsInitializationCompleted = true;
            }

            if (pendingStepModel != null)
            {
                if (!pendingStepModel.TryCompleteStep())
                    return;

                PublishModelOutputs(activeStepMap, pendingStepModel, activeStepStatus);
                pendingStepModel = null;
                activeStepModelIndex++;
            }

            while (activeStepModelIndex < fmuModels.Count)
            {
                FmuCoSimulationModel model = fmuModels[activeStepModelIndex];
                if (model == null)
                {
                    activeStepModelIndex++;
                    continue;
                }

                TransferConnectionsToModel(activeStepMap, model, activeStepStatus);
                ValidateIndoorUnitFanInputs(activeStepMap, model);
                model.BeginStep(activeStepTime, GetSafeStepSize());
                if (!model.TryCompleteStep())
                {
                    pendingStepModel = model;
                    lastStatus = $"External FMU step pending: {model.ModelId}.";
                    return;
                }

                PublishModelOutputs(activeStepMap, model, activeStepStatus);
                activeStepModelIndex++;
            }

            CompleteActiveStep();
        }
        catch (Exception ex)
        {
            FailActiveStep(ex);
        }
    }

    private bool ContinueSequentialModelInitialization()
    {
        double step = GetSafeStepSize();

        if (pendingInitializationModel != null)
        {
            if (!pendingInitializationModel.TryCompleteInitialization())
                return false;

            activeStepStatus.Append($"Initialized {pendingInitializationModel.ModelId}. ");
            pendingInitializationModel = null;
            activeInitializationModelIndex++;
        }

        while (activeInitializationModelIndex < fmuModels.Count)
        {
            FmuCoSimulationModel model = fmuModels[activeInitializationModelIndex];
            if (model == null || model.IsInitialized)
            {
                activeInitializationModelIndex++;
                continue;
            }

            model.BeginInitialize(activeStepTime, 0.0, step);
            if (!model.TryCompleteInitialization())
            {
                pendingInitializationModel = model;
                lastStatus = $"Sequential FMU initialization pending: {model.ModelId}.";
                return false;
            }

            activeStepStatus.Append($"Initialized {model.ModelId}. ");
            activeInitializationModelIndex++;
        }

        return true;
    }

    private void CompleteActiveStep()
    {
        bool appliedToAirflow = TransferConnectionsToReceiver(
            activeStepMap, airflowAdapter.ModelId, airflowAdapter, activeStepStatus);
        if (appliedToAirflow)
            airflowAdapter.SyncDynamicBoundaryInputsNow();

        RememberCurrentStepSignals();
        UpdateReadOnlyDebugValues(activeStepStatus.ToString());
        WriteCsvRow();
        completedCoSimStepCount++;

        if (logEveryCoSimStep)
        {
            Debug.Log(
                $"[CoSimulation] step={coSimStepIndex}, t={currentCoSimTime:F3}s, " +
                $"T_sensor={latestSensorTemperatureDegC:F3}C, T_set={latestControllerSetpointDegC:F3}C, " +
                $"Hz={latestHz:F3}, plantHz={latestPlantHzInput:F3}, " +
                $"T_dis={latestDischargeTemperatureDegC:F3}C, applied={latestAppliedInletTemperatureDegC:F3}C, " +
                $"targets={latestTargetInletCount}, runtime={runtimeModeSummary}, status={lastStatus}");
        }

        double step = GetSafeStepSize();
        nextCoSimTime = activeStepTime + step;
        ClearActiveStepState();
    }

    private void FailActiveStep(Exception ex)
    {
        lastStatus = $"Co-sim step failed: {ex.Message}";
        coSimFailureObserved = true;
        lastCoSimFailure = lastStatus;
        Debug.LogWarning($"[CoSimulation] {lastStatus}");
        CancelActiveStep();
        simulationController?.SetSimulationRunning(false);
    }

    private void CancelActiveStep()
    {
        if (fmuModels != null && coSimStepInProgress)
        {
            for (int i = 0; i < fmuModels.Count; i++)
            {
                if (fmuModels[i] != null)
                    fmuModels[i].TerminateOrDispose();
            }
        }

        ClearActiveStepState();
    }

    private void ClearActiveStepState()
    {
        simulationController?.SetExternalStepPause(false);
        coSimStepInProgress = false;
        activeStepMap = null;
        activeStepStatus = null;
        activeStepModelIndex = 0;
        activeInitializationModelIndex = 0;
        modelsInitializationCompleted = false;
        pendingInitializationModel = null;
        pendingStepModel = null;
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
            CancelActiveStep();
        else
            simulationController?.SetExternalStepPause(false);
    }

    private void ResolveReferences(bool forceAutoCollectFmus = false)
    {
        if (simulationController == null)
            simulationController = SimulationController.Instance != null
                ? SimulationController.Instance
                : FindFirstObjectByType<SimulationController>();

        if (airflowAdapter == null)
            airflowAdapter = FindFirstObjectByType<AirflowLbmSignalAdapter>();

        if (csvLogger == null)
            csvLogger = GetComponent<CoSimulationCsvLogger>();

        if (csvLogger == null)
            csvLogger = FindFirstObjectByType<CoSimulationCsvLogger>();

        if (forceAutoCollectFmus || fmuModels == null || fmuModels.Count == 0)
        {
            FmuCoSimulationModel[] models = FindObjectsByType<FmuCoSimulationModel>(FindObjectsSortMode.InstanceID);
            if (fmuModels == null)
                fmuModels = new List<FmuCoSimulationModel>();

            fmuModels.Clear();
            for (int i = 0; i < models.Length; i++)
            {
                if (models[i] != null)
                    fmuModels.Add(models[i]);
            }
        }
    }

    private CoSimConnectionMap GetActiveConnectionMap()
    {
        if (connectionMap != null)
            return connectionMap;

        if (coSimulationProfile != null)
        {
            if (runtimeProfileConnectionMap == null)
                runtimeProfileConnectionMap = coSimulationProfile.CreateRuntimeConnectionMap();

            return runtimeProfileConnectionMap;
        }

        if (runtimeDefaultConnectionMap == null)
            runtimeDefaultConnectionMap = CoSimConnectionMap.CreateDefaultRuntimeMap();

        return runtimeDefaultConnectionMap;
    }

    private void EnsureFmuModels()
    {
        if (fmuModels == null || fmuModels.Count == 0)
            ResolveReferences(true);
    }

    private void SortFmuModelsForMap(CoSimConnectionMap map)
    {
        if (map == null || fmuModels == null || fmuModels.Count < 2)
            return;

        for (int pass = 0; pass < fmuModels.Count; pass++)
        {
            bool changed = false;
            foreach (CoSimConnection connection in map.EnabledConnections)
            {
                int sourceIndex = IndexOfModel(connection.sourceModelId);
                int targetIndex = IndexOfModel(connection.targetModelId);

                if (sourceIndex < 0 || targetIndex < 0 || sourceIndex < targetIndex)
                    continue;

                FmuCoSimulationModel source = fmuModels[sourceIndex];
                fmuModels.RemoveAt(sourceIndex);
                fmuModels.Insert(targetIndex, source);
                changed = true;
            }

            if (!changed)
                break;
        }
    }

    private int IndexOfModel(string modelId)
    {
        if (fmuModels == null)
            return -1;

        for (int i = 0; i < fmuModels.Count; i++)
        {
            FmuCoSimulationModel model = fmuModels[i];
            if (model != null && string.Equals(model.ModelId, modelId, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private void SeedBusWithPreviousStepSignals(StringBuilder status)
    {
        if (previousStepSignals.Count == 0)
            return;

        signalBus.PublishAll(previousStepSignals);
        status.Append($"Seeded previous step signals: count={previousStepSignals.Count}. ");
    }

    private void PublishProfileConstantSignals(StringBuilder status)
    {
        if (coSimulationProfile == null || coSimulationProfile.ConstantSignals == null)
            return;

        int published = 0;
        for (int i = 0; i < coSimulationProfile.ConstantSignals.Count; i++)
        {
            CoSimConstantSignal signal = coSimulationProfile.ConstantSignals[i];
            if (signal == null || !signal.enabled || string.IsNullOrWhiteSpace(signal.modelId) ||
                string.IsNullOrWhiteSpace(signal.variableName))
            {
                continue;
            }

            signalBus.Publish(signal.Key, signal.ToSignalValue(currentCoSimTime));
            published++;
        }

        if (published > 0)
            status.Append($"Published profile constants: count={published}. ");
    }

    private void RememberCurrentStepSignals()
    {
        signalBus.CopyTo(previousStepSignals);
    }
    private void PublishProviderSources(
        CoSimConnectionMap map,
        string providerModelId,
        ICoSimSignalProvider provider,
        StringBuilder status)
    {
        HashSet<string> publishedVariables = new HashSet<string>(StringComparer.Ordinal);
        foreach (CoSimConnection connection in map.EnabledConnections)
        {
            if (!string.Equals(connection.sourceModelId, providerModelId, StringComparison.Ordinal))
                continue;

            if (!publishedVariables.Add(connection.sourceVariableName))
                continue;

            CoSimSignalKey key = connection.SourceKey;
            CoSimSignalValue value;
            if (provider.TryGetSignal(key, out value))
            {
                signalBus.Publish(key, value);
                status.Append($"Published {key}={value}. ");
            }
            else
            {
                status.Append($"Missing provider signal {key}. ");
            }
        }
    }

    private void TransferConnectionsToModel(
        CoSimConnectionMap map,
        FmuCoSimulationModel model,
        StringBuilder status)
    {
        foreach (CoSimConnection connection in map.EnabledConnections)
        {
            if (!string.Equals(connection.targetModelId, model.ModelId, StringComparison.Ordinal))
                continue;

            CoSimSignalValue value;
            string transferStatus;
            if (!signalBus.TryTransfer(connection, out value, out transferStatus))
            {
                status.Append(transferStatus).Append(". ");
                continue;
            }

            if (ShouldForceStoppedIndoorUnitFanMode(
                    map,
                    model,
                    connection,
                    out CoSimSignalValue stoppedFanMode,
                    out string onOffVariableName))
            {
                string signalKey = $"{connection.sourceModelId}.{connection.sourceVariableName}";
                fanModeSignalsWithValidControllerOutput.Remove(signalKey);
                model.SetInput(connection.targetVariableName, stoppedFanMode);
                status.Append(
                    $"Stop-synchronized {model.ModelId}.{connection.targetVariableName}=0 " +
                    $"because {model.ModelId}.{onOffVariableName}=0. ");
                continue;
            }

            if (ShouldUseInitialFanModeFallback(model, connection, value, out CoSimSignalValue fallback))
            {
                model.SetInput(connection.targetVariableName, fallback);
                status.Append($"Fan-mode fallback {model.ModelId}.{connection.targetVariableName}={fallback}. ");
                continue;
            }

            model.SetInput(connection.targetVariableName, value);
            status.Append(transferStatus).Append(". ");
        }
    }

    private bool ShouldForceStoppedIndoorUnitFanMode(
        CoSimConnectionMap map,
        FmuCoSimulationModel model,
        CoSimConnection fanModeConnection,
        out CoSimSignalValue stoppedFanMode,
        out string onOffVariableName)
    {
        stoppedFanMode = default;
        onOffVariableName = string.Empty;
        if (map == null || model == null || fanModeConnection == null)
            return false;

        const string fanModeSuffix = "_fan_mode";
        string fanModeVariableName = fanModeConnection.targetVariableName ?? string.Empty;
        if (!fanModeVariableName.EndsWith(fanModeSuffix, StringComparison.OrdinalIgnoreCase))
            return false;

        string indoorUnit = fanModeVariableName.Substring(
            0,
            fanModeVariableName.Length - fanModeSuffix.Length);
        onOffVariableName = indoorUnit + "_onoff";

        foreach (CoSimConnection connection in map.EnabledConnections)
        {
            if (!string.Equals(connection.targetModelId, model.ModelId, StringComparison.Ordinal) ||
                !string.Equals(connection.targetVariableName, onOffVariableName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!signalBus.TryTransfer(connection, out CoSimSignalValue onOff, out _) ||
                !onOff.TryGetReal(out double onOffValue) ||
                double.IsNaN(onOffValue) ||
                double.IsInfinity(onOffValue) ||
                onOffValue > 0.5)
            {
                return false;
            }

            stoppedFanMode = CoSimSignalValue.FromReal(0.0, activeStepTime);
            return true;
        }

        return false;
    }

    private bool ShouldUseInitialFanModeFallback(
        FmuCoSimulationModel model,
        CoSimConnection connection,
        CoSimSignalValue transferred,
        out CoSimSignalValue fallback)
    {
        fallback = default;
        if (model == null || connection == null ||
            !connection.targetVariableName.EndsWith("_fan_mode", StringComparison.OrdinalIgnoreCase) ||
            !transferred.TryGetReal(out double transferredValue))
        {
            return false;
        }

        string signalKey = $"{connection.sourceModelId}.{connection.sourceVariableName}";
        bool isValidControllerOutput =
            !double.IsNaN(transferredValue) &&
            !double.IsInfinity(transferredValue) &&
            transferredValue >= 1.0 &&
            transferredValue <= 5.0;
        if (isValidControllerOutput)
        {
            fanModeSignalsWithValidControllerOutput.Add(signalKey);
            return false;
        }

        if (fanModeSignalsWithValidControllerOutput.Contains(signalKey) ||
            !model.TryGetInitialRealInputValue(connection.targetVariableName, out double initialValue) ||
            double.IsNaN(initialValue) ||
            double.IsInfinity(initialValue) ||
            initialValue < 1.0 ||
            initialValue > 5.0)
        {
            return false;
        }

        fallback = CoSimSignalValue.FromReal(initialValue, activeStepTime);
        return true;
    }

    private void ValidateIndoorUnitFanInputs(
        CoSimConnectionMap map,
        FmuCoSimulationModel model)
    {
        const string onOffSuffix = "_onoff";
        const string fanModeSuffix = "_fan_mode";
        Dictionary<string, double> onOffByIndoorUnit = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, double> fanModeByIndoorUnit = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (CoSimConnection connection in map.EnabledConnections)
        {
            if (!string.Equals(connection.targetModelId, model.ModelId, StringComparison.Ordinal))
                continue;

            string targetName = connection.targetVariableName ?? string.Empty;
            string indoorUnit;
            Dictionary<string, double> destination;
            if (targetName.EndsWith(onOffSuffix, StringComparison.OrdinalIgnoreCase))
            {
                indoorUnit = targetName.Substring(0, targetName.Length - onOffSuffix.Length);
                destination = onOffByIndoorUnit;
            }
            else if (targetName.EndsWith(fanModeSuffix, StringComparison.OrdinalIgnoreCase))
            {
                indoorUnit = targetName.Substring(0, targetName.Length - fanModeSuffix.Length);
                destination = fanModeByIndoorUnit;
            }
            else
            {
                continue;
            }

            if (model.TryGetRealValue(targetName, out double realValue))
                destination[indoorUnit] = realValue;
        }

        foreach (KeyValuePair<string, double> pair in onOffByIndoorUnit)
        {
            double fanMode;
            if (!fanModeByIndoorUnit.TryGetValue(pair.Key, out fanMode))
                continue;

            if (pair.Value <= 0.5)
            {
                if (!double.IsNaN(fanMode) && !double.IsInfinity(fanMode) && Math.Abs(fanMode) <= 1.0e-9)
                    continue;

                throw new InvalidOperationException(
                    $"[CoSimulation Guard] Refusing {model.ModelId} step: " +
                    $"{pair.Key}{onOffSuffix}={pair.Value:G6} while {pair.Key}{fanModeSuffix}={fanMode:G6}. " +
                    "When an indoor unit is off, CurSetFan/fan_mode must be 0.");
            }

            if (!double.IsNaN(fanMode) && !double.IsInfinity(fanMode) && fanMode >= 1.0 && fanMode <= 5.0)
                continue;

            throw new InvalidOperationException(
                $"[CoSimulation Guard] Refusing {model.ModelId} step: " +
                $"{pair.Key}{onOffSuffix}={pair.Value:G6} while {pair.Key}{fanModeSuffix}={fanMode:G6}. " +
                "When an indoor unit is on, set_fan/fan_mode must be in the valid operating range 1..5.");
        }
    }

    private void PublishModelOutputs(
        CoSimConnectionMap map,
        FmuCoSimulationModel model,
        StringBuilder status)
    {
        HashSet<string> publishedVariables = new HashSet<string>(StringComparer.Ordinal);
        foreach (CoSimConnection connection in map.EnabledConnections)
        {
            if (!string.Equals(connection.sourceModelId, model.ModelId, StringComparison.Ordinal))
                continue;

            if (!publishedVariables.Add(connection.sourceVariableName))
                continue;

            CoSimSignalValue value = model.GetOutput(connection.sourceVariableName);
            if (ShouldPublishStoppedCurSetFan(
                    model,
                    connection.sourceVariableName,
                    value,
                    out CoSimSignalValue stoppedCurSetFan,
                    out string onOffVariableName))
            {
                value = stoppedCurSetFan;
                status.Append(
                    $"Stop-synchronized {model.ModelId}.{connection.sourceVariableName}=0 " +
                    $"because {model.ModelId}.{onOffVariableName}=0. ");
            }

            CoSimSignalKey key = connection.SourceKey;
            signalBus.Publish(key, value);
            status.Append($"Published {key}={value}. ");
        }
    }

    private static bool ShouldPublishStoppedCurSetFan(
        FmuCoSimulationModel model,
        string outputVariableName,
        CoSimSignalValue output,
        out CoSimSignalValue stoppedCurSetFan,
        out string onOffVariableName)
    {
        stoppedCurSetFan = default;
        onOffVariableName = string.Empty;
        if (model == null || string.IsNullOrWhiteSpace(outputVariableName))
            return false;

        const string curSetFanSuffix = ".CurSetFan";
        if (!outputVariableName.EndsWith(curSetFanSuffix, StringComparison.OrdinalIgnoreCase))
            return false;

        string indoorUnit = outputVariableName.Substring(
            0,
            outputVariableName.Length - curSetFanSuffix.Length);
        onOffVariableName = indoorUnit + ".FOnOff";
        if (!model.TryGetRealValue(onOffVariableName, out double onOffValue) ||
            double.IsNaN(onOffValue) ||
            double.IsInfinity(onOffValue) ||
            onOffValue > 0.5)
        {
            return false;
        }

        stoppedCurSetFan = CoSimSignalValue.FromReal(0.0, output.simTimeSeconds);
        return true;
    }

    private bool TransferConnectionsToReceiver(
        CoSimConnectionMap map,
        string receiverModelId,
        ICoSimSignalReceiver receiver,
        StringBuilder status)
    {
        bool anyApplied = false;
        foreach (CoSimConnection connection in map.EnabledConnections)
        {
            if (!string.Equals(connection.targetModelId, receiverModelId, StringComparison.Ordinal))
                continue;

            CoSimSignalValue value;
            string transferStatus;
            if (!signalBus.TryTransfer(connection, out value, out transferStatus))
            {
                status.Append(transferStatus).Append(". ");
                continue;
            }

            bool applied = receiver.TrySetSignal(connection.TargetKey, value);
            anyApplied |= applied;
            status.Append(transferStatus)
                  .Append(applied ? " Applied. " : " Receiver rejected. ");
        }

        return anyApplied;
    }

    private void UpdateReadOnlyDebugValues(string transferStatus)
    {
        CoSimSignalValue value;

        if (signalBus.TryGet(new CoSimSignalKey(airflowAdapter.ModelId, airflowAdapter.SensorSignalName), out value))
        {
            double real;
            if (value.TryGetReal(out real))
                latestSensorTemperatureDegC = real;
        }

        if (TryGetBusReal(debugHzModelId, debugHzVariableName, out latestHz) == false)
            latestHz = double.NaN;

        if (!TryGetBusReal(
                debugControllerSetpointModelId,
                debugControllerSetpointVariableName,
                out latestControllerSetpointDegC) &&
            !TryGetFmuReal(
                debugControllerSetpointModelId,
                debugControllerSetpointVariableName,
                out latestControllerSetpointDegC))
        {
            latestControllerSetpointDegC = double.NaN;
        }

        if (TryGetBusReal(debugPlantHzInputModelId, debugPlantHzInputVariableName, out latestPlantHzInput) == false)
            latestPlantHzInput = double.NaN;

        if (TryGetBusReal(debugDischargeModelId, debugDischargeVariableName, out latestDischargeTemperatureDegC) == false)
            latestDischargeTemperatureDegC = double.NaN;

        latestAppliedInletTemperatureDegC = airflowAdapter.LatestAppliedDischargeTemperatureDegC;
        latestTargetInletCount = airflowAdapter.TargetInletCount;
        activeFmuModelSummary = BuildFmuModelListSummary();
        latestDebugSignalSummary = BuildDebugSignalSummary();
        runtimeModeSummary = BuildRuntimeModeSummary();
        nativeFallbackActive = AnyNativeFallbackActive();
        lastStatus = string.IsNullOrEmpty(transferStatus) ? "Co-sim step completed." : transferStatus;
    }

    private bool TryGetBusReal(string modelId, string variableName, out double real)
    {
        CoSimSignalValue value;
        if (signalBus.TryGet(new CoSimSignalKey(modelId, variableName), out value))
            return value.TryGetReal(out real);

        real = 0.0;
        return false;
    }

    private bool TryGetFmuReal(string modelId, string variableName, out double real)
    {
        real = double.NaN;

        FmuCoSimulationModel model = FindFmuModel(modelId);
        return model != null && model.TryGetRealValue(variableName, out real);
    }

    private FmuCoSimulationModel FindFmuModel(string modelId)
    {
        if (fmuModels == null)
            return null;

        for (int i = 0; i < fmuModels.Count; i++)
        {
            FmuCoSimulationModel model = fmuModels[i];
            if (model != null && string.Equals(model.ModelId, modelId, StringComparison.Ordinal))
                return model;
        }

        return null;
    }

    private static void ApplyPrimaryDebugSignal(CoSimSignalReference reference, ref string modelId, ref string variableName)
    {
        if (!reference.IsConfigured)
            return;

        modelId = reference.modelId;
        variableName = reference.variableName;
    }

    private string BuildFmuModelListSummary()
    {
        if (fmuModels == null || fmuModels.Count == 0)
            return string.Empty;

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < fmuModels.Count; i++)
        {
            FmuCoSimulationModel model = fmuModels[i];
            if (model == null)
                continue;

            if (sb.Length > 0)
                sb.Append("; ");

            sb.Append(model.ModelId);
        }

        return sb.ToString();
    }

    private string BuildDebugSignalSummary()
    {
        if (coSimulationProfile == null || coSimulationProfile.DebugSignals == null || coSimulationProfile.DebugSignals.Count == 0)
            return string.Empty;

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < coSimulationProfile.DebugSignals.Count; i++)
        {
            CoSimDebugSignal signal = coSimulationProfile.DebugSignals[i];
            if (signal == null || !signal.Reference.IsConfigured)
                continue;

            double real;
            if (!TryGetSignalReal(signal.Reference, out real))
                continue;

            if (sb.Length > 0)
                sb.Append("; ");

            sb.Append(string.IsNullOrWhiteSpace(signal.label) ? signal.variableName : signal.label)
              .Append('=')
              .Append(real.ToString("G6", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private bool TryGetSignalReal(CoSimSignalReference reference, out double real)
    {
        if (TryGetBusReal(reference.modelId, reference.variableName, out real))
            return true;

        return TryGetFmuReal(reference.modelId, reference.variableName, out real);
    }
    private string BuildRuntimeModeSummary()
    {
        if (fmuModels == null || fmuModels.Count == 0)
            return "No FMU models.";

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < fmuModels.Count; i++)
        {
            FmuCoSimulationModel model = fmuModels[i];
            if (model == null)
                continue;

            if (sb.Length > 0)
                sb.Append("; ");

            sb.Append(model.ModelId).Append(":").Append(model.RuntimeMode);
        }

        return sb.Length > 0 ? sb.ToString() : "No active FMU models.";
    }

    private bool AnyNativeFallbackActive()
    {
        if (fmuModels == null)
            return false;

        for (int i = 0; i < fmuModels.Count; i++)
        {
            FmuCoSimulationModel model = fmuModels[i];
            if (model != null && model.NativeFallbackActive)
                return true;
        }

        return false;
    }

    private void WriteCsvRow()
    {
        if (csvLogger == null || airflowAdapter == null)
            return;

        SimulationResultMetrics metrics = airflowAdapter.LatestMetrics;
        CoSimulationCsvRow row = new CoSimulationCsvRow
        {
            simTimeSeconds = currentCoSimTime,
            coSimStepIndex = coSimStepIndex,
            profileName = activeProfileName,
            activeFmuModels = activeFmuModelSummary,
            sensorSource = airflowAdapter.SensorSource.ToString(),
            sensorTemperatureDegC = latestSensorTemperatureDegC,
            controllerSetpointDegC = latestControllerSetpointDegC,
            hz = latestHz,
            plantHzInput = latestPlantHzInput,
            dischargeTemperatureDegC = latestDischargeTemperatureDegC,
            appliedInletTemperatureDegC = latestAppliedInletTemperatureDegC,
            runtimeMode = runtimeModeSummary,
            debugSignals = latestDebugSignalSummary,
            status = lastStatus
        };

        if (metrics != null)
        {
            row.hasRoomAverage = metrics.hasValidRoomAverage;
            row.avgRoomTemperatureDegC = metrics.avgRoomTemperatureDegC;
            row.hasInletAverage = metrics.hasValidInletAverage;
            row.inletAverageTemperatureDegC = metrics.inletAverageTemperatureDegC;
            row.hasOutletAverage = metrics.hasValidOutletAverage;
            row.outletAverageTemperatureDegC = metrics.outletAverageTemperatureDegC;
        }

        csvLogger.WriteRow(row);
    }

    private double GetCurrentTime()
    {
        if (useLbmSimulatedTime && simulationController != null)
            return simulationController.SimulatedTimeSeconds;

        return Time.timeAsDouble;
    }

    private double GetSafeStepSize()
    {
        return Math.Max(coSimStepSizeSeconds, 1e-6);
    }
}
