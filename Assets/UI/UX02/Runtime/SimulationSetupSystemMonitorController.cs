using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

public readonly struct SimulationSetupSystemMonitorSnapshot
{
    public bool IsAvailable { get; }
    public string GpuName { get; }
    public string GraphicsApi { get; }
    public float GpuUsagePercent { get; }
    public float EstimatedSolverGpuMemoryMb { get; }
    public float TotalGpuMemoryMb { get; }
    public float UnityAllocatedMemoryMb { get; }
    public float TotalSystemMemoryMb { get; }
    public float Fps { get; }

    public SimulationSetupSystemMonitorSnapshot(
        bool isAvailable, string gpuName, string graphicsApi, float gpuUsagePercent,
        float estimatedSolverGpuMemoryMb, float totalGpuMemoryMb,
        float unityAllocatedMemoryMb, float totalSystemMemoryMb, float fps)
    {
        IsAvailable = isAvailable;
        GpuName = gpuName ?? string.Empty;
        GraphicsApi = graphicsApi ?? string.Empty;
        GpuUsagePercent = gpuUsagePercent;
        EstimatedSolverGpuMemoryMb = estimatedSolverGpuMemoryMb;
        TotalGpuMemoryMb = totalGpuMemoryMb;
        UnityAllocatedMemoryMb = unityAllocatedMemoryMb;
        TotalSystemMemoryMb = totalSystemMemoryMb;
        Fps = fps;
    }
}

public interface ISimulationSetupSystemMonitorSource
{
    SimulationSetupSystemMonitorSnapshot Read();
}

public sealed class UnitySimulationSetupSystemMonitorSource : ISimulationSetupSystemMonitorSource
{
    private const float BytesPerMiB = 1024f * 1024f;
    private readonly ISimulationSetupSolverStateSource solverStateSource;
    private int previousFrameCount = -1;
    private double previousSampleTime = -1.0;
    private float fps = float.NaN;

    public UnitySimulationSetupSystemMonitorSource(ISimulationSetupSolverStateSource source)
    {
        solverStateSource = source ?? throw new ArgumentNullException(nameof(source));
    }

    public SimulationSetupSystemMonitorSnapshot Read()
    {
        UpdateFps();
        SimulationSetupSolverStateSnapshot solverState = solverStateSource.Read();
        return new SimulationSetupSystemMonitorSnapshot(
            true,
            SystemInfo.graphicsDeviceName,
            SystemInfo.graphicsDeviceType.ToString(),
            float.NaN,
            solverState.IsAvailable ? solverState.EstimatedSolverGpuMemoryMb : 0f,
            Mathf.Max(0, SystemInfo.graphicsMemorySize),
            Profiler.GetTotalAllocatedMemoryLong() / BytesPerMiB,
            Mathf.Max(0, SystemInfo.systemMemorySize),
            fps);
    }

    private void UpdateFps()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        int frame = Time.frameCount;
        if (previousFrameCount >= 0 && previousSampleTime >= 0.0)
        {
            double elapsed = now - previousSampleTime;
            if (elapsed > 0.0001)
                fps = (float)((frame - previousFrameCount) / elapsed);
        }

        previousFrameCount = frame;
        previousSampleTime = now;
    }
}

public sealed class InMemorySimulationSetupSystemMonitorSource : ISimulationSetupSystemMonitorSource
{
    public SimulationSetupSystemMonitorSnapshot Snapshot { get; set; }

    public InMemorySimulationSetupSystemMonitorSource(SimulationSetupSystemMonitorSnapshot initial)
    {
        Snapshot = initial;
    }

    public SimulationSetupSystemMonitorSnapshot Read() => Snapshot;
}

public sealed class SimulationSetupSystemMonitorController : IDisposable
{
    public const string UnavailableText = "Unavailable";

    private readonly ISimulationSetupSystemMonitorSource source;
    private readonly Label stateValue;
    private readonly Label gpuNameValue;
    private readonly Label graphicsApiValue;
    private readonly Label gpuUsageValue;
    private readonly Label gpuMemoryValue;
    private readonly Label systemMemoryValue;
    private readonly Label fpsValue;
    private readonly Label status;
    private IVisualElementScheduledItem scheduledRefresh;

    private SimulationSetupSystemMonitorController(
        ISimulationSetupSystemMonitorSource source, Label stateValue, Label gpuNameValue,
        Label graphicsApiValue, Label gpuUsageValue, Label gpuMemoryValue,
        Label systemMemoryValue, Label fpsValue, Label status)
    {
        this.source = source;
        this.stateValue = stateValue;
        this.gpuNameValue = gpuNameValue;
        this.graphicsApiValue = graphicsApiValue;
        this.gpuUsageValue = gpuUsageValue;
        this.gpuMemoryValue = gpuMemoryValue;
        this.systemMemoryValue = systemMemoryValue;
        this.fpsValue = fpsValue;
        this.status = status;
    }

    public SimulationSetupSystemMonitorSnapshot CurrentSnapshot { get; private set; }

    public static bool TryCreate(
        VisualElement root, ISimulationSetupSystemMonitorSource source,
        out SimulationSetupSystemMonitorController controller, out string issue)
    {
        controller = null;
        if (root == null || source == null)
        {
            issue = "GPU/System Monitor UI root or data source is missing.";
            return false;
        }

        Label state = root.Q<Label>("SystemMonitorStateValue");
        Label gpuName = root.Q<Label>("SystemMonitorGpuNameValue");
        Label graphicsApi = root.Q<Label>("SystemMonitorGraphicsApiValue");
        Label gpuUsage = root.Q<Label>("SystemMonitorGpuUsageValue");
        Label gpuMemory = root.Q<Label>("SystemMonitorGpuMemoryValue");
        Label systemMemory = root.Q<Label>("SystemMonitorSystemMemoryValue");
        Label fps = root.Q<Label>("SystemMonitorFpsValue");
        Label status = root.Q<Label>("SystemMonitorStatus");
        if (state == null || gpuName == null || graphicsApi == null || gpuUsage == null ||
            gpuMemory == null || systemMemory == null || fps == null || status == null)
        {
            issue = "One or more GPU/System Monitor UI elements are missing.";
            return false;
        }

        controller = new SimulationSetupSystemMonitorController(
            source, state, gpuName, graphicsApi, gpuUsage, gpuMemory, systemMemory, fps, status);
        controller.Refresh();
        controller.scheduledRefresh = state.schedule.Execute(controller.Refresh).Every(500);
        issue = string.Empty;
        return true;
    }

    public void Refresh()
    {
        CurrentSnapshot = source.Read();
        if (!CurrentSnapshot.IsAvailable)
        {
            stateValue.text = "Unavailable";
            gpuNameValue.text = "-";
            graphicsApiValue.text = "-";
            gpuUsageValue.text = UnavailableText;
            gpuUsageValue.EnableInClassList("system-monitor__value--unavailable", true);
            gpuMemoryValue.text = "-";
            systemMemoryValue.text = "-";
            fpsValue.text = "-";
            status.text = "System monitor data is unavailable.";
            return;
        }

        stateValue.text = "Live";
        gpuNameValue.text = string.IsNullOrWhiteSpace(CurrentSnapshot.GpuName)
            ? UnavailableText : CurrentSnapshot.GpuName;
        graphicsApiValue.text = string.IsNullOrWhiteSpace(CurrentSnapshot.GraphicsApi)
            ? UnavailableText : CurrentSnapshot.GraphicsApi;
        gpuUsageValue.text = IsFinite(CurrentSnapshot.GpuUsagePercent)
            ? CurrentSnapshot.GpuUsagePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%"
            : UnavailableText;
        gpuUsageValue.EnableInClassList(
            "system-monitor__value--unavailable", !IsFinite(CurrentSnapshot.GpuUsagePercent));
        gpuMemoryValue.text = FormatGpuMemory(
            CurrentSnapshot.EstimatedSolverGpuMemoryMb, CurrentSnapshot.TotalGpuMemoryMb);
        systemMemoryValue.text = FormatSystemMemory(
            CurrentSnapshot.UnityAllocatedMemoryMb, CurrentSnapshot.TotalSystemMemoryMb);
        fpsValue.text = IsFinite(CurrentSnapshot.Fps) && CurrentSnapshot.Fps >= 0f
            ? CurrentSnapshot.Fps.ToString("0.0", CultureInfo.InvariantCulture)
            : UnavailableText;
        status.text = IsFinite(CurrentSnapshot.GpuUsagePercent)
            ? "GPU utilization is supplied by the configured monitor provider."
            : "GPU utilization is unavailable without a platform-specific provider; no Native Plugin is installed.";
    }

    public void Dispose()
    {
        scheduledRefresh?.Pause();
        scheduledRefresh = null;
    }

    private static string FormatGpuMemory(float estimatedMb, float totalMb)
    {
        bool hasEstimate = IsFinite(estimatedMb) && estimatedMb > 0f;
        bool hasTotal = IsFinite(totalMb) && totalMb > 0f;
        if (hasEstimate && hasTotal)
            return $"Est. {FormatNumber(estimatedMb, "0.0")} / {FormatNumber(totalMb, "0")} MiB";
        if (hasEstimate)
            return $"Est. {FormatNumber(estimatedMb, "0.0")} MiB";
        if (hasTotal)
            return $"Total {FormatNumber(totalMb, "0")} MiB";
        return UnavailableText;
    }

    private static string FormatSystemMemory(float allocatedMb, float totalMb)
    {
        bool hasAllocated = IsFinite(allocatedMb) && allocatedMb >= 0f;
        bool hasTotal = IsFinite(totalMb) && totalMb > 0f;
        if (hasAllocated && hasTotal)
            return $"Unity {FormatNumber(allocatedMb, "0.0")} / {FormatNumber(totalMb, "0")} MiB";
        if (hasAllocated)
            return $"Unity {FormatNumber(allocatedMb, "0.0")} MiB";
        if (hasTotal)
            return $"Total {FormatNumber(totalMb, "0")} MiB";
        return UnavailableText;
    }

    private static string FormatNumber(float value, string format)
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
