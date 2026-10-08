using System;
using System.Globalization;
using UnityEngine.UIElements;

public sealed class SystemMonitorActuatorController : IDisposable
{
    private const string ValueUnavailableClassName = "ux04-data-table__value--unavailable";
    private const string StatusUnavailableClassName = "ux04-data-table__status--unavailable";

    private readonly SignalBinding[] signals =
    {
        new SignalBinding(
            "CompressorCurrentValue", "CompressorCurrentStatus",
            "MULTIV_FMU_WARPPER", "Comp_CurFreq", "F1"),
        new SignalBinding(
            "OutdoorFanCurrentValue", "OutdoorFanCurrentStatus",
            "MULTIV_FMU_WARPPER", "Fan_CurRPM", "F0"),
        new SignalBinding(
            "MainEevCurrentValue", "MainEevCurrentStatus",
            "MULTIV_FMU_WARPPER", "MAIN_EEV_CurPulse", "F0"),
        new SignalBinding(
            "ReversingValveCurrentValue", "ReversingValveCurrentStatus",
            "MULTIV_FMU_WARPPER", "reversing_valve_mode_flag", "F0")
    };

    public bool Initialize(VisualElement documentRoot, out string issue)
    {
        if (documentRoot == null)
        {
            issue = "UIDocument root is unavailable.";
            return false;
        }

        for (int index = 0; index < signals.Length; index++)
        {
            SignalBinding signal = signals[index];
            signal.ValueLabel = documentRoot.Q<Label>(signal.ValueElementName);
            signal.StatusLabel = documentRoot.Q<Label>(signal.StatusElementName);
            if (signal.ValueLabel == null || signal.StatusLabel == null)
            {
                issue = $"Actuator row elements are unavailable: {signal.ValueElementName}";
                Dispose();
                return false;
            }

            signals[index] = signal;
        }

        RenderUnavailable();
        issue = string.Empty;
        return true;
    }

    public void Refresh(CoSimulationOrchestrator orchestrator)
    {
        for (int index = 0; index < signals.Length; index++)
        {
            SignalBinding signal = signals[index];
            double value = double.NaN;
            bool available = orchestrator != null &&
                             orchestrator.TryReadRealSignal(
                                 signal.ModelId,
                                 signal.VariableName,
                                 out value) &&
                             !double.IsNaN(value) &&
                             !double.IsInfinity(value);
            RenderSignal(signal, available, value);
        }
    }

    public void Dispose()
    {
        for (int index = 0; index < signals.Length; index++)
        {
            SignalBinding signal = signals[index];
            signal.ValueLabel = null;
            signal.StatusLabel = null;
            signals[index] = signal;
        }
    }

    private void RenderUnavailable()
    {
        for (int index = 0; index < signals.Length; index++)
            RenderSignal(signals[index], false, double.NaN);
    }

    private static void RenderSignal(SignalBinding signal, bool available, double value)
    {
        signal.ValueLabel.text = available
            ? value.ToString(signal.Format, CultureInfo.InvariantCulture)
            : "N/A";
        signal.StatusLabel.text = available ? "Live" : "N/A";
        signal.ValueLabel.EnableInClassList(ValueUnavailableClassName, !available);
        signal.StatusLabel.EnableInClassList(StatusUnavailableClassName, !available);
    }

    private struct SignalBinding
    {
        public SignalBinding(
            string valueElementName,
            string statusElementName,
            string modelId,
            string variableName,
            string format)
        {
            ValueElementName = valueElementName;
            StatusElementName = statusElementName;
            ModelId = modelId;
            VariableName = variableName;
            Format = format;
            ValueLabel = null;
            StatusLabel = null;
        }

        public string ValueElementName { get; }
        public string StatusElementName { get; }
        public string ModelId { get; }
        public string VariableName { get; }
        public string Format { get; }
        public Label ValueLabel { get; set; }
        public Label StatusLabel { get; set; }
    }
}
