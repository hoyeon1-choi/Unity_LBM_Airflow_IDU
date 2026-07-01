using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class CoSimulationOrchestrator : MonoBehaviour
{
    [Header("Co-Simulation")]
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
    [SerializeField, ReadOnly] private double currentCoSimTime = 0.0;
    [SerializeField, ReadOnly] private double nextCoSimTime = 0.0;
    [SerializeField, ReadOnly] private ulong coSimStepIndex = 0;
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
    private CoSimConnectionMap runtimeDefaultConnectionMap;
    private SimulationController simulationController;
    private bool scheduleInitialized;

    public ulong CoSimStepIndex => coSimStepIndex;
    public double CurrentCoSimTime => currentCoSimTime;
    public double LatestSensorTemperatureDegC => latestSensorTemperatureDegC;
    public double LatestControllerSetpointDegC => latestControllerSetpointDegC;
    public double LatestHz => latestHz;
    public double LatestPlantHzInput => latestPlantHzInput;
    public double LatestDischargeTemperatureDegC => latestDischargeTemperatureDegC;
    public double LatestAppliedInletTemperatureDegC => latestAppliedInletTemperatureDegC;
    public string RuntimeModeSummary => runtimeModeSummary;
    public string LastStatus => lastStatus;

    private void Awake()
    {
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

    [ContextMenu("Run One Co-Sim Step Now")]
    public void RunOneStepFromInspector()
    {
        ResolveReferences();
        double time = GetCurrentTime();
        DoCoSimulationStep(time);
        scheduleInitialized = true;
        nextCoSimTime = time + GetSafeStepSize();
    }

    [ContextMenu("Auto Find References")]
    public void AutoFindReferences()
    {
        ResolveReferences(true);
    }

    [ContextMenu("Reset Co-Sim Schedule")]
    public void ResetSchedule()
    {
        scheduleInitialized = false;
        nextCoSimTime = 0.0;
        coSimStepIndex = 0;
        lastStatus = "Co-sim schedule reset.";
    }

    private void TickIfDue()
    {
        if (!enableCoSimulation)
            return;

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

        DoCoSimulationStep(currentTime);

        double step = GetSafeStepSize();
        nextCoSimTime = Math.Max(nextCoSimTime + step, currentTime + step);
    }

    private void DoCoSimulationStep(double currentTime)
    {
        try
        {
            CoSimConnectionMap map = GetActiveConnectionMap();
            EnsureFmuModels();
            SortFmuModelsForMap(map);
            EnsureModelsInitialized(currentTime);

            signalBus.Clear();
            currentCoSimTime = currentTime;
            coSimStepIndex++;

            StringBuilder status = new StringBuilder(512);
            PublishProviderSources(map, airflowAdapter.ModelId, airflowAdapter, status);

            for (int i = 0; i < fmuModels.Count; i++)
            {
                FmuCoSimulationModel model = fmuModels[i];
                if (model == null)
                    continue;

                TransferConnectionsToModel(map, model, status);
                model.DoStep(currentTime, GetSafeStepSize());
                PublishModelOutputs(map, model, status);
            }

            bool appliedToAirflow = TransferConnectionsToReceiver(map, airflowAdapter.ModelId, airflowAdapter, status);
            if (appliedToAirflow)
                airflowAdapter.SyncDynamicBoundaryInputsNow();

            UpdateReadOnlyDebugValues(status.ToString());
            WriteCsvRow();

            if (logEveryCoSimStep)
            {
                Debug.Log(
                    $"[CoSimulation] step={coSimStepIndex}, t={currentCoSimTime:F3}s, " +
                    $"T_sensor={latestSensorTemperatureDegC:F3}C, T_set={latestControllerSetpointDegC:F3}C, " +
                    $"Hz={latestHz:F3}, plantHz={latestPlantHzInput:F3}, " +
                    $"T_dis={latestDischargeTemperatureDegC:F3}C, applied={latestAppliedInletTemperatureDegC:F3}C, " +
                    $"targets={latestTargetInletCount}, runtime={runtimeModeSummary}, status={lastStatus}");
            }
        }
        catch (Exception ex)
        {
            lastStatus = $"Co-sim step failed: {ex.Message}";
            Debug.LogWarning($"[CoSimulation] {lastStatus}");
        }
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

    private void EnsureModelsInitialized(double currentTime)
    {
        double step = GetSafeStepSize();
        for (int i = 0; i < fmuModels.Count; i++)
        {
            FmuCoSimulationModel model = fmuModels[i];
            if (model != null && !model.IsInitialized)
                model.Initialize(currentTime, 0.0, step);
        }
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

            model.SetInput(connection.targetVariableName, value);
            status.Append(transferStatus).Append(". ");
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
            CoSimSignalKey key = connection.SourceKey;
            signalBus.Publish(key, value);
            status.Append($"Published {key}={value}. ");
        }
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

        if (TryGetFmuReal(debugControllerSetpointModelId, debugControllerSetpointVariableName, out latestControllerSetpointDegC) == false)
            latestControllerSetpointDegC = double.NaN;

        if (TryGetBusReal(debugPlantHzInputModelId, debugPlantHzInputVariableName, out latestPlantHzInput) == false)
            latestPlantHzInput = double.NaN;

        if (TryGetBusReal(debugDischargeModelId, debugDischargeVariableName, out latestDischargeTemperatureDegC) == false)
            latestDischargeTemperatureDegC = double.NaN;

        latestAppliedInletTemperatureDegC = airflowAdapter.LatestAppliedDischargeTemperatureDegC;
        latestTargetInletCount = airflowAdapter.TargetInletCount;
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
            sensorSource = airflowAdapter.SensorSource.ToString(),
            sensorTemperatureDegC = latestSensorTemperatureDegC,
            controllerSetpointDegC = latestControllerSetpointDegC,
            hz = latestHz,
            plantHzInput = latestPlantHzInput,
            dischargeTemperatureDegC = latestDischargeTemperatureDegC,
            appliedInletTemperatureDegC = latestAppliedInletTemperatureDegC,
            runtimeMode = runtimeModeSummary,
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
