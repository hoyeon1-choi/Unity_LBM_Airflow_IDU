using TMPro;
using UnityEngine;

public class TemperatureGraphPresenter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SimulationController simulationController;
    [SerializeField] private SimulationResultSampler resultSampler;
    [SerializeField] private TemperatureGraphGraphic graph;

    [Header("Text References")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text inletLegendText;
    [SerializeField] private TMP_Text outletLegendText;
    [SerializeField] private TMP_Text avgLegendText;

    [SerializeField] private TMP_Text yMinText;
    [SerializeField] private TMP_Text yMidText;
    [SerializeField] private TMP_Text yMaxText;

    [SerializeField] private TMP_Text xMinText;
    [SerializeField] private TMP_Text xMidText;
    [SerializeField] private TMP_Text xMaxText;

    [SerializeField] private TMP_Text xAxisTitleText;
    [SerializeField] private TMP_Text yAxisTitleText;
    [SerializeField] private TMP_Text currentValuesText;

    [Header("Sampling")]
    [SerializeField] private float graphSampleIntervalSeconds = 1.0f;
    [SerializeField] private bool ignoreInvalidSamples = true;
    [SerializeField] private bool requireSamplerTimeMatch = true;
    [SerializeField] private float samplerTimeToleranceSeconds = 0.25f;

    private float _lastSampledTime = -1f;
    private float _nextGraphSampleTime = 0f;

    private void Awake()
    {
        if (simulationController == null)
            simulationController = FindFirstObjectByType<SimulationController>();

        if (resultSampler == null)
            resultSampler = FindFirstObjectByType<SimulationResultSampler>();

        if (graph == null)
            graph = GetComponentInChildren<TemperatureGraphGraphic>();
    }

    private void Start()
    {
        if (titleText != null)
            titleText.text = "Averaged Room Temperature";

        if (inletLegendText != null)
        {
            inletLegendText.text = "Inlet Temperature";
            inletLegendText.color = Color.blue;
        }

        if (outletLegendText != null)
        {
            outletLegendText.text = "Outlet Temperature";
            outletLegendText.color = Color.red;
        }

        if (avgLegendText != null)
        {
            avgLegendText.text = "Averaged Room Temperature";
            avgLegendText.color = Color.black;
        }

        if (xAxisTitleText != null)
            xAxisTitleText.text = "Actual Time (sec)";

        if (yAxisTitleText != null)
            yAxisTitleText.text = "Actual Temperature (°C)";

        ResetGraphSamplingSchedule();
        UpdateAxisLabels();
        UpdateCurrentValueText(0f, 0f, 0f, 0f);
    }

    private void Update()
    {
        if (simulationController == null || resultSampler == null || graph == null)
            return;

        float simTime = simulationController.SimulatedTimeSeconds;
        if (simTime + 1e-6f < _nextGraphSampleTime)
            return;

        if (simTime <= _lastSampledTime)
            return;

        var m = resultSampler.LatestMetrics;
        if (m == null)
            return;

        if (ignoreInvalidSamples)
        {
            if (!m.hasValidRoomAverage || !m.hasValidInletAverage || !m.hasValidOutletAverage)
                return;
        }

        if (requireSamplerTimeMatch && !IsSamplerTimeCloseEnough(m.statusMessage, simTime))
            return;

        graph.AddSample(
            simTime,
            m.inletAverageTemperatureDegC,
            m.outletAverageTemperatureDegC,
            m.avgRoomTemperatureDegC);

        _lastSampledTime = simTime;
        _nextGraphSampleTime += Mathf.Max(graphSampleIntervalSeconds, 1e-6f);

        UpdateAxisLabels();
        UpdateCurrentValueText(
            simTime,
            m.inletAverageTemperatureDegC,
            m.outletAverageTemperatureDegC,
            m.avgRoomTemperatureDegC);
    }

    private bool IsSamplerTimeCloseEnough(string statusMessage, float controllerSimTime)
    {
        if (string.IsNullOrWhiteSpace(statusMessage))
            return false;

        const string token = "Sampled at t=";
        int start = statusMessage.IndexOf(token, System.StringComparison.Ordinal);
        if (start < 0)
            return false;

        start += token.Length;
        int end = statusMessage.IndexOf('s', start);
        if (end < 0)
            end = statusMessage.Length;

        string valueText = statusMessage.Substring(start, end - start).Trim();

        if (!float.TryParse(valueText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float sampledTime))
            return false;

        return Mathf.Abs(sampledTime - controllerSimTime) <= Mathf.Max(samplerTimeToleranceSeconds, 1e-6f);
    }

    private void UpdateAxisLabels()
    {
        if (graph == null)
            return;

        if (yMinText != null) yMinText.text = graph.YMin.ToString("F0");
        if (yMidText != null) yMidText.text = ((graph.YMin + graph.YMax) * 0.5f).ToString("F0");
        if (yMaxText != null) yMaxText.text = graph.YMax.ToString("F0");

        float maxTime = graph.CurrentMaxTime;
        float midTime = maxTime * 0.5f;

        if (xMinText != null) xMinText.text = "0";
        if (xMidText != null) xMidText.text = midTime.ToString("F0");
        if (xMaxText != null) xMaxText.text = maxTime.ToString("F0");
    }

    private void UpdateCurrentValueText(float timeSec, float inlet, float outlet, float avg)
    {
        if (currentValuesText == null)
            return;

        currentValuesText.text =
            $"t = {timeSec:F1} s\n" +
            $"Inlet : {inlet:F2} °C\n" +
            $"Outlet: {outlet:F2} °C\n" +
            $"Avg   : {avg:F2} °C";
    }

    [ContextMenu("Clear Graph")]
    public void ClearGraph()
    {
        if (graph != null)
            graph.ClearAll();

        _lastSampledTime = -1f;
        ResetGraphSamplingSchedule();
        UpdateAxisLabels();
        UpdateCurrentValueText(0f, 0f, 0f, 0f);
    }

    private void ResetGraphSamplingSchedule()
    {
        if (simulationController != null)
            _nextGraphSampleTime = simulationController.SimulatedTimeSeconds + Mathf.Max(graphSampleIntervalSeconds, 1e-6f);
        else
            _nextGraphSampleTime = Mathf.Max(graphSampleIntervalSeconds, 1e-6f);
    }
}