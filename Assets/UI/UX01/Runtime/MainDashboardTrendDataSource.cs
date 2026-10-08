using UnityEngine;

public readonly struct MainDashboardTemperatureTrendSample
{
    public MainDashboardTemperatureTrendSample(
        float timeSeconds,
        float roomAverageDegC,
        float inletDegC,
        float outletDegC,
        float targetDegC)
    {
        TimeSeconds = timeSeconds;
        RoomAverageDegC = roomAverageDegC;
        InletDegC = inletDegC;
        OutletDegC = outletDegC;
        TargetDegC = targetDegC;
    }

    public float TimeSeconds { get; }
    public float RoomAverageDegC { get; }
    public float InletDegC { get; }
    public float OutletDegC { get; }
    public float TargetDegC { get; }
}

public readonly struct MainDashboardPerformanceTrendSample
{
    public MainDashboardPerformanceTrendSample(float timeSeconds, float gpuUsagePercent, float fps)
    {
        TimeSeconds = timeSeconds;
        GpuUsagePercent = gpuUsagePercent;
        Fps = fps;
    }

    public float TimeSeconds { get; }
    public float GpuUsagePercent { get; }
    public float Fps { get; }
}

public interface IMainDashboardPerformanceDataSource
{
    bool TryReadPerformanceSample(out MainDashboardPerformanceTrendSample sample);
}

public sealed class SimulationMainDashboardPerformanceDataSource : IMainDashboardPerformanceDataSource
{
    private readonly IMainDashboardGpuUsageProvider gpuUsageProvider;
    private int previousFrameCount = -1;
    private double previousFrameSampleTime = -1.0;

    public SimulationMainDashboardPerformanceDataSource()
    {
    }

    public SimulationMainDashboardPerformanceDataSource(
        IMainDashboardGpuUsageProvider gpuUsageProvider)
    {
        this.gpuUsageProvider = gpuUsageProvider;
    }

    public bool TryReadPerformanceSample(out MainDashboardPerformanceTrendSample sample)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        int currentFrameCount = Time.frameCount;
        if (previousFrameCount < 0 || previousFrameSampleTime < 0.0)
        {
            previousFrameCount = currentFrameCount;
            previousFrameSampleTime = now;
            sample = default;
            return false;
        }

        double elapsed = now - previousFrameSampleTime;
        float fps = elapsed > 0.0001
            ? (float)((currentFrameCount - previousFrameCount) / elapsed)
            : float.NaN;
        previousFrameCount = currentFrameCount;
        previousFrameSampleTime = now;

        float gpuUsagePercent = gpuUsageProvider != null &&
                                gpuUsageProvider.TryGetUsagePercent(out float usage)
            ? usage
            : float.NaN;
        sample = new MainDashboardPerformanceTrendSample((float)now, gpuUsagePercent, fps);
        return float.IsFinite(fps) && fps >= 0.0f;
    }

}
