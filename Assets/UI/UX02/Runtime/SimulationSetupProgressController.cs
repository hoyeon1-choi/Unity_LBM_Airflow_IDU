using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

public readonly struct SimulationSetupProgressSnapshot
{
    public bool IsAvailable { get; }
    public float SimulationTimeSeconds { get; }
    public bool UsesTargetTime { get; }
    public float TargetTimeSeconds { get; }
    public float TimeStepSeconds { get; }
    public ulong StepCount { get; }
    public bool IsCompleted { get; }

    public SimulationSetupProgressSnapshot(
        bool isAvailable, float simulationTimeSeconds, bool usesTargetTime,
        float targetTimeSeconds, float timeStepSeconds, ulong stepCount, bool isCompleted)
    {
        IsAvailable = isAvailable;
        SimulationTimeSeconds = simulationTimeSeconds;
        UsesTargetTime = usesTargetTime;
        TargetTimeSeconds = targetTimeSeconds;
        TimeStepSeconds = timeStepSeconds;
        StepCount = stepCount;
        IsCompleted = isCompleted;
    }
}

public interface ISimulationSetupProgressSource
{
    SimulationSetupProgressSnapshot Read();
}

public sealed class SimulationSetupSolverProgressSource : ISimulationSetupProgressSource
{
    private readonly ISimulationSetupSolverStateSource stateSource;

    public SimulationSetupSolverProgressSource(ISimulationSetupSolverStateSource source)
    {
        stateSource = source ?? throw new ArgumentNullException(nameof(source));
    }

    public SimulationSetupProgressSnapshot Read()
    {
        SimulationSetupSolverStateSnapshot state = stateSource.Read();
        return !state.IsAvailable
            ? default
            : new SimulationSetupProgressSnapshot(
                true,
                state.SimulationTimeSeconds,
                state.UsesTargetTime,
                state.TargetTimeSeconds,
                state.TimeStepSeconds,
                state.StepCount,
                state.IsCompleted);
    }
}

public sealed class InMemorySimulationSetupProgressSource : ISimulationSetupProgressSource
{
    public SimulationSetupProgressSnapshot Snapshot { get; set; }

    public InMemorySimulationSetupProgressSource(SimulationSetupProgressSnapshot initial)
    {
        Snapshot = initial;
    }

    public SimulationSetupProgressSnapshot Read() => Snapshot;
}

public sealed class SimulationSetupProgressController : IDisposable
{
    public const string UndefinedEtaText = "Not defined";

    private readonly ISimulationSetupProgressSource source;
    private readonly VisualElement fill;
    private readonly Label percentValue;
    private readonly Label simulationTimeValue;
    private readonly Label targetTimeValue;
    private readonly Label timeStepValue;
    private readonly Label stepCountValue;
    private readonly Label estimatedRemainingValue;
    private readonly Label status;
    private IVisualElementScheduledItem scheduledRefresh;

    private SimulationSetupProgressController(
        ISimulationSetupProgressSource source, VisualElement fill, Label percentValue,
        Label simulationTimeValue, Label targetTimeValue, Label timeStepValue,
        Label stepCountValue, Label estimatedRemainingValue, Label status)
    {
        this.source = source;
        this.fill = fill;
        this.percentValue = percentValue;
        this.simulationTimeValue = simulationTimeValue;
        this.targetTimeValue = targetTimeValue;
        this.timeStepValue = timeStepValue;
        this.stepCountValue = stepCountValue;
        this.estimatedRemainingValue = estimatedRemainingValue;
        this.status = status;
    }

    public float CurrentProgress01 { get; private set; }
    public SimulationSetupProgressSnapshot CurrentSnapshot { get; private set; }

    public static bool TryCreate(
        VisualElement root, ISimulationSetupProgressSource source,
        out SimulationSetupProgressController controller, out string issue)
    {
        controller = null;
        if (root == null || source == null)
        {
            issue = "Progress UI root or data source is missing.";
            return false;
        }

        VisualElement fill = root.Q<VisualElement>("SimulationProgressFill");
        Label percent = root.Q<Label>("SimulationProgressPercentValue");
        Label simulationTime = root.Q<Label>("SimulationTimeValue");
        Label targetTime = root.Q<Label>("SimulationTargetTimeValue");
        Label timeStep = root.Q<Label>("SimulationTimeStepValue");
        Label stepCount = root.Q<Label>("SimulationStepCountValue");
        Label remaining = root.Q<Label>("SimulationEstimatedRemainingValue");
        Label status = root.Q<Label>("SimulationProgressStatus");
        if (fill == null || percent == null || simulationTime == null || targetTime == null ||
            timeStep == null || stepCount == null || remaining == null || status == null)
        {
            issue = "One or more Simulation Progress UI elements are missing.";
            return false;
        }

        controller = new SimulationSetupProgressController(
            source, fill, percent, simulationTime, targetTime, timeStep,
            stepCount, remaining, status);
        controller.Refresh();
        controller.scheduledRefresh = percent.schedule.Execute(controller.Refresh).Every(250);
        issue = string.Empty;
        return true;
    }

    public void Refresh()
    {
        CurrentSnapshot = source.Read();
        if (!CurrentSnapshot.IsAvailable)
        {
            CurrentProgress01 = 0f;
            fill.style.width = Length.Percent(0f);
            percentValue.text = "Waiting";
            simulationTimeValue.text = "-";
            targetTimeValue.text = "-";
            timeStepValue.text = "-";
            stepCountValue.text = "-";
            estimatedRemainingValue.text = UndefinedEtaText;
            status.text = "Waiting for a selected SimulationController.";
            return;
        }

        float simulationTime = SanitizeNonNegative(CurrentSnapshot.SimulationTimeSeconds);
        float timeStep = SanitizeNonNegative(CurrentSnapshot.TimeStepSeconds);
        bool hasTarget = CurrentSnapshot.UsesTargetTime &&
                         IsFinite(CurrentSnapshot.TargetTimeSeconds) &&
                         CurrentSnapshot.TargetTimeSeconds > 0f;
        CurrentProgress01 = hasTarget
            ? Mathf.Clamp01(simulationTime / CurrentSnapshot.TargetTimeSeconds)
            : 0f;
        if (CurrentSnapshot.IsCompleted && hasTarget)
            CurrentProgress01 = 1f;

        fill.style.width = Length.Percent(CurrentProgress01 * 100f);
        percentValue.text = hasTarget ? $"{CurrentProgress01 * 100f:0.0}%" : "Manual";
        simulationTimeValue.text = FormatSeconds(simulationTime);
        targetTimeValue.text = hasTarget
            ? FormatSeconds(CurrentSnapshot.TargetTimeSeconds)
            : "Manual / no target";
        timeStepValue.text = FormatTimeStep(timeStep);
        stepCountValue.text = CurrentSnapshot.StepCount.ToString("N0", CultureInfo.InvariantCulture);

        // A wall-clock ETA requires a defined averaging window and execution-rate policy.
        // Neither exists in the current solver, so F08 intentionally does not infer one.
        estimatedRemainingValue.text = UndefinedEtaText;
        status.text = hasTarget
            ? "Progress uses physical simulation time. Wall-clock ETA policy is not defined."
            : "Target simulation time is disabled. Progress and wall-clock ETA are not calculated.";
    }

    public void Dispose()
    {
        scheduledRefresh?.Pause();
        scheduledRefresh = null;
    }

    private static string FormatSeconds(float seconds)
    {
        return seconds < 0.001f && seconds > 0f
            ? seconds.ToString("0.###E+0", CultureInfo.InvariantCulture) + " s"
            : seconds.ToString("0.###", CultureInfo.InvariantCulture) + " s";
    }

    private static string FormatTimeStep(float seconds)
    {
        if (seconds <= 0f)
            return "Not initialized";
        return seconds < 0.001f
            ? seconds.ToString("0.###E+0", CultureInfo.InvariantCulture) + " s/step"
            : seconds.ToString("0.####", CultureInfo.InvariantCulture) + " s/step";
    }

    private static float SanitizeNonNegative(float value)
    {
        return IsFinite(value) ? Mathf.Max(0f, value) : 0f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
