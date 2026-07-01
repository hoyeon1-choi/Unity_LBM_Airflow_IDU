using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public struct CoSimulationCsvRow
{
    public double simTimeSeconds;
    public ulong coSimStepIndex;
    public string sensorSource;
    public double sensorTemperatureDegC;
    public double controllerSetpointDegC;
    public double hz;
    public double plantHzInput;
    public double dischargeTemperatureDegC;
    public double appliedInletTemperatureDegC;
    public bool hasRoomAverage;
    public float avgRoomTemperatureDegC;
    public bool hasInletAverage;
    public float inletAverageTemperatureDegC;
    public bool hasOutletAverage;
    public float outletAverageTemperatureDegC;
    public string runtimeMode;
    public string status;
}

public class CoSimulationCsvLogger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SimulationMetricsFileLogger metricsFileLogger;

    [Header("Logging")]
    [SerializeField] private bool enableLogging = true;
    [SerializeField] private string filePrefix = "co_simulation";
    [SerializeField] private bool flushEveryRow = true;

    [Header("Read-Only Status")]
    [SerializeField, ReadOnly] private string resolvedDirectory = string.Empty;
    [SerializeField, ReadOnly] private string currentFilePath = string.Empty;
    [SerializeField, ReadOnly] private int rowsWritten = 0;
    [SerializeField, ReadOnly] private string lastStatus = "Not opened.";

    private StreamWriter writer;
    private string activeDirectory = string.Empty;

    public string CurrentFilePath => currentFilePath;
    public int RowsWritten => rowsWritten;
    public string LastStatus => lastStatus;

    private void Awake()
    {
        ResolveReferences();
    }

    public void WriteRow(CoSimulationCsvRow row)
    {
        if (!enableLogging)
            return;

        if (!EnsureWriter())
            return;

        writer.WriteLine(BuildRow(row));
        rowsWritten++;
        lastStatus = $"Rows written: {rowsWritten}";

        if (flushEveryRow)
            writer.Flush();
    }

    [ContextMenu("Close Current CSV")]
    public void Close()
    {
        if (writer != null)
        {
            writer.Flush();
            writer.Dispose();
            writer = null;
        }

        activeDirectory = string.Empty;
        lastStatus = "Closed.";
    }

    [ContextMenu("Start New CSV")]
    public void StartNewFile()
    {
        Close();
        currentFilePath = string.Empty;
        rowsWritten = 0;
        EnsureWriter();
    }

    private void OnDestroy()
    {
        Close();
    }

    private bool EnsureWriter()
    {
        ResolveReferences();

        string directory = ResolveLogDirectory();
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        if (writer != null && string.Equals(activeDirectory, directory, StringComparison.OrdinalIgnoreCase))
            return true;

        if (writer != null)
            Close();

        Directory.CreateDirectory(directory);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        currentFilePath = Path.Combine(directory, $"{SanitizeFileName(filePrefix)}_{stamp}.csv");
        writer = new StreamWriter(currentFilePath, false, new UTF8Encoding(false));
        writer.WriteLine(
            "timestamp_local,sim_time_sec,co_sim_step_index,sensor_source," +
            "T_sensor_degC,controller_T_set_degC,Hz,plant_hz_input,T_dis_Plant_degC,applied_inlet_temp_degC," +
            "avg_room_temp_degC,inlet_avg_temp_degC,outlet_avg_temp_degC,runtime_mode,status");
        writer.Flush();

        rowsWritten = 0;
        activeDirectory = directory;
        lastStatus = $"Opened: {currentFilePath}";
        return true;
    }

    private void ResolveReferences()
    {
        if (metricsFileLogger == null)
            metricsFileLogger = FindFirstObjectByType<SimulationMetricsFileLogger>();
    }

    private string ResolveLogDirectory()
    {
        if (metricsFileLogger != null)
        {
            string metricsFolder = metricsFileLogger.ResolvedBaseFolder;
            if (!string.IsNullOrWhiteSpace(metricsFolder))
            {
                resolvedDirectory = metricsFolder;
                return resolvedDirectory;
            }
        }

        resolvedDirectory = string.Empty;
        lastStatus = "SimulationMetricsFileLogger is missing; co-sim CSV disabled to keep log location aligned.";
        return string.Empty;
    }

    private static string BuildRow(CoSimulationCsvRow row)
    {
        return string.Join(",",
            Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
            Number(row.simTimeSeconds),
            row.coSimStepIndex.ToString(CultureInfo.InvariantCulture),
            Csv(row.sensorSource),
            Number(row.sensorTemperatureDegC),
            Number(row.controllerSetpointDegC),
            Number(row.hz),
            Number(row.plantHzInput),
            Number(row.dischargeTemperatureDegC),
            Number(row.appliedInletTemperatureDegC),
            MaybeNumber(row.hasRoomAverage, row.avgRoomTemperatureDegC),
            MaybeNumber(row.hasInletAverage, row.inletAverageTemperatureDegC),
            MaybeNumber(row.hasOutletAverage, row.outletAverageTemperatureDegC),
            Csv(row.runtimeMode),
            Csv(row.status));
    }

    private static string MaybeNumber(bool valid, float value)
    {
        return valid ? Number(value) : string.Empty;
    }

    private static string Number(double value)
    {
        return value.ToString("G9", CultureInfo.InvariantCulture);
    }

    private static string Csv(string text)
    {
        text = text ?? string.Empty;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static string SanitizeFileName(string text)
    {
        if (string.IsNullOrEmpty(text))
            text = "co_simulation";

        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            text = text.Replace(invalid[i], '_');

        return text;
    }
}
