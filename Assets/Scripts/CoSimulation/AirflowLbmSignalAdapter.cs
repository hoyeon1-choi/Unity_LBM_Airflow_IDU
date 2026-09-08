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

    private const string SuctionHumiditySignalName = "RH_suction";
    private const string SuctionMassFlowSignalName = "mfr_suction";
    private const string DischargeHumiditySignalName = "RH_discharge";
    private const string DischargeMassFlowSignalName = "mfr_discharge";

    [Header("LBM References")]
    [SerializeField] private SimulationController simulationController;
    [SerializeField] private SimulationResultSampler resultSampler;
    [SerializeField] private LBMZouHeBox[] inletTargets = Array.Empty<LBMZouHeBox>();

    [Header("Sensor Source")]
    [SerializeField] private SensorTemperatureSource sensorSource = SensorTemperatureSource.OutletAverageTemperatureDegC;
    [SerializeField] private float fallbackTemperatureDegC = 30.0f;
    [SerializeField, Range(0.0f, 100.0f)] private float fallbackRelativeHumidityPercent = 50.0f;
    [SerializeField, Min(0.1f)] private float airDensityKgPerM3 = 1.2f;
    [SerializeField] private bool logInvalidMetricWarning = true;

    [Header("Runtime Sync")]
    [SerializeField] private bool syncControllerAfterSet = false;

    [Header("Read-Only Status")]
    [SerializeField, ReadOnly] private float latestSensorTemperatureDegC = 0.0f;
    [SerializeField, ReadOnly] private float latestAppliedDischargeTemperatureDegC = 0.0f;
    [SerializeField, ReadOnly] private float latestRelativeHumidityPercent = 50.0f;
    [SerializeField, ReadOnly] private float latestAppliedMassFlowKgPerSecond = 0.0f;
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

    public void ConfigureFromProfile(
        CoSimulationProfile profile,
        SimulationController controller,
        SimulationResultSampler sampler,
        LBMZouHeBox[] inletTargets)
    {
        if (profile == null)
            return;

        modelId = profile.AirflowModelId;
        sensorSignalName = profile.SensorSignalName;
        dischargeSignalName = profile.DischargeSignalName;
        sensorSource = profile.SensorSource;
        fallbackTemperatureDegC = profile.FallbackTemperatureDegC;
        syncControllerAfterSet = profile.SyncControllerAfterSet;
        simulationController = controller;
        resultSampler = sampler;
        this.inletTargets = inletTargets ?? Array.Empty<LBMZouHeBox>();
        targetInletCount = CountValidInletTargets();
        targetInletNames = BuildTargetNamesText();
        lastStatus = $"Configured from co-sim profile '{profile.ProfileName}'.";
    }
    private void Awake()
    {
        latestRelativeHumidityPercent = Mathf.Clamp(fallbackRelativeHumidityPercent, 0.0f, 100.0f);
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

        bool isTemperature = IsSignal(key, sensorSignalName);
        bool isHumidity = IsSignal(key, SuctionHumiditySignalName);
        bool isMassFlow = IsSignal(key, SuctionMassFlowSignalName);
        if (!isTemperature && !isHumidity && !isMassFlow)
            return false;

        ResolveReferences();

        double simTime = simulationController != null
            ? simulationController.SimulatedTimeSeconds
            : Time.timeAsDouble;

        if (isHumidity)
        {
            value = CoSimSignalValue.FromReal(latestRelativeHumidityPercent, simTime);
            lastStatus = "Using LBM humidity proxy as RH_suction (humidity transport is not implemented).";
            return true;
        }

        if (isMassFlow)
        {
            SimulationResultMetrics metrics = LatestMetrics;
            float massFlow = metrics != null && metrics.hasValidFlowDiagnostic
                ? metrics.outletFlowRatePhysAbs * Mathf.Max(airDensityKgPerM3, 0.1f)
                : 0.0f;
            value = CoSimSignalValue.FromReal(massFlow, simTime);
            lastStatus = $"Using LBM outlet flow as mfr_suction={massFlow:F4} kg/s.";
            return true;
        }

        bool valid;
        string sourceStatus;
        float sensorTemperature = ReadSensorTemperature(out valid, out sourceStatus);
        latestSensorTemperatureDegC = sensorTemperature;
        lastStatus = sourceStatus;

        value = CoSimSignalValue.FromReal(sensorTemperature, simTime);
        return true;
    }

    public bool TrySetSignal(CoSimSignalKey key, CoSimSignalValue value)
    {
        bool isTemperature = IsSignal(key, dischargeSignalName);
        bool isHumidity = IsSignal(key, DischargeHumiditySignalName);
        bool isMassFlow = IsSignal(key, DischargeMassFlowSignalName);
        if (!isTemperature && !isHumidity && !isMassFlow)
            return false;

        double real;
        if (!value.TryGetReal(out real))
        {
            lastStatus = $"Signal {key} is not a Real value.";
            return false;
        }

        if (isHumidity)
        {
            latestRelativeHumidityPercent = Mathf.Clamp((float)real, 0.0f, 100.0f);
            lastStatus = $"Stored {key}={latestRelativeHumidityPercent:F2}% as LBM humidity proxy.";
            return true;
        }

        if (inletTargets == null || inletTargets.Length == 0)
            AutoCollectInletTargets();

        if (isMassFlow)
        {
            float totalVolumeFlow = Mathf.Max(0.0f, (float)real) / Mathf.Max(airDensityKgPerM3, 0.1f);
            float totalArea = 0.0f;
            for (int i = 0; i < inletTargets.Length; i++)
            {
                LBMZouHeBox target = inletTargets[i];
                if (target != null && target.Power && target.PatchKind == LBMZouHeBox.Kind.Inlet)
                    totalArea += Mathf.Max(target.PatchAreaPhysCached, 0.0f);
            }

            int flowTargets = 0;
            int validTargets = Mathf.Max(1, CountValidInletTargets());
            for (int i = 0; i < inletTargets.Length; i++)
            {
                LBMZouHeBox target = inletTargets[i];
                if (target == null || !target.Power || target.PatchKind != LBMZouHeBox.Kind.Inlet)
                    continue;

                float share = totalArea > 1e-8f
                    ? Mathf.Max(target.PatchAreaPhysCached, 0.0f) / totalArea
                    : 1.0f / validTargets;
                target.SetInletVolumeFlowRateM3ps(totalVolumeFlow * share, false);
                flowTargets++;
            }

            latestAppliedMassFlowKgPerSecond = Mathf.Max(0.0f, (float)real);
            lastStatus = $"Applied {key}={latestAppliedMassFlowKgPerSecond:F4} kg/s to {flowTargets} inlet target(s).";
            if (syncControllerAfterSet)
                SyncDynamicBoundaryInputsNow();
            return flowTargets > 0;
        }

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
