using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public interface IMainDashboardSimulationDataModel : IDisposable
{
    event Action<SimulationResultMetrics> MetricsUpdated;

    SimulationResultMetrics LatestMetrics { get; }
    SimulationController SimulationController { get; }
    float TargetTemperatureDegC { get; }

    void Tick();
}

public sealed class SimulationMainDashboardDataModel : IMainDashboardSimulationDataModel
{
    private const float ReferenceSearchIntervalSeconds = 1.0f;

    private SimulationResultSampler resultSampler;
    private SimulationController simulationController;
    private CoSimulationOrchestrator orchestrator;
    private float nextReferenceSearchTime;
    private bool disposed;

    public event Action<SimulationResultMetrics> MetricsUpdated;

    public SimulationResultMetrics LatestMetrics { get; private set; }
    public SimulationController SimulationController => simulationController;
    public float TargetTemperatureDegC => orchestrator != null
        ? orchestrator.RuntimeSetTemperatureDegC
        : float.NaN;

    public SimulationMainDashboardDataModel()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public void Tick()
    {
        if (disposed)
            return;

        if (resultSampler != null && simulationController != null && orchestrator != null)
            return;

        if (Time.unscaledTime < nextReferenceSearchTime)
            return;

        nextReferenceSearchTime = Time.unscaledTime + ReferenceSearchIntervalSeconds;
        if (resultSampler == null)
            AttachResultSampler(UnityEngine.Object.FindFirstObjectByType<SimulationResultSampler>());
        if (simulationController == null)
            simulationController = UnityEngine.Object.FindFirstObjectByType<SimulationController>();
        if (orchestrator == null)
            orchestrator = UnityEngine.Object.FindFirstObjectByType<CoSimulationOrchestrator>();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (resultSampler != null)
            resultSampler.MetricsUpdated -= OnMetricsUpdated;

        resultSampler = null;
        simulationController = null;
        orchestrator = null;
        LatestMetrics = null;
        MetricsUpdated = null;
    }

    private void AttachResultSampler(SimulationResultSampler sampler)
    {
        if (ReferenceEquals(resultSampler, sampler))
            return;

        if (resultSampler != null)
            resultSampler.MetricsUpdated -= OnMetricsUpdated;

        resultSampler = sampler;
        LatestMetrics = resultSampler != null ? resultSampler.LatestMetrics : null;
        if (resultSampler != null)
            resultSampler.MetricsUpdated += OnMetricsUpdated;

        MetricsUpdated?.Invoke(LatestMetrics);
    }

    private void OnMetricsUpdated(SimulationResultMetrics metrics)
    {
        LatestMetrics = metrics;
        MetricsUpdated?.Invoke(metrics);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Defer hierarchy searches to Update so scene integration stays lightweight.
        nextReferenceSearchTime = 0.0f;
    }
}
