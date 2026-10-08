using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;

public sealed class SystemMonitorSensorController : IDisposable
{
    private const string SelectedTabClassName = "ux04-sensor-tab--selected";
    private const string ValueUnavailableClassName = "ux04-data-table__value--unavailable";
    private const string StatusUnavailableClassName = "ux04-data-table__status--unavailable";

    private static readonly string[] TabElementNames =
    {
        "SensorTemperatureTab",
        "SensorPressureTab",
        "SensorFlowTab",
        "SensorHumidityTab",
        "SensorOtherTab"
    };

    private readonly SignalDefinition[] definitions =
    {
        new SignalDefinition(SensorCategory.Temperature, "Outdoor Air Temperature", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_OutAir", "degC", "F1"),
        new SignalDefinition(SensorCategory.Temperature, "Discharge Temperature", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Discharge", "degC", "F1"),
        new SignalDefinition(SensorCategory.Temperature, "Suction Temperature", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Suction", "degC", "F1"),
        new SignalDefinition(SensorCategory.Temperature, "Liquid Pipe Temperature", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Liquid", "degC", "F1"),
        new SignalDefinition(SensorCategory.Temperature, "HEX Pipe Temperature", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_HEXPipe", "degC", "F1"),
        new SignalDefinition(SensorCategory.Temperature, "Subcooler Inlet Temperature", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_SC_In", "degC", "F1"),
        new SignalDefinition(SensorCategory.Temperature, "Subcooler Outlet Temperature", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_SC_Out", "degC", "F1"),
        new SignalDefinition(SensorCategory.Pressure, "High Pressure", "MULTIV_FMU_WARPPER", "ODU_Sensor_Pressure_HI", "kPa", "F1"),
        new SignalDefinition(SensorCategory.Pressure, "Low Pressure", "MULTIV_FMU_WARPPER", "ODU_Sensor_Pressure_LO", "kPa", "F1"),
        new SignalDefinition(SensorCategory.Flow, "R1 Discharge Air Flow", "MULTIV_FMU_WARPPER", "IDU_01_Air_mfr_Discharge", "kg/s", "F3"),
        new SignalDefinition(SensorCategory.Flow, "R2 Discharge Air Flow", "MULTIV_FMU_WARPPER", "IDU_02_Air_mfr_Discharge", "kg/s", "F3"),
        new SignalDefinition(SensorCategory.Flow, "R3 Discharge Air Flow", "MULTIV_FMU_WARPPER", "IDU_03_Air_mfr_Discharge", "kg/s", "F3"),
        new SignalDefinition(SensorCategory.Flow, "R4 Discharge Air Flow", "MULTIV_FMU_WARPPER", "IDU_04_Air_mfr_Discharge", "kg/s", "F3"),
        new SignalDefinition(SensorCategory.Flow, "R5 Discharge Air Flow", "MULTIV_FMU_WARPPER", "IDU_05_Air_mfr_Discharge", "kg/s", "F3"),
        new SignalDefinition(SensorCategory.Humidity, "R1 Discharge Air Humidity", "MULTIV_FMU_WARPPER", "IDU_01_Air_RH_Discharge", "%", "F1"),
        new SignalDefinition(SensorCategory.Humidity, "R2 Discharge Air Humidity", "MULTIV_FMU_WARPPER", "IDU_02_Air_RH_Discharge", "%", "F1"),
        new SignalDefinition(SensorCategory.Humidity, "R3 Discharge Air Humidity", "MULTIV_FMU_WARPPER", "IDU_03_Air_RH_Discharge", "%", "F1"),
        new SignalDefinition(SensorCategory.Humidity, "R4 Discharge Air Humidity", "MULTIV_FMU_WARPPER", "IDU_04_Air_RH_Discharge", "%", "F1"),
        new SignalDefinition(SensorCategory.Humidity, "R5 Discharge Air Humidity", "MULTIV_FMU_WARPPER", "IDU_05_Air_RH_Discharge", "%", "F1")
    };

    private readonly Button[] tabButtons = new Button[5];
    private readonly Action[] tabHandlers = new Action[5];
    private readonly List<RowBinding> activeRows = new List<RowBinding>();
    private VisualElement tableBody;
    private Label emptyState;
    private SensorCategory currentCategory = SensorCategory.Temperature;
    private CoSimulationOrchestrator lastOrchestrator;
    private bool rowsBuilt;

    public bool Initialize(VisualElement documentRoot, out string issue)
    {
        if (documentRoot == null)
        {
            issue = "UIDocument root is unavailable.";
            return false;
        }

        tableBody = documentRoot.Q<VisualElement>("SensorTableBody");
        emptyState = documentRoot.Q<Label>("SensorEmptyState");
        if (tableBody == null || emptyState == null)
        {
            issue = "Sensor table elements are unavailable.";
            return false;
        }

        for (int index = 0; index < tabButtons.Length; index++)
        {
            tabButtons[index] = documentRoot.Q<Button>(TabElementNames[index]);
            if (tabButtons[index] == null)
            {
                issue = $"Sensor tab is unavailable: {TabElementNames[index]}";
                Dispose();
                return false;
            }

            SensorCategory category = (SensorCategory)index;
            Action handler = () => SelectCategory(category);
            tabHandlers[index] = handler;
            tabButtons[index].clicked += handler;
        }

        issue = string.Empty;
        return true;
    }

    public void Refresh(CoSimulationOrchestrator orchestrator)
    {
        lastOrchestrator = orchestrator;
        if (!rowsBuilt)
            BuildRows();

        for (int index = 0; index < activeRows.Count; index++)
        {
            RowBinding row = activeRows[index];
            double value = double.NaN;
            bool available = orchestrator != null &&
                             orchestrator.TryReadRealSignal(
                                 row.Definition.ModelId,
                                 row.Definition.VariableName,
                                 out value) &&
                             !double.IsNaN(value) &&
                             !double.IsInfinity(value);
            RenderRow(row, available, value);
        }
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

        activeRows.Clear();
        tableBody = null;
        emptyState = null;
        lastOrchestrator = null;
        rowsBuilt = false;
    }

    private void SelectCategory(SensorCategory category)
    {
        if (currentCategory == category && rowsBuilt)
            return;

        currentCategory = category;
        BuildRows();
        Refresh(lastOrchestrator);
    }

    private void BuildRows()
    {
        tableBody.Clear();
        activeRows.Clear();

        for (int index = 0; index < tabButtons.Length; index++)
            tabButtons[index].EnableInClassList(
                SelectedTabClassName,
                index == (int)currentCategory);

        int rowIndex = 0;
        for (int index = 0; index < definitions.Length; index++)
        {
            SignalDefinition definition = definitions[index];
            if (definition.Category != currentCategory)
                continue;

            activeRows.Add(CreateRow(definition, rowIndex++));
        }

        bool isEmpty = activeRows.Count == 0;
        emptyState.style.display = isEmpty ? DisplayStyle.Flex : DisplayStyle.None;
        rowsBuilt = true;
    }

    private RowBinding CreateRow(SignalDefinition definition, int rowIndex)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("ux04-data-table__row");
        if ((rowIndex & 1) == 1)
            row.AddToClassList("ux04-data-table__row--alternate");

        Label item = CreateCell(definition.DisplayName, "ux04-data-table__item");
        Label value = CreateCell("N/A", "ux04-data-table__value");
        Label unit = CreateCell(definition.Unit, "ux04-data-table__unit");
        Label status = CreateCell("N/A", "ux04-data-table__status");
        value.AddToClassList(ValueUnavailableClassName);
        status.AddToClassList(StatusUnavailableClassName);
        row.Add(item);
        row.Add(value);
        row.Add(unit);
        row.Add(status);
        tableBody.Add(row);
        return new RowBinding(definition, value, status);
    }

    private static Label CreateCell(string text, string columnClass)
    {
        Label label = new Label(text);
        label.AddToClassList("ux04-data-table__cell");
        label.AddToClassList(columnClass);
        return label;
    }

    private static void RenderRow(RowBinding row, bool available, double value)
    {
        row.ValueLabel.text = available
            ? value.ToString(row.Definition.Format, CultureInfo.InvariantCulture)
            : "N/A";
        row.StatusLabel.text = available ? "Live" : "N/A";
        row.ValueLabel.EnableInClassList(ValueUnavailableClassName, !available);
        row.StatusLabel.EnableInClassList(StatusUnavailableClassName, !available);
    }

    private enum SensorCategory
    {
        Temperature,
        Pressure,
        Flow,
        Humidity,
        Other
    }

    private readonly struct SignalDefinition
    {
        public SignalDefinition(
            SensorCategory category,
            string displayName,
            string modelId,
            string variableName,
            string unit,
            string format)
        {
            Category = category;
            DisplayName = displayName;
            ModelId = modelId;
            VariableName = variableName;
            Unit = unit;
            Format = format;
        }

        public SensorCategory Category { get; }
        public string DisplayName { get; }
        public string ModelId { get; }
        public string VariableName { get; }
        public string Unit { get; }
        public string Format { get; }
    }

    private readonly struct RowBinding
    {
        public RowBinding(SignalDefinition definition, Label valueLabel, Label statusLabel)
        {
            Definition = definition;
            ValueLabel = valueLabel;
            StatusLabel = statusLabel;
        }

        public SignalDefinition Definition { get; }
        public Label ValueLabel { get; }
        public Label StatusLabel { get; }
    }
}
