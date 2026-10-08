using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class MainDashboardTrendController : IDisposable
{
    private const float PerformanceSampleIntervalSeconds = 1.0f;

    private readonly IMainDashboardPerformanceDataSource performanceDataSource;
    private Button temperatureTab;
    private Button performanceTab;
    private VisualElement temperaturePanel;
    private VisualElement performancePanel;
    private Label roomLegend;
    private Label inletLegend;
    private Label outletLegend;
    private Label targetLegend;
    private Label gpuLegend;
    private Label fpsLegend;
    private Label temperatureTimeMin;
    private Label temperatureTimeMax;
    private Label performanceTimeMin;
    private Label performanceTimeMax;
    private Label samplingNote;
    private MainDashboardTrendChartElement temperatureChart;
    private MainDashboardTrendChartElement performanceChart;
    private float lastTemperatureSampleTime = -1.0f;
    private float nextPerformanceSampleTime;

    public MainDashboardTrendController(IMainDashboardPerformanceDataSource performanceDataSource)
    {
        this.performanceDataSource = performanceDataSource ??
            throw new ArgumentNullException(nameof(performanceDataSource));
    }

    public bool Initialize(VisualElement documentRoot, out string issue)
    {
        temperatureTab = documentRoot?.Q<Button>("TemperatureTrendTab");
        performanceTab = documentRoot?.Q<Button>("GpuPerformanceTrendTab");
        temperaturePanel = documentRoot?.Q<VisualElement>("TemperatureTrendPanel");
        performancePanel = documentRoot?.Q<VisualElement>("GpuPerformanceTrendPanel");
        VisualElement temperatureHost = documentRoot?.Q<VisualElement>("TemperatureTrendChartHost");
        VisualElement performanceHost = documentRoot?.Q<VisualElement>("GpuPerformanceTrendChartHost");
        roomLegend = documentRoot?.Q<Label>("RoomAverageTrendLegend");
        inletLegend = documentRoot?.Q<Label>("InletTrendLegend");
        outletLegend = documentRoot?.Q<Label>("OutletTrendLegend");
        targetLegend = documentRoot?.Q<Label>("TargetTrendLegend");
        gpuLegend = documentRoot?.Q<Label>("GpuUsageTrendLegend");
        fpsLegend = documentRoot?.Q<Label>("FpsTrendLegend");
        temperatureTimeMin = documentRoot?.Q<Label>("TemperatureTrendTimeMin");
        temperatureTimeMax = documentRoot?.Q<Label>("TemperatureTrendTimeMax");
        performanceTimeMin = documentRoot?.Q<Label>("PerformanceTrendTimeMin");
        performanceTimeMax = documentRoot?.Q<Label>("PerformanceTrendTimeMax");
        samplingNote = documentRoot?.Q<Label>("TrendSamplingNote");

        if (temperatureTab == null || performanceTab == null || temperaturePanel == null ||
            performancePanel == null || temperatureHost == null || performanceHost == null ||
            roomLegend == null || inletLegend == null || outletLegend == null || targetLegend == null ||
            gpuLegend == null || fpsLegend == null || temperatureTimeMin == null ||
            temperatureTimeMax == null || performanceTimeMin == null || performanceTimeMax == null ||
            samplingNote == null)
        {
            issue = "Trend Chart UI 구조가 올바르지 않습니다.";
            return false;
        }

        temperatureChart = new MainDashboardTrendChartElement(
            new[]
            {
                new Color(0.71f, 0.43f, 0.96f),
                new Color(0.12f, 0.83f, 0.94f),
                new Color(1.00f, 0.31f, 0.35f),
                new Color(0.74f, 0.80f, 0.85f)
            },
            15.0f,
            35.0f,
            false);
        performanceChart = new MainDashboardTrendChartElement(
            new[]
            {
                new Color(0.24f, 0.87f, 0.56f),
                new Color(0.20f, 0.70f, 0.95f)
            },
            0.0f,
            100.0f,
            false);
        temperatureHost.Add(temperatureChart);
        performanceHost.Add(performanceChart);

        temperatureTab.clicked += ShowTemperatureTrend;
        performanceTab.clicked += ShowGpuPerformanceTrend;
        ShowTemperatureTrend();
        issue = string.Empty;
        return true;
    }

    public void Tick()
    {
        if (Time.unscaledTime >= nextPerformanceSampleTime)
        {
            nextPerformanceSampleTime = Time.unscaledTime + PerformanceSampleIntervalSeconds;
            if (performanceDataSource.TryReadPerformanceSample(
                    out MainDashboardPerformanceTrendSample performanceSample))
            {
                AddPerformanceSample(performanceSample);
            }
        }
    }

    public void AddTemperatureSample(MainDashboardTemperatureTrendSample sample)
    {
        if (lastTemperatureSampleTime >= 0.0f &&
            sample.TimeSeconds < lastTemperatureSampleTime - 0.0001f)
        {
            temperatureChart.ClearSamples();
            lastTemperatureSampleTime = -1.0f;
        }

        lastTemperatureSampleTime = Mathf.Max(lastTemperatureSampleTime, sample.TimeSeconds);
        temperatureChart.AddSample(
            sample.TimeSeconds,
            sample.RoomAverageDegC,
            sample.InletDegC,
            sample.OutletDegC,
            sample.TargetDegC);
        roomLegend.text = FormatTemperature("Room Avg", sample.RoomAverageDegC);
        inletLegend.text = FormatTemperature("Inlet", sample.InletDegC);
        outletLegend.text = FormatTemperature("Outlet", sample.OutletDegC);
        targetLegend.text = FormatTemperature("Target", sample.TargetDegC);
        UpdateTimeLabels(temperatureChart, temperatureTimeMin, temperatureTimeMax);
    }

    public void AddPerformanceSample(MainDashboardPerformanceTrendSample sample)
    {
        performanceChart.AddSample(sample.TimeSeconds, sample.GpuUsagePercent, sample.Fps);
        gpuLegend.text = float.IsFinite(sample.GpuUsagePercent)
            ? $"GPU Usage {sample.GpuUsagePercent:F0}%"
            : "GPU Usage — Unavailable";
        fpsLegend.text = float.IsFinite(sample.Fps) ? $"FPS {sample.Fps:F0}" : "FPS —";
        UpdateTimeLabels(performanceChart, performanceTimeMin, performanceTimeMax);
    }

    public void ShowTemperatureTrend()
    {
        temperaturePanel.style.display = DisplayStyle.Flex;
        performancePanel.style.display = DisplayStyle.None;
        temperatureTab.EnableInClassList("trend-tab--selected", true);
        performanceTab.EnableInClassList("trend-tab--selected", false);
        samplingNote.text = "2 s metrics samples · 300 points · 60 s window";
    }

    public void ShowGpuPerformanceTrend()
    {
        temperaturePanel.style.display = DisplayStyle.None;
        performancePanel.style.display = DisplayStyle.Flex;
        temperatureTab.EnableInClassList("trend-tab--selected", false);
        performanceTab.EnableInClassList("trend-tab--selected", true);
        samplingNote.text = "1 s runtime samples · 300 points · 60 s window";
    }

    public void Dispose()
    {
        if (temperatureTab != null)
            temperatureTab.clicked -= ShowTemperatureTrend;
        if (performanceTab != null)
            performanceTab.clicked -= ShowGpuPerformanceTrend;
        temperatureChart?.RemoveFromHierarchy();
        performanceChart?.RemoveFromHierarchy();
        temperatureChart = null;
        performanceChart = null;
    }

    private static string FormatTemperature(string label, float value)
    {
        return float.IsFinite(value) ? $"{label} {value:F1}°C" : $"{label} —";
    }

    private static void UpdateTimeLabels(
        MainDashboardTrendChartElement chart,
        Label minLabel,
        Label maxLabel)
    {
        minLabel.text = chart.DisplayMinTime.ToString("F0");
        maxLabel.text = chart.DisplayMaxTime.ToString("F0");
    }
}
