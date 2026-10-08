using System;
using UnityEngine;
using UnityEngine.UIElements;

public enum SimulationSetupRunState
{
    Idle,
    Running,
    Paused,
    Completed,
    Error
}

public interface ISimulationSetupRunTarget
{
    SimulationSetupRunState State { get; }
    string StateDetail { get; }
    bool IsResumeBlockedExternally { get; }
    bool TryStartNewRun(out string issue);
    bool TryPause(out string issue);
    bool TryResume(out string issue);
    bool TryStopPreservingResults(out string issue);
}

public sealed class InMemorySimulationSetupRunTarget : ISimulationSetupRunTarget
{
    public SimulationSetupRunState State { get; private set; } = SimulationSetupRunState.Idle;
    public string StateDetail { get; private set; } = "Ready for validation.";
    public bool IsResumeBlockedExternally { get; set; }

    public bool TryStartNewRun(out string issue)
    {
        State = SimulationSetupRunState.Running;
        StateDetail = "Validation run started.";
        issue = string.Empty;
        return true;
    }

    public bool TryPause(out string issue)
    {
        if (State != SimulationSetupRunState.Running)
        {
            issue = "Not running.";
            return false;
        }
        State = SimulationSetupRunState.Paused;
        StateDetail = "Validation run paused.";
        issue = string.Empty;
        return true;
    }

    public bool TryResume(out string issue)
    {
        if (State != SimulationSetupRunState.Paused || IsResumeBlockedExternally)
        {
            issue = "Resume unavailable.";
            return false;
        }
        State = SimulationSetupRunState.Running;
        StateDetail = "Validation run resumed.";
        issue = string.Empty;
        return true;
    }

    public bool TryStopPreservingResults(out string issue)
    {
        if (State != SimulationSetupRunState.Running && State != SimulationSetupRunState.Paused &&
            State != SimulationSetupRunState.Error)
        {
            issue = "Stop unavailable.";
            return false;
        }
        State = SimulationSetupRunState.Idle;
        StateDetail = "Validation run stopped; results preserved.";
        issue = string.Empty;
        return true;
    }

    public void SetState(SimulationSetupRunState state, string detail = null)
    {
        State = state;
        StateDetail = detail ?? state.ToString();
    }
}

public sealed class SimulationSetupRunControlController : IDisposable
{
    private readonly SimulationSetupCaseController cases;
    private readonly SimulationSetupModelController models;
    private readonly SimulationSetupMeshController mesh;
    private readonly SimulationSetupPhysicsController physics;
    private readonly SimulationSetupBoundaryController boundary;
    private readonly ISimulationSetupRunTarget target;
    private readonly Button startButton;
    private readonly Button pauseButton;
    private readonly Button resumeButton;
    private readonly Button stopButton;
    private readonly Label stateValue;
    private readonly Label stateDetail;
    private readonly Label commandStatus;
    private readonly Label caseValue;
    private readonly Label modelValue;
    private readonly Label gridValue;
    private readonly Label physicsValue;
    private readonly Label boundaryValue;
    private readonly Label readinessValue;
    private IVisualElementScheduledItem scheduledRefresh;
    private SimulationSetupRunState state;

    private SimulationSetupRunControlController(
        SimulationSetupCaseController cases,
        SimulationSetupModelController models,
        SimulationSetupMeshController mesh,
        SimulationSetupPhysicsController physics,
        SimulationSetupBoundaryController boundary,
        ISimulationSetupRunTarget target,
        Button startButton, Button pauseButton, Button resumeButton, Button stopButton,
        Label stateValue, Label stateDetail, Label commandStatus,
        Label caseValue, Label modelValue, Label gridValue, Label physicsValue,
        Label boundaryValue, Label readinessValue)
    {
        this.cases = cases;
        this.models = models;
        this.mesh = mesh;
        this.physics = physics;
        this.boundary = boundary;
        this.target = target;
        this.startButton = startButton;
        this.pauseButton = pauseButton;
        this.resumeButton = resumeButton;
        this.stopButton = stopButton;
        this.stateValue = stateValue;
        this.stateDetail = stateDetail;
        this.commandStatus = commandStatus;
        this.caseValue = caseValue;
        this.modelValue = modelValue;
        this.gridValue = gridValue;
        this.physicsValue = physicsValue;
        this.boundaryValue = boundaryValue;
        this.readinessValue = readinessValue;
    }

    public SimulationSetupRunState State => state;

    public bool TryStartNewRun()
    {
        RefreshSetupValidation();
        if (!IsSetupValid())
        {
            SetCommandStatus("Start blocked: " + GetSetupValidationIssue(), true);
            Refresh();
            return false;
        }

        bool succeeded = target.TryStartNewRun(out string issue);
        SetCommandResult(succeeded, issue,
            "New run started. Solver state and physical time were reinitialized.");
        return succeeded;
    }

    public bool TryPause()
    {
        bool succeeded = target.TryPause(out string issue);
        SetCommandResult(succeeded, issue,
            "Paused. Solver buffers, results, and physical time are preserved.");
        return succeeded;
    }

    public bool TryResume()
    {
        bool succeeded = target.TryResume(out string issue);
        SetCommandResult(succeeded, issue, "Resumed from the preserved solver state.");
        return succeeded;
    }

    public bool TryStopPreservingResults()
    {
        bool succeeded = target.TryStopPreservingResults(out string issue);
        SetCommandResult(succeeded, issue,
            "Stopped. Results and physical time are preserved; Start creates a new run.");
        return succeeded;
    }

    public static bool TryCreate(
        VisualElement root, SimulationSetupCaseController cases,
        SimulationSetupModelController models, SimulationSetupMeshController mesh,
        SimulationSetupPhysicsController physics, SimulationSetupBoundaryController boundary,
        ISimulationSetupRunTarget target,
        out SimulationSetupRunControlController controller, out string issue)
    {
        controller = null;
        if (root == null || cases == null || models == null || mesh == null ||
            physics == null || boundary == null || target == null)
        {
            issue = "Run Control dependencies are missing.";
            return false;
        }

        Button start = root.Q<Button>("SimulationStartButton");
        Button pause = root.Q<Button>("SimulationPauseButton");
        Button resume = root.Q<Button>("SimulationResumeButton");
        Button stop = root.Q<Button>("SimulationStopButton");
        Label stateLabel = root.Q<Label>("SimulationRunStateValue");
        Label detail = root.Q<Label>("SimulationRunStateDetail");
        Label status = root.Q<Label>("SimulationRunCommandStatus");
        Label caseLabel = root.Q<Label>("RunReviewCaseValue");
        Label modelLabel = root.Q<Label>("RunReviewModelValue");
        Label gridLabel = root.Q<Label>("RunReviewGridValue");
        Label physicsLabel = root.Q<Label>("RunReviewPhysicsValue");
        Label boundaryLabel = root.Q<Label>("RunReviewBoundaryValue");
        Label readinessLabel = root.Q<Label>("RunReviewReadinessValue");
        if (start == null || pause == null || resume == null || stop == null ||
            stateLabel == null || detail == null || status == null || caseLabel == null ||
            modelLabel == null || gridLabel == null || physicsLabel == null ||
            boundaryLabel == null || readinessLabel == null)
        {
            issue = "One or more Run Control UI elements are missing.";
            return false;
        }

        controller = new SimulationSetupRunControlController(
            cases, models, mesh, physics, boundary, target,
            start, pause, resume, stop, stateLabel, detail, status,
            caseLabel, modelLabel, gridLabel, physicsLabel, boundaryLabel, readinessLabel);
        controller.Register();
        controller.Refresh();
        issue = string.Empty;
        return true;
    }

    public void Refresh()
    {
        // Scene loading and the setup pages are intentionally lazy.  Refresh the
        // staged dependencies here as well so opening Run never depends on the
        // user having visited Model, Mesh, Physics, and Boundary first.
        RefreshSetupValidation();
        state = target.State;
        bool setupValid = IsSetupValid();
        startButton.SetEnabled(setupValid &&
            (state == SimulationSetupRunState.Idle || state == SimulationSetupRunState.Completed ||
             state == SimulationSetupRunState.Error));
        pauseButton.SetEnabled(state == SimulationSetupRunState.Running);
        resumeButton.SetEnabled(state == SimulationSetupRunState.Paused &&
            !target.IsResumeBlockedExternally);
        stopButton.SetEnabled(state == SimulationSetupRunState.Running ||
            state == SimulationSetupRunState.Paused || state == SimulationSetupRunState.Error);

        stateValue.text = state.ToString();
        stateDetail.text = target.StateDetail;
        foreach (string className in new[] { "idle", "running", "paused", "completed", "error" })
            stateValue.EnableInClassList("run-state--" + className, false);
        stateValue.EnableInClassList("run-state--" + state.ToString().ToLowerInvariant(), true);

        caseValue.text = string.IsNullOrWhiteSpace(cases.SelectedDefinition.Name)
            ? "Not selected" : cases.SelectedDefinition.Name;
        modelValue.text = models.HasSelection ? models.SelectedModel.Name : "Not selected";
        gridValue.text = mesh.HasEstimate
            ? $"{mesh.CurrentEstimate.Nx} x {mesh.CurrentEstimate.Ny} x {mesh.CurrentEstimate.Nz} @ {mesh.CellSizeMeters:0.000} m"
            : "Not estimated";
        SimulationSetupPhysicsConfiguration physicsConfig = physics.CurrentConfiguration;
        physicsValue.text = physics.HasConfiguration
            ? $"{physicsConfig.CollisionModel} / {physicsConfig.TurbulenceModel}"
            : "Not configured";
        boundaryValue.text = boundary.BoundaryCount > 0
            ? $"{boundary.InletCount} inlet / {boundary.OutletCount} outlet / {boundary.WallCount} wall"
            : "Not scanned";
        readinessValue.text = setupValid
            ? (state == SimulationSetupRunState.Error ? "Ready to retry" : "Ready")
            : GetSetupValidationIssue();
        readinessValue.EnableInClassList("run-review__value--error", !setupValid);
    }

    public void Dispose()
    {
        scheduledRefresh?.Pause();
        scheduledRefresh = null;
        startButton.clicked -= OnStartClicked;
        pauseButton.clicked -= OnPauseClicked;
        resumeButton.clicked -= OnResumeClicked;
        stopButton.clicked -= OnStopClicked;
    }

    private void Register()
    {
        startButton.clicked += OnStartClicked;
        pauseButton.clicked += OnPauseClicked;
        resumeButton.clicked += OnResumeClicked;
        stopButton.clicked += OnStopClicked;
        scheduledRefresh = stateValue.schedule.Execute(Refresh).Every(250);
    }

    private void OnStartClicked() => TryStartNewRun();
    private void OnPauseClicked() => TryPause();
    private void OnResumeClicked() => TryResume();
    private void OnStopClicked() => TryStopPreservingResults();

    private bool IsSetupValid()
    {
        return !string.IsNullOrWhiteSpace(cases.SelectedDefinition.Id) && models.HasSelection &&
               cases.InitialConditionsValid && mesh.HasEstimate && physics.IsValid && boundary.IsValid;
    }

    private void RefreshSetupValidation()
    {
        models.RefreshIfNeeded();
        mesh.RefreshIfNeeded();
        physics.RefreshIfNeeded();
        boundary.RefreshIfNeeded();
    }

    private string GetSetupValidationIssue()
    {
        if (string.IsNullOrWhiteSpace(cases.SelectedDefinition.Id))
            return "select a Case.";
        if (!models.HasSelection)
            return "no loaded Solver model was found.";
        if (!cases.InitialConditionsValid)
            return "the Case initial conditions are invalid.";
        if (!mesh.HasEstimate)
            return "the Mesh estimate is unavailable.";
        if (!physics.IsValid)
            return "the Physics settings are invalid.";
        if (!boundary.IsValid)
        {
            if (boundary.InletCount == 0 || boundary.OutletCount == 0)
                return $"Boundary needs an inlet and outlet (found {boundary.InletCount}/{boundary.OutletCount}).";
            return "the Boundary settings are invalid.";
        }
        return "setup validation is incomplete.";
    }

    private void SetCommandResult(bool succeeded, string issue, string successMessage)
    {
        SetCommandStatus(succeeded ? successMessage : issue, !succeeded);
        Refresh();
    }

    private void SetCommandStatus(string message, bool error)
    {
        commandStatus.text = message;
        commandStatus.EnableInClassList("run-control__status--error", error);
    }
}
