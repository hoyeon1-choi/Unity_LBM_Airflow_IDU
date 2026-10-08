using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;

public sealed class SystemMonitorElectricalController : IDisposable
{
    private const string ValueUnavailableClassName = "ux04-data-table__value--unavailable";
    private const string StatusUnavailableClassName = "ux04-data-table__status--unavailable";

    private readonly SignalDefinition[] definitions =
    {
        new SignalDefinition("INV1 Input Current", "Multi_V_S.Inv1__Input_Current", "A", "F1"),
        new SignalDefinition("INV1 Input Voltage", "Multi_V_S.Inv1__Input_Voltage", "V", "F1"),
        new SignalDefinition("INV1 Power Frequency", "Multi_V_S.Inv1__InputPower_Freq", "Hz", "F1"),
        new SignalDefinition("INV1 Phase Current", "Multi_V_S.Inv1__Phase_Current", "A", "F1"),
        new SignalDefinition("INV1 DC Link Voltage", "Multi_V_S.Inv1__DCLink_Voltage", "V", "F1"),
        new SignalDefinition("INV1 IPM Temperature", "Multi_V_S.Inv1__IPM_Temp", "degC", "F1")
    };

    private readonly List<RowBinding> rows = new List<RowBinding>();
    private VisualElement tableBody;
    private bool rowsBuilt;

    public bool Initialize(VisualElement documentRoot, out string issue)
    {
        if (documentRoot == null)
        {
            issue = "UIDocument root is unavailable.";
            return false;
        }

        tableBody = documentRoot.Q<VisualElement>("ElectricalTableBody");
        if (tableBody == null)
        {
            issue = "Electrical table body is unavailable.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    public void Refresh(CoSimulationOrchestrator orchestrator)
    {
        if (!rowsBuilt)
            BuildRows();

        for (int index = 0; index < rows.Count; index++)
        {
            RowBinding row = rows[index];
            double value = double.NaN;
            bool available = orchestrator != null &&
                             orchestrator.TryReadRealSignal(
                                 "Multi_V_S__Set_CFMU",
                                 row.Definition.VariableName,
                                 out value) &&
                             !double.IsNaN(value) &&
                             !double.IsInfinity(value);

            row.ValueLabel.text = available
                ? value.ToString(row.Definition.Format, CultureInfo.InvariantCulture)
                : "N/A";
            row.StatusLabel.text = available ? "Live" : "N/A";
            row.ValueLabel.EnableInClassList(ValueUnavailableClassName, !available);
            row.StatusLabel.EnableInClassList(StatusUnavailableClassName, !available);
        }
    }

    public void Dispose()
    {
        rows.Clear();
        tableBody = null;
        rowsBuilt = false;
    }

    private void BuildRows()
    {
        tableBody.Clear();
        rows.Clear();

        for (int index = 0; index < definitions.Length; index++)
        {
            SignalDefinition definition = definitions[index];
            VisualElement row = new VisualElement();
            row.AddToClassList("ux04-data-table__row");
            if ((index & 1) == 1)
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
            rows.Add(new RowBinding(definition, value, status));
        }

        rowsBuilt = true;
    }

    private static Label CreateCell(string text, string columnClass)
    {
        Label label = new Label(text);
        label.AddToClassList("ux04-data-table__cell");
        label.AddToClassList(columnClass);
        return label;
    }

    private readonly struct SignalDefinition
    {
        public SignalDefinition(string displayName, string variableName, string unit, string format)
        {
            DisplayName = displayName;
            VariableName = variableName;
            Unit = unit;
            Format = format;
        }

        public string DisplayName { get; }
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
