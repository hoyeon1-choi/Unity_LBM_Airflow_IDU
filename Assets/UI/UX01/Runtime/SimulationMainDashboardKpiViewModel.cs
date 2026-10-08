using System;
using UnityEngine;

public sealed class SimulationMainDashboardKpiViewModel : IMainDashboardKpiViewModel
{
    private const string WaitingStatus = "Waiting for metrics";
    private const string GpuUnavailableStatus = "GPU usage source unavailable";
    private const float SamplerSearchIntervalSeconds = 1.0f;

    private readonly Func<SimulationResultMetrics> metricsSource;
    private readonly IMainDashboardGpuUsageProvider gpuUsageProvider;
    private SimulationResultSampler resultSampler;
    private float nextSamplerSearchTime;

    public SimulationMainDashboardKpiViewModel()
    {
        metricsSource = ResolveLatestMetrics;
        gpuUsageProvider = null;
        SetUnavailableValues();
    }

    public SimulationMainDashboardKpiViewModel(Func<SimulationResultMetrics> metricsSource)
        : this(metricsSource, null)
    {
    }

    public SimulationMainDashboardKpiViewModel(
        Func<SimulationResultMetrics> metricsSource,
        IMainDashboardGpuUsageProvider gpuUsageProvider)
    {
        this.metricsSource = metricsSource ?? throw new ArgumentNullException(nameof(metricsSource));
        this.gpuUsageProvider = gpuUsageProvider;
        SetUnavailableValues();
    }

    public MainDashboardKpiValue RoomAverage { get; private set; }
    public MainDashboardKpiValue TemperatureDelta { get; private set; }
    public MainDashboardKpiValue MaxVelocity { get; private set; }
    public MainDashboardKpiValue MassError { get; private set; }
    public MainDashboardKpiValue GpuUsage { get; private set; }

    public void Refresh()
    {
        SimulationResultMetrics metrics = metricsSource();
        if (metrics == null)
        {
            SetUnavailableValues();
            RefreshGpuUsage();
            return;
        }

        RoomAverage = metrics.hasValidRoomAverage && float.IsFinite(metrics.avgRoomTemperatureDegC)
            ? MainDashboardKpiValue.Available(
                metrics.avgRoomTemperatureDegC,
                "°C",
                1,
                "Live")
            : MainDashboardKpiValue.Unavailable("°C", WaitingStatus);

        bool hasTemperatureDelta =
            metrics.hasValidInletAverage &&
            metrics.hasValidOutletAverage &&
            float.IsFinite(metrics.inletAverageTemperatureDegC) &&
            float.IsFinite(metrics.outletAverageTemperatureDegC);
        TemperatureDelta = hasTemperatureDelta
            ? MainDashboardKpiValue.Available(
                metrics.inletAverageTemperatureDegC - metrics.outletAverageTemperatureDegC,
                "°C",
                1,
                $"In {metrics.inletAverageTemperatureDegC:F1} / Out {metrics.outletAverageTemperatureDegC:F1}")
            : MainDashboardKpiValue.Unavailable("°C", WaitingStatus);

        MaxVelocity = metrics.hasValidVelocityDiagnostic && float.IsFinite(metrics.maxSpeedPhys)
            ? MainDashboardKpiValue.Available(
                metrics.maxSpeedPhys,
                "m/s",
                1,
                "Live")
            : MainDashboardKpiValue.Unavailable("m/s", WaitingStatus);

        string massStatus = string.IsNullOrWhiteSpace(metrics.massConservationStatus)
            ? "Live"
            : metrics.massConservationStatus;
        MassError = metrics.hasValidDensityDiagnostic && float.IsFinite(metrics.massResidualNormalized)
            ? MainDashboardKpiValue.Available(
                metrics.massResidualNormalized * 100.0,
                "%",
                2,
                massStatus)
            : MainDashboardKpiValue.Unavailable("%", WaitingStatus);

        RefreshGpuUsage();
    }

    private SimulationResultMetrics ResolveLatestMetrics()
    {
        if (resultSampler == null && Time.unscaledTime >= nextSamplerSearchTime)
        {
            resultSampler = UnityEngine.Object.FindFirstObjectByType<SimulationResultSampler>();
            nextSamplerSearchTime = Time.unscaledTime + SamplerSearchIntervalSeconds;
        }

        return resultSampler != null ? resultSampler.LatestMetrics : null;
    }

    private void SetUnavailableValues()
    {
        RoomAverage = MainDashboardKpiValue.Unavailable("°C", WaitingStatus);
        TemperatureDelta = MainDashboardKpiValue.Unavailable("°C", WaitingStatus);
        MaxVelocity = MainDashboardKpiValue.Unavailable("m/s", WaitingStatus);
        MassError = MainDashboardKpiValue.Unavailable("%", WaitingStatus);
        GpuUsage = MainDashboardKpiValue.Unavailable("%", GpuUnavailableStatus);
    }

    private void RefreshGpuUsage()
    {
        if (gpuUsageProvider != null &&
            gpuUsageProvider.TryGetUsagePercent(out float usagePercent))
        {
            GpuUsage = MainDashboardKpiValue.Available(
                usagePercent,
                "%",
                0,
                gpuUsageProvider.Status,
                usagePercent >= 95.0f
                    ? MainDashboardKpiState.Warning
                    : MainDashboardKpiState.Normal);
            return;
        }

        string status = gpuUsageProvider != null
            ? gpuUsageProvider.Status
            : GpuUnavailableStatus;
        GpuUsage = MainDashboardKpiValue.Unavailable("%", status);
    }
}
