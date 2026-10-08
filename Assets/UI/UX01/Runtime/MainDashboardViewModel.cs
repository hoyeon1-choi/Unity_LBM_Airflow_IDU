using System;

public sealed class MainDashboardViewModel : IDisposable
{
    private readonly IMainDashboardSimulationDataModel dataModel;
    private bool disposed;

    public MainDashboardViewModel(IMainDashboardSimulationDataModel dataModel)
        : this(dataModel, null)
    {
    }

    public MainDashboardViewModel(
        IMainDashboardSimulationDataModel dataModel,
        IMainDashboardGpuUsageProvider gpuUsageProvider)
    {
        this.dataModel = dataModel ?? throw new ArgumentNullException(nameof(dataModel));
        Kpi = new SimulationMainDashboardKpiViewModel(
            () => this.dataModel.LatestMetrics,
            gpuUsageProvider);
        SimulationStatus = new SimulationMainDashboardStatusViewModel(
            () => this.dataModel.SimulationController,
            gpuUsageProvider);
        this.dataModel.MetricsUpdated += OnMetricsUpdated;
    }

    public event Action MetricsUpdated;
    public event Action<MainDashboardTemperatureTrendSample> TemperatureTrendSampled;

    public IMainDashboardKpiViewModel Kpi { get; }
    public IMainDashboardSimulationStatusViewModel SimulationStatus { get; }

    public void Tick()
    {
        if (!disposed)
            dataModel.Tick();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        dataModel.MetricsUpdated -= OnMetricsUpdated;
        dataModel.Dispose();
        MetricsUpdated = null;
        TemperatureTrendSampled = null;
    }

    private void OnMetricsUpdated(SimulationResultMetrics metrics)
    {
        MetricsUpdated?.Invoke();
        if (metrics == null ||
            (!metrics.hasValidRoomAverage && !metrics.hasValidInletAverage && !metrics.hasValidOutletAverage))
        {
            return;
        }

        TemperatureTrendSampled?.Invoke(new MainDashboardTemperatureTrendSample(
            metrics.simulationTimeSeconds,
            metrics.hasValidRoomAverage ? metrics.avgRoomTemperatureDegC : float.NaN,
            metrics.hasValidInletAverage ? metrics.inletAverageTemperatureDegC : float.NaN,
            metrics.hasValidOutletAverage ? metrics.outletAverageTemperatureDegC : float.NaN,
            dataModel.TargetTemperatureDegC));
    }
}
