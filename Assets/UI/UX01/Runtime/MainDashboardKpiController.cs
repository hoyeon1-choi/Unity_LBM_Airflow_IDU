using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;

public sealed class MainDashboardKpiController : IDisposable
{
    private const string UnavailableValue = "—";

    private static readonly string[] CardStateClasses =
    {
        "kpi-card--unavailable",
        "kpi-card--normal",
        "kpi-card--warning",
        "kpi-card--error",
        "kpi-card--mock"
    };

    private static readonly string[] StatusStateClasses =
    {
        "kpi-card__status--unavailable",
        "kpi-card__status--normal",
        "kpi-card__status--warning",
        "kpi-card__status--error",
        "kpi-card__status--mock"
    };

    private readonly Dictionary<string, CardBinding> bindings = new Dictionary<string, CardBinding>();

    public bool Initialize(VisualElement documentRoot, out string issue)
    {
        if (documentRoot == null)
        {
            issue = "UIDocument 루트가 없습니다.";
            return false;
        }

        if (!TryAddBinding(documentRoot, "RoomAverageKpiCard", "T", "Room Avg", out issue) ||
            !TryAddBinding(documentRoot, "TemperatureDeltaKpiCard", "ΔT", "ΔT", out issue) ||
            !TryAddBinding(documentRoot, "MaxVelocityKpiCard", "V", "Max Velocity", out issue) ||
            !TryAddBinding(documentRoot, "MassErrorKpiCard", "M", "Mass Error", out issue) ||
            !TryAddBinding(documentRoot, "GpuUsageKpiCard", "GPU", "GPU Usage", out issue))
        {
            Dispose();
            return false;
        }

        issue = string.Empty;
        return true;
    }

    public void Render(IMainDashboardKpiViewModel viewModel)
    {
        if (viewModel == null)
            throw new ArgumentNullException(nameof(viewModel));

        viewModel.Refresh();
        Render("RoomAverageKpiCard", viewModel.RoomAverage);
        Render("TemperatureDeltaKpiCard", viewModel.TemperatureDelta);
        Render("MaxVelocityKpiCard", viewModel.MaxVelocity);
        Render("MassErrorKpiCard", viewModel.MassError);
        Render("GpuUsageKpiCard", viewModel.GpuUsage);
    }

    public void Dispose()
    {
        bindings.Clear();
    }

    private bool TryAddBinding(
        VisualElement documentRoot,
        string instanceName,
        string icon,
        string label,
        out string issue)
    {
        VisualElement instance = documentRoot.Q<VisualElement>(instanceName);
        VisualElement card = instance?.Q<VisualElement>("KpiCard");
        Label iconLabel = card?.Q<Label>("KpiIcon");
        Label titleLabel = card?.Q<Label>("KpiLabel");
        Label valueLabel = card?.Q<Label>("KpiValue");
        Label unitLabel = card?.Q<Label>("KpiUnit");
        Label statusLabel = card?.Q<Label>("KpiStatus");
        if (instance == null || card == null || iconLabel == null || titleLabel == null ||
            valueLabel == null || unitLabel == null || statusLabel == null)
        {
            issue = $"KPI Card 구조가 올바르지 않습니다: {instanceName}";
            return false;
        }

        iconLabel.text = icon;
        titleLabel.text = label;
        bindings.Add(
            instanceName,
            new CardBinding(card, valueLabel, unitLabel, statusLabel));
        issue = string.Empty;
        return true;
    }

    private void Render(string instanceName, MainDashboardKpiValue value)
    {
        CardBinding binding = bindings[instanceName];
        binding.Value.text = value.HasValue
            ? value.Value.ToString($"F{value.DecimalPlaces}", CultureInfo.InvariantCulture)
            : UnavailableValue;
        binding.Unit.text = value.Unit;
        binding.Status.text = value.Status;
        binding.Status.style.display = string.IsNullOrEmpty(value.Status)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
        binding.Card.tooltip = value.Status;

        for (int i = 0; i < CardStateClasses.Length; i++)
            binding.Card.RemoveFromClassList(CardStateClasses[i]);
        for (int i = 0; i < StatusStateClasses.Length; i++)
            binding.Status.RemoveFromClassList(StatusStateClasses[i]);

        string suffix = GetStateClassSuffix(value.State);
        binding.Card.AddToClassList($"kpi-card--{suffix}");
        binding.Status.AddToClassList($"kpi-card__status--{suffix}");
    }

    private static string GetStateClassSuffix(MainDashboardKpiState state)
    {
        return state switch
        {
            MainDashboardKpiState.Normal => "normal",
            MainDashboardKpiState.Warning => "warning",
            MainDashboardKpiState.Error => "error",
            MainDashboardKpiState.Mock => "mock",
            _ => "unavailable"
        };
    }

    private sealed class CardBinding
    {
        public CardBinding(VisualElement card, Label value, Label unit, Label status)
        {
            Card = card;
            Value = value;
            Unit = unit;
            Status = status;
        }

        public VisualElement Card { get; }
        public Label Value { get; }
        public Label Unit { get; }
        public Label Status { get; }
    }
}
