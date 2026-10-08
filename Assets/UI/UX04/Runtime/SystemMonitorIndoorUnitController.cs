using System;
using System.Globalization;
using UnityEngine.UIElements;

public sealed class SystemMonitorIndoorUnitController : IDisposable
{
    private const int IndoorUnitCount = 5;
    private const string ProductModelId = "MULTIV_FMU_WARPPER";
    private const string ControllerModelId = "Multi_V_S__Set_CFMU";
    private const string AirflowModelId = "airflow";
    private const string UnavailableClassName = "ux04-indoor-table__value--unavailable";

    private readonly RowBinding[] rows = new RowBinding[IndoorUnitCount];
    private VisualElement tableBody;
    private Button indoorUnitTab;
    private bool rowsBuilt;

    public bool Initialize(VisualElement documentRoot, out string issue)
    {
        tableBody = documentRoot?.Q<VisualElement>("IndoorUnitTableBody");
        indoorUnitTab = documentRoot?.Q<Button>("IndoorUnitInfoTab");
        if (tableBody == null || indoorUnitTab == null)
        {
            issue = "Indoor-unit information UI elements are unavailable.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    public void Refresh(CoSimulationOrchestrator orchestrator)
    {
        if (!rowsBuilt)
            BuildRows();

        for (int room = 1; room <= IndoorUnitCount; room++)
        {
            RowBinding row = rows[room - 1];
            string suctionModelId = room == 1 ? AirflowModelId : $"Simple_Chamber_R{room}";
            string suctionTemperatureName = room == 1 ? "T_sensor" : "T_air_suc";
            string suctionHumidityName = room == 1 ? "RH_suction" : "RH_air_suc";

            double suctionTemperature = Read(orchestrator, suctionModelId, suctionTemperatureName);
            double suctionHumidity = Read(orchestrator, suctionModelId, suctionHumidityName);
            double dischargeTemperature = Read(
                orchestrator,
                ProductModelId,
                $"IDU_{room:00}_Air_Temp_Discharge");
            double dischargeHumidity = Read(
                orchestrator,
                ProductModelId,
                $"IDU_{room:00}_Air_RH_Discharge");
            double dischargeMassFlow = Read(
                orchestrator,
                ProductModelId,
                $"IDU_{room:00}_Air_mfr_Discharge");
            double fanMode = Read(
                orchestrator,
                ControllerModelId,
                $"IDU_{room:00}.CurSetFan");
            double eevPulse = Read(
                orchestrator,
                ControllerModelId,
                $"IDU_{room:00}.EEV_TarPulse");

            bool anySignalAvailable =
                IsFinite(suctionTemperature) || IsFinite(suctionHumidity) ||
                IsFinite(dischargeTemperature) || IsFinite(dischargeHumidity) ||
                IsFinite(dischargeMassFlow) || IsFinite(fanMode) || IsFinite(eevPulse);
            RenderState(row.State, orchestrator, room, anySignalAvailable);
            RenderValue(row.SuctionTemperature, suctionTemperature, "F1");
            RenderValue(row.SuctionHumidity, suctionHumidity, "F1");
            RenderValue(row.DischargeTemperature, dischargeTemperature, "F1");
            RenderValue(row.DischargeHumidity, dischargeHumidity, "F1");
            RenderValue(row.DischargeMassFlow, dischargeMassFlow, "F3");
            RenderValue(row.FanMode, fanMode, "F0");
            RenderValue(row.EevPulse, eevPulse, "F0");
        }
    }

    public void Dispose()
    {
        for (int index = 0; index < rows.Length; index++)
            rows[index] = null;

        tableBody = null;
        indoorUnitTab = null;
        rowsBuilt = false;
    }

    private void BuildRows()
    {
        tableBody.Clear();
        for (int room = 1; room <= IndoorUnitCount; room++)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("ux04-indoor-table__row");
            if ((room & 1) == 0)
                row.AddToClassList("ux04-indoor-table__row--alternate");
            if (room == IndoorUnitCount)
                row.AddToClassList("ux04-indoor-table__row--last");

            Label idu = CreateCell($"IDU {room}", "ux04-indoor-table__idu");
            Label state = CreateCell("N/A", "ux04-indoor-table__state");
            Label suctionTemperature = CreateValueCell("ux04-indoor-table__wide");
            Label suctionHumidity = CreateValueCell("ux04-indoor-table__wide");
            Label dischargeTemperature = CreateValueCell("ux04-indoor-table__wide");
            Label dischargeHumidity = CreateValueCell("ux04-indoor-table__wide");
            Label dischargeMassFlow = CreateValueCell("ux04-indoor-table__wide");
            Label fanMode = CreateValueCell("ux04-indoor-table__compact");
            Label eevPulse = CreateValueCell("ux04-indoor-table__eev");

            row.Add(idu);
            row.Add(state);
            row.Add(suctionTemperature);
            row.Add(suctionHumidity);
            row.Add(dischargeTemperature);
            row.Add(dischargeHumidity);
            row.Add(dischargeMassFlow);
            row.Add(fanMode);
            row.Add(eevPulse);
            tableBody.Add(row);
            rows[room - 1] = new RowBinding(
                state,
                suctionTemperature,
                suctionHumidity,
                dischargeTemperature,
                dischargeHumidity,
                dischargeMassFlow,
                fanMode,
                eevPulse);
        }

        rowsBuilt = true;
    }

    private static Label CreateCell(string text, string columnClass)
    {
        Label label = new Label(text);
        label.AddToClassList("ux04-indoor-table__cell");
        label.AddToClassList(columnClass);
        return label;
    }

    private static Label CreateValueCell(string columnClass)
    {
        Label label = CreateCell("N/A", columnClass);
        label.AddToClassList(UnavailableClassName);
        return label;
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

    private static void RenderValue(Label label, double value, string format)
    {
        bool available = IsFinite(value);
        label.text = available
            ? value.ToString(format, CultureInfo.InvariantCulture)
            : "N/A";
        label.EnableInClassList(UnavailableClassName, !available);
    }

    private static void RenderState(
        Label label,
        CoSimulationOrchestrator orchestrator,
        int room,
        bool anySignalAvailable)
    {
        label.EnableInClassList("ux04-indoor-table__state--on", false);
        label.EnableInClassList("ux04-indoor-table__state--off", false);
        label.EnableInClassList(UnavailableClassName, false);

        if (orchestrator == null || !anySignalAvailable)
        {
            label.text = "N/A";
            label.EnableInClassList(UnavailableClassName, true);
            return;
        }

        bool isOn = orchestrator.IsRuntimeIndoorUnitPowerOn(room);
        label.text = isOn ? "ON" : "OFF";
        label.EnableInClassList(
            isOn ? "ux04-indoor-table__state--on" : "ux04-indoor-table__state--off",
            true);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private sealed class RowBinding
    {
        public RowBinding(
            Label state,
            Label suctionTemperature,
            Label suctionHumidity,
            Label dischargeTemperature,
            Label dischargeHumidity,
            Label dischargeMassFlow,
            Label fanMode,
            Label eevPulse)
        {
            State = state;
            SuctionTemperature = suctionTemperature;
            SuctionHumidity = suctionHumidity;
            DischargeTemperature = dischargeTemperature;
            DischargeHumidity = dischargeHumidity;
            DischargeMassFlow = dischargeMassFlow;
            FanMode = fanMode;
            EevPulse = eevPulse;
        }

        public Label State { get; }
        public Label SuctionTemperature { get; }
        public Label SuctionHumidity { get; }
        public Label DischargeTemperature { get; }
        public Label DischargeHumidity { get; }
        public Label DischargeMassFlow { get; }
        public Label FanMode { get; }
        public Label EevPulse { get; }
    }
}
