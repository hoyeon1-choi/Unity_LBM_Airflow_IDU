using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct SimulationSetupSolverStateSnapshot
{
    public bool IsAvailable { get; }
    public string ErrorMessage { get; }
    public SimulationSetupRunState RunState { get; }
    public string StateDetail { get; }
    public bool IsResumeBlockedExternally { get; }
    public string ActiveCaseName { get; }
    public float SimulationTimeSeconds { get; }
    public bool UsesTargetTime { get; }
    public float TargetTimeSeconds { get; }
    public float TimeStepSeconds { get; }
    public ulong StepCount { get; }
    public bool IsCompleted { get; }
    public float EstimatedSolverGpuMemoryMb { get; }
    public float TemperatureMinDegC { get; }
    public float TemperatureMaxDegC { get; }
    public float ReferenceTemperatureDegC { get; }
    public float PrandtlTarget { get; }
    public float TurbulentPrandtl { get; }
    public float ThermalExpansionBeta { get; }
    public float GravityPhysicalY { get; }
    public float GravityLatticeY { get; }

    public SimulationSetupSolverStateSnapshot(
        bool isAvailable, string errorMessage, SimulationSetupRunState runState,
        string stateDetail, bool isResumeBlockedExternally, string activeCaseName,
        float simulationTimeSeconds, bool usesTargetTime, float targetTimeSeconds,
        float timeStepSeconds, ulong stepCount, bool isCompleted,
        float estimatedSolverGpuMemoryMb, float temperatureMinDegC,
        float temperatureMaxDegC, float referenceTemperatureDegC,
        float prandtlTarget, float turbulentPrandtl, float thermalExpansionBeta,
        float gravityPhysicalY, float gravityLatticeY)
    {
        IsAvailable = isAvailable;
        ErrorMessage = errorMessage ?? string.Empty;
        RunState = runState;
        StateDetail = stateDetail ?? string.Empty;
        IsResumeBlockedExternally = isResumeBlockedExternally;
        ActiveCaseName = activeCaseName ?? string.Empty;
        SimulationTimeSeconds = simulationTimeSeconds;
        UsesTargetTime = usesTargetTime;
        TargetTimeSeconds = targetTimeSeconds;
        TimeStepSeconds = timeStepSeconds;
        StepCount = stepCount;
        IsCompleted = isCompleted;
        EstimatedSolverGpuMemoryMb = estimatedSolverGpuMemoryMb;
        TemperatureMinDegC = temperatureMinDegC;
        TemperatureMaxDegC = temperatureMaxDegC;
        ReferenceTemperatureDegC = referenceTemperatureDegC;
        PrandtlTarget = prandtlTarget;
        TurbulentPrandtl = turbulentPrandtl;
        ThermalExpansionBeta = thermalExpansionBeta;
        GravityPhysicalY = gravityPhysicalY;
        GravityLatticeY = gravityLatticeY;
    }

    public static SimulationSetupSolverStateSnapshot Unavailable(string issue)
    {
        return new SimulationSetupSolverStateSnapshot(
            false, issue, SimulationSetupRunState.Error, issue, false, string.Empty,
            0f, false, 0f, 0f, 0, false, 0f, 0f, 30f, 30f, 0.71f, 0.7f,
            0f, -9.81f, 0f);
    }
}

public interface ISimulationSetupSolverStateSource
{
    SimulationSetupSolverStateSnapshot Read();
}

public sealed class SimulationControllerStateAdapter : ISimulationSetupSolverStateSource
{
    private readonly Func<SimulationController> controllerSource;

    public SimulationControllerStateAdapter(Func<SimulationController> source)
    {
        controllerSource = source ?? throw new ArgumentNullException(nameof(source));
    }

    public SimulationSetupSolverStateSnapshot Read()
    {
        try
        {
            SimulationController controller = controllerSource();
            if (controller == null)
                return SimulationSetupSolverStateSnapshot.Unavailable(
                    "No SimulationController is bound to the selected model.");

            SimulationSetupRunState state = ResolveRunState(controller);
            return new SimulationSetupSolverStateSnapshot(
                true, string.Empty, state, ResolveStateDetail(controller, state),
                controller.IsExternallyPaused, SafeCase(controller),
                controller.SimulatedTimeSeconds, controller.UseTargetSimulationTime,
                controller.TargetSimulationTimeSeconds, controller.DtPhys,
                controller.StepCount, controller.TargetTimeReached,
                controller.EstimatedTotalGpuMemoryMB, controller.TempPhysMinDegC,
                controller.TempPhysMaxDegC, controller.ReferenceTemperatureDegC,
                controller.PrandtlTarget, controller.TurbulentPrandtl,
                controller.ThermalExpansionBeta, controller.GravityPhysicalY,
                controller.GravityLatticeY);
        }
        catch (Exception exception)
        {
            string issue = $"Solver state read failed: {exception.Message}";
            Debug.LogError($"[UX02][F10][Case=StateRead] {issue}\n{exception}");
            return SimulationSetupSolverStateSnapshot.Unavailable(issue);
        }
    }

    private static SimulationSetupRunState ResolveRunState(SimulationController controller)
    {
        if (controller.ReadinessStatus == SimulationHealthStatus.Invalid ||
            controller.StabilityStatus == SimulationHealthStatus.Invalid)
            return SimulationSetupRunState.Error;
        if (controller.TargetTimeReached)
            return SimulationSetupRunState.Completed;
        if (controller.IsExternallyPaused || (!controller.IsSimulationRunning && controller.StepCount > 0))
            return SimulationSetupRunState.Paused;
        return controller.IsSimulationRunning
            ? SimulationSetupRunState.Running
            : SimulationSetupRunState.Idle;
    }

    private static string ResolveStateDetail(
        SimulationController controller, SimulationSetupRunState state)
    {
        if (controller.ReadinessStatus == SimulationHealthStatus.Invalid)
            return controller.ReadinessStatusText;
        if (controller.StabilityStatus == SimulationHealthStatus.Invalid)
            return controller.StabilityStatusText;

        switch (state)
        {
            case SimulationSetupRunState.Completed:
                return $"Target time reached at {controller.SimulatedTimeSeconds:0.###} s.";
            case SimulationSetupRunState.Running:
                return $"Running at t={controller.SimulatedTimeSeconds:0.###} s.";
            case SimulationSetupRunState.Paused:
                return controller.IsExternallyPaused
                    ? "Paused by the FMU/co-simulation synchronization gate."
                    : $"Paused at t={controller.SimulatedTimeSeconds:0.###} s.";
            default:
                return "Ready for a new run.";
        }
    }

    private static string SafeCase(SimulationController controller)
    {
        return string.IsNullOrWhiteSpace(controller.ActiveCaseName)
            ? "Manual"
            : controller.ActiveCaseName;
    }
}

public readonly struct SimulationSetupRunRequest
{
    public SimulationSetupCaseDefinition Case { get; }
    public SimulationSetupInitialConditions InitialConditions { get; }
    public float CellSizeMeters { get; }
    public SimulationSetupPhysicsConfiguration Physics { get; }
    public IReadOnlyList<SimulationSetupBoundaryConfiguration> Boundaries { get; }

    public SimulationSetupRunRequest(
        SimulationSetupCaseDefinition selectedCase,
        SimulationSetupInitialConditions initialConditions, float cellSizeMeters,
        SimulationSetupPhysicsConfiguration physics,
        IReadOnlyList<SimulationSetupBoundaryConfiguration> boundaries)
    {
        Case = selectedCase;
        InitialConditions = initialConditions;
        CellSizeMeters = cellSizeMeters;
        Physics = physics;
        Boundaries = boundaries ?? Array.Empty<SimulationSetupBoundaryConfiguration>();
    }
}

public sealed class SimulationSetupSolverCommandAdapter : ISimulationSetupRunTarget
{
    private const string LogPrefix = "[UX02][F10]";
    private readonly Func<SimulationController> controllerSource;
    private readonly Func<SimulationSetupRunRequest> requestSource;
    private readonly ISimulationSetupSolverStateSource stateSource;
    private bool explicitlyStopped;
    private string lastError = string.Empty;

    public SimulationSetupSolverCommandAdapter(
        Func<SimulationController> controllerSource,
        Func<SimulationSetupRunRequest> requestSource,
        ISimulationSetupSolverStateSource stateSource)
    {
        this.controllerSource = controllerSource ?? throw new ArgumentNullException(nameof(controllerSource));
        this.requestSource = requestSource ?? throw new ArgumentNullException(nameof(requestSource));
        this.stateSource = stateSource ?? throw new ArgumentNullException(nameof(stateSource));
    }

    public SimulationSetupRunState State
    {
        get
        {
            if (!string.IsNullOrEmpty(lastError))
                return SimulationSetupRunState.Error;
            SimulationSetupRunState state = stateSource.Read().RunState;
            return explicitlyStopped && state == SimulationSetupRunState.Paused
                ? SimulationSetupRunState.Idle
                : state;
        }
    }

    public string StateDetail
    {
        get
        {
            if (!string.IsNullOrEmpty(lastError))
                return lastError;
            SimulationSetupSolverStateSnapshot snapshot = stateSource.Read();
            if (explicitlyStopped && snapshot.RunState == SimulationSetupRunState.Paused)
                return $"Stopped at t={snapshot.SimulationTimeSeconds:0.###} s. Results are preserved.";
            return snapshot.StateDetail;
        }
    }

    public bool IsResumeBlockedExternally => stateSource.Read().IsResumeBlockedExternally;

    public bool TryStartNewRun(out string issue)
    {
        SimulationController controller = null;
        string caseName = "Unbound";
        try
        {
            controller = controllerSource();
            if (controller == null)
                return Fail("No SimulationController is bound to the selected model.", caseName, out issue);
            SimulationSetupRunRequest request = requestSource();
            caseName = string.IsNullOrWhiteSpace(request.Case.Name) ? "Manual" : request.Case.Name;
            if (!TryResolveTurbulence(request.Physics.TurbulenceModel, out SimulationController.TurbulenceModel turbulence))
                return Fail($"Unsupported turbulence model: {request.Physics.TurbulenceModel}.", caseName, out issue);
            if (!string.Equals(request.Physics.CollisionModel, "MRT", StringComparison.OrdinalIgnoreCase))
                return Fail("Only MRT collision is supported by the current solver.", caseName, out issue);
            if (!request.Physics.ThermalEnabled)
                return Fail("The current solver requires the D3Q7 thermal field.", caseName, out issue);
            if (!TryValidateInitialConditions(request.InitialConditions, out string initialConditionIssue))
                return Fail(initialConditionIssue, caseName, out issue);

            controller.SetSimulationRunning(false);
            float beta = request.Physics.BuoyancyEnabled
                ? request.Physics.ThermalExpansionBeta
                : 0f;
            if (!controller.TryApplySetupConfiguration(
                    caseName, request.Case.BasedOnPreset, request.CellSizeMeters,
                    request.Case.TauFluidMin, request.Physics.TauThermalMin,
                    turbulence, request.Physics.TurbulenceConstant,
                    request.Physics.TurbulentPrandtl,
                    request.Physics.TemperatureMinDegC,
                    request.Physics.TemperatureMaxDegC,
                    request.Physics.ReferenceTemperatureDegC,
                    request.Physics.PrandtlTarget, beta,
                    request.Physics.GravityPhysicalY, out string configurationIssue))
            {
                return Fail(configurationIssue, caseName, out issue);
            }

            ApplyInitialConditions(controller, request);
            ApplyBoundaries(request.Boundaries);
            controller.RebuildSolverNow();
            if (controller.ReadinessStatus == SimulationHealthStatus.Invalid || controller.LBMSolver == null)
                return Fail("Solver initialization failed. Review the readiness diagnostics.", caseName, out issue);

            controller.ResetPhysicalTime();
            explicitlyStopped = false;
            lastError = string.Empty;
            controller.SetSimulationRunning(true);
            CoSimulationStartupGate.Confirm();
            Debug.Log(
                $"{LogPrefix}[Case={caseName}] Staged setup applied; solver rebuilt and run started. " +
                $"Indoor={request.InitialConditions.IndoorTemperatureDegC:F1}C/" +
                $"{request.InitialConditions.IndoorHumidityPercent:F1}%, " +
                $"Outdoor={request.InitialConditions.OutdoorTemperatureDegC:F1}C/" +
                $"{request.InitialConditions.OutdoorHumidityPercent:F1}%, " +
                $"Set={request.InitialConditions.SetTemperatureDegC:F1}C, " +
                $"Target={request.InitialConditions.TargetSimulationTimeSeconds:F1}s.");
            issue = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            TryPauseAfterFailure(controller);
            Debug.LogError($"{LogPrefix}[Case={caseName}] Start failed: {exception}");
            return Fail($"Solver start failed: {exception.Message}", caseName, out issue, false);
        }
    }

    public bool TryPause(out string issue)
    {
        return TrySetRunning(false, false, "paused with state preserved", out issue);
    }

    public bool TryResume(out string issue)
    {
        try
        {
            SimulationSetupSolverStateSnapshot state = stateSource.Read();
            if (!state.IsAvailable)
                return Fail(state.ErrorMessage, state.ActiveCaseName, out issue);
            if (state.IsResumeBlockedExternally)
                return Fail("Resume is controlled by the active FMU/co-simulation step.", state.ActiveCaseName, out issue);
            if (explicitlyStopped || state.StepCount == 0 || state.IsCompleted ||
                state.RunState != SimulationSetupRunState.Paused)
                return Fail("The preserved run is not paused. Use Start for a new run.", state.ActiveCaseName, out issue);
            return TrySetRunning(true, false, "resumed", out issue);
        }
        catch (Exception exception)
        {
            Debug.LogError($"{LogPrefix}[Case=Resume] Command failed: {exception}");
            return Fail($"Solver resume failed: {exception.Message}", "Resume", out issue, false);
        }
    }

    public bool TryStopPreservingResults(out string issue)
    {
        return TrySetRunning(false, true, "stopped; results and physical time preserved", out issue);
    }

    private bool TrySetRunning(bool running, bool stop, string action, out string issue)
    {
        SimulationController controller = null;
        string caseName = "Unbound";
        try
        {
            controller = controllerSource();
            if (controller == null)
                return Fail("No SimulationController is available.", caseName, out issue);

            SimulationSetupSolverStateSnapshot state = stateSource.Read();
            caseName = SafeCase(controller);
            if (!state.IsAvailable)
                return Fail(state.ErrorMessage, caseName, out issue);
            if (!running && !stop && state.RunState != SimulationSetupRunState.Running)
                return Fail("Only a running simulation can be paused.", caseName, out issue);
            if (stop && state.RunState != SimulationSetupRunState.Running &&
                state.RunState != SimulationSetupRunState.Paused &&
                state.RunState != SimulationSetupRunState.Error)
                return Fail("Stop is unavailable in the current state.", caseName, out issue);

            controller.SetSimulationRunning(running);
            explicitlyStopped = stop;
            lastError = string.Empty;
            Debug.Log($"{LogPrefix}[Case={caseName}] Simulation {action}.");
            issue = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            TryPauseAfterFailure(controller);
            Debug.LogError($"{LogPrefix}[Case={caseName}] Command failed: {exception}");
            return Fail($"Solver command failed: {exception.Message}", caseName, out issue, false);
        }
    }

    private static void ApplyBoundaries(IReadOnlyList<SimulationSetupBoundaryConfiguration> boundaries)
    {
        for (int i = 0; i < boundaries.Count; i++)
        {
            SimulationSetupBoundaryConfiguration configuration = boundaries[i];
            if (configuration.SourceObject is LBMZouHeBox patch)
            {
                patch.SetKind(
                    configuration.Category == SimulationSetupBoundaryCategory.Inlet
                        ? LBMZouHeBox.Kind.Inlet
                        : LBMZouHeBox.Kind.Outlet,
                    false);
                patch.SetPower(configuration.Enabled, false);
                if (configuration.Category == SimulationSetupBoundaryCategory.Inlet)
                {
                    if (string.Equals(configuration.InputMode, "VolumeFlowRate", StringComparison.OrdinalIgnoreCase))
                        patch.SetInletVolumeFlowRateM3ps(configuration.VolumeFlowRateM3ps, false);
                    else
                        patch.SetInletVelocityPhys(configuration.VelocityPhys, false);
                    patch.SetCeilingDischargeAngleDeg(configuration.DischargeAngleDeg, false);
                    patch.SetInletTemperatureDegC(configuration.TemperatureDegC, false);
                }
                else if (configuration.Category == SimulationSetupBoundaryCategory.Outlet)
                {
                    patch.SetOutletParameters(
                        configuration.DensityTarget,
                        configuration.NormalVelocityBlend,
                        configuration.DensityAnchor,
                        false);
                }
            }
            else if (configuration.SourceObject is DeviceObstacles obstacle)
            {
                obstacle.SetThermalBoundary(configuration.ThermalBoundary, configuration.TemperatureDegC);
                obstacle.gameObject.SetActive(configuration.Enabled);
            }
        }
    }

    private static void ApplyInitialConditions(
        SimulationController controller, SimulationSetupRunRequest request)
    {
        SimulationSetupInitialConditions value = request.InitialConditions;
        controller.SetTargetSimulationTime(value.TargetSimulationTimeSeconds);
        controller.ApplyInitialRoomTemperatureDegC(value.IndoorTemperatureDegC);

        AirflowLbmSignalAdapter airflow = UnityEngine.Object.FindFirstObjectByType<AirflowLbmSignalAdapter>();
        airflow?.ApplyInitialIndoorConditions(
            value.IndoorTemperatureDegC, value.IndoorHumidityPercent);

        CoSimulationOrchestrator orchestrator =
            UnityEngine.Object.FindFirstObjectByType<CoSimulationOrchestrator>();
        if (orchestrator != null)
        {
            float dischargeAngle = ResolvePrimaryInletDischargeAngle(request.Boundaries);
            orchestrator.ApplyStartupConditions(
                value.IndoorTemperatureDegC,
                value.IndoorHumidityPercent,
                value.OutdoorTemperatureDegC,
                value.OutdoorHumidityPercent,
                value.SetTemperatureDegC,
                true,
                (int)CoSimulationInitialConditionsPanel.OperationMode.Cooling,
                (int)CoSimulationInitialConditionsPanel.FanStrength.High,
                3,
                dischargeAngle);
        }
    }

    private static float ResolvePrimaryInletDischargeAngle(
        IReadOnlyList<SimulationSetupBoundaryConfiguration> boundaries)
    {
        for (int i = 0; i < boundaries.Count; i++)
        {
            SimulationSetupBoundaryConfiguration boundary = boundaries[i];
            if (boundary.Enabled && boundary.Category == SimulationSetupBoundaryCategory.Inlet)
                return boundary.DischargeAngleDeg;
        }
        return 45f;
    }

    private static bool TryValidateInitialConditions(
        SimulationSetupInitialConditions value, out string issue)
    {
        if (!IsFinite(value.IndoorTemperatureDegC) || value.IndoorTemperatureDegC < -30f ||
            value.IndoorTemperatureDegC > 60f || !IsFinite(value.OutdoorTemperatureDegC) ||
            value.OutdoorTemperatureDegC < -50f || value.OutdoorTemperatureDegC > 70f)
        {
            issue = "Indoor/outdoor temperature is outside the supported range.";
            return false;
        }
        if (!IsFinite(value.IndoorHumidityPercent) || value.IndoorHumidityPercent < 0f ||
            value.IndoorHumidityPercent > 100f || !IsFinite(value.OutdoorHumidityPercent) ||
            value.OutdoorHumidityPercent < 0f || value.OutdoorHumidityPercent > 100f)
        {
            issue = "Indoor/outdoor humidity must be between 0 and 100%.";
            return false;
        }
        if (!IsFinite(value.SetTemperatureDegC) || !IsFinite(value.TargetSimulationTimeSeconds) ||
            value.TargetSimulationTimeSeconds <= 0f)
        {
            issue = "Set temperature or target simulation time is invalid.";
            return false;
        }
        issue = string.Empty;
        return true;
    }

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    private bool Fail(
        string message, string caseName, out string issue, bool writeLog = true)
    {
        lastError = string.IsNullOrWhiteSpace(message) ? "Unknown solver command failure." : message;
        issue = lastError;
        if (writeLog)
            Debug.LogError($"{LogPrefix}[Case={caseName}] {lastError}");
        return false;
    }

    private static void TryPauseAfterFailure(SimulationController controller)
    {
        try
        {
            controller?.SetSimulationRunning(false);
        }
        catch (Exception cleanupException)
        {
            Debug.LogError($"{LogPrefix}[Case=ExceptionCleanup] Failed to pause solver: {cleanupException}");
        }
    }

    private static bool TryResolveTurbulence(
        string value, out SimulationController.TurbulenceModel model)
    {
        if (string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
        {
            model = SimulationController.TurbulenceModel.None;
            return true;
        }
        return Enum.TryParse(value, true, out model);
    }

    private static string SafeCase(SimulationController controller)
    {
        if (controller == null || string.IsNullOrWhiteSpace(controller.ActiveCaseName))
            return "Manual";
        return controller.ActiveCaseName;
    }
}
