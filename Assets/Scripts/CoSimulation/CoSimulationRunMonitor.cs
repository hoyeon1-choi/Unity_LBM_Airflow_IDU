using System;
using UnityEngine;

[AddComponentMenu("Co-Simulation/Run Monitor")]
public class CoSimulationRunMonitor : MonoBehaviour
{
    [Header("Run Control")]
    [SerializeField] private bool startSimulationOnPlay = true;
    [SerializeField] private bool runInitialCoSimStepOnStart = true;

    [Header("Health Check")]
    [SerializeField] private int minimumHealthyCoSimSteps = 1;
    [SerializeField] private bool requireAppliedInletTarget = true;
    [SerializeField] private bool logSummaryWhenSimulationStops = true;
    [SerializeField] private bool disableWhenComplete = true;
    [SerializeField] private bool quitEditorWhenComplete = false;

    [Header("References")]
    [SerializeField] private CoSimulationOrchestrator orchestrator;
    [SerializeField] private AirflowLbmSignalAdapter airflowAdapter;
    [SerializeField] private SimulationController simulationController;

    [Header("Read-Only Status")]
    [SerializeField, ReadOnly] private bool runStarted = false;
    [SerializeField, ReadOnly] private bool runFinished = false;
    [SerializeField, ReadOnly] private bool lastRunHealthy = false;
    [SerializeField, ReadOnly] private float elapsedRealtimeSeconds = 0.0f;
    [TextArea(2, 6)]
    [SerializeField, ReadOnly] private string lastSummary = "Not started.";

    private float startRealtime;
    private bool observedSimulationRunning;

    public bool RunStarted => runStarted;
    public bool RunFinished => runFinished;
    public bool LastRunHealthy => lastRunHealthy;
    public string LastSummary => lastSummary;

    public void ConfigureProductionRun(
        CoSimulationOrchestrator orchestrator,
        AirflowLbmSignalAdapter airflowAdapter,
        SimulationController simulationController,
        int minimumHealthyCoSimSteps = 1,
        bool startSimulationOnPlay = true,
        bool runInitialCoSimStepOnStart = true,
        bool quitEditorWhenComplete = false)
    {
        this.orchestrator = orchestrator;
        this.airflowAdapter = airflowAdapter;
        this.simulationController = simulationController;
        this.minimumHealthyCoSimSteps = minimumHealthyCoSimSteps;
        this.startSimulationOnPlay = startSimulationOnPlay;
        this.runInitialCoSimStepOnStart = runInitialCoSimStepOnStart;
        this.quitEditorWhenComplete = quitEditorWhenComplete;
    }

    public void Configure(
        int minCoSimSteps,
        CoSimulationOrchestrator orchestrator,
        AirflowLbmSignalAdapter airflowAdapter,
        SimulationController simulationController,
        bool quitEditorWhenComplete = false)
    {
        ConfigureProductionRun(
            orchestrator,
            airflowAdapter,
            simulationController,
            minCoSimSteps,
            true,
            true,
            quitEditorWhenComplete);
    }

    [ContextMenu("Auto Find References")]
    public void AutoFindReferences()
    {
        ResolveReferences();
    }

    [ContextMenu("Start Simulation")]
    public void StartSimulationFromInspector()
    {
        ResolveReferences();
        simulationController?.SetSimulationRunning(true);
    }

    [ContextMenu("Stop Simulation")]
    public void StopSimulationFromInspector()
    {
        ResolveReferences();
        simulationController?.SetSimulationRunning(false);
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        ResolveReferences();

        startRealtime = Time.realtimeSinceStartup;
        runStarted = true;
        runFinished = false;
        lastRunHealthy = false;
        observedSimulationRunning = false;

        if (simulationController != null && startSimulationOnPlay)
            simulationController.SetSimulationRunning(true);

        if (orchestrator != null && runInitialCoSimStepOnStart)
            orchestrator.RunOneStepFromInspector();

        observedSimulationRunning =
            simulationController == null || simulationController.IsSimulationRunning;

        string stopCondition = simulationController != null && simulationController.UseTargetSimulationTime
            ? $"SimulationController target={simulationController.TargetSimulationTimeSeconds:F3}s"
            : "SimulationController/manual stop";

        lastSummary =
            $"Started. stopCondition={stopCondition}, minimumHealthyCoSimSteps={minimumHealthyCoSimSteps}.";

        Debug.Log($"[CoSimulation] {lastSummary}");
    }

    private void Update()
    {
        if (runFinished)
            return;

        ResolveReferences();

        elapsedRealtimeSeconds = Time.realtimeSinceStartup - startRealtime;

        if (simulationController == null)
            return;

        if (!observedSimulationRunning)
        {
            observedSimulationRunning = simulationController.IsSimulationRunning;
            return;
        }

        if (!simulationController.IsSimulationRunning)
            FinishRun();
    }

    private void FinishRun()
    {
        runFinished = true;
        elapsedRealtimeSeconds = Time.realtimeSinceStartup - startRealtime;

        ulong stepIndex = orchestrator != null ? orchestrator.CoSimStepIndex : 0UL;
        double sensor = orchestrator != null ? orchestrator.LatestSensorTemperatureDegC : double.NaN;
        double setpoint = orchestrator != null ? orchestrator.LatestControllerSetpointDegC : double.NaN;
        double hz = orchestrator != null ? orchestrator.LatestHz : double.NaN;
        double plantHz = orchestrator != null ? orchestrator.LatestPlantHzInput : double.NaN;
        double discharge = orchestrator != null ? orchestrator.LatestDischargeTemperatureDegC : double.NaN;
        double applied = orchestrator != null ? orchestrator.LatestAppliedInletTemperatureDegC : double.NaN;
        int inletTargets = airflowAdapter != null ? airflowAdapter.TargetInletCount : 0;
        float simTime = simulationController != null ? simulationController.SimulatedTimeSeconds : 0f;

        lastRunHealthy =
            orchestrator != null &&
            airflowAdapter != null &&
            stepIndex >= (ulong)Mathf.Max(1, minimumHealthyCoSimSteps) &&
            (!requireAppliedInletTarget || (inletTargets > 0 && !double.IsNaN(applied)));

        lastSummary =
            $"completed={(lastRunHealthy ? "OK" : "Check")}, " +
            $"elapsedRealtime={elapsedRealtimeSeconds:F2}s, " +
            $"lbmSimTime={simTime:F6}s, " +
            $"coSimSteps={stepIndex}, " +
            $"T_sensor={sensor:F3}C, T_set={setpoint:F3}C, Hz={hz:F3}, plantHz={plantHz:F3}, " +
            $"T_dis={discharge:F3}C, appliedInlet={applied:F3}C, targetInlets={inletTargets}, " +
            $"runtime={Safe(orchestrator != null ? orchestrator.RuntimeModeSummary : null)}, " +
            $"status={Safe(orchestrator != null ? orchestrator.LastStatus : null)}";

        if (logSummaryWhenSimulationStops)
        {
            if (lastRunHealthy)
                Debug.Log($"[CoSimulation] {lastSummary}");
            else
                Debug.LogWarning($"[CoSimulation] {lastSummary}");
        }

#if UNITY_EDITOR
        if (quitEditorWhenComplete)
        {
            UnityEditor.EditorApplication.Exit(lastRunHealthy ? 0 : 1);
            return;
        }
#endif

        if (disableWhenComplete)
            enabled = false;
    }

    private void ResolveReferences()
    {
        if (simulationController == null)
            simulationController = SimulationController.Instance != null
                ? SimulationController.Instance
                : FindFirstObjectByType<SimulationController>();

        if (orchestrator == null)
            orchestrator = FindFirstObjectByType<CoSimulationOrchestrator>();

        if (airflowAdapter == null)
            airflowAdapter = FindFirstObjectByType<AirflowLbmSignalAdapter>();
    }

    private static string Safe(string value)
    {
        return string.IsNullOrEmpty(value) ? "-" : value;
    }
}
