using System;
using UnityEngine.UIElements;

public sealed class MainDashboardSimulationStatusController : IDisposable
{
    private static readonly string[] StateClasses =
    {
        "simulation-status--idle",
        "simulation-status--running",
        "simulation-status--paused",
        "simulation-status--completed",
        "simulation-status--error"
    };

    private VisualElement panel;
    private Label stateValue;
    private Label timeValue;
    private Label timeStepValue;
    private Label gridValue;
    private Label fpsValue;
    private Label gpuValue;
    private Label memoryValue;

    public bool Initialize(VisualElement documentRoot, out string issue)
    {
        if (documentRoot == null)
        {
            issue = "UIDocument 루트가 없습니다.";
            return false;
        }

        panel = documentRoot.Q<VisualElement>("SimulationStatusPanel");
        stateValue = documentRoot.Q<Label>("SimulationStatusValue");
        timeValue = documentRoot.Q<Label>("SimulationTimeValue");
        timeStepValue = documentRoot.Q<Label>("SimulationTimeStepValue");
        gridValue = documentRoot.Q<Label>("SimulationGridValue");
        fpsValue = documentRoot.Q<Label>("SimulationFpsValue");
        gpuValue = documentRoot.Q<Label>("SimulationGpuValue");
        memoryValue = documentRoot.Q<Label>("SimulationMemoryValue");
        if (panel == null || stateValue == null || timeValue == null || timeStepValue == null ||
            gridValue == null || fpsValue == null || gpuValue == null || memoryValue == null)
        {
            issue = "Simulation Status Panel 구조가 올바르지 않습니다.";
            Dispose();
            return false;
        }

        issue = string.Empty;
        return true;
    }

    public void Render(IMainDashboardSimulationStatusViewModel viewModel)
    {
        if (viewModel == null)
            throw new ArgumentNullException(nameof(viewModel));

        viewModel.Refresh();
        for (int i = 0; i < StateClasses.Length; i++)
            panel.RemoveFromClassList(StateClasses[i]);

        string stateSuffix = GetStateSuffix(viewModel.State);
        panel.AddToClassList($"simulation-status--{stateSuffix}");
        stateValue.text = GetStateLabel(viewModel.State);
        timeValue.text = viewModel.TimeText;
        timeStepValue.text = viewModel.TimeStepText;
        gridValue.text = viewModel.GridText;
        fpsValue.text = viewModel.FpsText;
        gpuValue.text = viewModel.GpuText;
        gpuValue.tooltip = viewModel.GpuText;
        memoryValue.text = viewModel.MemoryText;
    }

    public void Dispose()
    {
        panel = null;
        stateValue = null;
        timeValue = null;
        timeStepValue = null;
        gridValue = null;
        fpsValue = null;
        gpuValue = null;
        memoryValue = null;
    }

    private static string GetStateSuffix(MainDashboardSimulationState state)
    {
        return state switch
        {
            MainDashboardSimulationState.Running => "running",
            MainDashboardSimulationState.Paused => "paused",
            MainDashboardSimulationState.Completed => "completed",
            MainDashboardSimulationState.Error => "error",
            _ => "idle"
        };
    }

    private static string GetStateLabel(MainDashboardSimulationState state)
    {
        return state switch
        {
            MainDashboardSimulationState.Running => "Running",
            MainDashboardSimulationState.Paused => "Paused",
            MainDashboardSimulationState.Completed => "Completed",
            MainDashboardSimulationState.Error => "Error",
            _ => "Idle"
        };
    }
}
