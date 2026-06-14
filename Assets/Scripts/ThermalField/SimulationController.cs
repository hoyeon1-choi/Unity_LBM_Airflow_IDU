using UnityEngine;
using Unity.Mathematics;
using System.Text;
using System.Collections;
using Unity.Collections;
using UnityEngine.Rendering;

public enum SolverEasePreset
{
    FastPreview,
    Balanced,
    HighFidelity,
    Custom
}

public enum SimulationHealthStatus
{
    OK,
    Warning,
    Invalid
}

public class SimulationController : Singleton<SimulationController>
{
    [Header("User Setup")]
    [Tooltip("Main run switch. Invalid readiness status will prevent stepping even if this is enabled.")]
    [SerializeField] private bool runSimulation = true;

    [Header("Domain / Resolution")]
    [SerializeField] private GameObject domain;
    [SerializeField] private ComputeShader lbmComputeShader;
    [Tooltip("Physical lattice cell size in meters. Smaller values increase resolution and memory cost.")]
    [SerializeField] private float dxPhys = 0.01f;

    [Header("Solver Preset")]
    [SerializeField] private SolverEasePreset solverPreset = SolverEasePreset.Balanced;

    [Header("Physical Properties")]
    [Tooltip("Reference maximum physical velocity for scaling (m/s). " +
             "The maximum speed of AC Sources can not exceed this value")]
    [Range(1.0f, 10.0f), SerializeField] private float U_ref = 10.0f;
    [Tooltip("Target Kinetic Viscosity [m^2/s] (air ≈ 1.5e-5)")]
    [SerializeField] private float nuPhysTarget = 1.5e-5f;
    [Tooltip("Target Prandtl Number (air ≈ 0.71)")]
    [SerializeField] private float prandtlTarget = 0.71f;
    [SerializeField] private float beta = 0.05f;
    [SerializeField] private float gravity_y = -9.81f;

    [Header("Temperature Setup")]
    [SerializeField] private float tempPhysMinDegC = 0.0f;
    [SerializeField] private float tempPhysMaxDegC = 40.0f;
    [Tooltip("Reference temperature used for initialization and buoyancy [degC].")]
    [SerializeField] private float referenceTemperatureDegCInput = 20.0f;

    [Header("Boundary Auto Sync")]
    [SerializeField] private bool enableAdaptiveOutletRhoFeedback = true;

    [Tooltip("rho correction gain from density error: offset = gain * (1 - avgDensity)")]
    [SerializeField] private float adaptiveOutletRhoGain = 0.25f;

    [Tooltip("Maximum absolute rho offset applied to outlet patches")]
    [SerializeField] private float adaptiveOutletRhoMaxOffset = 0.02f;

    [Tooltip("Do not react to very small density error")]
    [SerializeField] private float adaptiveOutletRhoDeadband = 0.0005f;

    [SerializeField, ReadOnly] private float adaptiveOutletRhoOffset = 0.0f;
    [SerializeField, ReadOnly] private float adaptiveFeedbackAvgDensity = 1.0f;

    [Header("Mass-Flux Corrected Outlet")]
    [SerializeField] private bool enableMassFluxCorrectedOutlet = true;

    [Tooltip("Apply target outlet normal speed from inlet abs flow / outlet total area.")]
    [SerializeField] private bool useInletAbsFlowAsOutletTarget = true;

    [Tooltip("Safety clamp for controller-computed outlet normal speed [m/s].")]
    [SerializeField] private float maxOutletTargetNormalSpeedPhys = 2.0f;

    [Header("Gross vs Effective Outlet Area Debug")]
    [SerializeField] private bool logGrossVsEffectiveOutletArea = true;
    [SerializeField] private int grossVsEffectiveAreaLogIntervalSteps = 300;
    [SerializeField] private bool logEachOutletPatchDetail = true;

    [Header("Outlet Root-Cause Debug")]
    [SerializeField] private bool logOutletRootCause = true;
    [SerializeField] private int outletRootCauseLogIntervalSteps = 300;

    [SerializeField, ReadOnly] private float debugInletFlowFromSampler = 0.0f;
    [SerializeField, ReadOnly] private float debugInletFlowFromBoxes = 0.0f;
    [SerializeField, ReadOnly] private float debugOutletAreaFromBoxes = 0.0f;
    [SerializeField, ReadOnly] private bool debugAnyOutletTargetApplied = false;
    [SerializeField, ReadOnly] private int debugOutletTargetAppliedPatchCount = 0;

    [SerializeField, ReadOnly] private float effectiveOutletAreaPhys = 0.0f;
    [SerializeField, ReadOnly] private float grossToEffectiveAreaRatio = 0.0f;
    [SerializeField, ReadOnly] private float effectiveOutletNormalSpeedFromFlow = 0.0f;
    [SerializeField, ReadOnly] private float grossAreaBasedTargetNormalSpeed = 0.0f;

    [SerializeField, ReadOnly] private float computedOutletTargetNormalSpeedPhys = 0.0f;
    [SerializeField, ReadOnly] private float totalOutletAreaPhys = 0.0f;

    [Header("Memory Check")]
    [Tooltip("Logs estimated GPU memory usage before solver creation.")]
    [SerializeField] private bool logMemoryEstimate = true;
    [Tooltip("Warn when estimated core buffer usage exceeds this ratio of total VRAM budget.")]
    [Range(0.1f, 1.0f), SerializeField] private float vramWarningRatio = 0.7f;
    [Tooltip("Approximate VRAM budget in GB used only for warnings. 0 = auto detect from SystemInfo.")]
    [SerializeField] private float manualVramBudgetGB = 0.0f;

    [Header("Scene Cache (Auto)")]
    private SimulationSceneCache sceneCache;
    [SerializeField, ReadOnly] private bool sceneCacheReady = false;
    [SerializeField, ReadOnly] private string sceneCacheStatus = "Not initialized";

    [Header("Debug / Logging")]
    [SerializeField] private bool autoLogSummaryOnStart = true;
    [SerializeField] private bool autoLogSummaryOnSceneRefresh = false;

    [Header("Simulation Stop Condition")]
    [SerializeField] private bool useTargetSimulationTime = false;
    [Tooltip("Automatically stop simulation when simulated physical time reaches this value [s].")]
    [SerializeField] private float targetSimulationTimeSeconds = 100.0f;
    [SerializeField, ReadOnly] private bool targetTimeReached = false;

    [Header("Time Consistency Debug")]
    [SerializeField] private bool logTimeConsistency = true;
    [SerializeField] private int timeConsistencyLogIntervalSteps = 300;
    [SerializeField] private float timeConsistencyWarningThreshold = 1e-6f;
    [SerializeField, ReadOnly] private float expectedSimulatedTimeSeconds = 0.0f;
    [SerializeField, ReadOnly] private float simulatedTimeErrorSeconds = 0.0f;

    [Header("Read-Only (Physical Properties)")]
    [SerializeField, ReadOnly] private float tau_f = 0.6f;
    [SerializeField, ReadOnly] private float tau_T = 0.5f;

    [Header("Read-Only (Reference Temperature)")]
    [SerializeField, ReadOnly] private float T_ref = 0.5f;
    [SerializeField, ReadOnly] private float referenceTemperatureDegC = 20.0f;

    [Header("Read-Only (Scaling)")]
    [SerializeField, ReadOnly] private float csLat = 1.0f / 1.7320508075688772f;
    [SerializeField, ReadOnly] private float targetWindSpeedLat = 0.0f;
    [SerializeField, ReadOnly] private float speedScalePhysToLat = 0.0f;

    [Header("Read-Only (Diagnostics)")]
    [SerializeField, ReadOnly] private float dtPhys = 0.0f;
    [SerializeField, ReadOnly] private float gravityLat = 0.0f;
    [SerializeField, ReadOnly] private float maxDomainLengthPhys = 0.0f;
    [SerializeField, ReadOnly] private float maxDomainLengthLat = 0f;
    [SerializeField, ReadOnly] private float maxWindSpeedPhys = 0.0f;
    [SerializeField, ReadOnly] private float maxWindSpeedLat = 0.0f;
    [SerializeField, ReadOnly] private float nuLat = 0f;
    [SerializeField, ReadOnly] private float alphaLat = 0f;
    [SerializeField, ReadOnly] private float nuPhys = 0f;
    [SerializeField, ReadOnly] private float alphaPhys = 0f;
    [SerializeField, ReadOnly] private float machNumber = 0.0f;
    [SerializeField, ReadOnly] private float reynoldsNumber = 0.0f;
    [SerializeField, ReadOnly] private float reynoldsNumberPhys = 0.0f;
    [SerializeField, ReadOnly] private float prandtlNumber = 0.0f;
    [SerializeField, ReadOnly] private uint nx;
    [SerializeField, ReadOnly] private uint ny;
    [SerializeField, ReadOnly] private uint nz;
    [SerializeField, ReadOnly] private uint N;

    [Header("Read-Only (Simulation Time)")]
    [SerializeField, ReadOnly] private ulong stepCount = 0;
    [SerializeField, ReadOnly] private float simulatedTimeSeconds = 0.0f;
    [SerializeField, ReadOnly] private float simulatedTimeMinutes = 0.0f;

    [Header("Read-Only (Summary)")]
    [TextArea(10, 30)]
    [SerializeField, ReadOnly] private string latestSummary;
    [TextArea(3, 8)]
    [SerializeField, ReadOnly] private string caseSummary;
    [TextArea(3, 8)]
    [SerializeField, ReadOnly] private string boundarySummary;
    [TextArea(3, 8)]
    [SerializeField, ReadOnly] private string solverSummary;
    [TextArea(3, 8)]
    [SerializeField, ReadOnly] private string stabilitySummary;
    [TextArea(3, 8)]
    [SerializeField, ReadOnly] private string recommendationSummary;
    [TextArea(3, 8)]
    [SerializeField, ReadOnly] private string readinessSummary;
    [SerializeField, ReadOnly] private SimulationHealthStatus stabilityStatus = SimulationHealthStatus.Warning;
    [SerializeField, ReadOnly] private SimulationHealthStatus readinessStatus = SimulationHealthStatus.Warning;

    [Header("Advanced LBM Parameters")]
    [Tooltip("Lattice Mach number limit for LBM stability. Lower is usually more stable but slower.")]
    [Range(0.10f, 0.30f), SerializeField] private float maxMach = 0.25f;
    [Range(0.53f, 1.0f), SerializeField] private float tauMin = 0.56f;
    [Range(1.0f, 4.0f), SerializeField] private float tauMax = 4.00f;

    [Header("Advanced Solver Models")]
    [SerializeField] private TurbulenceModel turbulenceModel = TurbulenceModel.Smagorinsky;
    [Tooltip("Smagorinsky: Cs, WALE: Cw")]
    [SerializeField] private float turbulenceModelConstant = 0.03f;
    [Tooltip("Turbulent Prandtl number")]
    [SerializeField] private float turbulentPrandtl = 0.7f;
    [SerializeField] private bool wallFunctionEnabled = false;

    [Header("Read-Only (Estimated GPU Memory)")]
    [SerializeField, ReadOnly] private float estimatedDistributionBuffersMB = 0.0f;
    [SerializeField, ReadOnly] private float estimatedCoreBuffersMB = 0.0f;
    [SerializeField, ReadOnly] private float estimatedTexturesMB = 0.0f;
    [SerializeField, ReadOnly] private float estimatedTotalGpuMemoryMB = 0.0f;
    [SerializeField, ReadOnly] private float perDirectionBufferMB = 0.0f;
    [SerializeField, ReadOnly] private bool estimatedSingleBufferSafe = true;

    [Header("Result Sampler (Auto)")]
    private SimulationResultSampler resultSampler;

    [Header("Read-Only (Result Metrics)")]
    [SerializeField, ReadOnly] private float avgRoomTemperatureDegC = 0.0f;
    [SerializeField, ReadOnly] private float inletAverageTemperatureDegC = 0.0f;
    [SerializeField, ReadOnly] private float outletAverageTemperatureDegC = 0.0f;
    [SerializeField, ReadOnly] private float inletAverageSpeedPhys = 0.0f;
    [SerializeField, ReadOnly] private float outletAverageSpeedPhys = 0.0f;
    [SerializeField, ReadOnly] private string resultMetricsStatus = "Result sampler not initialized.";

    [SerializeField, ReadOnly] private float inletAverageNormalSpeedPhys = 0.0f;
    [SerializeField, ReadOnly] private float outletAverageNormalSpeedPhys = 0.0f;
    [SerializeField, ReadOnly] private float inletFlowRatePhysSigned = 0.0f;
    [SerializeField, ReadOnly] private float outletFlowRatePhysSigned = 0.0f;
    [SerializeField, ReadOnly] private float inletFlowRatePhysAbs = 0.0f;
    [SerializeField, ReadOnly] private float outletFlowRatePhysAbs = 0.0f;
    [SerializeField, ReadOnly] private float netFlowRatePhysSigned = 0.0f;
    [SerializeField, ReadOnly] private float relativeFlowImbalance = 0.0f;

    [SerializeField, ReadOnly] private uint thermalInletClampCount = 0u;
    [SerializeField, ReadOnly] private uint thermalOutletClampCount = 0u;
    [SerializeField, ReadOnly] private uint fluidInletClampCount = 0u;
    [SerializeField, ReadOnly] private uint fluidOutletClampCount = 0u;

    private float lx, ly, lz;
    private bool scalingDirty = true;
    private bool solverRebuildRequired = true;
    private bool summaryDirty = true;

    const float eps = 1e-6f;
    private const long MaxGraphicsBufferBytes = 2147483648L;
    private const int Q_f = 19;
    private const int Q_t = 7;
    private const int Q_total = Q_f + Q_t;
    private const long BytesPerFloat = 4L;
    private const long BytesPerUint = 4L;
    private const long BytesPerFloat4 = 16L;
    private const long BytesPerVelocityTextureVoxel = 16L;
    private const long BytesPerThermalTextureVoxel = 4L;

    private ThermalSolver _lbmSolver;
    public ThermalSolver LBMSolver => _lbmSolver;

    [Header("For Zou-He BC")]
    public Transform DomainRoot => domain != null ? domain.transform : null;
    public float CellSize => dxPhys;
    public uint Nx => nx;
    public uint Ny => ny;
    public uint Nz => nz;
    public float Lx => lx;
    public float Ly => ly;
    public float Lz => lz;
    public float DtPhys => dtPhys;
    public float MaxWindSpeedPhys => maxWindSpeedPhys;
    public ulong StepCount => stepCount;
    public float SimulatedTimeSeconds => simulatedTimeSeconds;
    public float TempPhysMinDegC => tempPhysMinDegC;
    public float TempPhysMaxDegC => tempPhysMaxDegC;
    public bool IsSimulationRunning => runSimulation;
    public SolverEasePreset SolverPreset => solverPreset;
    public string SolverPresetName => solverPreset.ToString();
    public SimulationHealthStatus StabilityStatus => stabilityStatus;
    public SimulationHealthStatus ReadinessStatus => readinessStatus;
    public string StabilityStatusText => stabilitySummary;
    public string ReadinessStatusText => readinessSummary;
    public float MachNumber => machNumber;
    public float ReynoldsNumber => reynoldsNumberPhys;
    public float PrandtlNumber => prandtlNumber;
    public float TauF => tau_f;
    public float TauT => tau_T;
    public float MaxMachLimit => maxMach;
    public string CollisionModelName => "MRT";
    public string TurbulenceModelName => turbulenceModel.ToString();
    public float TurbulenceModelConstant => turbulenceModelConstant;
    public float TurbulentPrandtl => turbulentPrandtl;
    public bool WallFunctionEnabled => wallFunctionEnabled;
    public bool UseTargetSimulationTime => useTargetSimulationTime;
    public float TargetSimulationTimeSeconds => targetSimulationTimeSeconds;
    public bool TargetTimeReached => targetTimeReached;
    public bool IsSolverReadyForReadback
    {
        get
        {
            if (!runSimulation)
                return false;

            if (_lbmSolver == null)
                return false;

            if (scalingDirty || solverRebuildRequired)
                return false;

            if (sceneCache != null && sceneCache.IsDirty)
                return false;

            return true;
        }
    }

    public float LatticeSpeedToPhysicalScale
    {
        get
        {
            if (dtPhys <= 1e-8f) return 0f;
            return dxPhys / dtPhys;
        }
    }

    public enum TurbulenceModel
    {
        None = 0,
        Smagorinsky = 1,
        WALE = 2
    }

    public void SetSimulationRunning(bool running)
    {
        runSimulation = running;
        if (running)
            targetTimeReached = false;
    }

    protected override void Awake()
    {
        base.Awake();

        sceneCache = GetComponent<SimulationSceneCache>();
        if (sceneCache == null)
            sceneCache = gameObject.AddComponent<SimulationSceneCache>();

        resultSampler = GetComponent<SimulationResultSampler>();
        if (resultSampler == null)
            resultSampler = gameObject.AddComponent<SimulationResultSampler>();

        UpdateSceneCacheStatus();
    }

    void Start()
    {
        if (sceneCache != null)
        {
            sceneCache.ForceRefresh();
            sceneCache.LogIfEmpty();
        }

        UpdateSceneCacheStatus();

        RebuildScaling();
        ValidateAndLogMemoryEstimate();

        RebuildSolver();
        ResetAdaptiveOutletRhoFeedback();
        ResetMassFluxCorrectedOutletTargets();
        ApplyMassFluxCorrectedOutletTarget();
        MarkSummaryDirty();
        RefreshSummaryIfNeeded();

        CheckAndLogTimeConsistency(true);

        if (autoLogSummaryOnStart)
            Debug.Log(latestSummary);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        scalingDirty = true;
        solverRebuildRequired = true;

        if (sceneCache == null)
            sceneCache = GetComponent<SimulationSceneCache>();

        if (resultSampler == null)
            resultSampler = GetComponent<SimulationResultSampler>();

        UpdateSceneCacheStatus();
        MarkSummaryDirty();
    }
#endif

    void Update()
    {
        if (scalingDirty)
        {
            if (sceneCache != null)
            {
                sceneCache.ForceRefresh();
                sceneCache.LogIfEmpty();
            }

            UpdateSceneCacheStatus();

            RebuildScaling();
            ValidateAndLogMemoryEstimate();

            if (solverRebuildRequired)
                RebuildSolver();

            ResetMassFluxCorrectedOutletTargets();
            ApplyMassFluxCorrectedOutletTarget();

            MarkSummaryDirty();
            RefreshSummaryIfNeeded();
            CheckAndLogTimeConsistency(true);

            scalingDirty = false;
        }

        if (sceneCache != null && sceneCache.IsDirty)
        {
            sceneCache.Refresh();
            sceneCache.LogIfEmpty();
            UpdateSceneCacheStatus();

            RecalculateACSourceScaling();

            _lbmSolver?.SyncSourcesAtRuntime(
                sceneCache.HeatSources,
                sceneCache.Racks,
                sceneCache.ACSources,
                sceneCache.ZouHeBoxes);

            _lbmSolver?.MarkAllDynamicInputsDirty();

            ResetMassFluxCorrectedOutletTargets();
            ApplyMassFluxCorrectedOutletTarget();

            MarkSummaryDirty();
            RefreshSummaryIfNeeded();
            CheckAndLogTimeConsistency(true);

            if (autoLogSummaryOnSceneRefresh)
                Debug.Log(latestSummary);
        }
        else
        {
            UpdateSceneCacheStatus();
        }

        if (!runSimulation || _lbmSolver == null)
            return;

        if (readinessStatus == SimulationHealthStatus.Invalid)
            return;

        float nextSimulatedTimeSeconds = (stepCount + 1) * dtPhys;

        if (useTargetSimulationTime && nextSimulatedTimeSeconds > targetSimulationTimeSeconds)
        {
            simulatedTimeSeconds = stepCount * dtPhys;
            simulatedTimeMinutes = simulatedTimeSeconds / 60.0f;
            targetTimeReached = true;
            runSimulation = false;

            Debug.Log(
                $"[SimulationController] Target simulation time reached before next step. " +
                $"Target = {targetSimulationTimeSeconds:F3}s, Current = {simulatedTimeSeconds:F3}s. " +
                "Simulation stopped automatically.");

            PullLatestResultMetrics();
            MarkSummaryDirty();
            RefreshSummaryIfNeeded();
            CheckAndLogTimeConsistency(true);
            return;
        }

        _lbmSolver.SetCollisionAndForcing(tau_f, tau_T, gravityLat);
        _lbmSolver.SetTurbulenceModel(
            (ThermalSolver.TurbulenceModel)turbulenceModel,
            turbulenceModelConstant,
            turbulentPrandtl);
        _lbmSolver.updateShaderParameters();
        _lbmSolver.ResetDebugThermalClampCounters();
        _lbmSolver.Step();

        stepCount++;
        simulatedTimeSeconds = stepCount * dtPhys;
        simulatedTimeMinutes = simulatedTimeSeconds / 60.0f;
        targetTimeReached = false;

        PullLatestResultMetrics();

        ApplyMassFluxCorrectedOutletTarget();
        LogGrossVsEffectiveOutletAreaComparison();

        if (ApplyAdaptiveOutletRhoFeedback())
        {
            PushAdaptiveOutletRhoToSolver();
        }

        MarkSummaryDirty();
        RefreshSummaryIfNeeded();
        CheckAndLogTimeConsistency();
    }

    void OnDestroy()
    {
        _lbmSolver?.Dispose();
        _lbmSolver = null;
    }

    [ContextMenu("Refresh Scene Cache")]
    public void RefreshSceneCacheNow()
    {
        if (sceneCache == null)
        {
            UpdateSceneCacheStatus();
            Debug.LogWarning("Scene cache is not available.");
            return;
        }

        sceneCache.ForceRefresh();
        UpdateSceneCacheStatus();
        RecalculateACSourceScaling();

        if (_lbmSolver != null)
        {
            _lbmSolver.SyncSourcesAtRuntime(
                sceneCache.HeatSources,
                sceneCache.Racks,
                sceneCache.ACSources,
                sceneCache.ZouHeBoxes);
        }

        ResetMassFluxCorrectedOutletTargets();
        ApplyMassFluxCorrectedOutletTarget();

        MarkSummaryDirty();
        RefreshSummaryIfNeeded();
        CheckAndLogTimeConsistency(true);
        Debug.Log(latestSummary);
    }

    [ContextMenu("Rebuild Solver")]
    public void RebuildSolverNow()
    {
        if (sceneCache != null)
            sceneCache.ForceRefresh();

        UpdateSceneCacheStatus();

        RebuildScaling();
        ValidateAndLogMemoryEstimate();

        RebuildSolver();
        ResetAdaptiveOutletRhoFeedback();
        ResetMassFluxCorrectedOutletTargets();
        ApplyMassFluxCorrectedOutletTarget();

        MarkSummaryDirty();
        RefreshSummaryIfNeeded();
        CheckAndLogTimeConsistency(true);
        Debug.Log(latestSummary);
    }

    [ContextMenu("Apply Solver Preset")]
    public void ApplySolverPreset()
    {
        switch (solverPreset)
        {
            case SolverEasePreset.FastPreview:
                dxPhys = Mathf.Max(dxPhys, 0.05f);
                maxMach = 0.25f;
                logGrossVsEffectiveOutletArea = false;
                logOutletRootCause = false;
                break;

            case SolverEasePreset.Balanced:
                dxPhys = Mathf.Clamp(dxPhys, 0.01f, 0.05f);
                maxMach = 0.20f;
                logGrossVsEffectiveOutletArea = true;
                logOutletRootCause = false;
                break;

            case SolverEasePreset.HighFidelity:
                dxPhys = Mathf.Min(dxPhys, 0.01f);
                maxMach = 0.15f;
                logGrossVsEffectiveOutletArea = true;
                logOutletRootCause = true;
                break;

            case SolverEasePreset.Custom:
            default:
                break;
        }

        scalingDirty = true;
        solverRebuildRequired = true;
        MarkSummaryDirty();
        RefreshSummaryIfNeeded();
    }

    [ContextMenu("Check Run Readiness")]
    public void PrintRunReadiness()
    {
        CheckRunReadiness();
        RefreshSummaryIfNeeded();

        if (readinessStatus == SimulationHealthStatus.Invalid)
            Debug.LogError(readinessSummary);
        else if (readinessStatus == SimulationHealthStatus.Warning)
            Debug.LogWarning(readinessSummary);
        else
            Debug.Log(readinessSummary);
    }

    [ContextMenu("Print Simulation Summary")]
    public void PrintSimulationSummary()
    {
        MarkSummaryDirty();
        RefreshSummaryIfNeeded();
        Debug.Log(latestSummary);
    }

    [ContextMenu("Print Time Consistency Check")]
    public void PrintTimeConsistencyCheck()
    {
        CheckAndLogTimeConsistency(true);
    }

    [ContextMenu("Reset Physical Time")]
    public void ResetPhysicalTime()
    {
        stepCount = 0;
        simulatedTimeSeconds = 0.0f;
        simulatedTimeMinutes = 0.0f;
        expectedSimulatedTimeSeconds = 0.0f;
        simulatedTimeErrorSeconds = 0.0f;
        targetTimeReached = false;
        computedOutletTargetNormalSpeedPhys = 0.0f;
        totalOutletAreaPhys = 0.0f;
        MarkSummaryDirty();
        RefreshSummaryIfNeeded();
        CheckAndLogTimeConsistency(true);
        ResetAdaptiveOutletRhoFeedback();
        ResetMassFluxCorrectedOutletTargets();
    }

    [ContextMenu("Force Sample Result Metrics")]
    public void ForceSampleResultMetrics()
    {
        if (resultSampler == null)
        {
            Debug.LogWarning("Result sampler is null.");
            return;
        }

        resultSampler.ForceSampleNow();
    }

    public float TemperatureLBMToDegC(float tempLBM)
    {
        return Mathf.Lerp(tempPhysMinDegC, tempPhysMaxDegC, Mathf.Clamp01(tempLBM));
    }

    public float TemperatureDegCToLBM(float tempDegC)
    {
        if (tempPhysMaxDegC <= tempPhysMinDegC)
            return 0.5f;

        return Mathf.InverseLerp(tempPhysMinDegC, tempPhysMaxDegC, tempDegC);
    }

    private void UpdateSceneCacheStatus()
    {
        sceneCacheReady = sceneCache != null;

        if (!sceneCacheReady)
        {
            sceneCacheStatus = "Scene cache not connected yet.";
            return;
        }

        int heatSourceCount = sceneCache.HeatSources != null ? sceneCache.HeatSources.Length : 0;
        int rackCount = sceneCache.Racks != null ? sceneCache.Racks.Length : 0;
        int acCount = sceneCache.ACSources != null ? sceneCache.ACSources.Length : 0;
        int zhCount = sceneCache.ZouHeBoxes != null ? sceneCache.ZouHeBoxes.Length : 0;

        sceneCacheStatus =
            $"Ready | HeatSources={heatSourceCount}, Racks={rackCount}, ACSources={acCount}, ZouHeBoxes={zhCount}, Dirty={sceneCache.IsDirty}";
    }

    private void CheckAndLogTimeConsistency(bool forceLog = false)
    {
        expectedSimulatedTimeSeconds = stepCount * dtPhys;
        simulatedTimeErrorSeconds = math.abs(expectedSimulatedTimeSeconds - simulatedTimeSeconds);

        if (!logTimeConsistency)
            return;

        bool shouldLog = forceLog;

        if (!shouldLog && timeConsistencyLogIntervalSteps > 0)
        {
            shouldLog = (stepCount > 0) &&
                        (stepCount % (ulong)timeConsistencyLogIntervalSteps == 0);
        }

        if (!shouldLog)
            return;

        string message =
            "[LBM Time Check] " +
            $"stepCount={stepCount:N0}, " +
            $"dtPhys={dtPhys:E6} s, " +
            $"expectedTime={expectedSimulatedTimeSeconds:F6} s, " +
            $"actualTime={simulatedTimeSeconds:F6} s, " +
            $"error={simulatedTimeErrorSeconds:E6} s";

        if (simulatedTimeErrorSeconds > timeConsistencyWarningThreshold)
            Debug.LogWarning(message);
        else
            Debug.Log(message);
    }

    private void MarkSummaryDirty()
    {
        summaryDirty = true;
    }

    private void PullLatestResultMetrics()
    {
        if (resultSampler == null)
        {
            resultMetricsStatus = "Result sampler is null.";
            return;
        }

        SimulationResultMetrics m = resultSampler.LatestMetrics;
        if (m == null)
        {
            resultMetricsStatus = "No result metrics available.";
            return;
        }

        avgRoomTemperatureDegC = m.avgRoomTemperatureDegC;
        inletAverageTemperatureDegC = m.inletAverageTemperatureDegC;
        outletAverageTemperatureDegC = m.outletAverageTemperatureDegC;
        inletAverageSpeedPhys = m.inletAverageSpeedPhys;
        outletAverageSpeedPhys = m.outletAverageSpeedPhys;

        inletAverageNormalSpeedPhys = m.inletAverageNormalSpeedPhys;
        outletAverageNormalSpeedPhys = m.outletAverageNormalSpeedPhys;
        inletFlowRatePhysSigned = m.inletFlowRatePhysSigned;
        outletFlowRatePhysSigned = m.outletFlowRatePhysSigned;
        inletFlowRatePhysAbs = m.inletFlowRatePhysAbs;
        outletFlowRatePhysAbs = m.outletFlowRatePhysAbs;
        netFlowRatePhysSigned = m.netFlowRatePhysSigned;
        relativeFlowImbalance = m.relativeFlowImbalance;

        thermalInletClampCount = m.thermalInletClampCount;
        thermalOutletClampCount = m.thermalOutletClampCount;
        fluidInletClampCount = m.fluidInletClampCount;
        fluidOutletClampCount = m.fluidOutletClampCount;

        resultMetricsStatus = m.statusMessage;
    }

    private void ResetAdaptiveOutletRhoFeedback()
    {
        adaptiveOutletRhoOffset = 0.0f;
        adaptiveFeedbackAvgDensity = 1.0f;

        if (sceneCache == null || sceneCache.ZouHeBoxes == null)
            return;

        foreach (var box in sceneCache.ZouHeBoxes)
        {
            if (box == null || !box.Power || box.PatchKind != LBMZouHeBox.Kind.Outlet)
                continue;

            box.ResetAdaptiveRhoOutOffset();
        }
    }

    private void ResetMassFluxCorrectedOutletTargets()
    {
        computedOutletTargetNormalSpeedPhys = 0.0f;
        totalOutletAreaPhys = 0.0f;

        if (sceneCache == null || sceneCache.ZouHeBoxes == null)
            return;

        foreach (var box in sceneCache.ZouHeBoxes)
        {
            if (box == null || !box.Power || box.PatchKind != LBMZouHeBox.Kind.Outlet)
                continue;

            box.ResetTargetOutletNormalSpeedPhys();
        }
    }

    private bool ApplyAdaptiveOutletRhoFeedback()
    {
        if (!enableAdaptiveOutletRhoFeedback)
            return false;

        if (sceneCache == null || sceneCache.ZouHeBoxes == null || resultSampler == null)
            return false;

        SimulationResultMetrics m = resultSampler.LatestMetrics;
        if (m == null || !m.hasValidRoomAverage)
            return false;

        adaptiveFeedbackAvgDensity = m.avgDensity;

        float densityError = 1.0f - m.avgDensity;

        if (Mathf.Abs(densityError) < adaptiveOutletRhoDeadband)
            densityError = 0.0f;

        float newOffset = Mathf.Clamp(
            adaptiveOutletRhoGain * densityError,
            -adaptiveOutletRhoMaxOffset,
            adaptiveOutletRhoMaxOffset);

        if (Mathf.Abs(newOffset - adaptiveOutletRhoOffset) < 1e-7f)
            return false;

        adaptiveOutletRhoOffset = newOffset;

        bool changed = false;
        foreach (var box in sceneCache.ZouHeBoxes)
        {
            if (box == null || !box.Power || box.PatchKind != LBMZouHeBox.Kind.Outlet)
                continue;

            box.SetAdaptiveRhoOutOffset(adaptiveOutletRhoOffset);
            changed = true;
        }

        return changed;
    }

    private void PushAdaptiveOutletRhoToSolver()
    {
        if (_lbmSolver == null || sceneCache == null)
            return;

        _lbmSolver.SyncSourcesAtRuntime(
            sceneCache.HeatSources,
            sceneCache.Racks,
            sceneCache.ACSources,
            sceneCache.ZouHeBoxes);

        _lbmSolver.MarkAllDynamicInputsDirty();
        MarkSummaryDirty();
    }

    public SimulationHealthStatus CheckRunReadiness()
    {
        if (sceneCache == null)
            sceneCache = GetComponent<SimulationSceneCache>();

        if (sceneCache != null &&
            (sceneCache.IsDirty || sceneCache.ZouHeBoxes == null || sceneCache.ZouHeBoxes.Length == 0))
        {
            sceneCache.ForceRefresh();
            UpdateSceneCacheStatus();
        }

        var errors = new StringBuilder();
        var warnings = new StringBuilder();

        if (domain == null)
            AppendReadinessLine(errors, "Domain object is missing.");

        if (lbmComputeShader == null)
            AppendReadinessLine(errors, "LBM compute shader is missing.");

        if (dxPhys <= 0.0f)
            AppendReadinessLine(errors, "dxPhys must be greater than zero.");

        if (tempPhysMaxDegC <= tempPhysMinDegC)
            AppendReadinessLine(errors, "Temperature max must be greater than temperature min.");

        if (dtPhys <= 0.0f)
            AppendReadinessLine(errors, "dtPhys is not positive. Check dxPhys and reference velocity.");

        if (nx <= 1 || ny <= 1 || nz <= 1)
            AppendReadinessLine(errors, $"Grid resolution is too small: {nx} x {ny} x {nz}.");

        if (GetCellCount64() <= 0)
            AppendReadinessLine(errors, "Total cell count is invalid.");

        if (!estimatedSingleBufferSafe)
            AppendReadinessLine(errors, "A single distribution buffer exceeds the Unity GraphicsBuffer limit.");

        if (tau_f < 0.53f || tau_T < 0.53f)
            AppendReadinessLine(errors, $"Relaxation time is invalid: tau_f={tau_f:F4}, tau_T={tau_T:F4}.");

        if (machNumber > 0.30f)
            AppendReadinessLine(errors, $"Mach number is too high for this setup: Ma={machNumber:F4}.");
        else if (machNumber > 0.15f)
            AppendReadinessLine(warnings, $"Mach number is high: Ma={machNumber:F4}. Lower maxMach for better stability.");

        if (tau_f < 0.55f || tau_T < 0.55f)
            AppendReadinessLine(warnings, $"tau is close to the lower stability limit: tau_f={tau_f:F4}, tau_T={tau_T:F4}.");

        if (estimatedTotalGpuMemoryMB > 0.0f)
        {
            float vramBudgetGB = manualVramBudgetGB > 0f
                ? manualVramBudgetGB
                : SystemInfo.graphicsMemorySize / 1024f;
            float warnBudgetMB = Mathf.Max(vramBudgetGB, 0.0f) * 1024.0f * Mathf.Clamp(vramWarningRatio, 0.1f, 1.0f);
            if (warnBudgetMB > 0.0f && estimatedTotalGpuMemoryMB > warnBudgetMB)
                AppendReadinessLine(warnings, $"Estimated GPU memory is high: {estimatedTotalGpuMemoryMB:F1} MiB.");
        }

        int inletCount = 0;
        int outletCount = 0;
        bool hasBadPatch = false;
        if (sceneCache != null && sceneCache.ZouHeBoxes != null)
        {
            foreach (var box in sceneCache.ZouHeBoxes)
            {
                if (box == null || !box.Power)
                    continue;

                if (box.PatchKind == LBMZouHeBox.Kind.Inlet)
                    inletCount++;
                else
                    outletCount++;

                if (box.PatchCellCount == 0)
                    hasBadPatch = true;
            }
        }

        if (inletCount == 0)
            AppendReadinessLine(errors, "At least one inlet boundary is required.");

        if (outletCount == 0)
            AppendReadinessLine(errors, "At least one outlet boundary is required.");

        if (hasBadPatch)
            AppendReadinessLine(errors, "One or more boundary patches have zero cells.");

        if (resultSampler == null)
            AppendReadinessLine(warnings, "ResultSampler is missing. Results and CSV may not update.");

        if (FindFirstObjectByType<SimulationMetricsFileLogger>() == null)
            AppendReadinessLine(warnings, "SimulationMetricsFileLogger is missing. CSV output is disabled.");

        readinessStatus = errors.Length > 0
            ? SimulationHealthStatus.Invalid
            : (warnings.Length > 0 ? SimulationHealthStatus.Warning : SimulationHealthStatus.OK);

        readinessSummary =
            $"=== Run Readiness ===\n" +
            $"Status : {readinessStatus}\n" +
            $"Inlets : {inletCount}, Outlets : {outletCount}\n" +
            $"Errors :\n{(errors.Length > 0 ? errors.ToString() : "  None\n")}" +
            $"Warnings :\n{(warnings.Length > 0 ? warnings.ToString() : "  None\n")}";

        return readinessStatus;
    }

    private void UpdatePowerFlowSummaries()
    {
        UpdateStabilitySummary();
        CheckRunReadiness();

        long cellCount = GetCellCount64();
        caseSummary =
            "=== Case Summary ===\n" +
            $"Preset          : {solverPreset}\n" +
            $"Domain [m]      : {lx:F3} x {ly:F3} x {lz:F3}\n" +
            $"Cell size [m]   : {dxPhys:F5}\n" +
            $"Grid            : {nx} x {ny} x {nz} ({cellCount:N0} cells)\n" +
            $"Estimated memory: {estimatedTotalGpuMemoryMB:F1} MiB\n" +
            $"Simulation time : {simulatedTimeSeconds:F3} s";

        solverSummary =
            "=== Solver Summary ===\n" +
            $"dtPhys [s]      : {dtPhys:E6}\n" +
            $"maxMach limit   : {maxMach:F3}\n" +
            $"Mach            : {machNumber:F4}\n" +
            $"tau_f / tau_T   : {tau_f:F4} / {tau_T:F4}\n" +
            $"nu / alpha phys : {nuPhys:E4} / {alphaPhys:E4}\n" +
            $"Re / Pr         : {reynoldsNumberPhys:E4} / {prandtlNumber:F4}\n" +
            $"Collision       : MRT, {turbulenceModel}";

        boundarySummary = BuildBoundarySummaryText();
        recommendationSummary = BuildRecommendationSummary();
    }

    private void UpdateStabilitySummary()
    {
        bool invalid =
            dxPhys <= 0.0f ||
            dtPhys <= 0.0f ||
            tau_f < 0.53f ||
            tau_T < 0.53f ||
            machNumber > 0.30f;

        bool warning =
            machNumber > 0.15f ||
            tau_f < 0.55f ||
            tau_T < 0.55f ||
            !estimatedSingleBufferSafe;

        stabilityStatus = invalid
            ? SimulationHealthStatus.Invalid
            : (warning ? SimulationHealthStatus.Warning : SimulationHealthStatus.OK);

        stabilitySummary =
            "=== Stability Summary ===\n" +
            $"Status          : {stabilityStatus}\n" +
            $"Mach            : {machNumber:F4} (recommended <= 0.15)\n" +
            $"tau_f / tau_T   : {tau_f:F4} / {tau_T:F4} (recommended >= 0.55)\n" +
            $"Pr              : {prandtlNumber:F4}\n" +
            $"Buffer safe     : {estimatedSingleBufferSafe}";
    }

    private string BuildBoundarySummaryText()
    {
        var sb = new StringBuilder(2048);
        sb.AppendLine("=== Boundary Summary ===");

        if (sceneCache == null || sceneCache.ZouHeBoxes == null || sceneCache.ZouHeBoxes.Length == 0)
        {
            sb.AppendLine("No Zou-He inlet/outlet boxes found.");
            return sb.ToString();
        }

        int inletCount = 0;
        int outletCount = 0;
        float inletFlowAbs = 0.0f;
        float outletArea = 0.0f;

        foreach (var box in sceneCache.ZouHeBoxes)
        {
            if (box == null || !box.Power)
                continue;

            if (box.PatchKind == LBMZouHeBox.Kind.Inlet)
            {
                inletCount++;
                inletFlowAbs += Mathf.Abs(box.GetFlowRatePhys(dxPhys));
            }
            else
            {
                outletCount++;
                outletArea += box.PatchAreaPhys(dxPhys);
            }

            sb.AppendLine(box.GetSummaryText(dxPhys));
        }

        sb.Insert(0,
            $"Inlets={inletCount}, Outlets={outletCount}, InletFlow={inletFlowAbs:F4} m3/s ({inletFlowAbs * 60.0f:F2} CMM), OutletArea={outletArea:F4} m2\n");

        return sb.ToString();
    }

    private string BuildRecommendationSummary()
    {
        if (readinessStatus == SimulationHealthStatus.Invalid)
            return "=== Recommendation ===\nFix Invalid readiness items before running.";

        if (!estimatedSingleBufferSafe || estimatedTotalGpuMemoryMB > 4096.0f)
            return "=== Recommendation ===\nUse FastPreview or increase dxPhys to reduce memory.";

        if (machNumber > 0.15f)
            return "=== Recommendation ===\nReduce maxMach or characteristic velocity for better LBM stability.";

        if (readinessStatus == SimulationHealthStatus.Warning || stabilityStatus == SimulationHealthStatus.Warning)
            return "=== Recommendation ===\nReady with warnings. Review stability and boundary summaries.";

        return "=== Recommendation ===\nReady to run.";
    }

    private static void AppendReadinessLine(StringBuilder sb, string message)
    {
        sb.Append("  - ");
        sb.AppendLine(message);
    }

    private void RefreshSummaryIfNeeded()
    {
        if (!summaryDirty)
            return;

        var zhBoxes = (sceneCache != null) ? sceneCache.ZouHeBoxes : null;
        UpdatePowerFlowSummaries();

        latestSummary = SimulationDiagnostics.BuildSimulationSummary(
            new Vector3(lx, ly, lz),
            nx, ny, nz,
            dxPhys,
            dtPhys,
            stepCount,
            simulatedTimeSeconds,
            tau_f,
            tau_T,
            nuPhys,
            alphaPhys,
            machNumber,
            reynoldsNumberPhys,
            prandtlNumber,
            maxWindSpeedPhys,
            "MRT",
            turbulenceModel.ToString(),
            turbulenceModelConstant,
            turbulentPrandtl,
            wallFunctionEnabled,
            zhBoxes
        );

        latestSummary =
            caseSummary +
            "\n\n" + boundarySummary +
            "\n\n" + solverSummary +
            "\n\n" + stabilitySummary +
            "\n\n" + readinessSummary +
            "\n\n" + recommendationSummary +
            "\n\n" + latestSummary;

        latestSummary +=
            $"\n\n=== Temperature Mapping ===\n" +
            $"Temp Range [degC]        : {tempPhysMinDegC:F2} ~ {tempPhysMaxDegC:F2}\n" +
            $"Reference Temp [degC]    : {referenceTemperatureDegC:F2}\n" +
            $"Reference Temp [LBM]     : {T_ref:F6}" +
            $"\n\n=== Stop Condition ===\n" +
            $"Use Target Simulation Time : {useTargetSimulationTime}\n" +
            $"Target Simulation Time [s] : {targetSimulationTimeSeconds:F3}\n" +
            $"Target Time Reached        : {targetTimeReached}" +
            $"\n\n=== Mass-Flux Corrected Outlet ===\n" +
            $"Enable                    : {enableMassFluxCorrectedOutlet}\n" +
            $"Use Inlet Abs Flow Target : {useInletAbsFlowAsOutletTarget}\n" +
            $"Total Outlet Area [m^2]   : {totalOutletAreaPhys:F6}\n" +
            $"Target Outlet Normal [m/s]: {computedOutletTargetNormalSpeedPhys:F6}" +
            $"\nDebug Inlet Flow Sampler  : {debugInletFlowFromSampler:F6}" +
            $"\nDebug Inlet Flow Direct   : {debugInletFlowFromBoxes:F6}" +
            $"\nDebug Outlet Area Direct  : {debugOutletAreaFromBoxes:F6}" +
            $"\nDebug Target Applied Cnt  : {debugOutletTargetAppliedPatchCount}";

        if (resultSampler != null && resultSampler.LatestMetrics != null)
        {
            latestSummary += "\n\n" + resultSampler.LatestMetrics.ToSummaryText();
        }

        summaryDirty = false;
    }

    private void RebuildSolver()
    {
        _lbmSolver?.Dispose();
        _lbmSolver = null;

        if (domain == null || lbmComputeShader == null)
        {
            Debug.LogError("SimulationController: domain or compute shader is missing.");
            return;
        }

        if (sceneCache == null)
        {
            Debug.LogError("SimulationController: sceneCache is missing.");
            return;
        }

        if (CheckRunReadiness() == SimulationHealthStatus.Invalid)
        {
            Debug.LogError("[SimulationController] Solver rebuild blocked by invalid readiness.\n" + readinessSummary);
            return;
        }

        if (sceneCache.ZouHeBoxes != null)
        {
            foreach (var box in sceneCache.ZouHeBoxes)
            {
                if (box != null && box.Power)
                    box.Refresh();
            }
        }

        _lbmSolver = new ThermalSolver(
            domain,
            lbmComputeShader,
            dxPhys,
            tau_f,
            tau_T,
            T_ref,
            beta,
            gravityLat,
            nx, ny, nz,
            sceneCache.HeatSources,
            sceneCache.Racks,
            sceneCache.ACSources,
            sceneCache.ZouHeBoxes);

        _lbmSolver.SetVisualizeInterval(4);
        ResetAdaptiveOutletRhoFeedback();
        ResetMassFluxCorrectedOutletTargets();
        solverRebuildRequired = false;
    }

    private void RebuildScaling()
    {
        if (domain == null)
            return;

        if (tempPhysMaxDegC <= tempPhysMinDegC)
            tempPhysMaxDegC = tempPhysMinDegC + 0.01f;

        T_ref = Mathf.Clamp01(TemperatureDegCToLBM(referenceTemperatureDegCInput));
        referenceTemperatureDegC = referenceTemperatureDegCInput;

        lx = domain.transform.localScale.x;
        ly = domain.transform.localScale.y;
        lz = domain.transform.localScale.z;
        maxDomainLengthPhys = math.max(math.max(lx, ly), lz);

        nx = (uint)math.max(1, math.round(lx / dxPhys));
        ny = (uint)math.max(1, math.round(ly / dxPhys));
        nz = (uint)math.max(1, math.round(lz / dxPhys));
        maxDomainLengthLat = math.max(math.max(nx, ny), nz);
        N = nx * ny * nz;

        targetWindSpeedLat = maxMach * csLat;
        speedScalePhysToLat = targetWindSpeedLat / math.max(U_ref, eps);
        dtPhys = speedScalePhysToLat * dxPhys;
        gravityLat = gravity_y * dtPhys * dtPhys / dxPhys;

        float nuPhys_tgt = math.max(nuPhysTarget, 1e-12f);
        float pr_tgt = math.max(prandtlTarget, 1e-6f);
        float alphaPhys_tgt = nuPhys_tgt / pr_tgt;

        float inv_dx2 = 1.0f / (dxPhys * dxPhys);
        float nuLat_tgt = nuPhys_tgt * dtPhys * inv_dx2;
        float alphaLat_tgt = alphaPhys_tgt * dtPhys * inv_dx2;

        float tau_f_raw = 3.0f * nuLat_tgt + 0.5f;
        float tau_T_raw = 4.0f * alphaLat_tgt + 0.5f;

        tau_f = math.clamp(tau_f_raw, tauMin, tauMax);
        tau_T = math.clamp(tau_T_raw, tauMin, tauMax);

        nuLat = (tau_f - 0.5f) / 3.0f;
        alphaLat = (tau_T - 0.5f) / 4.0f;

        if (dtPhys > 0f)
        {
            float dx2_over_dt = (dxPhys * dxPhys) / dtPhys;
            nuPhys = nuLat * dx2_over_dt;
            alphaPhys = alphaLat * dx2_over_dt;
        }
        else
        {
            nuPhys = 0f;
            alphaPhys = 0f;
        }

        prandtlNumber = (alphaLat > 0f) ? (nuLat / alphaLat) : 0f;

        RecalculateACSourceScaling();

        machNumber = (csLat > 0f) ? (maxWindSpeedLat / csLat) : 0f;
        reynoldsNumber = (nuLat > 0f) ? (maxWindSpeedLat * maxDomainLengthLat / nuLat) : 0f;

        float U_phys_for_Re = math.max(maxWindSpeedPhys, eps);
        reynoldsNumberPhys = (nuPhys > 0f) ? (U_phys_for_Re * maxDomainLengthPhys / nuPhys) : 0f;

        UpdateMemoryEstimateReadOnly();
    }

    private void LogGrossVsEffectiveOutletAreaComparison()
    {
        if (!logGrossVsEffectiveOutletArea)
            return;

        if (grossVsEffectiveAreaLogIntervalSteps > 0)
        {
            if ((stepCount % (ulong)grossVsEffectiveAreaLogIntervalSteps) != 0)
                return;
        }

        if (sceneCache == null || sceneCache.ZouHeBoxes == null)
            return;

        SimulationResultMetrics m = resultSampler != null ? resultSampler.LatestMetrics : null;
        if (m == null || !m.hasValidFlowDiagnostic)
            return;

        float grossAreaSum = 0f;
        int grossCellCountSum = 0;
        int outletPatchCount = 0;

        StringBuilder patchSb = null;
        if (logEachOutletPatchDetail)
            patchSb = new StringBuilder(512);

        foreach (var box in sceneCache.ZouHeBoxes)
        {
            if (box == null || !box.Power || box.PatchKind != LBMZouHeBox.Kind.Outlet)
                continue;

            float patchArea = box.PatchAreaPhys(dxPhys);
            int patchCells = (int)box.PatchCellCount;

            grossAreaSum += patchArea;
            grossCellCountSum += patchCells;
            outletPatchCount++;

            if (patchSb != null)
            {
                patchSb.AppendLine(
                    $"  - OutletPatch \"{box.name}\": " +
                    $"grossCells={patchCells}, grossArea={patchArea:F6} m^2, " +
                    $"planeIndex={box.PlaneIndex}, axis={box.NormalAxis}, sign={box.NormalSign}, " +
                    $"targetNormalPhys={box.TargetOutletNormalSpeedPhys:F6} m/s");
            }
        }

        totalOutletAreaPhys = grossAreaSum;
        grossAreaBasedTargetNormalSpeed = computedOutletTargetNormalSpeedPhys;

        float effArea = 0f;
        if (Mathf.Abs(m.outletAverageNormalSpeedPhys) > 1e-8f)
        {
            effArea = m.outletFlowRatePhysAbs / Mathf.Abs(m.outletAverageNormalSpeedPhys);
        }

        effectiveOutletAreaPhys = effArea;
        grossToEffectiveAreaRatio = (effArea > 1e-12f) ? (grossAreaSum / effArea) : 0f;
        effectiveOutletNormalSpeedFromFlow =
            (effArea > 1e-12f) ? (m.outletFlowRatePhysAbs / effArea) : 0f;

        float targetNormalFromInletUsingEffectiveArea = 0f;
        if (effArea > 1e-12f)
            targetNormalFromInletUsingEffectiveArea = m.inletFlowRatePhysAbs / effArea;

        int effectiveCellEstimate = 0;
        if (dxPhys > 1e-12f)
        {
            float cellArea = dxPhys * dxPhys;
            effectiveCellEstimate = Mathf.RoundToInt(effArea / cellArea);
        }

        float areaMismatchPercent = 0f;
        if (grossAreaSum > 1e-12f)
        {
            areaMismatchPercent = 100f * (grossAreaSum - effArea) / grossAreaSum;
        }

        var sb = new StringBuilder(1024);
        sb.AppendLine("[OutletAreaDebug] Gross vs Effective Outlet Area");
        sb.AppendLine($"  stepCount                          : {stepCount}");
        sb.AppendLine($"  simulatedTimeSeconds               : {simulatedTimeSeconds:F3}");
        sb.AppendLine($"  outletPatchCount                   : {outletPatchCount}");
        sb.AppendLine($"  grossOutletCellCount(sum patches)  : {grossCellCountSum}");
        sb.AppendLine($"  effectiveOutletSampleCount         : {m.outletSampleCount}");
        sb.AppendLine($"  dxPhys                             : {dxPhys:F6} m");
        sb.AppendLine($"  grossOutletAreaPhys                : {grossAreaSum:F6} m^2");
        sb.AppendLine($"  effectiveOutletAreaPhys            : {effArea:F6} m^2");
        sb.AppendLine($"  gross/effective area ratio         : {grossToEffectiveAreaRatio:F6}");
        sb.AppendLine($"  area mismatch                      : {areaMismatchPercent:F3} %");
        sb.AppendLine($"  effectiveCellEstimate(area/dx^2)   : {effectiveCellEstimate}");
        sb.AppendLine($"  inletFlowAbs                       : {m.inletFlowRatePhysAbs:F6} m^3/s");
        sb.AppendLine($"  outletFlowAbs                      : {m.outletFlowRatePhysAbs:F6} m^3/s");
        sb.AppendLine($"  inletAverageNormalSpeedPhys        : {m.inletAverageNormalSpeedPhys:F6} m/s");
        sb.AppendLine($"  outletAverageNormalSpeedPhys       : {m.outletAverageNormalSpeedPhys:F6} m/s");
        sb.AppendLine($"  grossAreaBasedTargetNormalSpeed    : {grossAreaBasedTargetNormalSpeed:F6} m/s");
        sb.AppendLine($"  targetNormalUsingEffectiveArea     : {targetNormalFromInletUsingEffectiveArea:F6} m/s");
        sb.AppendLine($"  relativeFlowImbalance              : {m.relativeFlowImbalance:F6}");
        sb.AppendLine($"  avgDensity                         : {m.avgDensity:F6}");

        if (patchSb != null && patchSb.Length > 0)
        {
            sb.AppendLine("  Patch Details:");
            sb.Append(patchSb);
        }

        Debug.Log(sb.ToString());
    }

    private float ComputeInletFlowAbsDirectFromBoxes()
    {
        if (sceneCache == null || sceneCache.ZouHeBoxes == null)
            return 0f;

        float sumAbs = 0f;

        foreach (var box in sceneCache.ZouHeBoxes)
        {
            if (box == null || !box.Power || box.PatchKind != LBMZouHeBox.Kind.Inlet)
                continue;

            float q = box.GetFlowRatePhys(dxPhys);
            sumAbs += Mathf.Abs(q);
        }

        return sumAbs;
    }

    private void LogOutletRootCauseOnce(
        float inletFlowAbsSampler,
        float inletFlowAbsDirect,
        float outletAreaSum,
        float targetNormalSpeedPhys,
        int appliedPatchCount)
    {
        if (!logOutletRootCause)
            return;

        int interval = Mathf.Max(outletRootCauseLogIntervalSteps, 1);

        if (stepCount != 0 && (stepCount % (ulong)interval) != 0UL)
            return;

        var sb = new StringBuilder(1024);
        sb.AppendLine("[OutletRootCauseDebug] Controller -> Patch Target");
        sb.AppendLine($"  stepCount                    : {stepCount}");
        sb.AppendLine($"  simulatedTimeSeconds         : {simulatedTimeSeconds:F3}");
        sb.AppendLine($"  resultMetricsStatus          : {resultMetricsStatus}");
        sb.AppendLine($"  inletFlowAbs(sampler)        : {inletFlowAbsSampler:F6} m^3/s");
        sb.AppendLine($"  inletFlowAbs(direct boxes)   : {inletFlowAbsDirect:F6} m^3/s");
        sb.AppendLine($"  outletAreaSum                : {outletAreaSum:F6} m^2");
        sb.AppendLine($"  computedTargetNormalSpeed    : {targetNormalSpeedPhys:F6} m/s");
        sb.AppendLine($"  targetAppliedPatchCount      : {appliedPatchCount}");
        sb.AppendLine($"  anyTargetApplied             : {debugAnyOutletTargetApplied}");
        sb.AppendLine($"  currentOutletFlowAbs         : {outletFlowRatePhysAbs:F6} m^3/s");
        sb.AppendLine($"  currentOutletNormalSpeed     : {outletAverageNormalSpeedPhys:F6} m/s");
        sb.AppendLine($"  currentRelativeImbalance     : {relativeFlowImbalance:F6}");
        Debug.Log(sb.ToString());
    }

    private void ApplyMassFluxCorrectedOutletTarget()
    {
        if (!enableMassFluxCorrectedOutlet || sceneCache == null || sceneCache.ZouHeBoxes == null)
            return;

        float outletAreaSum = 0f;
        int outletCount = 0;

        foreach (var box in sceneCache.ZouHeBoxes)
        {
            if (box == null || !box.Power || box.PatchKind != LBMZouHeBox.Kind.Outlet)
                continue;

            outletAreaSum += box.PatchAreaPhys(dxPhys);
            outletCount++;
        }

        totalOutletAreaPhys = outletAreaSum;
        debugOutletAreaFromBoxes = outletAreaSum;

        if (outletCount == 0 || outletAreaSum <= 1e-12f)
        {
            computedOutletTargetNormalSpeedPhys = 0f;
            debugAnyOutletTargetApplied = false;
            debugOutletTargetAppliedPatchCount = 0;

            Debug.LogWarning(
                "[MassFluxTargetDebug] No valid outlet area. " +
                $"outletCount={outletCount}, outletAreaSum={outletAreaSum:F6}");

            return;
        }

        float inletFlowAbsSampler = Mathf.Max(0f, inletFlowRatePhysAbs);
        float inletFlowAbsDirect = Mathf.Max(0f, ComputeInletFlowAbsDirectFromBoxes());

        debugInletFlowFromSampler = inletFlowAbsSampler;
        debugInletFlowFromBoxes = inletFlowAbsDirect;

        float targetOutletFlowAbs = Mathf.Max(inletFlowAbsSampler, inletFlowAbsDirect);

        float rawTargetNormalSpeedPhys =
            (outletAreaSum > 1e-12f) ? (targetOutletFlowAbs / outletAreaSum) : 0f;

        float safeMaxTargetNormalSpeedPhys = Mathf.Max(maxOutletTargetNormalSpeedPhys, 0.1f);

        float clampedTargetNormalSpeedPhys = Mathf.Clamp(
            rawTargetNormalSpeedPhys,
            0f,
            safeMaxTargetNormalSpeedPhys);

        computedOutletTargetNormalSpeedPhys = clampedTargetNormalSpeedPhys;

        bool anyChanged = false;
        int appliedCount = 0;

        const float targetChangeEpsilon = 1e-5f;

        foreach (var box in sceneCache.ZouHeBoxes)
        {
            if (box == null || !box.Power || box.PatchKind != LBMZouHeBox.Kind.Outlet)
                continue;

            if (!box.EnableMassFluxCorrection)
                continue;

            appliedCount++;

            if (Mathf.Abs(box.TargetOutletNormalSpeedPhys - clampedTargetNormalSpeedPhys) <= targetChangeEpsilon)
                continue;

            box.SetTargetOutletNormalSpeedPhys(clampedTargetNormalSpeedPhys);
            anyChanged = true;
        }

        debugAnyOutletTargetApplied = anyChanged;
        debugOutletTargetAppliedPatchCount = appliedCount;

        int interval = Mathf.Max(outletRootCauseLogIntervalSteps, 1);
        if (stepCount == 0 || (stepCount % (ulong)interval) == 0UL)
        {
            Debug.Log(
                "[MassFluxTargetDebug] " +
                $"step={stepCount}, " +
                $"inletFlowAbsSampler={inletFlowAbsSampler:F6}, " +
                $"inletFlowAbsDirect={inletFlowAbsDirect:F6}, " +
                $"targetOutletFlowAbs={targetOutletFlowAbs:F6}, " +
                $"outletAreaSum={outletAreaSum:F6}, " +
                $"rawTarget={rawTargetNormalSpeedPhys:F6}, " +
                $"maxOutletTargetNormalSpeedPhys={maxOutletTargetNormalSpeedPhys:F6}, " +
                $"safeMaxTargetNormalSpeedPhys={safeMaxTargetNormalSpeedPhys:F6}, " +
                $"clampedTarget={clampedTargetNormalSpeedPhys:F6}, " +
                $"appliedCount={appliedCount}");
        }

        if (anyChanged)
        {
            _lbmSolver?.SyncSourcesAtRuntime(
                sceneCache.HeatSources,
                sceneCache.Racks,
                sceneCache.ACSources,
                sceneCache.ZouHeBoxes);

            _lbmSolver?.MarkAllDynamicInputsDirty();
        }

        LogOutletRootCauseOnce(
            inletFlowAbsSampler,
            inletFlowAbsDirect,
            outletAreaSum,
            clampedTargetNormalSpeedPhys,
            appliedCount);
    }

    [ContextMenu("Debug Readback Outlet BC State")]
    public void DebugReadbackOutletBcState()
    {
        if (_lbmSolver == null || _lbmSolver.DebugOutletBcStateBuffer == null)
        {
            Debug.LogWarning("Debug outlet BC buffer is not ready.");
            return;
        }

        StartCoroutine(CoReadbackOutletBcState());
    }

    private IEnumerator CoReadbackOutletBcState()
    {
        var req = AsyncGPUReadback.Request(_lbmSolver.DebugOutletBcStateBuffer);
        yield return new WaitUntil(() => req.done);

        if (req.hasError)
        {
            Debug.LogWarning("Outlet BC state readback failed.");
            yield break;
        }

        NativeArray<Vector4> data = req.GetData<Vector4>();
        if (data.Length < 4)
        {
            Debug.LogWarning("Outlet BC state readback length is too small.");
            yield break;
        }

        Vector4 s0 = data[0];
        Vector4 s1 = data[1];
        Vector4 s2 = data[2];
        Vector4 s3 = data[3];

        Debug.Log(
            "[OutletShaderDebug] First outlet sample\n" +
            $"  un_nb      = {s0.x:F6}\n" +
            $"  target_un  = {s0.y:F6}\n" +
            $"  blend      = {s0.z:F6}\n" +
            $"  un_eff     = {s0.w:F6}\n" +
            $"  rho_nb     = {s1.x:F6}\n" +
            $"  rho_target = {s1.y:F6}\n" +
            $"  rho_anchor = {s1.z:F6}\n" +
            $"  rho_eff    = {s1.w:F6}\n" +
            $"  u_nb       = ({s2.x:F6}, {s2.y:F6}, {s2.z:F6})\n" +
            $"  T_nb       = {s2.w:F6}\n" +
            $"  u_eff      = ({s3.x:F6}, {s3.y:F6}, {s3.z:F6})");
    }

    private void RecalculateACSourceScaling()
    {
        maxWindSpeedLat = 0f;
        maxWindSpeedPhys = 0f;

        ACSource[] acSources = (sceneCache != null) ? sceneCache.ACSources : null;

        if (acSources == null || acSources.Length == 0)
        {
            maxWindSpeedLat = targetWindSpeedLat;
            maxWindSpeedPhys = U_ref;
            return;
        }

        foreach (var ac in acSources)
        {
            if (ac == null)
                continue;

            float3 uPhys = ac.WindSpeedPhys;
            float magPhys = math.length(uPhys);

            if (magPhys > U_ref && magPhys > eps)
                uPhys *= U_ref / magPhys;

            float3 uLat = speedScalePhysToLat * uPhys;
            ac.WindSpeedLat = uLat;

            maxWindSpeedLat = math.max(maxWindSpeedLat, math.length(uLat));
            maxWindSpeedPhys = math.max(maxWindSpeedPhys, math.length(uPhys));
        }

        if (maxWindSpeedPhys <= 0f)
        {
            maxWindSpeedLat = targetWindSpeedLat;
            maxWindSpeedPhys = U_ref;
        }
    }

    private void UpdateMemoryEstimateReadOnly()
    {
        long cellCount = GetCellCount64();
        long perDirBytes = cellCount * BytesPerFloat;
        long distributionBytes = cellCount * Q_total * 2L * BytesPerFloat;
        long coreBytes = distributionBytes
                       + cellCount * BytesPerFloat4
                       + cellCount * BytesPerFloat
                       + cellCount * BytesPerUint;
        long textureBytes = cellCount * (BytesPerVelocityTextureVoxel + BytesPerThermalTextureVoxel);
        long totalBytes = coreBytes + textureBytes;

        perDirectionBufferMB = BytesToMiB(perDirBytes);
        estimatedDistributionBuffersMB = BytesToMiB(distributionBytes);
        estimatedCoreBuffersMB = BytesToMiB(coreBytes);
        estimatedTexturesMB = BytesToMiB(textureBytes);
        estimatedTotalGpuMemoryMB = BytesToMiB(totalBytes);
        estimatedSingleBufferSafe = perDirBytes <= MaxGraphicsBufferBytes;
    }

    private void ValidateAndLogMemoryEstimate()
    {
        long cellCount = GetCellCount64();
        if (cellCount <= 0)
        {
            Debug.LogError("[LBM Memory Check] Invalid cell count. Check domain scale and dxPhys.");
            return;
        }

        long perDirBytes = cellCount * BytesPerFloat;
        long distributionBytes = cellCount * Q_total * 2L * BytesPerFloat;
        long velocityRhoBytes = cellCount * BytesPerFloat4;
        long temperatureBytes = cellCount * BytesPerFloat;
        long fieldBytes = cellCount * BytesPerUint;
        long coreBytes = distributionBytes + velocityRhoBytes + temperatureBytes + fieldBytes;
        long velocityTextureBytes = cellCount * BytesPerVelocityTextureVoxel;
        long thermalTextureBytes = cellCount * BytesPerThermalTextureVoxel;
        long textureBytes = velocityTextureBytes + thermalTextureBytes;
        long totalBytes = coreBytes + textureBytes;

        float vramBudgetGB = manualVramBudgetGB > 0f
            ? manualVramBudgetGB
            : SystemInfo.graphicsMemorySize / 1024f;
        long vramBudgetBytes = (long)(math.max(vramBudgetGB, 0f) * 1024f * 1024f * 1024f);
        long warnBudgetBytes = (long)(vramBudgetBytes * math.clamp(vramWarningRatio, 0.1f, 1.0f));

        if (logMemoryEstimate)
        {
            Debug.Log(
                "[LBM Memory Check] " +
                $"Grid={nx} x {ny} x {nz} (N={cellCount:N0}), dxPhys={dxPhys:F4} m\n" +
                $"Per-direction distribution buffer = {BytesToMiB(perDirBytes):F1} MiB " +
                $"(limit {BytesToMiB(MaxGraphicsBufferBytes):F1} MiB)\n" +
                $"Distribution buffers total (26 dirs x prev/cur) = {BytesToMiB(distributionBytes):F1} MiB\n" +
                $"Core structured buffers total = {BytesToMiB(coreBytes):F1} MiB\n" +
                $"3D textures total = {BytesToMiB(textureBytes):F1} MiB\n" +
                $"Estimated total GPU memory = {BytesToMiB(totalBytes):F1} MiB\n" +
                $"Graphics API VRAM budget(reference) = {vramBudgetGB:F1} GiB, warning threshold = {BytesToMiB(warnBudgetBytes):F1} MiB");
        }

        if (perDirBytes > MaxGraphicsBufferBytes)
        {
            double recommendedDx = EstimateRecommendedDxForBufferLimit();
            Debug.LogError(
                "[LBM Memory Check] A single split distribution buffer still exceeds Unity GraphicsBuffer limit. " +
                $"Required per-direction buffer = {BytesToMiB(perDirBytes):F1} MiB, max = {BytesToMiB(MaxGraphicsBufferBytes):F1} MiB. " +
                $"Increase dxPhys above about {recommendedDx:F4} m or reduce the domain size.");
        }

        if (vramBudgetBytes > 0 && totalBytes > warnBudgetBytes)
        {
            Debug.LogWarning(
                "[LBM Memory Check] Estimated GPU memory usage is high. " +
                $"Estimated total = {BytesToMiB(totalBytes):F1} MiB, warning threshold = {BytesToMiB(warnBudgetBytes):F1} MiB. " +
                "You may avoid the 2 GB single-buffer error, but overall VRAM pressure, 3D texture allocation failure, or severe slowdown can still occur.");
        }

        if (vramBudgetBytes > 0 && totalBytes > vramBudgetBytes)
        {
            Debug.LogError(
                "[LBM Memory Check] Estimated GPU memory usage exceeds the approximate VRAM budget. " +
                $"Estimated total = {BytesToMiB(totalBytes):F1} MiB, VRAM budget = {BytesToMiB(vramBudgetBytes):F1} MiB. " +
                "Reduce grid resolution or domain size before running.");
        }
    }

    private long GetCellCount64()
    {
        return (long)nx * (long)ny * (long)nz;
    }

    private float BytesToMiB(long bytes)
    {
        return bytes / (1024f * 1024f);
    }

    private double EstimateRecommendedDxForBufferLimit()
    {
        double volume = (double)lx * (double)ly * (double)lz;
        if (volume <= 0.0)
            return dxPhys;

        double maxCellCount = MaxGraphicsBufferBytes / (double)BytesPerFloat;
        double dxRecommended = math.pow((float)(volume / maxCellCount), 1.0f / 3.0f);
        return math.max((float)dxRecommended, dxPhys);
    }
}
