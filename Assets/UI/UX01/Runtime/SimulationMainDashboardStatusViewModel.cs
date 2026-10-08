using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class SimulationMainDashboardStatusViewModel : IMainDashboardSimulationStatusViewModel
{
    private const string UnavailableText = "—";
    private const float ControllerSearchIntervalSeconds = 1.0f;

    private readonly Func<SimulationController> controllerSource;
    private readonly IMainDashboardGpuUsageProvider gpuUsageProvider;
    private SimulationController simulationController;
    private float nextControllerSearchTime;
    private int previousFrameCount = -1;
    private double previousFpsSampleTime = -1.0;
    private float measuredFps = -1.0f;

    public MainDashboardSimulationState State { get; private set; } = MainDashboardSimulationState.Idle;
    public string TimeText { get; private set; } = UnavailableText;
    public string TimeStepText { get; private set; } = UnavailableText;
    public string GridText { get; private set; } = UnavailableText;
    public string FpsText { get; private set; } = UnavailableText;
    public string GpuText { get; private set; } = UnavailableText;
    public string MemoryText { get; private set; } = UnavailableText;

    public SimulationMainDashboardStatusViewModel()
    {
        controllerSource = ResolveSimulationController;
        gpuUsageProvider = null;
    }

    public SimulationMainDashboardStatusViewModel(Func<SimulationController> controllerSource)
        : this(controllerSource, null)
    {
    }

    public SimulationMainDashboardStatusViewModel(
        Func<SimulationController> controllerSource,
        IMainDashboardGpuUsageProvider gpuUsageProvider)
    {
        this.controllerSource = controllerSource ?? throw new ArgumentNullException(nameof(controllerSource));
        this.gpuUsageProvider = gpuUsageProvider;
    }

    public void Refresh()
    {
        UpdateMeasuredFps();
        simulationController = controllerSource();

        string gpuName = string.IsNullOrWhiteSpace(SystemInfo.graphicsDeviceName)
            ? UnavailableText
            : SystemInfo.graphicsDeviceName;
        GpuText = gpuUsageProvider != null &&
                  gpuUsageProvider.TryGetUsagePercent(out float gpuUsagePercent)
            ? $"{gpuUsagePercent:F0}% · {gpuName}"
            : gpuName;
        FpsText = measuredFps > 0.0f ? measuredFps.ToString("F0") : UnavailableText;

        if (simulationController == null)
        {
            State = MainDashboardSimulationState.Idle;
            TimeText = UnavailableText;
            TimeStepText = UnavailableText;
            GridText = UnavailableText;
            MemoryText = FormatMemory(0.0f, SystemInfo.graphicsMemorySize);
            return;
        }

        State = ResolveState(simulationController);
        TimeText = $"{simulationController.SimulatedTimeSeconds:F1} s";
        TimeStepText = FormatTimeStep(simulationController.DtPhys);
        GridText = simulationController.Nx > 0 && simulationController.Ny > 0 && simulationController.Nz > 0
            ? $"{simulationController.Nx} × {simulationController.Ny} × {simulationController.Nz}"
            : UnavailableText;
        MemoryText = FormatMemory(
            simulationController.EstimatedTotalGpuMemoryMB,
            SystemInfo.graphicsMemorySize);
    }

    private SimulationController ResolveSimulationController()
    {
        if (simulationController != null || Time.unscaledTime < nextControllerSearchTime)
            return simulationController;

        simulationController = UnityEngine.Object.FindFirstObjectByType<SimulationController>();
        nextControllerSearchTime = Time.unscaledTime + ControllerSearchIntervalSeconds;
        return simulationController;
    }

    private void UpdateMeasuredFps()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        int currentFrameCount = Time.frameCount;
        if (previousFrameCount >= 0 && previousFpsSampleTime >= 0.0)
        {
            double elapsed = now - previousFpsSampleTime;
            if (elapsed > 0.0001)
                measuredFps = (float)((currentFrameCount - previousFrameCount) / elapsed);
        }

        previousFrameCount = currentFrameCount;
        previousFpsSampleTime = now;
    }

    private static MainDashboardSimulationState ResolveState(SimulationController controller)
    {
        if (controller.ReadinessStatus == SimulationHealthStatus.Invalid ||
            controller.StabilityStatus == SimulationHealthStatus.Invalid)
        {
            return MainDashboardSimulationState.Error;
        }

        if (controller.TargetTimeReached)
            return MainDashboardSimulationState.Completed;

        if (controller.IsExternallyPaused)
            return MainDashboardSimulationState.Paused;

        if (controller.IsSimulationRunning)
            return MainDashboardSimulationState.Running;

        return controller.StepCount > 0
            ? MainDashboardSimulationState.Paused
            : MainDashboardSimulationState.Idle;
    }

    private static string FormatTimeStep(float dtPhys)
    {
        if (!float.IsFinite(dtPhys) || dtPhys <= 0.0f)
            return UnavailableText;

        return dtPhys >= 0.001f
            ? $"{dtPhys:F4} s"
            : $"{dtPhys:E3} s";
    }

    private static string FormatMemory(float estimatedMemoryMb, int totalMemoryMb)
    {
        bool hasEstimate = float.IsFinite(estimatedMemoryMb) && estimatedMemoryMb > 0.0f;
        bool hasTotal = totalMemoryMb > 0;
        if (hasEstimate && hasTotal)
            return $"Est. {estimatedMemoryMb / 1024.0f:F1} / {totalMemoryMb / 1024.0f:F1} GB";
        if (hasEstimate)
            return $"Est. {estimatedMemoryMb / 1024.0f:F1} GB";
        if (hasTotal)
            return $"— / {totalMemoryMb / 1024.0f:F1} GB";
        return UnavailableText;
    }
}

public sealed class DashboardHeaderController
{
    private const float RefreshIntervalSeconds = 1.0f;

    private static readonly string[] StateClasses =
    {
        "top-bar__state--idle",
        "top-bar__state--running",
        "top-bar__state--paused",
        "top-bar__state--completed",
        "top-bar__state--error"
    };

    private readonly Label projectValue;
    private readonly Label caseValue;
    private readonly Label clockValue;
    private readonly VisualElement state;
    private readonly Label stateValue;
    private readonly SimulationMainDashboardStatusViewModel statusViewModel;

    private SimulationController simulationController;
    private float nextRefreshTime;

    private DashboardHeaderController(
        Label projectValue,
        Label caseValue,
        Label clockValue,
        VisualElement state,
        Label stateValue)
    {
        this.projectValue = projectValue;
        this.caseValue = caseValue;
        this.clockValue = clockValue;
        this.state = state;
        this.stateValue = stateValue;
        statusViewModel = new SimulationMainDashboardStatusViewModel(ResolveSimulationController);
    }

    public static bool TryCreate(
        VisualElement root,
        string elementPrefix,
        out DashboardHeaderController controller,
        out string issue)
    {
        controller = null;
        if (root == null)
        {
            issue = "Dashboard header root is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(elementPrefix))
        {
            issue = "Dashboard header element prefix is missing.";
            return false;
        }

        Label projectValue = root.Q<Label>($"{elementPrefix}ProjectValue");
        Label caseValue = root.Q<Label>($"{elementPrefix}CaseValue");
        Label clockValue = root.Q<Label>($"{elementPrefix}ClockValue");
        VisualElement state = root.Q<VisualElement>($"{elementPrefix}TopBarState");
        Label stateValue = root.Q<Label>($"{elementPrefix}StateValue");

        if (projectValue == null || caseValue == null || clockValue == null ||
            state == null || stateValue == null)
        {
            issue = $"Dashboard header elements for prefix '{elementPrefix}' are incomplete.";
            return false;
        }

        controller = new DashboardHeaderController(
            projectValue,
            caseValue,
            clockValue,
            state,
            stateValue);
        issue = string.Empty;
        return true;
    }

    public void Refresh(bool force = false)
    {
        if (!force && Time.unscaledTime < nextRefreshTime)
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        projectValue.text = string.IsNullOrWhiteSpace(activeScene.name) ? "—" : activeScene.name;
        projectValue.tooltip = activeScene.path;

        simulationController = ResolveSimulationController();
        caseValue.text = simulationController != null &&
                         !string.IsNullOrWhiteSpace(simulationController.ActiveCaseName)
            ? simulationController.ActiveCaseName
            : "Waiting";
        caseValue.tooltip = caseValue.text;
        clockValue.text = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");

        statusViewModel.Refresh();
        RenderState(statusViewModel.State);
        nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
    }

    private SimulationController ResolveSimulationController()
    {
        if (simulationController == null)
            simulationController = UnityEngine.Object.FindFirstObjectByType<SimulationController>();

        return simulationController;
    }

    private void RenderState(MainDashboardSimulationState simulationState)
    {
        for (int i = 0; i < StateClasses.Length; i++)
            state.RemoveFromClassList(StateClasses[i]);

        string suffix;
        string label;
        switch (simulationState)
        {
            case MainDashboardSimulationState.Running:
                suffix = "running";
                label = "RUNNING";
                break;
            case MainDashboardSimulationState.Paused:
                suffix = "paused";
                label = "PAUSED";
                break;
            case MainDashboardSimulationState.Completed:
                suffix = "completed";
                label = "DONE";
                break;
            case MainDashboardSimulationState.Error:
                suffix = "error";
                label = "ERROR";
                break;
            default:
                suffix = "idle";
                label = "IDLE";
                break;
        }

        state.AddToClassList($"top-bar__state--{suffix}");
        stateValue.text = label;
    }
}
