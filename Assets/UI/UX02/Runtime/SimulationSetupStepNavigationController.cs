using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

public sealed class SimulationSetupStepNavigationController : IDisposable
{
    public enum SimulationStep
    {
        Case,
        Model,
        Mesh,
        Physics,
        Boundary,
        Run
    }

    private const string SelectedButtonClassName = "simulation-step-item--selected";
    private const string SelectedPanelClassName = "simulation-step-panel--selected";

    private static readonly SimulationStep[] Steps =
    {
        SimulationStep.Case,
        SimulationStep.Model,
        SimulationStep.Mesh,
        SimulationStep.Physics,
        SimulationStep.Boundary,
        SimulationStep.Run
    };

    private readonly Dictionary<SimulationStep, Button> buttons =
        new Dictionary<SimulationStep, Button>();
    private readonly Dictionary<SimulationStep, VisualElement> panels =
        new Dictionary<SimulationStep, VisualElement>();
    private readonly Dictionary<SimulationStep, Action> clickHandlers =
        new Dictionary<SimulationStep, Action>();

    public SimulationStep CurrentStep { get; private set; } = SimulationStep.Case;

    public event Action<SimulationStep> CurrentStepChanged;

    public bool Initialize(VisualElement documentRoot, out string issue)
    {
        Dispose();
        if (documentRoot == null)
        {
            issue = "UIDocument root is missing.";
            return false;
        }

        for (int i = 0; i < Steps.Length; i++)
        {
            SimulationStep step = Steps[i];
            Button button = documentRoot.Q<Button>(GetButtonName(step));
            VisualElement panel = documentRoot.Q<VisualElement>(GetPanelName(step));
            if (button == null || panel == null)
            {
                issue = $"Simulation step UI is missing: {step}.";
                Dispose();
                return false;
            }

            SimulationStep capturedStep = step;
            Action handler = () => SelectStep(capturedStep);
            buttons.Add(step, button);
            panels.Add(step, panel);
            clickHandlers.Add(step, handler);
            button.clicked += handler;
        }

        SelectStep(SimulationStep.Case, false);
        issue = string.Empty;
        return true;
    }

    public bool SelectStep(SimulationStep step)
    {
        return SelectStep(step, true);
    }

    public void Dispose()
    {
        foreach (KeyValuePair<SimulationStep, Action> entry in clickHandlers)
        {
            if (buttons.TryGetValue(entry.Key, out Button button))
                button.clicked -= entry.Value;
        }

        clickHandlers.Clear();
        buttons.Clear();
        panels.Clear();
        CurrentStepChanged = null;
    }

    private bool SelectStep(SimulationStep step, bool notify)
    {
        if (!buttons.ContainsKey(step) || !panels.ContainsKey(step))
            return false;

        CurrentStep = step;
        for (int i = 0; i < Steps.Length; i++)
        {
            SimulationStep candidate = Steps[i];
            bool selected = candidate == step;
            buttons[candidate].EnableInClassList(SelectedButtonClassName, selected);
            panels[candidate].EnableInClassList(SelectedPanelClassName, selected);
            panels[candidate].style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (notify)
            CurrentStepChanged?.Invoke(step);
        return true;
    }

    private static string GetButtonName(SimulationStep step)
    {
        return step + "StepItem";
    }

    private static string GetPanelName(SimulationStep step)
    {
        return step + "Panel";
    }
}
