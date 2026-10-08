using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class SystemMonitorGraphController : IDisposable
{
    private const string ProductModelId = "MULTIV_FMU_WARPPER";
    private const string AirflowModelId = "airflow";
    private const int MaximumSamples = 300;
    private const float VisibleTimeRangeSeconds = 120.0f;
    private const float InitializationRetrySeconds = 1.0f;
    private const string SelectedTabClassName = "ux04-graph-tab--selected";

    private static readonly string[] TabElementNames =
    {
        "PrimaryGraphHzTab",
        "PrimaryGraphShTab",
        "PrimaryGraphScTab",
        "PrimaryGraphIdu1Tab"
    };

    private static readonly Color PressureHighColor = new Color(1.00f, 0.25f, 0.32f);
    private static readonly Color PressureLowColor = new Color(0.10f, 0.64f, 1.00f);
    private static readonly Color CompressorColor = new Color(1.00f, 0.68f, 0.12f);
    private static readonly Color FanColor = new Color(0.68f, 0.28f, 0.96f);
    private static readonly Color PrimaryTemperatureColor = new Color(0.12f, 0.82f, 0.94f);
    private static readonly Color SaturationTemperatureColor = new Color(0.96f, 0.52f, 0.16f);
    private static readonly Color DerivedTemperatureColor = new Color(0.40f, 0.88f, 0.48f);
    private static readonly Color HumidityColor = new Color(0.68f, 0.43f, 0.96f);
    private static readonly Color FlowColor = new Color(1.00f, 0.38f, 0.46f);

    private readonly Button[] tabButtons = new Button[4];
    private readonly Action[] tabHandlers = new Action[4];
    private readonly GraphBinding[] graphs = new GraphBinding[4];
    private readonly VisualElement[] axisElements = new VisualElement[3];
    private readonly Label[] axisUnitLabels = new Label[3];
    private readonly VisualElement[] axisTickStacks = new VisualElement[3];
    private readonly Label[,] axisTickLabels = new Label[3, 5];

    private VisualElement chartHost;
    private VisualElement legendHost;
    private VisualElement axesHost;
    private VisualElement footerHost;
    private Label timeMinLabel;
    private Label timeQuarterLabel;
    private Label timeMiddleLabel;
    private Label timeThreeQuarterLabel;
    private Label timeMaxLabel;
    private Label statusLabel;
    private CoSimulationOrchestrator lastOrchestrator;
    private ulong lastSampledCoSimStep = ulong.MaxValue;
    private float lastSampleTime = -1.0f;
    private float nextInitializationRetryTime;
    private int selectedGraphIndex;
    private bool graphsBuilt;
    private bool hasSampledValidSignal;

    public bool Initialize(VisualElement documentRoot, out string issue)
    {
        chartHost = documentRoot?.Q<VisualElement>("PrimaryGraphChartHost");
        legendHost = documentRoot?.Q<VisualElement>("PrimaryGraphLegend");
        axesHost = documentRoot?.Q<VisualElement>("PrimaryGraphAxes");
        footerHost = documentRoot?.Q<VisualElement>("PrimaryGraphFooter");
        timeMinLabel = documentRoot?.Q<Label>("PrimaryGraphTimeMin");
        timeQuarterLabel = documentRoot?.Q<Label>("PrimaryGraphTimeQuarter");
        timeMiddleLabel = documentRoot?.Q<Label>("PrimaryGraphTimeMiddle");
        timeThreeQuarterLabel = documentRoot?.Q<Label>("PrimaryGraphTimeThreeQuarter");
        timeMaxLabel = documentRoot?.Q<Label>("PrimaryGraphTimeMax");
        statusLabel = documentRoot?.Q<Label>("PrimaryGraphStatus");

        if (chartHost == null || legendHost == null || axesHost == null || footerHost == null ||
            timeMinLabel == null || timeQuarterLabel == null || timeMiddleLabel == null ||
            timeThreeQuarterLabel == null || timeMaxLabel == null || statusLabel == null)
        {
            issue = "Primary graph UI elements are unavailable.";
            return false;
        }

        for (int axisIndex = 0; axisIndex < axisElements.Length; axisIndex++)
        {
            axisElements[axisIndex] = documentRoot.Q<VisualElement>($"PrimaryGraphAxis{axisIndex}");
            axisUnitLabels[axisIndex] = documentRoot.Q<Label>($"PrimaryGraphAxisUnit{axisIndex}");
            axisTickStacks[axisIndex] =
                axisElements[axisIndex]?.Q<VisualElement>(className: "ux04-graph-axis-ticks");
            for (int tickIndex = 0; tickIndex < axisTickLabels.GetLength(1); tickIndex++)
            {
                axisTickLabels[axisIndex, tickIndex] =
                    documentRoot.Q<Label>($"PrimaryGraphValue{axisIndex}_{tickIndex}");
            }

            if (axisElements[axisIndex] == null || axisUnitLabels[axisIndex] == null ||
                axisTickStacks[axisIndex] == null || HasMissingAxisTick(axisIndex))
            {
                issue = $"Primary graph axis is unavailable: {axisIndex}";
                Dispose();
                return false;
            }
        }

        for (int index = 0; index < tabButtons.Length; index++)
        {
            tabButtons[index] = documentRoot.Q<Button>(TabElementNames[index]);
            if (tabButtons[index] == null)
            {
                issue = $"Primary graph tab is unavailable: {TabElementNames[index]}";
                Dispose();
                return false;
            }

            int capturedIndex = index;
            Action handler = () => SelectGraph(capturedIndex);
            tabHandlers[index] = handler;
            tabButtons[index].clicked += handler;
        }

        UpdateTabPresentation();
        issue = string.Empty;
        return true;
    }

    public void Refresh(CoSimulationOrchestrator orchestrator)
    {
        EnsureGraphsBuilt();

        if (orchestrator != lastOrchestrator)
        {
            lastOrchestrator = orchestrator;
            lastSampledCoSimStep = ulong.MaxValue;
            lastSampleTime = -1.0f;
            hasSampledValidSignal = false;
            ClearSamples();
        }

        bool canReadFmuSignals = orchestrator != null && !orchestrator.IsCoSimStepInProgress;
        bool coSimulationAdvanced = canReadFmuSignals &&
                                    orchestrator.CompletedCoSimStepCount != lastSampledCoSimStep;
        bool initializationRetryDue = canReadFmuSignals &&
                                      !hasSampledValidSignal &&
                                      Time.unscaledTime >= nextInitializationRetryTime;
        if (coSimulationAdvanced || initializationRetryDue)
        {
            float sampleTime = (float)orchestrator.CurrentCoSimTime;
            if (lastSampleTime >= 0.0f &&
                (orchestrator.CompletedCoSimStepCount < lastSampledCoSimStep ||
                 sampleTime < lastSampleTime - 0.0001f))
            {
                ClearSamples();
                hasSampledValidSignal = false;
            }

            SampleAllGraphs(orchestrator, sampleTime);
            lastSampledCoSimStep = orchestrator.CompletedCoSimStepCount;
            lastSampleTime = sampleTime;
            nextInitializationRetryTime = Time.unscaledTime + InitializationRetrySeconds;
        }

        UpdateSelectedGraphPresentation();
        UpdateStatus(orchestrator);
    }

    public void Dispose()
    {
        for (int index = 0; index < tabButtons.Length; index++)
        {
            if (tabButtons[index] != null && tabHandlers[index] != null)
                tabButtons[index].clicked -= tabHandlers[index];
            tabButtons[index] = null;
            tabHandlers[index] = null;
        }

        for (int index = 0; index < graphs.Length; index++)
        {
            graphs[index]?.Chart?.RemoveFromHierarchy();
            graphs[index] = null;
        }

        chartHost = null;
        legendHost = null;
        axesHost = null;
        footerHost = null;
        timeMinLabel = null;
        timeQuarterLabel = null;
        timeMiddleLabel = null;
        timeThreeQuarterLabel = null;
        timeMaxLabel = null;
        statusLabel = null;
        for (int axisIndex = 0; axisIndex < axisElements.Length; axisIndex++)
        {
            axisElements[axisIndex] = null;
            axisUnitLabels[axisIndex] = null;
            axisTickStacks[axisIndex] = null;
            for (int tickIndex = 0; tickIndex < axisTickLabels.GetLength(1); tickIndex++)
                axisTickLabels[axisIndex, tickIndex] = null;
        }
        lastOrchestrator = null;
        lastSampleTime = -1.0f;
        graphsBuilt = false;
    }

    private void EnsureGraphsBuilt()
    {
        if (graphsBuilt)
            return;

        graphs[0] = CreateGraph(
            new[] { "Hz", "rpm", "kPa" },
            new[]
            {
                new Color(0.12f, 0.82f, 0.94f),
                new Color(1.00f, 0.25f, 0.32f),
                new Color(0.86f, 0.90f, 0.94f)
            },
            new[] { "High Pressure", "Low Pressure", "Compressor", "Outdoor Fan" },
            new[] { "kPa", "kPa", "Hz", "rpm" },
            new[] { PressureHighColor, PressureLowColor, CompressorColor, FanColor },
            new[] { 2, 2, 0, 1 });
        graphs[1] = CreateGraph(
            new[] { "degC" },
            new[] { PrimaryTemperatureColor },
            new[] { "Evaporator Outlet", "Evaporating Temp.", "SH" },
            new[] { "degC", "degC", "degC" },
            new[] { PrimaryTemperatureColor, SaturationTemperatureColor, DerivedTemperatureColor },
            new[] { 0, 0, 0 });
        graphs[2] = CreateGraph(
            new[] { "degC" },
            new[] { PrimaryTemperatureColor },
            new[] { "Condenser Outlet", "Condensing Temp.", "SC" },
            new[] { "degC", "degC", "degC" },
            new[] { PrimaryTemperatureColor, SaturationTemperatureColor, DerivedTemperatureColor },
            new[] { 0, 0, 0 });
        graphs[3] = CreateGraph(
            new[] { "Mixed: degC / % / kg/s" },
            new[] { PrimaryTemperatureColor },
            new[] { "R1 Suction Temp.", "R1 Discharge Temp.", "R1 Discharge RH", "R1 Air Flow" },
            new[] { "degC", "degC", "%", "kg/s" },
            new[] { PrimaryTemperatureColor, SaturationTemperatureColor, HumidityColor, FlowColor },
            new[] { 0, 0, 0, 0 });

        graphsBuilt = true;
        ShowSelectedGraph();
    }

    private static GraphBinding CreateGraph(
        string[] axisUnits,
        Color[] axisColors,
        string[] seriesNames,
        string[] seriesUnits,
        Color[] colors,
        int[] seriesAxisIndices)
    {
        MainDashboardTrendChartElement chart = new MainDashboardTrendChartElement(
            colors,
            seriesAxisIndices,
            new float[axisUnits.Length],
            CreateUnitMaximums(axisUnits.Length),
            true,
            MaximumSamples,
            VisibleTimeRangeSeconds);
        chart.SetGridDivisions(8, 12);
        chart.style.flexGrow = 1.0f;
        chart.style.minWidth = 0.0f;
        chart.style.minHeight = 0.0f;
        return new GraphBinding(axisUnits, axisColors, seriesNames, seriesUnits, colors, chart);
    }

    private static float[] CreateUnitMaximums(int count)
    {
        float[] maximums = new float[count];
        for (int index = 0; index < maximums.Length; index++)
            maximums[index] = 1.0f;
        return maximums;
    }

    private void SampleAllGraphs(CoSimulationOrchestrator orchestrator, float sampleTime)
    {
        double highPressure = Read(orchestrator, ProductModelId, "ODU_Sensor_Pressure_HI");
        double lowPressure = Read(orchestrator, ProductModelId, "ODU_Sensor_Pressure_LO");
        double compressorFrequency = Read(orchestrator, ProductModelId, "Comp_CurFreq");
        double outdoorFanRpm = Read(orchestrator, ProductModelId, "Fan_CurRPM");

        double suctionTemperature = Read(orchestrator, ProductModelId, "ODU_Sensor_Temp_Suction");
        double evaporatingTemperature = Read(
            orchestrator,
            ProductModelId,
            "comp_evaporating_temperature_degC.y");
        double superheat = Difference(suctionTemperature, evaporatingTemperature);

        double liquidTemperature = Read(orchestrator, ProductModelId, "ODU_Sensor_Temp_Liquid");
        double condensingTemperature = Read(
            orchestrator,
            ProductModelId,
            "comp_condensing_temperature_degC.y");
        double subcooling = Difference(liquidTemperature, condensingTemperature);

        double idu1SuctionTemperature = Read(orchestrator, AirflowModelId, "T_sensor");
        double idu1DischargeTemperature = Read(
            orchestrator,
            ProductModelId,
            "IDU_01_Air_Temp_Discharge");
        double idu1DischargeHumidity = Read(
            orchestrator,
            ProductModelId,
            "IDU_01_Air_RH_Discharge");
        double idu1DischargeMassFlow = Read(
            orchestrator,
            ProductModelId,
            "IDU_01_Air_mfr_Discharge");

        AddGraphSample(graphs[0], sampleTime, highPressure, lowPressure, compressorFrequency, outdoorFanRpm);
        AddGraphSample(graphs[1], sampleTime, suctionTemperature, evaporatingTemperature, superheat);
        AddGraphSample(graphs[2], sampleTime, liquidTemperature, condensingTemperature, subcooling);
        AddGraphSample(
            graphs[3],
            sampleTime,
            idu1SuctionTemperature,
            idu1DischargeTemperature,
            idu1DischargeHumidity,
            idu1DischargeMassFlow);
    }

    private void AddGraphSample(GraphBinding graph, float sampleTime, params double[] values)
    {
        float[] samples = new float[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            samples[index] = IsFinite(values[index]) ? (float)values[index] : float.NaN;
            hasSampledValidSignal |= IsFinite(values[index]);
        }

        graph.Chart.AddSample(sampleTime, samples);
    }

    private static double Read(
        CoSimulationOrchestrator orchestrator,
        string modelId,
        string variableName)
    {
        return orchestrator != null &&
               orchestrator.TryReadRealSignal(modelId, variableName, out double value) &&
               IsFinite(value)
            ? value
            : double.NaN;
    }

    private static double Difference(double minuend, double subtrahend)
    {
        return IsFinite(minuend) && IsFinite(subtrahend)
            ? minuend - subtrahend
            : double.NaN;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private void SelectGraph(int index)
    {
        if (selectedGraphIndex == index)
            return;

        selectedGraphIndex = index;
        UpdateTabPresentation();
        if (graphsBuilt)
            ShowSelectedGraph();
    }

    private void ShowSelectedGraph()
    {
        chartHost.Clear();
        chartHost.Add(graphs[selectedGraphIndex].Chart);
        RebuildLegend();
        UpdateSelectedGraphPresentation();
    }

    private void UpdateTabPresentation()
    {
        for (int index = 0; index < tabButtons.Length; index++)
            tabButtons[index]?.EnableInClassList(SelectedTabClassName, index == selectedGraphIndex);
    }

    private void RebuildLegend()
    {
        legendHost.Clear();
        GraphBinding graph = graphs[selectedGraphIndex];
        for (int index = 0; index < graph.SeriesNames.Length; index++)
        {
            VisualElement item = new VisualElement();
            item.AddToClassList("ux04-graph-legend__item");

            VisualElement swatch = new VisualElement();
            swatch.AddToClassList("ux04-graph-legend__swatch");
            swatch.style.backgroundColor = graph.Colors[index];

            Label label = new Label();
            label.AddToClassList("ux04-graph-legend__label");
            item.Add(swatch);
            item.Add(label);
            legendHost.Add(item);
            graph.LegendLabels[index] = label;
        }
    }

    private void UpdateSelectedGraphPresentation()
    {
        if (!graphsBuilt)
            return;

        GraphBinding graph = graphs[selectedGraphIndex];
        int axisCount = graph.AxisUnits.Length;
        float axisAreaWidth = axisCount > 1 ? 150.0f : 80.0f;
        axesHost.style.width = axisAreaWidth;
        legendHost.style.paddingLeft = axisAreaWidth;
        footerHost.style.paddingLeft = axisAreaWidth;
        AlignYAxisTicksToPlot(graph);
        for (int axisIndex = 0; axisIndex < axisElements.Length; axisIndex++)
        {
            bool visible = axisIndex < axisCount;
            axisElements[axisIndex].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
                continue;

            graph.Chart.GetDisplayValueRange(axisIndex, out float minValue, out float maxValue);
            Color axisColor = graph.AxisColors[axisIndex];
            axisUnitLabels[axisIndex].text = graph.AxisUnits[axisIndex];
            axisUnitLabels[axisIndex].style.color = axisColor;
            for (int tickIndex = 0; tickIndex < axisTickLabels.GetLength(1); tickIndex++)
            {
                float ratio = 1.0f - tickIndex / (float)(axisTickLabels.GetLength(1) - 1);
                Label tickLabel = axisTickLabels[axisIndex, tickIndex];
                tickLabel.text = FormatAxisValue(Mathf.Lerp(minValue, maxValue, ratio));
                tickLabel.style.color = axisColor;
            }
        }

        float minimumTime = graph.Chart.DisplayMinTime;
        float maximumTime = graph.Chart.DisplayMaxTime;
        float timeRange = maximumTime - minimumTime;
        timeMinLabel.text = minimumTime.ToString("F1", CultureInfo.InvariantCulture);
        timeQuarterLabel.text = (minimumTime + timeRange * 0.25f).ToString("F1", CultureInfo.InvariantCulture);
        timeMiddleLabel.text = (minimumTime + timeRange * 0.50f).ToString("F1", CultureInfo.InvariantCulture);
        timeThreeQuarterLabel.text = (minimumTime + timeRange * 0.75f).ToString("F1", CultureInfo.InvariantCulture);
        timeMaxLabel.text = maximumTime.ToString("F1", CultureInfo.InvariantCulture);

        for (int index = 0; index < graph.LegendLabels.Length; index++)
        {
            Label label = graph.LegendLabels[index];
            if (label == null)
                continue;

            label.text = string.IsNullOrWhiteSpace(graph.SeriesUnits[index])
                ? graph.SeriesNames[index]
                : $"{graph.SeriesNames[index]} [{graph.SeriesUnits[index]}]";
        }
    }

    private void AlignYAxisTicksToPlot(GraphBinding graph)
    {
        const float tickLabelHeight = 16.0f;
        Rect plotBounds = graph.Chart.worldBound;
        if (plotBounds.width <= 1.0f || plotBounds.height <= 1.0f)
            return;

        for (int axisIndex = 0; axisIndex < axisTickStacks.Length; axisIndex++)
        {
            VisualElement axis = axisElements[axisIndex];
            VisualElement tickStack = axisTickStacks[axisIndex];
            if (axis == null || tickStack == null)
                continue;

            float plotTopInAxis = plotBounds.yMin - axis.worldBound.yMin;
            tickStack.style.top = plotTopInAxis - tickLabelHeight * 0.5f;
            tickStack.style.bottom = StyleKeyword.Auto;
            tickStack.style.height = plotBounds.height + tickLabelHeight;

            for (int tickIndex = 0; tickIndex < axisTickLabels.GetLength(1); tickIndex++)
            {
                float ratio = tickIndex / (float)(axisTickLabels.GetLength(1) - 1);
                axisTickLabels[axisIndex, tickIndex].style.top = ratio * plotBounds.height;
            }
        }
    }

    private static string FormatAxisValue(float value)
    {
        return value.ToString("G5", CultureInfo.InvariantCulture);
    }

    private bool HasMissingAxisTick(int axisIndex)
    {
        for (int tickIndex = 0; tickIndex < axisTickLabels.GetLength(1); tickIndex++)
        {
            if (axisTickLabels[axisIndex, tickIndex] == null)
                return true;
        }

        return false;
    }

    private void UpdateStatus(CoSimulationOrchestrator orchestrator)
    {
        if (orchestrator == null)
        {
            statusLabel.text = "Waiting for Co-Simulation";
            statusLabel.EnableInClassList("ux04-graph-status--error", false);
            return;
        }

        if (orchestrator.CoSimFailureObserved)
        {
            statusLabel.text = "FMU error - showing last completed sample";
            statusLabel.EnableInClassList("ux04-graph-status--error", true);
            return;
        }

        statusLabel.text = hasSampledValidSignal
            ? $"LIVE | t={orchestrator.CurrentCoSimTime:F1}s | step={orchestrator.CompletedCoSimStepCount}"
            : "Waiting for verified FMU signals";
        statusLabel.EnableInClassList("ux04-graph-status--error", false);
    }

    private void ClearSamples()
    {
        if (!graphsBuilt)
            return;

        for (int graphIndex = 0; graphIndex < graphs.Length; graphIndex++)
            graphs[graphIndex].Chart.ClearSamples();
    }

    private sealed class GraphBinding
    {
        public GraphBinding(
            string[] axisUnits,
            Color[] axisColors,
            string[] seriesNames,
            string[] seriesUnits,
            Color[] colors,
            MainDashboardTrendChartElement chart)
        {
            AxisUnits = axisUnits;
            AxisColors = axisColors;
            SeriesNames = seriesNames;
            SeriesUnits = seriesUnits;
            Colors = colors;
            Chart = chart;
            LegendLabels = new Label[seriesNames.Length];
        }

        public string[] AxisUnits { get; }
        public Color[] AxisColors { get; }
        public string[] SeriesNames { get; }
        public string[] SeriesUnits { get; }
        public Color[] Colors { get; }
        public MainDashboardTrendChartElement Chart { get; }
        public Label[] LegendLabels { get; }
    }
}
