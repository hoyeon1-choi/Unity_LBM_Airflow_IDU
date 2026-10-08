public enum MainDashboardSimulationState
{
    Idle,
    Running,
    Paused,
    Completed,
    Error
}

public interface IMainDashboardSimulationStatusViewModel
{
    MainDashboardSimulationState State { get; }
    string TimeText { get; }
    string TimeStepText { get; }
    string GridText { get; }
    string FpsText { get; }
    string GpuText { get; }
    string MemoryText { get; }

    void Refresh();
}

public sealed class MainDashboardMockSimulationStatusViewModel : IMainDashboardSimulationStatusViewModel
{
    public MainDashboardMockSimulationStatusViewModel(
        MainDashboardSimulationState state = MainDashboardSimulationState.Running)
    {
        State = state;
    }

    public MainDashboardSimulationState State { get; set; }
    public string TimeText { get; private set; }
    public string TimeStepText { get; private set; }
    public string GridText { get; private set; }
    public string FpsText { get; private set; }
    public string GpuText { get; private set; }
    public string MemoryText { get; private set; }

    public void Refresh()
    {
        TimeText = "320.0 s";
        TimeStepText = "0.0200 s";
        GridText = "256 × 128 × 96";
        FpsText = "60";
        GpuText = "NVIDIA RTX 3090";
        MemoryText = "Est. 21.4 / 24.0 GB";
    }
}
