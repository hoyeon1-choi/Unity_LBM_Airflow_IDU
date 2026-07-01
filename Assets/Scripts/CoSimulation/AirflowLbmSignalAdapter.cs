using System;
using System.Collections.Generic;
using UnityEngine;

public class AirflowLbmSignalAdapter : MonoBehaviour, ICoSimSignalProvider, ICoSimSignalReceiver
{
    public enum SensorTemperatureSource
    {
        OutletAverageTemperatureDegC,
        RoomAverageTemperatureDegC,
        InletAverageTemperatureDegC
    }

    [Header("Signal Names")]
    [SerializeField] private string modelId = "airflow";
    [SerializeField] private string sensorSignalName = "T_sensor";
    [SerializeField] private string dischargeSignalName = "T_discharge";

    [Header("LBM References")]
    [SerializeField] private SimulationController simulationController;
    [SerializeField] private SimulationResultSampler resultSampler;
    [SerializeField] private LBMZouHeBox[] inletTargets = Array.Empty<LBMZouHeBox>();

    [Header("Sensor Source")]
    [SerializeField] private SensorTemperatureSource sensorSource = SensorTemperatureSource.OutletAverageTemperatureDegC;
    [SerializeField] private float fallbackTemperatureDegC = 30.0f;
    [SerializeField] private bool logInvalidMetricWarning = true;

    [Header("Runtime Sync")]
    [SerializeField] private bool syncControllerAfterSet = false;

    [Header("Read-Only Status")]
    [SerializeField, ReadOnly] private float latestSensorTemperatureDegC = 0.0f;
    [SerializeField, ReadOnly] private float latestAppliedDischargeTemperatureDegC = 0.0f;
    [SerializeField, ReadOnly] private int targetInletCount = 0;
    [SerializeField, ReadOnly] private string targetInletNames = string.Empty;
    [SerializeField, ReadOnly] private string lastStatus = "Not initialized.";

    private bool warnedInvalidMetrics;

    public string ModelId => string.IsNullOrEmpty(modelId) ? "airflow" : modelId;
    public string SensorSignalName => sensorSignalName;
    public string DischargeSignalName => dischargeSignalName;
    public SensorTemperatureSource SensorSource => sensorSource;
    public float LatestSensorTemperatureDegC => latestSensorTemperatureDegC;
    public float LatestAppliedDischargeTemperatureDegC => latestAppliedDischargeTemperatureDegC;
    public int TargetInletCount => targetInletCount;
    public string LastStatus => lastStatus;
    public SimulationResultMetrics LatestMetrics => resultSampler != null ? resultSampler.LatestMetrics : null;

    private void Awake()
    {
        ResolveReferences();
        if (inletTargets == null || inletTargets.Length == 0)
            AutoCollectInletTargets();
    }

    private void OnValidate()
    {
        targetInletCount = CountValidInletTargets();
        targetInletNames = BuildTargetNamesText();
    }

    public bool TryGetSignal(CoSimSignalKey key, out CoSimSignalValue value)
    {
        value = default;

        if (!IsSignal(key, sensorSignalName))
            return false;

        ResolveReferences();

        bool valid;
        string sourceStatus;
        float sensorTemperature = ReadSensorTemperature(out valid, out sourceStatus);
        latestSensorTemperatureDegC = sensorTemperature;
        lastStatus = sourceStatus;

        double simTime = simulationController != null
            ? simulationController.SimulatedTimeSeconds
            : Time.timeAsDouble;

        value = CoSimSignalValue.FromReal(sensorTemperature, simTime);
        return true;
    }

    public bool TrySetSignal(CoSimSignalKey key, CoSimSignalValue value)
    {
        if (!IsSignal(key, dischargeSignalName))
            return false;

        double real;
        if (!value.TryGetReal(out real))
        {
            lastStatus = $"Signal {key} is not a Real value.";
            return false;
        }

        if (inletTargets == null || inletTargets.Length == 0)
            AutoCollectInletTargets();

        int applied = 0;
        for (int i = 0; i < inletTargets.Length; i++)
        {
            LBMZouHeBox target = inletTargets[i];
            if (target == null || !target.Power || target.PatchKind != LBMZouHeBox.Kind.Inlet)
                continue;

            target.SetInletTemperatureDegC((float)real, false);
            applied++;
        }

        latestAppliedDischargeTemperatureDegC = (float)real;
        targetInletCount = applied;
        targetInletNames = BuildTargetNamesText();
        lastStatus = $"Applied {key}={real:F3} degC to {applied} inlet target(s).";

        if (syncControllerAfterSet)
            SyncDynamicBoundaryInputsNow();

        return applied > 0;
    }

    public void SyncDynamicBoundaryInputsNow()
    {
        ResolveReferences();
        if (simulationController == null)
        {
            lastStatus = "SimulationController is missing; solver sync skipped.";
            return;
        }

        simulationController.SyncDynamicBoundaryInputsNow();
    }

    [ContextMenu("Auto Collect Inlet Targets")]
    public void AutoCollectInletTargets()
    {
        LBMZouHeBox[] boxes = FindObjectsByType<LBMZouHeBox>(FindObjectsSortMode.InstanceID);
        List<LBMZouHeBox> inlets = new List<LBMZouHeBox>();

        for (int i = 0; i < boxes.Length; i++)
        {
            LBMZouHeBox box = boxes[i];
            if (box != null && box.Power && box.PatchKind == LBMZouHeBox.Kind.Inlet)
                inlets.Add(box);
        }

        inletTargets = inlets.ToArray();
        targetInletCount = inletTargets.Length;
        targetInletNames = BuildTargetNamesText();
        lastStatus = $"Collected {targetInletCount} inlet target(s).";
    }

    private void ResolveReferences()
    {
        if (simulationController == null)
            simulationController = SimulationController.Instance != null
                ? SimulationController.Instance
                : FindFirstObjectByType<SimulationController>();

        if (resultSampler == null && simulationController != null)
            resultSampler = simulationController.GetComponent<SimulationResultSampler>();

        if (resultSampler == null)
            resultSampler = FindFirstObjectByType<SimulationResultSampler>();
    }

    private bool IsSignal(CoSimSignalKey key, string variableName)
    {
        return string.Equals(key.modelId, ModelId, StringComparison.Ordinal) &&
               string.Equals(key.variableName, variableName, StringComparison.Ordinal);
    }

    private float ReadSensorTemperature(out bool valid, out string status)
    {
        valid = false;
        SimulationResultMetrics metrics = resultSampler != null ? resultSampler.LatestMetrics : null;

        if (metrics != null)
        {
            switch (sensorSource)
            {
                case SensorTemperatureSource.OutletAverageTemperatureDegC:
                    if (metrics.hasValidOutletAverage)
                    {
                        valid = true;
                        status = "Using outlet average temperature as T_sensor.";
                        return metrics.outletAverageTemperatureDegC;
                    }
                    break;

                case SensorTemperatureSource.RoomAverageTemperatureDegC:
                    if (metrics.hasValidRoomAverage)
                    {
                        valid = true;
                        status = "Using room average temperature as T_sensor.";
                        return metrics.avgRoomTemperatureDegC;
                    }
                    break;

                case SensorTemperatureSource.InletAverageTemperatureDegC:
                    if (metrics.hasValidInletAverage)
                    {
                        valid = true;
                        status = "Using inlet average temperature as T_sensor.";
                        return metrics.inletAverageTemperatureDegC;
                    }
                    break;
            }

            if (metrics.hasValidRoomAverage)
            {
                status = $"Sensor source {sensorSource} is invalid; using room average fallback.";
                WarnInvalidMetric(status);
                return metrics.avgRoomTemperatureDegC;
            }
        }

        status = $"Sensor source {sensorSource} is invalid; using configured fallback temperature.";
        WarnInvalidMetric(status);
        return fallbackTemperatureDegC;
    }

    private void WarnInvalidMetric(string message)
    {
        if (!logInvalidMetricWarning || warnedInvalidMetrics)
            return;

        warnedInvalidMetrics = true;
        Debug.LogWarning($"[CoSimulation][{ModelId}] {message}");
    }

    private int CountValidInletTargets()
    {
        if (inletTargets == null)
            return 0;

        int count = 0;
        for (int i = 0; i < inletTargets.Length; i++)
        {
            LBMZouHeBox target = inletTargets[i];
            if (target != null && target.Power && target.PatchKind == LBMZouHeBox.Kind.Inlet)
                count++;
        }

        return count;
    }

    private string BuildTargetNamesText()
    {
        if (inletTargets == null || inletTargets.Length == 0)
            return "-";

        List<string> names = new List<string>();
        for (int i = 0; i < inletTargets.Length; i++)
        {
            LBMZouHeBox target = inletTargets[i];
            if (target != null && target.Power && target.PatchKind == LBMZouHeBox.Kind.Inlet)
                names.Add(target.name);
        }

        return names.Count > 0 ? string.Join(", ", names) : "-";
    }
}
