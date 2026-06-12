using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class SimulationMetricsFileLogger : MonoBehaviour
{
    public enum SaveLocationMode
    {
        PersistentDataPath = 0,
        ProjectRoot = 1,
        CustomFolder = 2
    }

    [Header("References")]
    [SerializeField] private SimulationController simulationController;
    [SerializeField] private SimulationResultSampler resultSampler;

    [Header("Logging")]
    [SerializeField] private bool enableTimeSeriesLogging = true;
    [SerializeField] private bool enableSummaryLogging = true;
    [SerializeField] private string experimentTag = "baseline";

    [Tooltip("Write interval in simulated seconds.")]
    [SerializeField] private float writeIntervalSeconds = 2.0f;

    [SerializeField] private bool writeOnlyWhenTimeAdvanced = true;
    [SerializeField] private bool autoWriteSummaryWhenSimulationStops = true;
    [SerializeField] private bool skipInvalidMetricRows = true;
    [SerializeField] private bool skipRowsWhenSamplerTimeDoesNotMatch = true;
    [SerializeField] private float samplerTimeToleranceSeconds = 0.25f;

    [Header("Save Location")]
    [SerializeField] private SaveLocationMode saveLocationMode = SaveLocationMode.PersistentDataPath;
    [SerializeField] private string resultsFolderName = "LBMResults";
    [SerializeField] private string customFolderPath = "";

    [Header("File Names")]
    [SerializeField] private string timeSeriesPrefix = "lbm_timeseries";
    [SerializeField] private string summaryFilePrefix = "lbm_summary";

    [Header("Read Only")]
    [SerializeField, ReadOnly] private string resolvedBaseFolder = "";
    [SerializeField, ReadOnly] private string timeSeriesFilePath = "";
    [SerializeField, ReadOnly] private string summaryFilePath = "";
    [SerializeField, ReadOnly] private float lastWrittenSimTime = -1f;
    [SerializeField, ReadOnly] private float nextScheduledSimTime = 0f;
    [SerializeField, ReadOnly] private string lastWriteStatus = "Idle";
    [SerializeField, ReadOnly] private bool summaryWrittenForCurrentRun = false;

    private bool _timeSeriesHeaderWritten = false;
    private bool _summaryHeaderWritten = false;
    private bool _previousSimulationRunning = false;

    private void Awake()
    {
        if (simulationController == null)
            simulationController = FindFirstObjectByType<SimulationController>();

        if (resultSampler == null)
            resultSampler = FindFirstObjectByType<SimulationResultSampler>();

        RefreshPaths();
        RefreshHeaderStates();
        ResetWriteSchedule();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RefreshPaths();
        RefreshHeaderStates();
    }
#endif

    private void Update()
    {
        if (simulationController == null || resultSampler == null)
            return;

        RefreshPaths();

        bool isRunning = simulationController.IsSimulationRunning;

        if (enableTimeSeriesLogging && isRunning)
        {
            float simTime = simulationController.SimulatedTimeSeconds;

            if (simTime + 1e-6f >= nextScheduledSimTime)
            {
                if (TryWriteTimeSeriesRow())
                    nextScheduledSimTime += Mathf.Max(writeIntervalSeconds, 1e-6f);
            }
        }

        if (autoWriteSummaryWhenSimulationStops && _previousSimulationRunning && !isRunning)
        {
            if (enableSummaryLogging && !summaryWrittenForCurrentRun)
                TryWriteSummaryRow();
        }

        if (isRunning)
            summaryWrittenForCurrentRun = false;

        _previousSimulationRunning = isRunning;
    }

    private void OnDisable()
    {
        if (enableSummaryLogging && !summaryWrittenForCurrentRun)
            TryWriteSummaryRow();
    }

    private void OnApplicationQuit()
    {
        if (enableSummaryLogging && !summaryWrittenForCurrentRun)
            TryWriteSummaryRow();
    }

    [ContextMenu("Reset Write Schedule")]
    public void ResetWriteSchedule()
    {
        lastWrittenSimTime = -1f;

        if (simulationController != null)
            nextScheduledSimTime = simulationController.SimulatedTimeSeconds + Mathf.Max(writeIntervalSeconds, 1e-6f);
        else
            nextScheduledSimTime = Mathf.Max(writeIntervalSeconds, 1e-6f);
    }

    private void RefreshPaths()
    {
        resolvedBaseFolder = ResolveBaseFolder();

        string safeTag = string.IsNullOrWhiteSpace(experimentTag)
            ? "experiment"
            : Sanitize(experimentTag);

        timeSeriesFilePath = Path.Combine(resolvedBaseFolder, $"{timeSeriesPrefix}_{safeTag}.csv");
        summaryFilePath = Path.Combine(resolvedBaseFolder, $"{summaryFilePrefix}_{safeTag}.csv");
    }

    private string ResolveBaseFolder()
    {
        switch (saveLocationMode)
        {
            case SaveLocationMode.ProjectRoot:
                return Path.Combine(Directory.GetParent(Application.dataPath).FullName, resultsFolderName);

            case SaveLocationMode.CustomFolder:
                return string.IsNullOrWhiteSpace(customFolderPath)
                    ? Path.Combine(Application.persistentDataPath, resultsFolderName)
                    : customFolderPath;

            case SaveLocationMode.PersistentDataPath:
            default:
                return Path.Combine(Application.persistentDataPath, resultsFolderName);
        }
    }

    private void RefreshHeaderStates()
    {
        _timeSeriesHeaderWritten = File.Exists(timeSeriesFilePath) && new FileInfo(timeSeriesFilePath).Length > 0;
        _summaryHeaderWritten = File.Exists(summaryFilePath) && new FileInfo(summaryFilePath).Length > 0;
    }

    private bool TryWriteTimeSeriesRow(bool forceWrite = false)
    {
        SimulationResultMetrics m = resultSampler.LatestMetrics;
        if (m == null)
        {
            lastWriteStatus = "No metrics available for time-series row.";
            return false;
        }

        float simTime = simulationController.SimulatedTimeSeconds;
        if (!forceWrite && writeOnlyWhenTimeAdvanced && simTime <= lastWrittenSimTime)
        {
            lastWriteStatus = "Skipped time-series write: simulated time did not advance.";
            return false;
        }

        if (!forceWrite && skipInvalidMetricRows && !IsMetricRowWritable(m))
        {
            lastWriteStatus = $"Skipped invalid metric row: {m.statusMessage}";
            return false;
        }

        if (!forceWrite && skipRowsWhenSamplerTimeDoesNotMatch && !IsSamplerTimeCloseEnough(m.statusMessage, simTime))
        {
            lastWriteStatus =
                $"Skipped stale metric row: controller={simTime:F3}s, status=\"{m.statusMessage}\"";
            return false;
        }

        try
        {
            EnsureDirectoryExists(timeSeriesFilePath);

            using (var stream = new FileStream(timeSeriesFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                if (!_timeSeriesHeaderWritten)
                {
                    writer.WriteLine(BuildTimeSeriesHeader());
                    _timeSeriesHeaderWritten = true;
                }

                writer.WriteLine(BuildTimeSeriesRow(m));
            }

            lastWrittenSimTime = simTime;
            lastWriteStatus = $"Saved time-series row at t={simTime:F3}s";
            return true;
        }
        catch (Exception ex)
        {
            lastWriteStatus = $"Time-series write failed: {ex.Message}";
            Debug.LogError($"[SimulationMetricsFileLogger] Time-series CSV write failed: {ex}");
            return false;
        }
    }

    private void TryWriteSummaryRow()
    {
        SimulationResultMetrics m = resultSampler.LatestMetrics;
        if (m == null)
        {
            lastWriteStatus = "No metrics available for summary row.";
            return;
        }

        if (skipInvalidMetricRows && !IsMetricRowWritable(m))
        {
            lastWriteStatus = $"Skipped summary row: invalid metrics ({m.statusMessage})";
            return;
        }

        try
        {
            EnsureDirectoryExists(summaryFilePath);

            using (var stream = new FileStream(summaryFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                if (!_summaryHeaderWritten)
                {
                    writer.WriteLine(BuildSummaryHeader());
                    _summaryHeaderWritten = true;
                }

                writer.WriteLine(BuildSummaryRow(m));
            }

            summaryWrittenForCurrentRun = true;
            lastWriteStatus = $"Saved summary row for experiment '{experimentTag}'";
        }
        catch (Exception ex)
        {
            lastWriteStatus = $"Summary write failed: {ex.Message}";
            Debug.LogError($"[SimulationMetricsFileLogger] Summary CSV write failed: {ex}");
        }
    }

    private bool IsMetricRowWritable(SimulationResultMetrics m)
    {
        if (m == null)
            return false;

        if (!m.hasValidRoomAverage || !m.hasValidInletAverage || !m.hasValidOutletAverage)
            return false;

        string status = m.statusMessage ?? string.Empty;

        if (string.IsNullOrWhiteSpace(status))
            return false;
        if (status.Contains("null", StringComparison.OrdinalIgnoreCase)) return false;
        if (status.Contains("not ready", StringComparison.OrdinalIgnoreCase)) return false;
        if (status.Contains("failed", StringComparison.OrdinalIgnoreCase)) return false;
        if (status.Contains("empty", StringComparison.OrdinalIgnoreCase)) return false;
        if (status.Contains("No sampled data", StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    private bool IsSamplerTimeCloseEnough(string statusMessage, float controllerSimTime)
    {
        if (string.IsNullOrWhiteSpace(statusMessage))
            return false;

        const string token = "Sampled at t=";
        int start = statusMessage.IndexOf(token, StringComparison.Ordinal);
        if (start < 0)
            return false;

        start += token.Length;
        int end = statusMessage.IndexOf('s', start);
        if (end < 0)
            end = statusMessage.Length;

        string valueText = statusMessage.Substring(start, end - start).Trim();

        if (!float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float sampledTime))
            return false;

        return Mathf.Abs(sampledTime - controllerSimTime) <= Mathf.Max(samplerTimeToleranceSeconds, 1e-6f);
    }

    private string BuildTimeSeriesHeader()
    {
        return string.Join(",",
            "timestamp_local",
            "experiment_tag",
            "step_count",
            "sim_time_sec",
            "dt_phys",
            "graph_inlet_temp_degC",
            "graph_outlet_temp_degC",
            "graph_avg_room_temp_degC",
            "room_temp_stddev_degC",
            "outlet_temp_stddev_degC",
            "inlet_avg_speed_phys",
            "outlet_avg_speed_phys",
            "inlet_normal_speed_phys",
            "outlet_normal_speed_phys",
            "inlet_flow_signed_m3ps",
            "outlet_flow_signed_m3ps",
            "inlet_flow_abs_m3ps",
            "outlet_flow_abs_m3ps",
            "net_flow_signed_m3ps",
            "relative_flow_imbalance",
            "thermal_inlet_clamp_count",
            "thermal_outlet_clamp_count",
            "fluid_inlet_clamp_count",
            "fluid_outlet_clamp_count",
            "max_speed_phys",
            "avg_density",
            "density_stddev",
            "mass_residual_normalized",
            "avg_kinetic_energy_lat",
            "fluid_cell_count",
            "rack_cell_count",
            "inlet_sample_count",
            "outlet_sample_count",
            "status"
        );
    }

    private string BuildSummaryHeader()
    {
        return string.Join(",",
            "timestamp_local",
            "experiment_tag",
            "collision_model",
            "turbulence_model",
            "turbulence_model_constant",
            "turbulent_prandtl",
            "wall_function_enabled",
            "step_count",
            "sim_time_sec",
            "dt_phys",
            "grid_nx",
            "grid_ny",
            "grid_nz",
            "avg_room_temp_degC",
            "min_room_temp_degC",
            "max_room_temp_degC",
            "room_temp_stddev_degC",
            "inlet_avg_temp_degC",
            "outlet_avg_temp_degC",
            "outlet_temp_stddev_degC",
            "inlet_avg_speed_phys",
            "outlet_avg_speed_phys",
            "inlet_normal_speed_phys",
            "outlet_normal_speed_phys",
            "inlet_flow_signed_m3ps",
            "outlet_flow_signed_m3ps",
            "inlet_flow_abs_m3ps",
            "outlet_flow_abs_m3ps",
            "net_flow_signed_m3ps",
            "relative_flow_imbalance",
            "thermal_inlet_clamp_count",
            "thermal_outlet_clamp_count",
            "fluid_inlet_clamp_count",
            "fluid_outlet_clamp_count",
            "max_speed_phys",
            "avg_density",
            "density_stddev",
            "mass_residual_normalized",
            "avg_kinetic_energy_lat",
            "fluid_cell_count",
            "rack_cell_count",
            "inlet_sample_count",
            "outlet_sample_count",
            "status"
        );
    }

    private string BuildTimeSeriesRow(SimulationResultMetrics m)
    {
        return string.Join(",",
            Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            Csv(experimentTag),
            Csv(simulationController.StepCount.ToString(CultureInfo.InvariantCulture)),
            Csv(simulationController.SimulatedTimeSeconds.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(simulationController.DtPhys.ToString("E6", CultureInfo.InvariantCulture)),
            Csv(m.inletAverageTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletAverageTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.avgRoomTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.roomTemperatureStdDevDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletTemperatureStdDevDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.inletAverageSpeedPhys.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletAverageSpeedPhys.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.inletAverageNormalSpeedPhys.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletAverageNormalSpeedPhys.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.inletFlowRatePhysSigned.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletFlowRatePhysSigned.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.inletFlowRatePhysAbs.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletFlowRatePhysAbs.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.netFlowRatePhysSigned.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.relativeFlowImbalance.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.thermalInletClampCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.thermalOutletClampCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.fluidInletClampCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.fluidOutletClampCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.maxSpeedPhys.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.avgDensity.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.densityStdDev.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.massResidualNormalized.ToString("E6", CultureInfo.InvariantCulture)),
            Csv(m.averageKineticEnergyLat.ToString("E6", CultureInfo.InvariantCulture)),
            Csv(m.fluidCellCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.rackCellCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.inletSampleCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.outletSampleCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.statusMessage ?? "")
        );
    }

    private string BuildSummaryRow(SimulationResultMetrics m)
    {
        return string.Join(",",
            Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            Csv(experimentTag),
            Csv(simulationController.CollisionModelName),
            Csv(simulationController.TurbulenceModelName),
            Csv(simulationController.TurbulenceModelConstant.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(simulationController.TurbulentPrandtl.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(simulationController.WallFunctionEnabled.ToString()),
            Csv(simulationController.StepCount.ToString(CultureInfo.InvariantCulture)),
            Csv(simulationController.SimulatedTimeSeconds.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(simulationController.DtPhys.ToString("E6", CultureInfo.InvariantCulture)),
            Csv(simulationController.Nx.ToString(CultureInfo.InvariantCulture)),
            Csv(simulationController.Ny.ToString(CultureInfo.InvariantCulture)),
            Csv(simulationController.Nz.ToString(CultureInfo.InvariantCulture)),
            Csv(m.avgRoomTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.minRoomTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.maxRoomTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.roomTemperatureStdDevDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.inletAverageTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletAverageTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletTemperatureStdDevDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.inletAverageSpeedPhys.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletAverageSpeedPhys.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.inletAverageNormalSpeedPhys.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletAverageNormalSpeedPhys.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.inletFlowRatePhysSigned.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletFlowRatePhysSigned.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.inletFlowRatePhysAbs.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletFlowRatePhysAbs.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.netFlowRatePhysSigned.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.relativeFlowImbalance.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.thermalInletClampCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.thermalOutletClampCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.fluidInletClampCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.fluidOutletClampCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.maxSpeedPhys.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.avgDensity.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.densityStdDev.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.massResidualNormalized.ToString("E6", CultureInfo.InvariantCulture)),
            Csv(m.averageKineticEnergyLat.ToString("E6", CultureInfo.InvariantCulture)),
            Csv(m.fluidCellCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.rackCellCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.inletSampleCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.outletSampleCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.statusMessage ?? "")
        );
    }

    private void EnsureDirectoryExists(string filePath)
    {
        string dir = Path.GetDirectoryName(filePath);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static string Csv(string s)
    {
        if (s == null)
            return "\"\"";

        string escaped = s.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static string Sanitize(string input)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            input = input.Replace(c, '_');

        return input.Replace(" ", "_");
    }
}