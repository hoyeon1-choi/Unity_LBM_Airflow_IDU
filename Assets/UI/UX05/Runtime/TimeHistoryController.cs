using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class TimeHistoryController : IDisposable
{
    private const string LogTag = "[UX05][TimeHistory]";
    private const int MaximumSamples = 1200;
    private const int MaximumSelectedSignals = 12;
    private const int GraphCount = 3;
    private const float DefaultVisibleRangeSeconds = 30.0f;
    private const float CatalogRetrySeconds = 1.0f;

    private static readonly Color[] Palette =
    {
        new Color(0.12f, 0.82f, 0.94f), new Color(1.00f, 0.68f, 0.12f),
        new Color(0.36f, 0.86f, 0.52f), new Color(0.80f, 0.48f, 0.96f),
        new Color(1.00f, 0.38f, 0.48f), new Color(0.23f, 0.58f, 1.00f),
        new Color(0.98f, 0.52f, 0.20f), new Color(0.91f, 0.42f, 0.75f)
    };

    private sealed class SignalDefinition
    {
        public string Id;
        public string ModelId;
        public string VariableName;
        public string Unit;
        public SignalDirection Direction;
        public SignalValueType ValueType;
        public Color Color;
        public int SelectedGraphMask;
        public double LatestValue = double.NaN;
        public readonly List<Vector2> History = new List<Vector2>(MaximumSamples);

        public string DisplayName => $"{ModelId}.{VariableName}";
    }

    private sealed class VariableRow
    {
        public VisualElement Root;
        public VisualElement Swatch;
        public Toggle Toggle;
        public Label Unit;
        public SignalDefinition Signal;
    }

    private readonly List<SignalDefinition> catalog = new List<SignalDefinition>();
    private readonly List<SignalDefinition> filteredInputs = new List<SignalDefinition>();
    private readonly List<SignalDefinition> filteredOutputs = new List<SignalDefinition>();
    private readonly List<SignalDefinition> selectedSignals = new List<SignalDefinition>();
    private readonly Dictionary<string, Label> legendLabels = new Dictionary<string, Label>();
    private readonly Dictionary<string, Label> hoverValueLabels = new Dictionary<string, Label>();
    private readonly List<Label[]> axisTickLabels = new List<Label[]>();
    private readonly float[] graphVisibleRanges =
    {
        DefaultVisibleRangeSeconds, DefaultVisibleRangeSeconds, DefaultVisibleRangeSeconds
    };

    private TextField searchField;
    private Button clearSelectionButton;
    private ListView inputList;
    private ListView outputList;
    private Label inputHeading;
    private Label outputHeading;
    private Label selectionCount;
    private Button graph1Tab;
    private Button graph2Tab;
    private Button graph3Tab;
    private Button exportButton;
    private Label statusLabel;
    private Label timeMinLabel;
    private Label timeQuarterLabel;
    private Label timeMiddleLabel;
    private Label timeThreeQuarterLabel;
    private Label timeMaxLabel;
    private VisualElement yAxisHost;
    private VisualElement chartHost;
    private VisualElement hoverLine;
    private VisualElement hoverTooltip;
    private Label hoverTimeLabel;
    private VisualElement hoverValuesHost;
    private ScrollView legendHost;
    private MainDashboardTrendChartElement chart;
    private CoSimulationOrchestrator lastOrchestrator;
    private ulong lastSampledStep = ulong.MaxValue;
    private float lastSampleTime = -1.0f;
    private float nextCatalogRetryTime;
    private int activeGraphIndex;
    private bool chartPanning;
    private int chartPanButton = -1;
    private int chartPanPointerId = -1;
    private Vector2 chartPanPointerPosition;
    private bool initialized;

    public int SelectedVariableCount => selectedSignals.Count;

    public bool Initialize(VisualElement documentRoot, out string issue)
    {
        searchField = documentRoot?.Q<TextField>("TimeHistorySearchField");
        clearSelectionButton = documentRoot?.Q<Button>("TimeHistoryClearSelectionButton");
        inputList = documentRoot?.Q<ListView>("TimeHistoryInputList");
        outputList = documentRoot?.Q<ListView>("TimeHistoryOutputList");
        inputHeading = documentRoot?.Q<Label>("TimeHistoryInputHeading");
        outputHeading = documentRoot?.Q<Label>("TimeHistoryOutputHeading");
        selectionCount = documentRoot?.Q<Label>("TimeHistorySelectionCount");
        graph1Tab = documentRoot?.Q<Button>("TimeHistoryGraph1Tab");
        graph2Tab = documentRoot?.Q<Button>("TimeHistoryGraph2Tab");
        graph3Tab = documentRoot?.Q<Button>("TimeHistoryGraph3Tab");
        exportButton = documentRoot?.Q<Button>("TimeHistoryExportButton");
        statusLabel = documentRoot?.Q<Label>("TimeHistoryStatus");
        timeMinLabel = documentRoot?.Q<Label>("TimeHistoryTimeMin");
        timeQuarterLabel = documentRoot?.Q<Label>("TimeHistoryTimeQuarter");
        timeMiddleLabel = documentRoot?.Q<Label>("TimeHistoryTimeMiddle");
        timeThreeQuarterLabel = documentRoot?.Q<Label>("TimeHistoryTimeThreeQuarter");
        timeMaxLabel = documentRoot?.Q<Label>("TimeHistoryTimeMax");
        yAxisHost = documentRoot?.Q<VisualElement>("TimeHistoryYAxisHost");
        chartHost = documentRoot?.Q<VisualElement>("TimeHistoryChartHost");
        hoverLine = documentRoot?.Q<VisualElement>("TimeHistoryHoverLine");
        hoverTooltip = documentRoot?.Q<VisualElement>("TimeHistoryHoverTooltip");
        hoverTimeLabel = documentRoot?.Q<Label>("TimeHistoryHoverTime");
        hoverValuesHost = documentRoot?.Q<VisualElement>("TimeHistoryHoverValues");
        legendHost = documentRoot?.Q<ScrollView>("TimeHistoryLegend");

        if (searchField == null || clearSelectionButton == null || inputList == null ||
            outputList == null || inputHeading == null || outputHeading == null ||
            selectionCount == null || graph1Tab == null || graph2Tab == null ||
            graph3Tab == null || exportButton == null || statusLabel == null ||
            timeMinLabel == null || timeQuarterLabel == null || timeMiddleLabel == null ||
            timeThreeQuarterLabel == null || timeMaxLabel == null || yAxisHost == null ||
            chartHost == null || hoverLine == null || hoverTooltip == null ||
            hoverTimeLabel == null || hoverValuesHost == null || legendHost == null)
        {
            issue = "Time History UI elements are unavailable.";
            return false;
        }

        ConfigureList(inputList, filteredInputs);
        ConfigureList(outputList, filteredOutputs);
        searchField.label = string.Empty;
        searchField.tooltip = "Search by model or variable name";

        searchField.RegisterValueChangedCallback(OnSearchChanged);
        clearSelectionButton.clicked += ClearSelection;
        graph1Tab.clicked += SelectGraph1;
        graph2Tab.clicked += SelectGraph2;
        graph3Tab.clicked += SelectGraph3;
        exportButton.clicked += ExportCsv;
        chartHost.RegisterCallback<WheelEvent>(OnChartWheel, TrickleDown.TrickleDown);
        chartHost.RegisterCallback<PointerDownEvent>(OnChartPointerDown, TrickleDown.TrickleDown);
        chartHost.RegisterCallback<PointerMoveEvent>(OnChartPointerMove, TrickleDown.TrickleDown);
        chartHost.RegisterCallback<PointerUpEvent>(OnChartPointerUp, TrickleDown.TrickleDown);
        chartHost.RegisterCallback<PointerCaptureOutEvent>(OnChartPointerCaptureOut, TrickleDown.TrickleDown);
        chartHost.RegisterCallback<PointerLeaveEvent>(OnChartPointerLeave);
        hoverLine.pickingMode = PickingMode.Ignore;
        hoverTooltip.pickingMode = PickingMode.Ignore;

        RebuildChart();
        ApplyFilter();
        initialized = true;
        issue = string.Empty;
        return true;
    }

    public void Refresh(CoSimulationOrchestrator orchestrator)
    {
        if (!initialized)
            return;

        if (orchestrator != lastOrchestrator)
        {
            lastOrchestrator = orchestrator;
            lastSampledStep = ulong.MaxValue;
            lastSampleTime = -1.0f;
            ClearHistory();
            catalog.Clear();
            selectedSignals.Clear();
            ApplyFilter();
            RebuildChart();
            RebuildCatalog(orchestrator);
        }
        else if (catalog.Count == 0 && Time.unscaledTime >= nextCatalogRetryTime)
        {
            RebuildCatalog(orchestrator);
        }

        bool canSample = orchestrator != null &&
                         !orchestrator.IsCoSimStepInProgress && HasAnySelectedSignals();
        bool stepAdvanced = canSample && orchestrator.CompletedCoSimStepCount != lastSampledStep;
        if (stepAdvanced)
        {
            float sampleTime = (float)orchestrator.CurrentCoSimTime;
            if (lastSampleTime >= 0.0f &&
                (orchestrator.CompletedCoSimStepCount < lastSampledStep ||
                 sampleTime < lastSampleTime - 0.0001f))
            {
                ClearHistory();
            }

            SampleSelectedSignals(orchestrator, sampleTime);
            lastSampledStep = orchestrator.CompletedCoSimStepCount;
            lastSampleTime = sampleTime;
        }

        UpdatePresentation(orchestrator);
    }

    public void Dispose()
    {
        if (!initialized)
            return;

        searchField.UnregisterValueChangedCallback(OnSearchChanged);
        clearSelectionButton.clicked -= ClearSelection;
        graph1Tab.clicked -= SelectGraph1;
        graph2Tab.clicked -= SelectGraph2;
        graph3Tab.clicked -= SelectGraph3;
        exportButton.clicked -= ExportCsv;
        chartHost.UnregisterCallback<WheelEvent>(OnChartWheel, TrickleDown.TrickleDown);
        chartHost.UnregisterCallback<PointerDownEvent>(OnChartPointerDown, TrickleDown.TrickleDown);
        chartHost.UnregisterCallback<PointerMoveEvent>(OnChartPointerMove, TrickleDown.TrickleDown);
        chartHost.UnregisterCallback<PointerUpEvent>(OnChartPointerUp, TrickleDown.TrickleDown);
        chartHost.UnregisterCallback<PointerCaptureOutEvent>(OnChartPointerCaptureOut, TrickleDown.TrickleDown);
        chartHost.UnregisterCallback<PointerLeaveEvent>(OnChartPointerLeave);
        chart?.RemoveFromHierarchy();
        chart = null;
        catalog.Clear();
        selectedSignals.Clear();
        filteredInputs.Clear();
        filteredOutputs.Clear();
        legendLabels.Clear();
        hoverValueLabels.Clear();
        lastOrchestrator = null;
        initialized = false;
    }

    private void ConfigureList(ListView list, List<SignalDefinition> source)
    {
        list.itemsSource = source;
        list.fixedItemHeight = 29.0f;
        list.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
        list.selectionType = SelectionType.None;
        list.makeItem = MakeVariableRow;
        list.bindItem = (element, index) => BindVariableRow(element, source[index]);
    }

    private VisualElement MakeVariableRow()
    {
        VariableRow row = new VariableRow();
        row.Root = new VisualElement();
        row.Root.AddToClassList("ux05-variable-row");
        row.Swatch = new VisualElement();
        row.Swatch.AddToClassList("ux05-variable-row__swatch");
        row.Toggle = new Toggle();
        row.Toggle.AddToClassList("ux05-variable-row__toggle");
        row.Unit = new Label();
        row.Unit.AddToClassList("ux05-variable-row__unit");
        row.Root.Add(row.Swatch);
        row.Root.Add(row.Toggle);
        row.Root.Add(row.Unit);
        row.Root.userData = row;
        row.Toggle.RegisterValueChangedCallback(change =>
        {
            if (row.Signal == null || IsSelected(row.Signal, activeGraphIndex) == change.newValue)
                return;
            if (change.newValue && CountSelectedSignals(activeGraphIndex) >= MaximumSelectedSignals)
            {
                row.Toggle.SetValueWithoutNotify(false);
                statusLabel.text = $"Select up to {MaximumSelectedSignals} variables";
                return;
            }
            SetSelected(row.Signal, activeGraphIndex, change.newValue);
            RebuildSelectedSignalsAndChart();
        });
        return row.Root;
    }

    private void BindVariableRow(VisualElement element, SignalDefinition signal)
    {
        VariableRow row = element.userData as VariableRow;
        if (row == null)
            return;

        row.Signal = signal;
        row.Swatch.style.backgroundColor = signal.Color;
        row.Toggle.text = signal.DisplayName;
        row.Toggle.tooltip = $"{signal.Direction} | {signal.ValueType} | {signal.DisplayName}";
        row.Toggle.SetValueWithoutNotify(IsSelected(signal, activeGraphIndex));
        row.Unit.text = signal.Unit;
    }

    private void RebuildCatalog(CoSimulationOrchestrator orchestrator)
    {
        nextCatalogRetryTime = Time.unscaledTime + CatalogRetrySeconds;
        if (orchestrator == null || orchestrator.FmuModels == null)
            return;

        List<SignalDefinition> discovered = new List<SignalDefinition>();
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<FmuCoSimulationModel> models = orchestrator.FmuModels;
        bool hasPendingDescription = false;
        for (int modelIndex = 0; modelIndex < models.Count; modelIndex++)
        {
            FmuCoSimulationModel model = models[modelIndex];
            FmuModelDescription description = model != null ? model.ModelDescription : null;
            if (description == null)
            {
                if (model != null)
                    hasPendingDescription = true;
                continue;
            }

            IReadOnlyList<FmuVariableInfo> variables = description.Variables;
            for (int variableIndex = 0; variableIndex < variables.Count; variableIndex++)
            {
                FmuVariableInfo variable = variables[variableIndex];
                if (variable == null ||
                    (variable.causality != SignalDirection.Input &&
                     variable.causality != SignalDirection.Output) ||
                    variable.valueType != SignalValueType.Real)
                {
                    continue;
                }

                string id = $"{model.ModelId}\n{variable.name}";
                if (!ids.Add(id))
                    continue;

                discovered.Add(new SignalDefinition
                {
                    Id = id,
                    ModelId = model.ModelId,
                    VariableName = variable.name,
                    Unit = InferUnit(variable.name),
                    Direction = variable.causality,
                    ValueType = variable.valueType,
                    Color = ColorFor(id)
                });
            }
        }

        if (hasPendingDescription || discovered.Count == 0)
            return;

        AddAirflowSignal(discovered, ids, "T_sensor", "degC", SignalDirection.Output);

        discovered.Sort(CompareSignals);
        catalog.Clear();
        catalog.AddRange(discovered);
        ApplyDefaultSelection();
        ApplyFilter();
        RebuildSelectedSignalsAndChart();
        Debug.Log($"{LogTag}[Case={orchestrator.ProfileName}] Loaded {catalog.Count} numeric FMU/LBM variables.");
    }

    private static int CompareSignals(SignalDefinition left, SignalDefinition right)
    {
        int direction = left.Direction.CompareTo(right.Direction);
        if (direction != 0)
            return direction;
        int model = string.Compare(left.ModelId, right.ModelId, StringComparison.OrdinalIgnoreCase);
        return model != 0
            ? model
            : string.Compare(left.VariableName, right.VariableName, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddAirflowSignal(
        List<SignalDefinition> discovered,
        HashSet<string> ids,
        string variableName,
        string unit,
        SignalDirection direction)
    {
        string id = $"airflow\n{variableName}";
        if (!ids.Add(id))
            return;
        discovered.Add(new SignalDefinition
        {
            Id = id,
            ModelId = "airflow",
            VariableName = variableName,
            Unit = unit,
            Direction = direction,
            ValueType = SignalValueType.Real,
            Color = ColorFor(id)
        });
    }

    private void ApplyDefaultSelection()
    {
        string[] preferred =
        {
            "Comp_CurFreq", "ODU_Sensor_Pressure_HI", "ODU_Sensor_Pressure_LO", "T_sensor"
        };
        int count = 0;
        for (int preferredIndex = 0; preferredIndex < preferred.Length; preferredIndex++)
        {
            for (int signalIndex = 0; signalIndex < catalog.Count; signalIndex++)
            {
                SignalDefinition signal = catalog[signalIndex];
                if (!IsSelected(signal, 0) && string.Equals(
                        signal.VariableName,
                        preferred[preferredIndex],
                        StringComparison.OrdinalIgnoreCase))
                {
                    SetSelected(signal, 0, true);
                    count++;
                    break;
                }
            }
        }

        for (int index = 0; count < 3 && index < catalog.Count; index++)
        {
            if (catalog[index].Direction != SignalDirection.Output || IsSelected(catalog[index], 0))
                continue;
            SetSelected(catalog[index], 0, true);
            count++;
        }
    }

    private void OnSearchChanged(ChangeEvent<string> change)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = searchField?.value?.Trim() ?? string.Empty;
        filteredInputs.Clear();
        filteredOutputs.Clear();
        for (int index = 0; index < catalog.Count; index++)
        {
            SignalDefinition signal = catalog[index];
            if (query.Length > 0 &&
                signal.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (signal.Direction == SignalDirection.Input)
                filteredInputs.Add(signal);
            else if (signal.Direction == SignalDirection.Output)
                filteredOutputs.Add(signal);
        }

        inputHeading.text = $"INPUT  ({filteredInputs.Count})";
        outputHeading.text = $"OUTPUT  ({filteredOutputs.Count})";
        inputList.Rebuild();
        outputList.Rebuild();
    }

    private void ClearSelection()
    {
        for (int index = 0; index < catalog.Count; index++)
            SetSelected(catalog[index], activeGraphIndex, false);
        inputList.Rebuild();
        outputList.Rebuild();
        RebuildSelectedSignalsAndChart();
    }

    private void RebuildSelectedSignalsAndChart()
    {
        selectedSignals.Clear();
        for (int index = 0; index < catalog.Count; index++)
        {
            if (IsSelected(catalog[index], activeGraphIndex))
                selectedSignals.Add(catalog[index]);
        }
        RebuildChart();
        inputList?.Rebuild();
        outputList?.Rebuild();
    }

    private int CountSelectedSignals(int graphIndex)
    {
        int count = 0;
        for (int index = 0; index < catalog.Count; index++)
        {
            if (IsSelected(catalog[index], graphIndex))
                count++;
        }
        return count;
    }

    private static bool IsSelected(SignalDefinition signal, int graphIndex)
    {
        return signal != null && (signal.SelectedGraphMask & (1 << graphIndex)) != 0;
    }

    private static void SetSelected(SignalDefinition signal, int graphIndex, bool selected)
    {
        if (signal == null)
            return;

        int mask = 1 << graphIndex;
        signal.SelectedGraphMask = selected
            ? signal.SelectedGraphMask | mask
            : signal.SelectedGraphMask & ~mask;
    }

    private void RebuildChart()
    {
        chart?.RemoveFromHierarchy();
        chart = null;
        legendHost?.Clear();
        hoverValuesHost?.Clear();
        yAxisHost?.Clear();
        legendLabels.Clear();
        hoverValueLabels.Clear();
        axisTickLabels.Clear();
        HideHoverValues();

        selectionCount.text = $"Graph {activeGraphIndex + 1} · {selectedSignals.Count} selected";
        UpdateGraphTabs();
        if (selectedSignals.Count == 0)
        {
            Label emptyAxis = new Label("Select variables\nfor this graph");
            emptyAxis.AddToClassList("ux05-y-axis-empty");
            yAxisHost.Add(emptyAxis);
            return;
        }

        Dictionary<string, int> axisByUnit = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        List<string> units = new List<string>();
        Color[] colors = new Color[selectedSignals.Count];
        int[] axisIndices = new int[selectedSignals.Count];
        for (int index = 0; index < selectedSignals.Count; index++)
        {
            SignalDefinition signal = selectedSignals[index];
            string unit = string.IsNullOrWhiteSpace(signal.Unit) ? "value" : signal.Unit;
            if (!axisByUnit.TryGetValue(unit, out int axisIndex))
            {
                axisIndex = units.Count;
                units.Add(unit);
                axisByUnit.Add(unit, axisIndex);
            }
            colors[index] = signal.Color;
            axisIndices[index] = axisIndex;
        }

        float[] minimums = new float[units.Count];
        float[] maximums = new float[units.Count];
        for (int index = 0; index < maximums.Length; index++)
            maximums[index] = 1.0f;

        chart = new MainDashboardTrendChartElement(
            colors,
            axisIndices,
            minimums,
            maximums,
            true,
            MaximumSamples,
            graphVisibleRanges[activeGraphIndex],
            0.10f);
        chart.SetGridDivisions(8, 12);
        chart.style.flexGrow = 1.0f;
        chart.style.minWidth = 0.0f;
        chart.style.minHeight = 0.0f;
        chartHost.Insert(0, chart);

        BuildYAxisColumns(units);

        for (int seriesIndex = 0; seriesIndex < selectedSignals.Count; seriesIndex++)
        {
            SignalDefinition signal = selectedSignals[seriesIndex];
            for (int pointIndex = 0; pointIndex < signal.History.Count; pointIndex++)
            {
                Vector2 point = signal.History[pointIndex];
                chart.AddSeriesSample(seriesIndex, point.x, point.y);
            }
            AddLegendItem(signal);
            AddHoverItem(signal);
        }

        UpdateAxisSummary(units);
    }

    private void BuildYAxisColumns(IReadOnlyList<string> units)
    {
        yAxisHost.Clear();
        axisTickLabels.Clear();
        for (int axisIndex = 0; axisIndex < units.Count; axisIndex++)
        {
            VisualElement column = new VisualElement();
            column.AddToClassList("ux05-y-axis-column");
            Label unitLabel = new Label($"[{units[axisIndex]}]");
            unitLabel.AddToClassList("ux05-y-axis-unit");
            column.Add(unitLabel);
            VisualElement tickStack = new VisualElement();
            tickStack.AddToClassList("ux05-y-axis-ticks");
            Label[] ticks = new Label[5];
            for (int tickIndex = 0; tickIndex < ticks.Length; tickIndex++)
            {
                Label tick = new Label();
                tick.AddToClassList("ux05-y-axis-tick");
                tickStack.Add(tick);
                ticks[tickIndex] = tick;
            }
            column.Add(tickStack);
            yAxisHost.Add(column);
            axisTickLabels.Add(ticks);
        }
    }

    private void AddLegendItem(SignalDefinition signal)
    {
        VisualElement item = new VisualElement();
        item.AddToClassList("ux05-legend-item");
        VisualElement swatch = new VisualElement();
        swatch.AddToClassList("ux05-legend-swatch");
        swatch.style.backgroundColor = signal.Color;
        Label label = new Label();
        label.AddToClassList("ux05-legend-label");
        item.Add(swatch);
        item.Add(label);
        legendHost.Add(item);
        legendLabels[signal.Id] = label;
        UpdateLegendLabel(signal);
    }

    private void AddHoverItem(SignalDefinition signal)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("ux05-hover-row");
        VisualElement swatch = new VisualElement();
        swatch.AddToClassList("ux05-legend-swatch");
        swatch.style.backgroundColor = signal.Color;
        Label valueLabel = new Label();
        valueLabel.AddToClassList("ux05-hover-value");
        row.Add(swatch);
        row.Add(valueLabel);
        hoverValuesHost.Add(row);
        hoverValueLabels[signal.Id] = valueLabel;
    }

    private void SampleSelectedSignals(CoSimulationOrchestrator orchestrator, float sampleTime)
    {
        for (int index = 0; index < catalog.Count; index++)
        {
            SignalDefinition signal = catalog[index];
            if (signal.SelectedGraphMask == 0)
                continue;

            bool valid = orchestrator.TryReadRealSignal(
                signal.ModelId,
                signal.VariableName,
                out double value) && IsFinite(value);
            signal.LatestValue = valid ? value : double.NaN;
            if (valid)
            {
                signal.History.Add(new Vector2(sampleTime, (float)value));
                while (signal.History.Count > MaximumSamples)
                    signal.History.RemoveAt(0);
            }
        }

        float[] values = new float[selectedSignals.Count];
        for (int index = 0; index < selectedSignals.Count; index++)
        {
            double latestValue = selectedSignals[index].LatestValue;
            values[index] = IsFinite(latestValue) ? (float)latestValue : float.NaN;
        }
        chart?.AddSample(sampleTime, values);
    }

    private bool HasAnySelectedSignals()
    {
        for (int index = 0; index < catalog.Count; index++)
        {
            if (catalog[index].SelectedGraphMask != 0)
                return true;
        }
        return false;
    }

    private void UpdatePresentation(CoSimulationOrchestrator orchestrator)
    {
        if (chart != null)
        {
            UpdateTimeAxisPresentation();
            UpdateAxisSummary(null);
        }

        statusLabel.EnableInClassList("ux05-graph-status--live", false);
        statusLabel.EnableInClassList("ux05-graph-status--error", false);
        if (orchestrator == null)
        {
            statusLabel.text = "Waiting for Co-Simulation";
        }
        else if (orchestrator.CoSimFailureObserved)
        {
            statusLabel.text = "FMU error - showing last completed sample";
            statusLabel.EnableInClassList("ux05-graph-status--error", true);
        }
        else
        {
            statusLabel.text = $"LIVE | t={orchestrator.CurrentCoSimTime:F1}s | step={orchestrator.CompletedCoSimStepCount}";
            statusLabel.EnableInClassList("ux05-graph-status--live", true);
        }
    }

    private void UpdateAxisSummary(IReadOnlyList<string> knownUnits)
    {
        if (chart == null)
            return;

        List<string> units = knownUnits != null
            ? new List<string>(knownUnits)
            : BuildSelectedUnitList();
        int axisCount = Mathf.Min(units.Count, axisTickLabels.Count);
        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
        {
            chart.GetDisplayValueRange(axisIndex, out float minimum, out float maximum);
            Label[] ticks = axisTickLabels[axisIndex];
            ticks[0].text = FormatAxisValue(maximum);
            ticks[1].text = FormatAxisValue(Mathf.Lerp(minimum, maximum, 0.75f));
            ticks[2].text = FormatAxisValue(Mathf.Lerp(minimum, maximum, 0.50f));
            ticks[3].text = FormatAxisValue(Mathf.Lerp(minimum, maximum, 0.25f));
            ticks[4].text = FormatAxisValue(minimum);
        }
    }

    private static string FormatAxisValue(float value)
    {
        return value.ToString("G5", CultureInfo.InvariantCulture);
    }

    private void SelectGraph1() => SelectGraph(0);
    private void SelectGraph2() => SelectGraph(1);
    private void SelectGraph3() => SelectGraph(2);

    private void SelectGraph(int graphIndex)
    {
        graphIndex = Mathf.Clamp(graphIndex, 0, GraphCount - 1);
        if (activeGraphIndex == graphIndex)
            return;

        activeGraphIndex = graphIndex;
        RebuildSelectedSignalsAndChart();
    }

    private void UpdateGraphTabs()
    {
        graph1Tab.EnableInClassList("ux05-graph-tab--active", activeGraphIndex == 0);
        graph2Tab.EnableInClassList("ux05-graph-tab--active", activeGraphIndex == 1);
        graph3Tab.EnableInClassList("ux05-graph-tab--active", activeGraphIndex == 2);
    }

    private void OnChartWheel(WheelEvent wheelEvent)
    {
        if (chart == null || Mathf.Abs(wheelEvent.delta.y) < 0.001f)
            return;

        float scale = wheelEvent.delta.y > 0.0f ? 1.25f : 0.8f;
        graphVisibleRanges[activeGraphIndex] = Mathf.Clamp(
            graphVisibleRanges[activeGraphIndex] * scale,
            2.0f,
            3600.0f);
        chart.SetVisibleTimeRange(graphVisibleRanges[activeGraphIndex]);
        UpdateTimeAxisPresentation();
        UpdateHoverValues(wheelEvent.mousePosition);
        wheelEvent.StopPropagation();
    }

    private void OnChartPointerDown(PointerDownEvent pointerEvent)
    {
        if (chart == null)
            return;

        if (pointerEvent.button == 2)
        {
            graphVisibleRanges[activeGraphIndex] = DefaultVisibleRangeSeconds;
            chart.ResetView();
            chart.SetVisibleTimeRange(DefaultVisibleRangeSeconds);
            UpdateTimeAxisPresentation();
            UpdateAxisSummary(null);
            UpdateHoverValues(new Vector2(pointerEvent.position.x, pointerEvent.position.y));
            pointerEvent.StopPropagation();
            return;
        }

        if (pointerEvent.button != 0 && pointerEvent.button != 1)
            return;

        chartPanning = true;
        chartPanButton = pointerEvent.button;
        chartPanPointerId = pointerEvent.pointerId;
        chartPanPointerPosition = new Vector2(pointerEvent.position.x, pointerEvent.position.y);
        chartHost.CapturePointer(chartPanPointerId);
        pointerEvent.StopPropagation();
    }

    private void OnChartPointerMove(PointerMoveEvent pointerEvent)
    {
        Vector2 position = new Vector2(pointerEvent.position.x, pointerEvent.position.y);
        if (!chartPanning || chart == null || pointerEvent.pointerId != chartPanPointerId)
        {
            UpdateHoverValues(position);
            return;
        }

        Vector2 delta = position - chartPanPointerPosition;
        if (chartPanButton == 1)
        {
            float width = Mathf.Max(1.0f, chartHost.contentRect.width);
            float deltaSeconds = -delta.x / width * graphVisibleRanges[activeGraphIndex];
            chart.PanTime(deltaSeconds);
            UpdateTimeAxisPresentation();
        }
        else if (chartPanButton == 0)
        {
            float height = Mathf.Max(1.0f, chartHost.contentRect.height);
            chart.PanValues(delta.y / height);
            UpdateAxisSummary(null);
        }
        chartPanPointerPosition = position;
        UpdateHoverValues(position);
        pointerEvent.StopPropagation();
    }

    private void OnChartPointerUp(PointerUpEvent pointerEvent)
    {
        if (!chartPanning || pointerEvent.pointerId != chartPanPointerId)
            return;

        if (chartHost.HasPointerCapture(chartPanPointerId))
            chartHost.ReleasePointer(chartPanPointerId);
        chartPanning = false;
        chartPanButton = -1;
        chartPanPointerId = -1;
        pointerEvent.StopPropagation();
    }

    private void OnChartPointerCaptureOut(PointerCaptureOutEvent pointerEvent)
    {
        chartPanning = false;
        chartPanButton = -1;
        chartPanPointerId = -1;
    }

    private void OnChartPointerLeave(PointerLeaveEvent pointerEvent)
    {
        if (!chartPanning)
            HideHoverValues();
    }

    private void UpdateHoverValues(Vector2 panelPosition)
    {
        if (chart == null || selectedSignals.Count == 0)
        {
            HideHoverValues();
            return;
        }

        Vector2 localPosition = chartHost.WorldToLocal(panelPosition);
        Rect bounds = chartHost.contentRect;
        if (!bounds.Contains(localPosition))
        {
            HideHoverValues();
            return;
        }

        float cursorTime = Mathf.Lerp(
            chart.DisplayMinTime,
            chart.DisplayMaxTime,
            Mathf.InverseLerp(bounds.xMin, bounds.xMax, localPosition.x));
        bool foundSample = false;
        float sampleTime = 0.0f;
        float closestDistance = float.PositiveInfinity;
        for (int index = 0; index < selectedSignals.Count; index++)
        {
            if (!TryGetNearestPoint(selectedSignals[index].History, cursorTime, out Vector2 point))
                continue;

            float distance = Mathf.Abs(point.x - cursorTime);
            if (distance < closestDistance)
            {
                foundSample = true;
                closestDistance = distance;
                sampleTime = point.x;
            }
        }

        if (!foundSample)
        {
            HideHoverValues();
            return;
        }

        hoverTimeLabel.text = $"Simulation Time: {sampleTime:F3} s";
        for (int index = 0; index < selectedSignals.Count; index++)
        {
            SignalDefinition signal = selectedSignals[index];
            if (!hoverValueLabels.TryGetValue(signal.Id, out Label valueLabel))
                continue;

            if (TryGetNearestPoint(signal.History, sampleTime, out Vector2 point))
            {
                valueLabel.text = string.IsNullOrWhiteSpace(signal.Unit)
                    ? $"{signal.VariableName}: {point.y:G6}"
                    : $"{signal.VariableName}: {point.y:G6} {signal.Unit}";
            }
            else
            {
                valueLabel.text = $"{signal.VariableName}: N/A";
            }
        }

        float sampleX = Mathf.Lerp(
            bounds.xMin,
            bounds.xMax,
            Mathf.InverseLerp(chart.DisplayMinTime, chart.DisplayMaxTime, sampleTime));
        const float tooltipWidth = 250.0f;
        float tooltipHeight = hoverTooltip.resolvedStyle.height;
        if (!float.IsFinite(tooltipHeight) || tooltipHeight < 30.0f)
            tooltipHeight = Mathf.Min(260.0f, 34.0f + selectedSignals.Count * 19.0f);
        float tooltipLeft = sampleX + 12.0f;
        if (tooltipLeft + tooltipWidth > bounds.xMax)
            tooltipLeft = sampleX - tooltipWidth - 12.0f;
        tooltipLeft = Mathf.Clamp(tooltipLeft, bounds.xMin + 4.0f, bounds.xMax - tooltipWidth - 4.0f);
        float tooltipTop = Mathf.Clamp(
            localPosition.y - 10.0f,
            bounds.yMin + 4.0f,
            Mathf.Max(bounds.yMin + 4.0f, bounds.yMax - tooltipHeight - 4.0f));

        hoverLine.style.left = sampleX;
        hoverTooltip.style.left = tooltipLeft;
        hoverTooltip.style.top = tooltipTop;
        hoverLine.style.display = DisplayStyle.Flex;
        hoverTooltip.style.display = DisplayStyle.Flex;
    }

    private void HideHoverValues()
    {
        if (hoverLine != null)
            hoverLine.style.display = DisplayStyle.None;
        if (hoverTooltip != null)
            hoverTooltip.style.display = DisplayStyle.None;
    }

    private static bool TryGetNearestPoint(
        IReadOnlyList<Vector2> history,
        float requestedTime,
        out Vector2 point)
    {
        point = default;
        if (history == null || history.Count == 0)
            return false;

        int low = 0;
        int high = history.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            float middleTime = history[middle].x;
            if (middleTime < requestedTime)
                low = middle + 1;
            else if (middleTime > requestedTime)
                high = middle - 1;
            else
            {
                point = history[middle];
                return true;
            }
        }

        int upperIndex = Mathf.Clamp(low, 0, history.Count - 1);
        int lowerIndex = Mathf.Clamp(low - 1, 0, history.Count - 1);
        point = Mathf.Abs(history[lowerIndex].x - requestedTime) <=
                Mathf.Abs(history[upperIndex].x - requestedTime)
            ? history[lowerIndex]
            : history[upperIndex];
        return true;
    }

    private List<string> BuildSelectedUnitList()
    {
        List<string> units = new List<string>();
        HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < selectedSignals.Count; index++)
        {
            string unit = string.IsNullOrWhiteSpace(selectedSignals[index].Unit)
                ? "value"
                : selectedSignals[index].Unit;
            if (unique.Add(unit))
                units.Add(unit);
        }
        return units;
    }

    private void UpdateLegendLabel(SignalDefinition signal)
    {
        if (!legendLabels.TryGetValue(signal.Id, out Label label))
            return;
        label.text = string.IsNullOrWhiteSpace(signal.Unit)
            ? signal.VariableName
            : $"{signal.VariableName} [{signal.Unit}]";
        label.tooltip = signal.DisplayName;
    }

    private void UpdateTimeAxisPresentation()
    {
        if (chart == null)
            return;

        float minimumTime = chart.DisplayMinTime;
        float maximumTime = chart.DisplayMaxTime;
        float timeRange = maximumTime - minimumTime;
        timeMinLabel.text = minimumTime.ToString("F1", CultureInfo.InvariantCulture);
        timeQuarterLabel.text = (minimumTime + timeRange * 0.25f).ToString("F1", CultureInfo.InvariantCulture);
        timeMiddleLabel.text = (minimumTime + timeRange * 0.50f).ToString("F1", CultureInfo.InvariantCulture);
        timeThreeQuarterLabel.text = (minimumTime + timeRange * 0.75f).ToString("F1", CultureInfo.InvariantCulture);
        timeMaxLabel.text = maximumTime.ToString("F1", CultureInfo.InvariantCulture);
    }

    private void ClearHistory()
    {
        for (int index = 0; index < catalog.Count; index++)
        {
            catalog[index].History.Clear();
            catalog[index].LatestValue = double.NaN;
        }
        chart?.ClearSamples();
        HideHoverValues();
        lastSampleTime = -1.0f;
    }

    private void ExportCsv()
    {
        if (selectedSignals.Count == 0)
        {
            statusLabel.text = "Select at least one variable before export";
            return;
        }

        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "UX05");
            Directory.CreateDirectory(directory);
            string tag = SanitizeFileName(GetExperimentTag());
            string defaultFileName =
                $"TimeHistory_{tag}_Graph{activeGraphIndex + 1}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string path = RequestCsvSavePath(directory, defaultFileName);
            if (string.IsNullOrWhiteSpace(path))
            {
                statusLabel.text = "CSV export cancelled";
                return;
            }

            if (!string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
                path += ".csv";
            string selectedDirectory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(selectedDirectory))
                Directory.CreateDirectory(selectedDirectory);

            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("simulation_time_s,direction,model,variable,unit,value");
                for (int signalIndex = 0; signalIndex < selectedSignals.Count; signalIndex++)
                {
                    SignalDefinition signal = selectedSignals[signalIndex];
                    for (int pointIndex = 0; pointIndex < signal.History.Count; pointIndex++)
                    {
                        Vector2 point = signal.History[pointIndex];
                        writer.Write(point.x.ToString("R", CultureInfo.InvariantCulture));
                        writer.Write(',');
                        writer.Write(signal.Direction);
                        writer.Write(',');
                        writer.Write(EscapeCsv(signal.ModelId));
                        writer.Write(',');
                        writer.Write(EscapeCsv(signal.VariableName));
                        writer.Write(',');
                        writer.Write(EscapeCsv(signal.Unit));
                        writer.Write(',');
                        writer.WriteLine(point.y.ToString("R", CultureInfo.InvariantCulture));
                    }
                }
            }
            statusLabel.text = $"Exported: {Path.GetFileName(path)}";
            Debug.Log($"{LogTag}[Case={GetExperimentTag()}] Exported {path}");
        }
        catch (Exception exception)
        {
            statusLabel.text = "CSV export failed";
            statusLabel.EnableInClassList("ux05-graph-status--error", true);
            Debug.LogError($"{LogTag}[Case={GetExperimentTag()}] CSV export failed: {exception.Message}");
        }
    }

    private static string RequestCsvSavePath(string initialDirectory, string defaultFileName)
    {
#if UNITY_EDITOR
        return UnityEditor.EditorUtility.SaveFilePanel(
            "Export Time History CSV",
            initialDirectory,
            Path.GetFileNameWithoutExtension(defaultFileName),
            "csv");
#elif UNITY_STANDALONE_WIN
        StringBuilder fileBuffer = new StringBuilder(1024);
        fileBuffer.Append(Path.Combine(initialDirectory, defaultFileName));
        NativeSaveFileName dialog = new NativeSaveFileName
        {
            StructSize = Marshal.SizeOf(typeof(NativeSaveFileName)),
            Filter = "CSV Files (*.csv)\0*.csv\0All Files (*.*)\0*.*\0\0",
            File = fileBuffer,
            MaxFile = fileBuffer.Capacity,
            InitialDirectory = initialDirectory,
            Title = "Export Time History CSV",
            DefaultExtension = "csv",
            Flags = 0x00000002 | 0x00000008 | 0x00000800
        };
        return GetSaveFileName(ref dialog) ? dialog.File.ToString() : string.Empty;
#else
        return Path.Combine(initialDirectory, defaultFileName);
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeSaveFileName
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        [MarshalAs(UnmanagedType.LPWStr)] public string Filter;
        [MarshalAs(UnmanagedType.LPWStr)] public string CustomFilter;
        public int MaxCustomFilter;
        public int FilterIndex;
        public StringBuilder File;
        public int MaxFile;
        public StringBuilder FileTitle;
        public int MaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string InitialDirectory;
        [MarshalAs(UnmanagedType.LPWStr)] public string Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string DefaultExtension;
        public IntPtr CustomData;
        public IntPtr Hook;
        [MarshalAs(UnmanagedType.LPWStr)] public string TemplateName;
        public IntPtr Reserved;
        public int ReservedValue;
        public int ExtendedFlags;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileName(ref NativeSaveFileName saveFileName);
#endif

    private static string EscapeCsv(string value)
    {
        value = value ?? string.Empty;
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private string GetExperimentTag()
    {
        SimulationController controller = UnityEngine.Object.FindFirstObjectByType<SimulationController>();
        if (controller != null && !string.IsNullOrWhiteSpace(controller.ActiveCaseName))
            return controller.ActiveCaseName;
        return lastOrchestrator != null && !string.IsNullOrWhiteSpace(lastOrchestrator.ProfileName)
            ? lastOrchestrator.ProfileName
            : "NoCase";
    }

    private static string SanitizeFileName(string value)
    {
        StringBuilder result = new StringBuilder(value ?? "NoCase");
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int index = 0; index < result.Length; index++)
        {
            if (Array.IndexOf(invalid, result[index]) >= 0)
                result[index] = '_';
        }
        return result.ToString();
    }

    private static Color ColorFor(string id)
    {
        int hash = StringComparer.Ordinal.GetHashCode(id ?? string.Empty) & int.MaxValue;
        return Palette[hash % Palette.Length];
    }

    private static string InferUnit(string variableName)
    {
        string name = (variableName ?? string.Empty).ToLowerInvariant();
        if (name.Contains("temp") || name.EndsWith("_t") || name.Contains("t_air")) return "degC";
        if (name.Contains("pressure") || name.Contains("press")) return "kPa";
        if (name.Contains("humidity") || name.Contains("_rh") || name.Contains("rh_")) return "%";
        if (name.Contains("rpm")) return "rpm";
        if (name.Contains("freq") || name.EndsWith("hz") || name.Contains("_hz")) return "Hz";
        if (name.Contains("mfr") || name.Contains("mass_flow")) return "kg/s";
        if (name.Contains("flow")) return "m3/s";
        if (name.Contains("power")) return "W";
        if (name.Contains("current")) return "A";
        if (name.Contains("voltage")) return "V";
        if (name.Contains("pulse")) return "pulse";
        return string.Empty;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
