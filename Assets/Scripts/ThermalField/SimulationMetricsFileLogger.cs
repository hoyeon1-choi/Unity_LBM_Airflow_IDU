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

    public enum CsvSchemaMismatchHandling
    {
        RotateOldFileAndStartFresh = 0,
        StopWriting = 1
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

    [Header("CSV Schema Safety")]
    [Tooltip("When an existing CSV header does not match the current output schema, keep the old file as a legacy backup and start a fresh CSV with the expected header.")]
    [SerializeField] private CsvSchemaMismatchHandling schemaMismatchHandling =
        CsvSchemaMismatchHandling.RotateOldFileAndStartFresh;

    [Header("Read Only")]
    [SerializeField, ReadOnly] private string resolvedBaseFolder = "";
    [SerializeField, ReadOnly] private string timeSeriesFilePath = "";
    [SerializeField, ReadOnly] private string summaryFilePath = "";
    [SerializeField, ReadOnly] private float lastWrittenSimTime = -1f;
    [SerializeField, ReadOnly] private float nextScheduledSimTime = 0f;
    [SerializeField, ReadOnly] private string lastWriteStatus = "Idle";
    [SerializeField, ReadOnly] private string csvSchemaStatus = "Not checked";
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

    public void SetExperimentTag(string tag)
    {
        experimentTag = string.IsNullOrWhiteSpace(tag) ? "experiment" : tag.Trim();
        RefreshPaths();
        RefreshHeaderStates();
        ResetWriteSchedule();
        lastWriteStatus = $"Experiment tag set to '{experimentTag}'.";
    }

    public void ForceWriteSummaryRow()
    {
        TryWriteSummaryRow();
    }

    public bool TryForceWriteTimeSeriesRow()
    {
        return TryWriteTimeSeriesRow(true);
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
        _timeSeriesHeaderWritten = HasMatchingCsvHeader(timeSeriesFilePath, BuildTimeSeriesHeader());
        _summaryHeaderWritten = HasMatchingCsvHeader(summaryFilePath, BuildSummaryHeader());
        csvSchemaStatus = BuildSchemaStatusText();
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
            string header = BuildTimeSeriesHeader();
            string row = BuildTimeSeriesRow(m);

            if (!ValidateCsvColumnCount("time-series", header, row))
                return false;

            if (!EnsureCsvReadyForAppend(timeSeriesFilePath, header, ref _timeSeriesHeaderWritten, "time-series"))
                return false;

            using (var stream = new FileStream(timeSeriesFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                if (!_timeSeriesHeaderWritten)
                {
                    writer.WriteLine(header);
                    _timeSeriesHeaderWritten = true;
                }

                writer.WriteLine(row);
            }

            lastWrittenSimTime = simTime;
            lastWriteStatus = $"Saved time-series row at t={simTime:F3}s";
            csvSchemaStatus = BuildSchemaStatusText();
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
            string header = BuildSummaryHeader();
            string row = BuildSummaryRow(m);

            if (!ValidateCsvColumnCount("summary", header, row))
                return;

            if (!EnsureCsvReadyForAppend(summaryFilePath, header, ref _summaryHeaderWritten, "summary"))
                return;

            using (var stream = new FileStream(summaryFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                if (!_summaryHeaderWritten)
                {
                    writer.WriteLine(header);
                    _summaryHeaderWritten = true;
                }

                writer.WriteLine(row);
            }

            summaryWrittenForCurrentRun = true;
            lastWriteStatus = $"Saved summary row for experiment '{experimentTag}'";
            csvSchemaStatus = BuildSchemaStatusText();
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

        if (!m.hasValidVelocityDiagnostic || !m.hasValidFlowDiagnostic || !m.hasValidDensityDiagnostic)
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
            "preset",
            "case_name",
            "tau_f_raw",
            "tau_T_raw",
            "tau_f",
            "tau_T",
            "tau_fluid_min",
            "tau_thermal_min",
            "tau_f_clamped",
            "tau_T_clamped",
            "nu_phys_target",
            "alpha_phys_target",
            "nu_phys_effective",
            "alpha_phys_effective",
            "nu_effective_ratio",
            "alpha_effective_ratio",
            "max_mach_limit",
            "room_avg_temp_degC",
            "room_min_temp_degC",
            "room_max_temp_degC",
            "room_temp_stddev_degC",
            "inlet_avg_temp_degC",
            "outlet_avg_temp_degC",
            "inlet_flow_m3ps",
            "outlet_flow_m3ps",
            "inlet_flow_cmm",
            "outlet_flow_cmm",
            "relative_flow_imbalance",
            "max_speed_phys",
            "max_mach",
            "avg_density",
            "density_stddev",
            "mass_residual_normalized",
            "stability_status",
            "readiness_status",
            "status"
        );
    }

    private string BuildSummaryHeader()
    {
        return BuildTimeSeriesHeader();
    }

    private string BuildTimeSeriesRow(SimulationResultMetrics m)
    {
        return string.Join(",",
            Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            Csv(experimentTag),
            Csv(m.stepCount.ToString(CultureInfo.InvariantCulture)),
            Csv(m.simulationTimeSeconds.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.dtPhys.ToString("E6", CultureInfo.InvariantCulture)),
            Csv(m.preset ?? ""),
            Csv(m.caseName ?? ""),
            Csv(m.tauFRaw.ToString("F8", CultureInfo.InvariantCulture)),
            Csv(m.tauTRaw.ToString("F8", CultureInfo.InvariantCulture)),
            Csv(m.tauF.ToString("F8", CultureInfo.InvariantCulture)),
            Csv(m.tauT.ToString("F8", CultureInfo.InvariantCulture)),
            Csv(m.tauFluidMin.ToString("F8", CultureInfo.InvariantCulture)),
            Csv(m.tauThermalMin.ToString("F8", CultureInfo.InvariantCulture)),
            Csv(m.tauFWasClamped ? "True" : "False"),
            Csv(m.tauTWasClamped ? "True" : "False"),
            Csv(m.nuPhysTarget.ToString("E8", CultureInfo.InvariantCulture)),
            Csv(m.alphaPhysTarget.ToString("E8", CultureInfo.InvariantCulture)),
            Csv(m.nuPhysEffective.ToString("E8", CultureInfo.InvariantCulture)),
            Csv(m.alphaPhysEffective.ToString("E8", CultureInfo.InvariantCulture)),
            Csv(m.nuPhysEffectiveRatio.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.alphaPhysEffectiveRatio.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.maxMachLimit.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.avgRoomTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.minRoomTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.maxRoomTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.roomTemperatureStdDevDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.inletAverageTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            Csv(m.outletAverageTemperatureDegC.ToString("F6", CultureInfo.InvariantCulture)),
            CsvFloat(m.hasValidFlowDiagnostic, m.inletFlowRatePhysAbs, "F6"),
            CsvFloat(m.hasValidFlowDiagnostic, m.outletFlowRatePhysAbs, "F6"),
            CsvFloat(m.hasValidFlowDiagnostic, m.inletFlowRateCMM, "F6"),
            CsvFloat(m.hasValidFlowDiagnostic, m.outletFlowRateCMM, "F6"),
            CsvFloat(m.hasValidFlowDiagnostic, m.relativeFlowImbalance, "F6"),
            CsvFloat(m.hasValidVelocityDiagnostic, m.maxSpeedPhys, "F6"),
            CsvFloat(m.hasValidVelocityDiagnostic, m.maxMach, "F6"),
            CsvFloat(m.hasValidDensityDiagnostic, m.avgDensity, "F6"),
            CsvFloat(m.hasValidDensityDiagnostic, m.densityStdDev, "F6"),
            CsvFloat(m.hasValidDensityDiagnostic, m.massResidualNormalized, "E6"),
            Csv(m.stabilityStatus ?? ""),
            Csv(m.readinessStatus ?? ""),
            Csv(m.statusMessage ?? "")
        );
    }

    private string BuildSummaryRow(SimulationResultMetrics m)
    {
        return BuildTimeSeriesRow(m);
    }

    private bool ValidateCsvColumnCount(string label, string header, string row)
    {
        int headerCount = CountCsvFields(header);
        int rowCount = CountCsvFields(row);

        if (headerCount == rowCount)
            return true;

        lastWriteStatus = $"Skipped {label} CSV write: header has {headerCount} columns but row has {rowCount}.";
        Debug.LogError(
            "[SimulationMetricsFileLogger] CSV schema error. " +
            $"{label} header columns={headerCount}, row columns={rowCount}. " +
            "The row was not written.");
        return false;
    }

    private bool EnsureCsvReadyForAppend(
        string filePath,
        string expectedHeader,
        ref bool headerWritten,
        string label)
    {
        EnsureDirectoryExists(filePath);

        if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
        {
            headerWritten = false;
            return true;
        }

        string existingHeader = ReadFirstNonEmptyLine(filePath);
        if (HeadersMatch(existingHeader, expectedHeader))
        {
            headerWritten = true;
            return true;
        }

        int existingColumnCount = CountCsvFields(existingHeader);
        int expectedColumnCount = CountCsvFields(expectedHeader);
        string message =
            $"{label} CSV header mismatch at {filePath}. " +
            $"existing columns={existingColumnCount}, expected columns={expectedColumnCount}.";

        if (schemaMismatchHandling == CsvSchemaMismatchHandling.StopWriting)
        {
            headerWritten = false;
            lastWriteStatus = $"Stopped {label} CSV write: {message}";
            csvSchemaStatus = lastWriteStatus;
            Debug.LogError($"[SimulationMetricsFileLogger] {message} Writing stopped.");
            return false;
        }

        string backupPath = BuildLegacyCsvPath(filePath);
        File.Move(filePath, backupPath);

        headerWritten = false;
        lastWriteStatus =
            $"Rotated legacy {label} CSV to {Path.GetFileName(backupPath)} and started a fresh schema.";
        csvSchemaStatus = lastWriteStatus;
        Debug.LogWarning($"[SimulationMetricsFileLogger] {message} Moved old file to {backupPath}.");
        return true;
    }

    private bool HasMatchingCsvHeader(string filePath, string expectedHeader)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        var info = new FileInfo(filePath);
        if (info.Length == 0)
            return false;

        return HeadersMatch(ReadFirstNonEmptyLine(filePath), expectedHeader);
    }

    private string BuildSchemaStatusText()
    {
        bool timeSeriesExists = File.Exists(timeSeriesFilePath) && new FileInfo(timeSeriesFilePath).Length > 0;
        bool summaryExists = File.Exists(summaryFilePath) && new FileInfo(summaryFilePath).Length > 0;

        string timeSeriesStatus = !timeSeriesExists
            ? "time-series: no file"
            : (_timeSeriesHeaderWritten ? "time-series: current schema" : "time-series: legacy/header mismatch");

        string summaryStatus = !summaryExists
            ? "summary: no file"
            : (_summaryHeaderWritten ? "summary: current schema" : "summary: legacy/header mismatch");

        return $"{timeSeriesStatus}; {summaryStatus}";
    }

    private static bool HeadersMatch(string actualHeader, string expectedHeader)
    {
        return string.Equals(
            NormalizeCsvHeader(actualHeader),
            NormalizeCsvHeader(expectedHeader),
            StringComparison.Ordinal);
    }

    private static string NormalizeCsvHeader(string header)
    {
        return (header ?? string.Empty).Trim().TrimStart('\uFEFF');
    }

    private static string ReadFirstNonEmptyLine(string filePath)
    {
        using (var reader = new StreamReader(filePath, Encoding.UTF8, true))
        {
            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                    return line;
            }
        }

        return string.Empty;
    }

    private static string BuildLegacyCsvPath(string filePath)
    {
        string directory = Path.GetDirectoryName(filePath);
        string name = Path.GetFileNameWithoutExtension(filePath);
        string extension = Path.GetExtension(filePath);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        string candidate = Path.Combine(directory, $"{name}.legacy_schema_{timestamp}{extension}");
        int suffix = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{name}.legacy_schema_{timestamp}_{suffix}{extension}");
            suffix++;
        }

        return candidate;
    }

    private static int CountCsvFields(string line)
    {
        if (string.IsNullOrEmpty(line))
            return 0;

        int count = 1;
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                count++;
            }
        }

        return count;
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

    private static string CsvFloat(bool valid, float value, string format)
    {
        return valid
            ? Csv(value.ToString(format, CultureInfo.InvariantCulture))
            : Csv("");
    }

    private static string Sanitize(string input)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            input = input.Replace(c, '_');

        return input.Replace(" ", "_");
    }
}
