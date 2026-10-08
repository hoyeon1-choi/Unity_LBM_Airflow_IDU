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

public enum CaseStudyPreset
{
    A0_Baseline,
    A1_FluidTau_0530_Thermal_0560_Off,
    A2_FluidTau_0510_Thermal_0560_Off,
    A3_FluidTau_0510_Thermal_0530_Off,
    A4_FluidTau_0510_Thermal_0530_Smag003,
    B1_FluidTau_0515_Thermal_0535_Off,
    B2_FluidTau_0510_Thermal_0530_Off,
    B3_FluidTau_0505_Thermal_0525_Off,
    B4_FluidTau_0510_Thermal_0530_Smag003,
    B5_FluidTau_0510_Thermal_0530_Smag006,
    B6_FluidTau_0510_Thermal_0530_Smag010
}

public class SimulationController : Singleton<SimulationController>
{
    [Header("User Setup")]
    [SerializeField] private SolverEasePreset solverPreset = SolverEasePreset.Balanced;
    [Tooltip("Main run switch. Invalid readiness status will prevent stepping even if this is enabled.")]
    [SerializeField] private bool runSimulation = true;
    [SerializeField, ReadOnly] private bool externalStepPause = false;
    [SerializeField, ReadOnly] private string externalStepPauseReason = string.Empty;

    [Header("Domain / Resolution")]
    [SerializeField] private GameObject domain;

    [Header("Simulation Parameters")]
    [SerializeField] private ComputeShader lbmComputeShader;

    [Header("Scene Cache (Auto)")]
    private SimulationSceneCache sceneCache;
    [SerializeField, ReadOnly] private bool sceneCacheReady = false;
    [SerializeField, ReadOnly] private string sceneCacheStatus = "Not initialized";

    [Header("Reference Maximum Physical Velocity")]
    [Tooltip("Reference maximum physical velocity for scaling (m/s). " +
             "The maximum speed of AC Sources can not exceed this value")]
    [Range(1.0f, 10.0f), SerializeField] private float U_ref = 10.0f;

    [Tooltip("Physical lattice cell size in meters. Smaller values increase resolution and memory cost.")]
    [SerializeField] private float dxPhys = 0.01f;

    [Header("Physical Properties")]
    [Tooltip("Target Kinetic Viscosity [m^2/s] (air ≈ 1.5e-5)")]
    [SerializeField] private float nuPhysTarget = 1.5e-5f;
    [Tooltip("Target Prandtl Number (air ≈ 0.71)")]
    [SerializeField] private float prandtlTarget = 0.71f;
    [SerializeField] private float beta = 0.05f;
    [SerializeField] private float gravity_y = -9.81f;

    [Header("Temperature Setup")]
    [SerializeField] private float tempPhysMinDegC = 0.0f;
    [SerializeField] private float tempPhysMaxDegC = 30.0f;
    [Tooltip("Reference temperature used for initialization and buoyancy [degC].")]
    [SerializeField] private float referenceTemperatureDegCInput = 30.0f;

    [Header("Advanced LBM Parameters")]
    [Tooltip("Lattice Mach number limit for LBM stability. Lower is usually more stable but slower.")]
    [Range(0.10f, 0.30f), SerializeField] private float maxMach = 0.25f;
    [Tooltip("Minimum fluid relaxation time clamp. Values close to 0.5 reduce numerical viscosity but can be unstable.")]
    [Range(0.5001f, 1.0f), SerializeField] private float tauFluidMin = 0.56f;
    [Tooltip("Minimum thermal relaxation time clamp. Values close to 0.5 reduce thermal diffusion but can be unstable.")]
    [Range(0.5001f, 1.0f), SerializeField] private float tauThermalMin = 0.56f;
    [Tooltip("Maximum fluid relaxation time clamp.")]
    [Range(1.0f, 4.0f), SerializeField] private float tauFluidMax = 4.00f;
    [Tooltip("Maximum thermal relaxation time clamp.")]
    [Range(1.0f, 4.0f), SerializeField] private float tauThermalMax = 4.00f;
    [SerializeField, HideInInspector] private float tauMin = 0.56f;
    [SerializeField, HideInInspector] private float tauMax = 4.00f;

    [Header("Advanced Solver Models")]
    [SerializeField] private TurbulenceModel turbulenceModel = TurbulenceModel.Smagorinsky;
    [Tooltip("Smagorinsky: Cs, WALE: Cw")]
    [SerializeField] private float turbulenceModelConstant = 0.03f;
    [Tooltip("Turbulent Prandtl number")]
    [SerializeField] private float turbulentPrandtl = 0.7f;
    [SerializeField] private bool wallFunctionEnabled = false;

    [Header("Case Study")]
    [Tooltip("Enable this before running A0-A4 case-study automation. Keep it off to prevent accidental long runs.")]
    [SerializeField, HideInInspector] private bool enableCaseStudyExecution = false;
    [SerializeField, HideInInspector] private CaseStudyPreset selectedCaseStudy = CaseStudyPreset.A0_Baseline;
    [SerializeField, ReadOnly] private string activeCaseName = "Manual";
    [TextArea(3, 8)]
    [SerializeField, ReadOnly] private string caseStudySummary = "No case study preset applied.";

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

    [Header("Debug / Logging")]
    [SerializeField] private bool autoLogSummaryOnStart = true;
    [SerializeField] private bool autoLogSummaryOnSceneRefresh = false;

    [Header("Inspector / Plot Performance")]
    [Tooltip("Minimum real-time interval for refreshing serialized read-only Inspector fields and large summary strings.")]
    [Min(0.05f)]
    [SerializeField] private float readOnlyInspectorUpdateIntervalSeconds = 1.0f;
    [Tooltip("Minimum simulated-time interval for updating velocity/thermal contour volume textures.")]
    [Min(0.0f)]
    [SerializeField] private float contourPlotUpdateIntervalSeconds = 1.0f;
    [SerializeField, ReadOnly] private int contourPlotUpdateIntervalSteps = 1;
    [SerializeField, ReadOnly] private string readOnlyInspectorUpdateStatus = "Not updated";

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
    [SerializeField, ReadOnly] private float nuPhysTargetReadOnly = 0f;
    [SerializeField, ReadOnly] private float alphaPhysTargetReadOnly = 0f;
    [SerializeField, ReadOnly] private float tauFRaw = 0.5f;
    [SerializeField, ReadOnly] private float tauTRaw = 0.5f;
    [SerializeField, ReadOnly] private bool tauFWasClamped = false;
    [SerializeField, ReadOnly] private bool tauTWasClamped = false;
    [SerializeField, ReadOnly] private float nuPhysEffectiveRatio = 0f;
    [SerializeField, ReadOnly] private float alphaPhysEffectiveRatio = 0f;
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
    [SerializeField, ReadOnly] private string scalingDiagnosticsSummary;
    [TextArea(3, 8)]
    [SerializeField, ReadOnly] private string stabilitySummary;
    [TextArea(3, 8)]
    [SerializeField, ReadOnly] private string recommendationSummary;
    [TextArea(3, 8)]
    [SerializeField, ReadOnly] private string readinessSummary;
    [SerializeField, ReadOnly] private SimulationHealthStatus stabilityStatus = SimulationHealthStatus.Warning;
    [SerializeField, ReadOnly] private SimulationHealthStatus readinessStatus = SimulationHealthStatus.Warning;

    [Header("Read-Only (Estimated GPU Memory)")]
    [SerializeField, ReadOnly] private float estimatedDistributionBuffersMB = 0.0f;
    [SerializeField, ReadOnly] private float estimatedCoreBuffersMB = 0.0f;
    [SerializeField, ReadOnly] private float estimatedTexturesMB = 0.0f;
    [SerializeField, ReadOnly] private float estimatedTotalGpuMemoryMB = 0.0f;
    [Tooltip("Largest grouped distribution GraphicsBuffer (6 directions). The field name is retained for scene compatibility.")]
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

    private ulong runtimeStepCount = 0;
    private float runtimeSimulatedTimeSeconds = 0.0f;
    private float runtimeSimulatedTimeMinutes = 0.0f;
    private float nextReadOnlyInspectorUpdateRealtime = 0.0f;
    private bool readOnlyInspectorDirty = true;
    private float runtimeAdaptiveOutletRhoOffset = 0.0f;
    private float runtimeAdaptiveFeedbackAvgDensity = 1.0f;
    private float runtimeDebugInletFlowFromSampler = 0.0f;
    private float runtimeDebugInletFlowFromBoxes = 0.0f;
    private float runtimeDebugOutletAreaFromBoxes = 0.0f;
    private bool runtimeDebugAnyOutletTargetApplied = false;
    private int runtimeDebugOutletTargetAppliedPatchCount = 0;
    private float runtimeEffectiveOutletAreaPhys = 0.0f;
    private float runtimeGrossToEffectiveAreaRatio = 0.0f;
    private float runtimeEffectiveOutletNormalSpeedFromFlow = 0.0f;
    private float runtimeGrossAreaBasedTargetNormalSpeed = 0.0f;
    private float runtimeComputedOutletTargetNormalSpeedPhys = 0.0f;
    private float runtimeTotalOutletAreaPhys = 0.0f;

    private float lx, ly, lz;
    private bool scalingDirty = true;
    private bool solverRebuildRequired = true;
    private bool summaryDirty = true;

    const float eps = 1e-6f;

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
    public ulong StepCount => runtimeStepCount;
    public float SimulatedTimeSeconds => runtimeSimulatedTimeSeconds;
    public float TempPhysMinDegC => tempPhysMinDegC;
    public float TempPhysMaxDegC => tempPhysMaxDegC;
    public float ReferenceTemperatureDegC => referenceTemperatureDegCInput;
    public float PrandtlTarget => prandtlTarget;
    public float ThermalExpansionBeta => beta;
    public float GravityPhysicalY => gravity_y;
    public float GravityLatticeY => gravityLat;
    public bool IsSimulationRunning => runSimulation;
    public bool IsExternallyPaused => externalStepPause;
    public SolverEasePreset SolverPreset => solverPreset;
    public string SolverPresetName => solverPreset.ToString();
    public string ActiveCaseName => activeCaseName;
    public SimulationHealthStatus StabilityStatus => stabilityStatus;
    public SimulationHealthStatus ReadinessStatus => readinessStatus;
    public string StabilityStatusText => stabilitySummary;
    public string ReadinessStatusText => readinessSummary;
    public float MachNumber => machNumber;
    public float ReynoldsNumber => reynoldsNumberPhys;
    public float PrandtlNumber => prandtlNumber;
    public float TauF => tau_f;
    public float TauT => tau_T;
    public float TauFRaw => tauFRaw;
    public float TauTRaw => tauTRaw;
    public float TauFluidMin => tauFluidMin;
    public float TauThermalMin => tauThermalMin;
    public float TauFluidMax => tauFluidMax;
    public float TauThermalMax => tauThermalMax;
    public bool TauFWasClamped => tauFWasClamped;
    public bool TauTWasClamped => tauTWasClamped;
    public float NuPhysTarget => nuPhysTargetReadOnly;
    public float AlphaPhysTarget => alphaPhysTargetReadOnly;
    public float NuPhysEffective => nuPhys;
    public float AlphaPhysEffective => alphaPhys;
    public float NuPhysEffectiveRatio => nuPhysEffectiveRatio;
    public float AlphaPhysEffectiveRatio => alphaPhysEffectiveRatio;
    public float MaxMachLimit => maxMach;
    public string CollisionModelName => "MRT";
    public string TurbulenceModelName => turbulenceModel.ToString();
    public float TurbulenceModelConstant => turbulenceModelConstant;
    public float TurbulentPrandtl => turbulentPrandtl;
    public bool WallFunctionEnabled => wallFunctionEnabled;
    public bool UseTargetSimulationTime => useTargetSimulationTime;
    public float TargetSimulationTimeSeconds => targetSimulationTimeSeconds;
    public bool TargetTimeReached => targetTimeReached;
    public float EstimatedTotalGpuMemoryMB => estimatedTotalGpuMemoryMB;
    public float EstimatedDistributionBuffersMB => estimatedDistributionBuffersMB;
    public float EstimatedCoreBuffersMB => estimatedCoreBuffersMB;
    public float EstimatedTexturesMB => estimatedTexturesMB;
    public float EstimatedLargestDistributionBufferMB => perDirectionBufferMB;
    public bool EstimatedSingleBufferSafe => estimatedSingleBufferSafe;
    public bool CaseStudyExecutionEnabled => enableCaseStudyExecution;
    public CaseStudyPreset SelectedCaseStudy => selectedCaseStudy;
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

    public void SetSelectedCaseStudy(CaseStudyPreset preset)
    {
        selectedCaseStudy = preset;
        MarkSummaryDirty();
    }

    public void SetCaseStudyExecutionEnabled(bool enabled)
    {
        enableCaseStudyExecution = enabled;
        if (!enabled)
        {
            activeCaseName = "Manual";
            TryApplyExperimentTagToLogger(activeCaseName);
        }
        MarkSummaryDirty();
    }

    public void SetTargetSimulationTime(float seconds, bool enabled = true)
    {
        useTargetSimulationTime = enabled;
        targetSimulationTimeSeconds = Mathf.Max(0.0f, seconds);
        targetTimeReached = false;
        MarkSummaryDirty();
    }

    /// <summary>
    /// Narrow configuration entry point used by the UX-02 solver command adapter.
    /// It stages values only; callers rebuild the solver after scene boundary values
    /// have also been applied.
    /// </summary>
    public bool TryApplySetupConfiguration(
        string experimentTag,
        CaseStudyPreset basedOnPreset,
        float requestedDxPhys,
        float requestedTauFluidMin,
        float requestedTauThermalMin,
        TurbulenceModel requestedTurbulenceModel,
        float requestedTurbulenceConstant,
        float requestedTurbulentPrandtl,
        float requestedTemperatureMinDegC,
        float requestedTemperatureMaxDegC,
        float requestedReferenceTemperatureDegC,
        float requestedPrandtlTarget,
        float requestedThermalExpansionBeta,
        float requestedGravityPhysicalY,
        out string issue)
    {
        const float requiredCaseStudyDx = 0.04f;
        if (!IsFiniteSetupValue(requestedDxPhys) ||
            Mathf.Abs(requestedDxPhys - requiredCaseStudyDx) > 0.00001f)
        {
            issue = $"UX-02 Case Study requires dxPhys={requiredCaseStudyDx:F2} m.";
            return false;
        }

        if (!IsFiniteSetupValue(requestedTauFluidMin) ||
            requestedTauFluidMin < 0.5001f || requestedTauFluidMin > 1.0f)
        {
            issue = "tauFluidMin must be between 0.5001 and 1.0.";
            return false;
        }

        if (!IsFiniteSetupValue(requestedTauThermalMin) ||
            requestedTauThermalMin < 0.5001f || requestedTauThermalMin > 1.0f)
        {
            issue = "tauThermalMin must be between 0.5001 and 1.0.";
            return false;
        }

        if (!IsFiniteSetupValue(requestedTurbulenceConstant) || requestedTurbulenceConstant < 0f ||
            !IsFiniteSetupValue(requestedTurbulentPrandtl) || requestedTurbulentPrandtl <= 0f ||
            !IsFiniteSetupValue(requestedTemperatureMinDegC) ||
            !IsFiniteSetupValue(requestedTemperatureMaxDegC) ||
            !IsFiniteSetupValue(requestedReferenceTemperatureDegC) ||
            !IsFiniteSetupValue(requestedPrandtlTarget) || requestedPrandtlTarget <= 0f ||
            !IsFiniteSetupValue(requestedThermalExpansionBeta) || requestedThermalExpansionBeta < 0f ||
            !IsFiniteSetupValue(requestedGravityPhysicalY))
        {
            issue = "One or more UX-02 physics values are outside the supported finite range.";
            return false;
        }

        if (requestedTemperatureMaxDegC <= requestedTemperatureMinDegC ||
            requestedReferenceTemperatureDegC < requestedTemperatureMinDegC ||
            requestedReferenceTemperatureDegC > requestedTemperatureMaxDegC)
        {
            issue = "The reference temperature must be inside a valid min/max temperature range.";
            return false;
        }

        SetSimulationRunning(false);
        selectedCaseStudy = basedOnPreset;
        solverPreset = SolverEasePreset.Custom;
        activeCaseName = string.IsNullOrWhiteSpace(experimentTag) ? "Manual" : experimentTag.Trim();
        dxPhys = requiredCaseStudyDx;
        tauFluidMin = requestedTauFluidMin;
        tauThermalMin = requestedTauThermalMin;
        turbulenceModel = requestedTurbulenceModel;
        turbulenceModelConstant = requestedTurbulenceConstant;
        turbulentPrandtl = requestedTurbulentPrandtl;
        tempPhysMinDegC = requestedTemperatureMinDegC;
        tempPhysMaxDegC = requestedTemperatureMaxDegC;
        referenceTemperatureDegCInput = requestedReferenceTemperatureDegC;
        prandtlTarget = requestedPrandtlTarget;
        beta = requestedThermalExpansionBeta;
        gravity_y = requestedGravityPhysicalY;

        SyncLegacyTauClampFields();
        scalingDirty = true;
        solverRebuildRequired = true;
        targetTimeReached = false;
        caseStudySummary =
            "=== UX-02 Setup ===\n" +
            $"Active Case      : {activeCaseName}\n" +
            $"dxPhys           : {dxPhys:F4} m\n" +
            $"tauFluidMin      : {tauFluidMin:F4}\n" +
            $"tauThermalMin    : {tauThermalMin:F4}\n" +
            $"Turbulence       : {turbulenceModel}, C={turbulenceModelConstant:F3}";
        TryApplyExperimentTagToLogger(activeCaseName);
        MarkSummaryDirty();
        RefreshReadOnlyInspectorNow();
        issue = string.Empty;
        return true;
    }

    private static bool IsFiniteSetupValue(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public void SetExternalStepPause(bool paused, string reason = null)
    {
        externalStepPause = paused;
        externalStepPauseReason = paused ? (reason ?? string.Empty) : string.Empty;
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
        bool deferSolverUntilConfirmation = CoSimulationStartupGate.IsWaitingForConfirmation;
        if (deferSolverUntilConfirmation)
            runSimulation = false;

        if (sceneCache != null)
        {
            sceneCache.ForceRefresh();
            sceneCache.LogIfEmpty();
        }

        UpdateSceneCacheStatus();

        if (deferSolverUntilConfirmation)
        {
            ResetAdaptiveOutletRhoFeedback();
            ResetMassFluxCorrectedOutletTargets();
            MarkSummaryDirty();
            Debug.Log(
                "[SimulationController] Scaling, memory validation, and LBM solver initialization " +
                "deferred until initial conditions are confirmed.");
            return;
        }

        RebuildScaling();
        ValidateAndLogMemoryEstimate();

        RebuildSolver();
        ResetAdaptiveOutletRhoFeedback();
        ResetMassFluxCorrectedOutletTargets();
        ApplyMassFluxCorrectedOutletTarget();
        MarkSummaryDirty();
        RefreshReadOnlyInspectorNow();

        CheckAndLogTimeConsistency(true);

        if (autoLogSummaryOnStart)
            Debug.Log(latestSummary);

        scalingDirty = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        scalingDirty = true;
        solverRebuildRequired = true;
        NormalizeTauClampFields();

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

            if (solverRebuildRequired && !CoSimulationStartupGate.IsWaitingForConfirmation)
                RebuildSolver();

            ResetMassFluxCorrectedOutletTargets();
            ApplyMassFluxCorrectedOutletTarget();

            MarkSummaryDirty();
            RefreshReadOnlyInspectorNow();
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
            RefreshReadOnlyInspectorNow();
            CheckAndLogTimeConsistency(true);

            if (autoLogSummaryOnSceneRefresh)
                Debug.Log(latestSummary);
        }
        else
        {
            UpdateSceneCacheStatus();
        }

        if (!runSimulation || externalStepPause || CoSimulationStartupGate.IsWaitingForConfirmation || _lbmSolver == null)
            return;

        if (readinessStatus == SimulationHealthStatus.Invalid)
            return;

        float nextSimulatedTimeSeconds = (runtimeStepCount + 1) * dtPhys;

        if (useTargetSimulationTime && nextSimulatedTimeSeconds > targetSimulationTimeSeconds)
        {
            runtimeSimulatedTimeSeconds = runtimeStepCount * dtPhys;
            runtimeSimulatedTimeMinutes = runtimeSimulatedTimeSeconds / 60.0f;
            targetTimeReached = true;
            runSimulation = false;

            Debug.Log(
                $"[SimulationController] Target simulation time reached before next step. " +
                $"Target = {targetSimulationTimeSeconds:F3}s, Current = {runtimeSimulatedTimeSeconds:F3}s. " +
                "Simulation stopped automatically.");

            MarkSummaryDirty();
            RefreshReadOnlyInspectorNow();
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

        runtimeStepCount++;
        runtimeSimulatedTimeSeconds = runtimeStepCount * dtPhys;
        runtimeSimulatedTimeMinutes = runtimeSimulatedTimeSeconds / 60.0f;
        if (targetTimeReached)
            targetTimeReached = false;

        ApplyMassFluxCorrectedOutletTarget();
        LogGrossVsEffectiveOutletAreaComparison();

        if (ApplyAdaptiveOutletRhoFeedback())
        {
            PushAdaptiveOutletRhoToSolver();
        }

        MarkSummaryDirty();
        RefreshReadOnlyInspectorIfDue();
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
        RefreshReadOnlyInspectorNow();
        CheckAndLogTimeConsistency(true);
        Debug.Log(latestSummary);
    }

    public void SyncDynamicBoundaryInputsNow()
    {
        if (sceneCache == null)
        {
            UpdateSceneCacheStatus();
            Debug.LogWarning("[SimulationController] Dynamic boundary sync skipped because scene cache is missing.");
            return;
        }

        if (sceneCache.IsDirty)
        {
            sceneCache.Refresh();
        }
        else if (sceneCache.ZouHeBoxes != null)
        {
            foreach (var box in sceneCache.ZouHeBoxes)
            {
                if (box != null && box.Power)
                    box.Refresh();
            }
        }

        UpdateSceneCacheStatus();

        if (_lbmSolver != null)
        {
            _lbmSolver.SyncSourcesAtRuntime(
                sceneCache.HeatSources,
                sceneCache.Racks,
                sceneCache.ACSources,
                sceneCache.ZouHeBoxes);

            _lbmSolver.MarkAllDynamicInputsDirty();
        }

        MarkSummaryDirty();
        RefreshReadOnlyInspectorNow();
    }

    public void ApplyInitialRoomTemperatureDegC(float value)
    {
        referenceTemperatureDegCInput = value;
        if (referenceTemperatureDegCInput < tempPhysMinDegC)
            tempPhysMinDegC = referenceTemperatureDegCInput;
        if (referenceTemperatureDegCInput > tempPhysMaxDegC)
            tempPhysMaxDegC = referenceTemperatureDegCInput;

        scalingDirty = true;
        solverRebuildRequired = true;
        ResetPhysicalTime();
        MarkSummaryDirty();
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
        RefreshReadOnlyInspectorNow();
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
        RefreshReadOnlyInspectorNow();
    }

    [ContextMenu("Apply Selected Case Study")]
    public void ApplySelectedCaseStudy()
    {
        ApplyCaseStudyPresetInternal(selectedCaseStudy, false);
    }

    [ContextMenu("Run Selected Case Study")]
    public void RunSelectedCaseStudy()
    {
        ApplyCaseStudyPresetInternal(selectedCaseStudy, true);
    }

    [ContextMenu("Apply Case Study A0 Baseline")]
    public void ApplyCaseStudyA0Baseline()
    {
        selectedCaseStudy = CaseStudyPreset.A0_Baseline;
        ApplySelectedCaseStudy();
    }

    [ContextMenu("Apply Next Case Study")]
    public void ApplyNextCaseStudy()
    {
        int next = (int)selectedCaseStudy + 1;
        if (next > (int)CaseStudyPreset.B6_FluidTau_0510_Thermal_0530_Smag010)
            next = (int)CaseStudyPreset.A0_Baseline;

        selectedCaseStudy = (CaseStudyPreset)next;
        ApplySelectedCaseStudy();
    }

    [ContextMenu("Return To Case Study Baseline")]
    public void ReturnToCaseStudyBaseline()
    {
        ApplyCaseStudyA0Baseline();
    }

    public void ApplyCaseStudyPreset(CaseStudyPreset preset)
    {
        ApplyCaseStudyPresetInternal(preset, false);
    }

    public void RunCaseStudyPreset(CaseStudyPreset preset)
    {
        ApplyCaseStudyPresetInternal(preset, true);
    }

    private void ApplyCaseStudyPresetInternal(CaseStudyPreset preset, bool startRunning)
    {
        CaseStudyDefinition definition = GetCaseStudyDefinition(preset);
        bool shouldRun = startRunning && enableCaseStudyExecution;

        selectedCaseStudy = preset;
        solverPreset = SolverEasePreset.Custom;
        activeCaseName = definition.Name;
        dxPhys = 0.04f;
        tauFluidMin = definition.TauFluidMin;
        tauThermalMin = definition.TauThermalMin;
        tauFluidMax = 4.0f;
        tauThermalMax = 4.0f;
        turbulenceModel = definition.TurbulenceModel;
        turbulenceModelConstant = definition.TurbulenceConstant;

        SyncLegacyTauClampFields();
        scalingDirty = true;
        solverRebuildRequired = true;
        targetTimeReached = false;
        runSimulation = shouldRun;

        ResetPhysicalTime();
        RebuildScaling();
        UpdateCaseStudySummary(definition);
        TryApplyExperimentTagToLogger(activeCaseName);

        MarkSummaryDirty();
        RefreshReadOnlyInspectorNow();

        if (startRunning && !enableCaseStudyExecution)
        {
            Debug.LogWarning(
                "[SimulationController] Case study execution is disabled. " +
                "Enable 'Enable Case Study Execution' in the Case Study section before running.");
        }

        string mode = shouldRun ? "Run" : "Apply";
        Debug.Log($"[SimulationController] {mode} case study preset: {activeCaseName}\n{caseStudySummary}");
    }

    [ContextMenu("Check Run Readiness")]
    public void PrintRunReadiness()
    {
        CheckRunReadiness();
        MarkSummaryDirty();
        RefreshReadOnlyInspectorNow();

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
        RefreshReadOnlyInspectorNow();
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
        runtimeStepCount = 0;
        runtimeSimulatedTimeSeconds = 0.0f;
        runtimeSimulatedTimeMinutes = 0.0f;
        stepCount = 0;
        simulatedTimeSeconds = 0.0f;
        simulatedTimeMinutes = 0.0f;
        expectedSimulatedTimeSeconds = 0.0f;
        simulatedTimeErrorSeconds = 0.0f;
        targetTimeReached = false;
        runtimeComputedOutletTargetNormalSpeedPhys = 0.0f;
        runtimeTotalOutletAreaPhys = 0.0f;
        MarkSummaryDirty();
        RefreshReadOnlyInspectorNow();
        CheckAndLogTimeConsistency(true);
        ResetAdaptiveOutletRhoFeedback();
        ResetMassFluxCorrectedOutletTargets();

        if (resultSampler == null)
            resultSampler = GetComponent<SimulationResultSampler>();

        resultSampler?.ResetSamplingSchedule();
    }

    private readonly struct CaseStudyDefinition
    {
        public readonly string Name;
        public readonly float TauFluidMin;
        public readonly float TauThermalMin;
        public readonly TurbulenceModel TurbulenceModel;
        public readonly float TurbulenceConstant;
        public readonly string Description;

        public CaseStudyDefinition(
            string name,
            float tauFluidMin,
            float tauThermalMin,
            TurbulenceModel turbulenceModel,
            float turbulenceConstant,
            string description)
        {
            Name = name;
            TauFluidMin = tauFluidMin;
            TauThermalMin = tauThermalMin;
            TurbulenceModel = turbulenceModel;
            TurbulenceConstant = turbulenceConstant;
            Description = description;
        }
    }

    private static CaseStudyDefinition GetCaseStudyDefinition(CaseStudyPreset preset)
    {
        switch (preset)
        {
            case CaseStudyPreset.A1_FluidTau_0530_Thermal_0560_Off:
                return new CaseStudyDefinition(
                    "A1_FluidTau_0530_Thermal_0560_Off",
                    0.530f,
                    0.560f,
                    TurbulenceModel.None,
                    0.0f,
                    "Fluid viscosity diffusion reduction only; thermal diffusion kept at baseline.");

            case CaseStudyPreset.A2_FluidTau_0510_Thermal_0560_Off:
                return new CaseStudyDefinition(
                    "A2_FluidTau_0510_Thermal_0560_Off",
                    0.510f,
                    0.560f,
                    TurbulenceModel.None,
                    0.0f,
                    "Aggressive fluid viscosity reduction; thermal diffusion kept at baseline.");

            case CaseStudyPreset.A3_FluidTau_0510_Thermal_0530_Off:
                return new CaseStudyDefinition(
                    "A3_FluidTau_0510_Thermal_0530_Off",
                    0.510f,
                    0.530f,
                    TurbulenceModel.None,
                    0.0f,
                    "Fluid and thermal diffusion both reduced without turbulence model.");

            case CaseStudyPreset.A4_FluidTau_0510_Thermal_0530_Smag003:
                return new CaseStudyDefinition(
                    "A4_FluidTau_0510_Thermal_0530_Smag003",
                    0.510f,
                    0.530f,
                    TurbulenceModel.Smagorinsky,
                    0.03f,
                    "Reduced fluid/thermal diffusion with light Smagorinsky stabilization.");

            case CaseStudyPreset.B1_FluidTau_0515_Thermal_0535_Off:
                return new CaseStudyDefinition(
                    "B1_FluidTau_0515_Thermal_0535_Off",
                    0.515f,
                    0.535f,
                    TurbulenceModel.None,
                    0.0f,
                    "Secondary sweep: moderate fluid and thermal diffusion reduction.");

            case CaseStudyPreset.B2_FluidTau_0510_Thermal_0530_Off:
                return new CaseStudyDefinition(
                    "B2_FluidTau_0510_Thermal_0530_Off",
                    0.510f,
                    0.530f,
                    TurbulenceModel.None,
                    0.0f,
                    "Secondary sweep reference without a turbulence model.");

            case CaseStudyPreset.B3_FluidTau_0505_Thermal_0525_Off:
                return new CaseStudyDefinition(
                    "B3_FluidTau_0505_Thermal_0525_Off",
                    0.505f,
                    0.525f,
                    TurbulenceModel.None,
                    0.0f,
                    "Secondary sweep near the fluid relaxation stability limit.");

            case CaseStudyPreset.B4_FluidTau_0510_Thermal_0530_Smag003:
                return new CaseStudyDefinition(
                    "B4_FluidTau_0510_Thermal_0530_Smag003",
                    0.510f,
                    0.530f,
                    TurbulenceModel.Smagorinsky,
                    0.03f,
                    "Secondary sweep with Smagorinsky Cs=0.03.");

            case CaseStudyPreset.B5_FluidTau_0510_Thermal_0530_Smag006:
                return new CaseStudyDefinition(
                    "B5_FluidTau_0510_Thermal_0530_Smag006",
                    0.510f,
                    0.530f,
                    TurbulenceModel.Smagorinsky,
                    0.06f,
                    "Secondary sweep with Smagorinsky Cs=0.06.");

            case CaseStudyPreset.B6_FluidTau_0510_Thermal_0530_Smag010:
                return new CaseStudyDefinition(
                    "B6_FluidTau_0510_Thermal_0530_Smag010",
                    0.510f,
                    0.530f,
                    TurbulenceModel.Smagorinsky,
                    0.10f,
                    "Secondary sweep with Smagorinsky Cs=0.10.");

            case CaseStudyPreset.A0_Baseline:
            default:
                return new CaseStudyDefinition(
                    "A0_Baseline",
                    0.560f,
                    0.560f,
                    TurbulenceModel.Smagorinsky,
                    0.03f,
                    "Current conservative clamp baseline.");
        }
    }

    private void UpdateCaseStudySummary(CaseStudyDefinition definition)
    {
        caseStudySummary =
            "=== Case Study ===\n" +
            $"Active Case      : {definition.Name}\n" +
            $"Execution Enabled: {enableCaseStudyExecution}\n" +
            $"dxPhys           : {dxPhys:F4} m\n" +
            $"tauFluidMin      : {tauFluidMin:F4}\n" +
            $"tauThermalMin    : {tauThermalMin:F4}\n" +
            $"Turbulence       : {turbulenceModel}, C={turbulenceModelConstant:F3}\n" +
            $"Target Run Time  : {(useTargetSimulationTime ? targetSimulationTimeSeconds.ToString("F3") + " s" : "Manual")}\n" +
            $"Meaning          : {definition.Description}";
    }

    private void TryApplyExperimentTagToLogger(string tag)
    {
        var logger = FindFirstObjectByType<SimulationMetricsFileLogger>();
        if (logger != null)
            logger.SetExperimentTag(tag);
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
        expectedSimulatedTimeSeconds = runtimeStepCount * dtPhys;
        simulatedTimeErrorSeconds = math.abs(expectedSimulatedTimeSeconds - runtimeSimulatedTimeSeconds);

        if (!logTimeConsistency)
            return;

        bool shouldLog = forceLog;

        if (!shouldLog && timeConsistencyLogIntervalSteps > 0)
        {
            shouldLog = (runtimeStepCount > 0) &&
                        (runtimeStepCount % (ulong)timeConsistencyLogIntervalSteps == 0);
        }

        if (!shouldLog)
            return;

        string message =
            "[LBM Time Check] " +
            $"stepCount={runtimeStepCount:N0}, " +
            $"dtPhys={dtPhys:E6} s, " +
            $"expectedTime={expectedSimulatedTimeSeconds:F6} s, " +
            $"actualTime={runtimeSimulatedTimeSeconds:F6} s, " +
            $"error={simulatedTimeErrorSeconds:E6} s";

        if (simulatedTimeErrorSeconds > timeConsistencyWarningThreshold)
            Debug.LogWarning(message);
        else
            Debug.Log(message);
    }

    private void MarkSummaryDirty()
    {
        summaryDirty = true;
        readOnlyInspectorDirty = true;
    }

    private void RefreshReadOnlyInspectorIfDue()
    {
        if (!readOnlyInspectorDirty)
            return;

        float now = Time.realtimeSinceStartup;
        if (now < nextReadOnlyInspectorUpdateRealtime)
            return;

        RefreshReadOnlyInspectorNow();
    }

    private void RefreshReadOnlyInspectorNow()
    {
        stepCount = runtimeStepCount;
        simulatedTimeSeconds = runtimeSimulatedTimeSeconds;
        simulatedTimeMinutes = runtimeSimulatedTimeMinutes;
        SyncRuntimeDiagnosticsToInspector();

        PullLatestResultMetrics();
        CheckAndLogTimeConsistency();
        RefreshSummaryIfNeeded();

        float interval = Mathf.Max(readOnlyInspectorUpdateIntervalSeconds, 0.05f);
        nextReadOnlyInspectorUpdateRealtime = Time.realtimeSinceStartup + interval;
        readOnlyInspectorDirty = false;
        readOnlyInspectorUpdateStatus =
            $"Updated at t={runtimeSimulatedTimeSeconds:F3}s, step={runtimeStepCount:N0}, interval={interval:F2}s";
    }

    private void SyncRuntimeDiagnosticsToInspector()
    {
        adaptiveOutletRhoOffset = runtimeAdaptiveOutletRhoOffset;
        adaptiveFeedbackAvgDensity = runtimeAdaptiveFeedbackAvgDensity;

        debugInletFlowFromSampler = runtimeDebugInletFlowFromSampler;
        debugInletFlowFromBoxes = runtimeDebugInletFlowFromBoxes;
        debugOutletAreaFromBoxes = runtimeDebugOutletAreaFromBoxes;
        debugAnyOutletTargetApplied = runtimeDebugAnyOutletTargetApplied;
        debugOutletTargetAppliedPatchCount = runtimeDebugOutletTargetAppliedPatchCount;

        effectiveOutletAreaPhys = runtimeEffectiveOutletAreaPhys;
        grossToEffectiveAreaRatio = runtimeGrossToEffectiveAreaRatio;
        effectiveOutletNormalSpeedFromFlow = runtimeEffectiveOutletNormalSpeedFromFlow;
        grossAreaBasedTargetNormalSpeed = runtimeGrossAreaBasedTargetNormalSpeed;

        computedOutletTargetNormalSpeedPhys = runtimeComputedOutletTargetNormalSpeedPhys;
        totalOutletAreaPhys = runtimeTotalOutletAreaPhys;
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
        runtimeAdaptiveOutletRhoOffset = 0.0f;
        runtimeAdaptiveFeedbackAvgDensity = 1.0f;

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
        runtimeComputedOutletTargetNormalSpeedPhys = 0.0f;
        runtimeTotalOutletAreaPhys = 0.0f;
        runtimeDebugInletFlowFromSampler = 0.0f;
        runtimeDebugInletFlowFromBoxes = 0.0f;
        runtimeDebugOutletAreaFromBoxes = 0.0f;
        runtimeDebugAnyOutletTargetApplied = false;
        runtimeDebugOutletTargetAppliedPatchCount = 0;
        runtimeEffectiveOutletAreaPhys = 0.0f;
        runtimeGrossToEffectiveAreaRatio = 0.0f;
        runtimeEffectiveOutletNormalSpeedFromFlow = 0.0f;
        runtimeGrossAreaBasedTargetNormalSpeed = 0.0f;

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

        runtimeAdaptiveFeedbackAvgDensity = m.avgDensity;

        float densityError = 1.0f - m.avgDensity;

        if (Mathf.Abs(densityError) < adaptiveOutletRhoDeadband)
            densityError = 0.0f;

        float newOffset = Mathf.Clamp(
            adaptiveOutletRhoGain * densityError,
            -adaptiveOutletRhoMaxOffset,
            adaptiveOutletRhoMaxOffset);

        if (Mathf.Abs(newOffset - runtimeAdaptiveOutletRhoOffset) < 1e-7f)
            return false;

        runtimeAdaptiveOutletRhoOffset = newOffset;

        bool changed = false;
        foreach (var box in sceneCache.ZouHeBoxes)
        {
            if (box == null || !box.Power || box.PatchKind != LBMZouHeBox.Kind.Outlet)
                continue;

            box.SetAdaptiveRhoOutOffset(runtimeAdaptiveOutletRhoOffset);
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

        if (tau_f <= 0.5f || tau_T <= 0.5f)
            AppendReadinessLine(errors, $"Relaxation time must be greater than 0.5: tau_f={tau_f:F4}, tau_T={tau_T:F4}.");

        if (machNumber > 0.30f)
            AppendReadinessLine(errors, $"Mach number is too high for this setup: Ma={machNumber:F4}.");
        else if (machNumber > 0.15f)
            AppendReadinessLine(warnings, $"Mach number is high: Ma={machNumber:F4}. Lower maxMach for better stability.");

        if (tau_f < 0.51f || tau_T < 0.51f)
            AppendReadinessLine(warnings, $"tau is very close to the LBM lower limit 0.5: tau_f={tau_f:F4}, tau_T={tau_T:F4}.");
        else if (tau_f < 0.53f || tau_T < 0.53f)
            AppendReadinessLine(warnings, $"tau is below the conservative range but allowed for case study: tau_f={tau_f:F4}, tau_T={tau_T:F4}.");

        if (estimatedTotalGpuMemoryMB > 0.0f)
        {
            float vramBudgetGB = manualVramBudgetGB > 0f
                ? manualVramBudgetGB
                : SystemInfo.graphicsMemorySize / 1024f;
            float warnBudgetMB = Mathf.Max(vramBudgetGB, 0.0f) * 1024.0f * Mathf.Clamp(vramWarningRatio, 0.1f, 1.0f);
            if (warnBudgetMB > 0.0f && estimatedTotalGpuMemoryMB > warnBudgetMB)
                AppendReadinessLine(warnings, $"Estimated GPU memory is high: {estimatedTotalGpuMemoryMB:F1} MiB.");
        }

        int configuredInletCount = 0;
        int configuredOutletCount = 0;
        int activeInletCount = 0;
        int activeOutletCount = 0;
        bool hasBadPatch = false;
        if (sceneCache != null && sceneCache.ZouHeBoxes != null)
        {
            foreach (var box in sceneCache.ZouHeBoxes)
            {
                if (box == null)
                    continue;

                if (box.PatchKind == LBMZouHeBox.Kind.Inlet)
                {
                    configuredInletCount++;
                    if (box.Power)
                        activeInletCount++;
                }
                else
                {
                    configuredOutletCount++;
                    if (box.Power)
                        activeOutletCount++;
                }

                if (box.Power && box.PatchCellCount == 0)
                    hasBadPatch = true;
            }
        }

        if (configuredInletCount == 0)
            AppendReadinessLine(errors, "At least one inlet boundary is required.");
        else if (activeInletCount == 0)
            AppendReadinessLine(warnings, "All configured inlet boundaries are currently Off. Flow remains zero until R1 is enabled.");

        if (configuredOutletCount == 0)
            AppendReadinessLine(errors, "At least one outlet boundary is required.");
        else if (activeOutletCount == 0)
            AppendReadinessLine(warnings, "All configured outlet boundaries are currently Off.");

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
            $"Inlets : {activeInletCount}/{configuredInletCount} active, " +
            $"Outlets : {activeOutletCount}/{configuredOutletCount} active\n" +
            $"Errors :\n{(errors.Length > 0 ? errors.ToString() : "  None\n")}" +
            $"Warnings :\n{(warnings.Length > 0 ? warnings.ToString() : "  None\n")}";

        return readinessStatus;
    }

    private void UpdatePowerFlowSummaries()
    {
        UpdateStabilitySummary();
        CheckRunReadiness();
        RefreshCaseStudySummaryText();

        long cellCount = GetCellCount64();
        caseSummary =
            "=== Case Summary ===\n" +
            $"Preset          : {solverPreset}\n" +
            $"Active Case     : {activeCaseName}\n" +
            $"Domain [m]      : {lx:F3} x {ly:F3} x {lz:F3}\n" +
            $"Cell size [m]   : {dxPhys:F5}\n" +
            $"Grid            : {nx} x {ny} x {nz} ({cellCount:N0} cells)\n" +
            $"Estimated memory: {estimatedTotalGpuMemoryMB:F1} MiB\n" +
            $"Simulation time : {runtimeSimulatedTimeSeconds:F3} s";

        solverSummary =
            "=== Solver Summary ===\n" +
            $"dtPhys [s]      : {dtPhys:E6}\n" +
            $"maxMach limit   : {maxMach:F3}\n" +
            $"Mach            : {machNumber:F4}\n" +
            $"tau_f / tau_T   : {tauFRaw:F4}->{tau_f:F4} / {tauTRaw:F4}->{tau_T:F4}\n" +
            $"nu / alpha phys : {nuPhys:E4} / {alphaPhys:E4}\n" +
            $"target ratio    : nu x{nuPhysEffectiveRatio:F2}, alpha x{alphaPhysEffectiveRatio:F2}\n" +
            $"Re / Pr         : {reynoldsNumberPhys:E4} / {prandtlNumber:F4}\n" +
            $"Collision       : MRT, {turbulenceModel}";

        boundarySummary = BuildBoundarySummaryText();
        recommendationSummary = BuildRecommendationSummary();
    }

    private void RefreshCaseStudySummaryText()
    {
        if (!string.IsNullOrWhiteSpace(activeCaseName) && activeCaseName != "Manual")
        {
            UpdateCaseStudySummary(GetCaseStudyDefinition(selectedCaseStudy));
            return;
        }

        activeCaseName = "Manual";
        caseStudySummary =
            "=== Case Study ===\n" +
            "Active Case      : Manual\n" +
            $"Execution Enabled: {enableCaseStudyExecution}\n" +
            $"dxPhys           : {dxPhys:F4} m\n" +
            $"tauFluidMin      : {tauFluidMin:F4}\n" +
            $"tauThermalMin    : {tauThermalMin:F4}\n" +
            $"Turbulence       : {turbulenceModel}, C={turbulenceModelConstant:F3}\n" +
            "Meaning          : Manual solver settings.";
    }

    private void UpdateStabilitySummary()
    {
        bool invalid =
            dxPhys <= 0.0f ||
            dtPhys <= 0.0f ||
            tau_f <= 0.5f ||
            tau_T <= 0.5f ||
            machNumber > 0.30f;

        bool warning =
            machNumber > 0.15f ||
            tau_f < 0.53f ||
            tau_T < 0.53f ||
            !estimatedSingleBufferSafe;

        stabilityStatus = invalid
            ? SimulationHealthStatus.Invalid
            : (warning ? SimulationHealthStatus.Warning : SimulationHealthStatus.OK);

        stabilitySummary =
            "=== Stability Summary ===\n" +
            $"Status          : {stabilityStatus}\n" +
            $"Mach            : {machNumber:F4} (recommended <= 0.15)\n" +
            $"tau_f / tau_T   : {tau_f:F4} / {tau_T:F4} (case-study warning below 0.53, invalid <= 0.5)\n" +
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

        if (tau_f < 0.53f || tau_T < 0.53f)
            return "=== Recommendation ===\nCase-study tau setting is near 0.5. Monitor mass residual, Mach, and density drift closely.";

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
            runtimeStepCount,
            runtimeSimulatedTimeSeconds,
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
            "\n\n" + caseStudySummary +
            "\n\n" + boundarySummary +
            "\n\n" + solverSummary +
            "\n\n" + scalingDiagnosticsSummary +
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

        UpdateContourPlotUpdateInterval();
        ResetAdaptiveOutletRhoFeedback();
        ResetMassFluxCorrectedOutletTargets();
        solverRebuildRequired = false;
    }

    private void UpdateContourPlotUpdateInterval()
    {
        float intervalSeconds = Mathf.Max(contourPlotUpdateIntervalSeconds, 0.0f);
        float safeDt = Mathf.Max(dtPhys, 1e-8f);

        contourPlotUpdateIntervalSteps = intervalSeconds <= 0.0f
            ? 1
            : Mathf.Max(1, Mathf.CeilToInt(intervalSeconds / safeDt));

        _lbmSolver?.SetVisualizeInterval(contourPlotUpdateIntervalSteps);
    }

    private void RebuildScaling()
    {
        if (domain == null)
            return;

        if (tempPhysMaxDegC <= tempPhysMinDegC)
            tempPhysMaxDegC = tempPhysMinDegC + 0.01f;

        NormalizeTauClampFields();

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
        nuPhysTargetReadOnly = nuPhys_tgt;
        alphaPhysTargetReadOnly = alphaPhys_tgt;

        float inv_dx2 = 1.0f / (dxPhys * dxPhys);
        float nuLat_tgt = nuPhys_tgt * dtPhys * inv_dx2;
        float alphaLat_tgt = alphaPhys_tgt * dtPhys * inv_dx2;

        tauFRaw = 3.0f * nuLat_tgt + 0.5f;
        tauTRaw = 4.0f * alphaLat_tgt + 0.5f;

        tau_f = math.clamp(tauFRaw, tauFluidMin, tauFluidMax);
        tau_T = math.clamp(tauTRaw, tauThermalMin, tauThermalMax);
        tauFWasClamped = Mathf.Abs(tau_f - tauFRaw) > 1e-6f;
        tauTWasClamped = Mathf.Abs(tau_T - tauTRaw) > 1e-6f;

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
        nuPhysEffectiveRatio = nuPhys_tgt > 0f ? nuPhys / nuPhys_tgt : 0f;
        alphaPhysEffectiveRatio = alphaPhys_tgt > 0f ? alphaPhys / alphaPhys_tgt : 0f;

        RecalculateACSourceScaling();

        machNumber = (csLat > 0f) ? (maxWindSpeedLat / csLat) : 0f;
        reynoldsNumber = (nuLat > 0f) ? (maxWindSpeedLat * maxDomainLengthLat / nuLat) : 0f;

        float U_phys_for_Re = math.max(maxWindSpeedPhys, eps);
        reynoldsNumberPhys = (nuPhys > 0f) ? (U_phys_for_Re * maxDomainLengthPhys / nuPhys) : 0f;

        UpdateMemoryEstimateReadOnly();
        UpdateScalingDiagnosticsSummary();
        LogScalingGuardrails();
    }

    private void LogScalingGuardrails()
    {
        string caseTag = string.IsNullOrWhiteSpace(activeCaseName) ? "Manual" : activeCaseName;

        if (tauFWasClamped)
        {
            string diffusionRisk = tau_f > tauFRaw
                ? "Jet momentum diffusion may be excessive."
                : "Effective momentum diffusion is below the requested target.";
            Debug.LogWarning(
                $"[LBM Scaling][{caseTag}] tau_f raw={tauFRaw:F6} was clamped to {tau_f:F6}. " +
                $"nuPhys target={nuPhysTargetReadOnly:E4}, effective={nuPhys:E4}, ratio={nuPhysEffectiveRatio:F2}x. " +
                diffusionRisk);
        }

        if (tauTWasClamped)
        {
            string diffusionRisk = tau_T > tauTRaw
                ? "Thermal diffusion may be excessive."
                : "Effective thermal diffusion is below the requested target.";
            Debug.LogWarning(
                $"[LBM Scaling][{caseTag}] tau_T raw={tauTRaw:F6} was clamped to {tau_T:F6}. " +
                $"alphaPhys target={alphaPhysTargetReadOnly:E4}, effective={alphaPhys:E4}, ratio={alphaPhysEffectiveRatio:F2}x. " +
                diffusionRisk);
        }

        if (tauFluidMin < 0.51f)
        {
            Debug.LogWarning(
                $"[LBM Stability][{caseTag}] tauFluidMin={tauFluidMin:F4} is close to 0.5. " +
                "NaN, density growth, Mach, and mass residual must be monitored; the requested value was not raised automatically.");
        }

        if (tauThermalMin < 0.51f)
        {
            Debug.LogWarning(
                $"[LBM Stability][{caseTag}] tauThermalMin={tauThermalMin:F4} is close to 0.5. " +
                "Thermal oscillation and clamp counters must be monitored; the requested value was not raised automatically.");
        }
    }

    private void NormalizeTauClampFields()
    {
        tauFluidMin = Mathf.Clamp(tauFluidMin, 0.5001f, 1.0f);
        tauThermalMin = Mathf.Clamp(tauThermalMin, 0.5001f, 1.0f);
        tauFluidMax = Mathf.Clamp(tauFluidMax, 1.0f, 4.0f);
        tauThermalMax = Mathf.Clamp(tauThermalMax, 1.0f, 4.0f);

        if (tauFluidMax <= tauFluidMin)
            tauFluidMax = Mathf.Min(4.0f, tauFluidMin + 0.0001f);

        if (tauThermalMax <= tauThermalMin)
            tauThermalMax = Mathf.Min(4.0f, tauThermalMin + 0.0001f);

        SyncLegacyTauClampFields();
    }

    private void SyncLegacyTauClampFields()
    {
        tauMin = Mathf.Min(tauFluidMin, tauThermalMin);
        tauMax = Mathf.Max(tauFluidMax, tauThermalMax);
    }

    private void UpdateScalingDiagnosticsSummary()
    {
        scalingDiagnosticsSummary =
            "=== Scaling Diagnostics ===\n" +
            $"Active Case      : {activeCaseName}\n" +
            $"tau_f raw/clamp  : {tauFRaw:F6} -> {tau_f:F6} (min={tauFluidMin:F4}, max={tauFluidMax:F4}, clamped={tauFWasClamped})\n" +
            $"tau_T raw/clamp  : {tauTRaw:F6} -> {tau_T:F6} (min={tauThermalMin:F4}, max={tauThermalMax:F4}, clamped={tauTWasClamped})\n" +
            $"nu target/effect : {nuPhysTargetReadOnly:E4} / {nuPhys:E4} (x{nuPhysEffectiveRatio:F2})\n" +
            $"alpha target/eff : {alphaPhysTargetReadOnly:E4} / {alphaPhys:E4} (x{alphaPhysEffectiveRatio:F2})\n" +
            $"maxMach limit    : {maxMach:F4}\n" +
            $"Re / Pr          : {reynoldsNumberPhys:E4} / {prandtlNumber:F4}";
    }

    private void LogGrossVsEffectiveOutletAreaComparison()
    {
        if (!logGrossVsEffectiveOutletArea)
            return;

        if (grossVsEffectiveAreaLogIntervalSteps > 0)
        {
            if ((runtimeStepCount % (ulong)grossVsEffectiveAreaLogIntervalSteps) != 0)
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

        runtimeTotalOutletAreaPhys = grossAreaSum;
        runtimeGrossAreaBasedTargetNormalSpeed = runtimeComputedOutletTargetNormalSpeedPhys;

        float effArea = 0f;
        if (Mathf.Abs(m.outletAverageNormalSpeedPhys) > 1e-8f)
        {
            effArea = m.outletFlowRatePhysAbs / Mathf.Abs(m.outletAverageNormalSpeedPhys);
        }

        runtimeEffectiveOutletAreaPhys = effArea;
        runtimeGrossToEffectiveAreaRatio = (effArea > 1e-12f) ? (grossAreaSum / effArea) : 0f;
        runtimeEffectiveOutletNormalSpeedFromFlow =
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
        sb.AppendLine($"  stepCount                          : {runtimeStepCount}");
        sb.AppendLine($"  simulatedTimeSeconds               : {runtimeSimulatedTimeSeconds:F3}");
        sb.AppendLine($"  outletPatchCount                   : {outletPatchCount}");
        sb.AppendLine($"  grossOutletCellCount(sum patches)  : {grossCellCountSum}");
        sb.AppendLine($"  effectiveOutletSampleCount         : {m.outletSampleCount}");
        sb.AppendLine($"  dxPhys                             : {dxPhys:F6} m");
        sb.AppendLine($"  grossOutletAreaPhys                : {grossAreaSum:F6} m^2");
        sb.AppendLine($"  effectiveOutletAreaPhys            : {effArea:F6} m^2");
        sb.AppendLine($"  gross/effective area ratio         : {runtimeGrossToEffectiveAreaRatio:F6}");
        sb.AppendLine($"  area mismatch                      : {areaMismatchPercent:F3} %");
        sb.AppendLine($"  effectiveCellEstimate(area/dx^2)   : {effectiveCellEstimate}");
        sb.AppendLine($"  inletFlowAbs                       : {m.inletFlowRatePhysAbs:F6} m^3/s");
        sb.AppendLine($"  outletFlowAbs                      : {m.outletFlowRatePhysAbs:F6} m^3/s");
        sb.AppendLine($"  inletAverageNormalSpeedPhys        : {m.inletAverageNormalSpeedPhys:F6} m/s");
        sb.AppendLine($"  outletAverageNormalSpeedPhys       : {m.outletAverageNormalSpeedPhys:F6} m/s");
        sb.AppendLine($"  grossAreaBasedTargetNormalSpeed    : {runtimeGrossAreaBasedTargetNormalSpeed:F6} m/s");
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

        if (runtimeStepCount != 0 && (runtimeStepCount % (ulong)interval) != 0UL)
            return;

        var sb = new StringBuilder(1024);
        sb.AppendLine("[OutletRootCauseDebug] Controller -> Patch Target");
        sb.AppendLine($"  stepCount                    : {runtimeStepCount}");
        sb.AppendLine($"  simulatedTimeSeconds         : {runtimeSimulatedTimeSeconds:F3}");
        sb.AppendLine($"  resultMetricsStatus          : {resultMetricsStatus}");
        sb.AppendLine($"  inletFlowAbs(sampler)        : {inletFlowAbsSampler:F6} m^3/s");
        sb.AppendLine($"  inletFlowAbs(direct boxes)   : {inletFlowAbsDirect:F6} m^3/s");
        sb.AppendLine($"  outletAreaSum                : {outletAreaSum:F6} m^2");
        sb.AppendLine($"  computedTargetNormalSpeed    : {targetNormalSpeedPhys:F6} m/s");
        sb.AppendLine($"  targetAppliedPatchCount      : {appliedPatchCount}");
        sb.AppendLine($"  anyTargetApplied             : {runtimeDebugAnyOutletTargetApplied}");
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

        runtimeTotalOutletAreaPhys = outletAreaSum;
        runtimeDebugOutletAreaFromBoxes = outletAreaSum;

        if (outletCount == 0 || outletAreaSum <= 1e-12f)
        {
            runtimeComputedOutletTargetNormalSpeedPhys = 0f;
            runtimeDebugAnyOutletTargetApplied = false;
            runtimeDebugOutletTargetAppliedPatchCount = 0;

            Debug.LogWarning(
                "[MassFluxTargetDebug] No valid outlet area. " +
                $"outletCount={outletCount}, outletAreaSum={outletAreaSum:F6}");

            return;
        }

        SimulationResultMetrics latestMetrics = resultSampler != null ? resultSampler.LatestMetrics : null;
        float inletFlowAbsSampler = latestMetrics != null && latestMetrics.hasValidFlowDiagnostic
            ? Mathf.Max(0f, latestMetrics.inletFlowRatePhysAbs)
            : Mathf.Max(0f, inletFlowRatePhysAbs);
        float inletFlowAbsDirect = Mathf.Max(0f, ComputeInletFlowAbsDirectFromBoxes());

        runtimeDebugInletFlowFromSampler = inletFlowAbsSampler;
        runtimeDebugInletFlowFromBoxes = inletFlowAbsDirect;

        float targetOutletFlowAbs = Mathf.Max(inletFlowAbsSampler, inletFlowAbsDirect);

        float rawTargetNormalSpeedPhys =
            (outletAreaSum > 1e-12f) ? (targetOutletFlowAbs / outletAreaSum) : 0f;

        float safeMaxTargetNormalSpeedPhys = Mathf.Max(maxOutletTargetNormalSpeedPhys, 0.1f);

        float clampedTargetNormalSpeedPhys = Mathf.Clamp(
            rawTargetNormalSpeedPhys,
            0f,
            safeMaxTargetNormalSpeedPhys);

        runtimeComputedOutletTargetNormalSpeedPhys = clampedTargetNormalSpeedPhys;

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

        runtimeDebugAnyOutletTargetApplied = anyChanged;
        runtimeDebugOutletTargetAppliedPatchCount = appliedCount;

        int interval = Mathf.Max(outletRootCauseLogIntervalSteps, 1);
        if (runtimeStepCount == 0 || (runtimeStepCount % (ulong)interval) == 0UL)
        {
            Debug.Log(
                "[MassFluxTargetDebug] " +
                $"step={runtimeStepCount}, " +
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
        if (!LbmGridMemoryEstimator.TryEstimate(nx, ny, nz, out LbmGridMemoryEstimate estimate, out _))
        {
            perDirectionBufferMB = 0f;
            estimatedDistributionBuffersMB = 0f;
            estimatedCoreBuffersMB = 0f;
            estimatedTexturesMB = 0f;
            estimatedTotalGpuMemoryMB = 0f;
            estimatedSingleBufferSafe = false;
            return;
        }

        perDirectionBufferMB = LbmGridMemoryEstimator.BytesToMiB(
            estimate.LargestDistributionBufferBytes);
        estimatedDistributionBuffersMB = LbmGridMemoryEstimator.BytesToMiB(
            estimate.DistributionBytes);
        estimatedCoreBuffersMB = LbmGridMemoryEstimator.BytesToMiB(estimate.CoreBufferBytes);
        estimatedTexturesMB = LbmGridMemoryEstimator.BytesToMiB(estimate.TextureBytes);
        estimatedTotalGpuMemoryMB = LbmGridMemoryEstimator.BytesToMiB(estimate.TotalBytes);
        estimatedSingleBufferSafe = estimate.IsSingleBufferSafe;
    }

    private void ValidateAndLogMemoryEstimate()
    {
        string caseTag = string.IsNullOrWhiteSpace(activeCaseName) ? "Manual" : activeCaseName;
        if (!LbmGridMemoryEstimator.TryEstimate(
                nx, ny, nz, out LbmGridMemoryEstimate estimate, out string estimateIssue))
        {
            Debug.LogError($"[LBM Memory Check][Case={caseTag}] {estimateIssue}");
            return;
        }

        float vramBudgetGB = manualVramBudgetGB > 0f
            ? manualVramBudgetGB
            : SystemInfo.graphicsMemorySize / 1024f;
        long vramBudgetBytes = (long)(math.max(vramBudgetGB, 0f) * 1024f * 1024f * 1024f);
        long warnBudgetBytes = (long)(vramBudgetBytes * math.clamp(vramWarningRatio, 0.1f, 1.0f));

        if (logMemoryEstimate)
        {
            Debug.Log(
                $"[LBM Memory Check][Case={caseTag}] " +
                $"Grid={nx} x {ny} x {nz} (N={estimate.CellCount:N0}), dxPhys={dxPhys:F4} m\n" +
                $"Largest grouped distribution buffer (6 dirs) = " +
                $"{LbmGridMemoryEstimator.BytesToMiB(estimate.LargestDistributionBufferBytes):F1} MiB " +
                $"(limit {LbmGridMemoryEstimator.BytesToMiB(LbmGridMemoryEstimator.MaxGraphicsBufferBytes):F1} MiB)\n" +
                $"Distribution buffers total (26 dirs x prev/cur) = " +
                $"{LbmGridMemoryEstimator.BytesToMiB(estimate.DistributionBytes):F1} MiB\n" +
                $"Core structured buffers total = {LbmGridMemoryEstimator.BytesToMiB(estimate.CoreBufferBytes):F1} MiB\n" +
                $"3D textures total = {LbmGridMemoryEstimator.BytesToMiB(estimate.TextureBytes):F1} MiB\n" +
                $"Estimated total GPU memory = {LbmGridMemoryEstimator.BytesToMiB(estimate.TotalBytes):F1} MiB\n" +
                $"Graphics API VRAM budget(reference) = {vramBudgetGB:F1} GiB, warning threshold = " +
                $"{LbmGridMemoryEstimator.BytesToMiB(warnBudgetBytes):F1} MiB");
        }

        if (!estimate.IsSingleBufferSafe)
        {
            double recommendedDx = EstimateRecommendedDxForBufferLimit();
            Debug.LogError(
                $"[LBM Memory Check][Case={caseTag}] A grouped distribution buffer exceeds the Unity GraphicsBuffer limit. " +
                $"Required largest buffer (6 dirs) = " +
                $"{LbmGridMemoryEstimator.BytesToMiB(estimate.LargestDistributionBufferBytes):F1} MiB, max = " +
                $"{LbmGridMemoryEstimator.BytesToMiB(LbmGridMemoryEstimator.MaxGraphicsBufferBytes):F1} MiB. " +
                $"Increase dxPhys above about {recommendedDx:F4} m or reduce the domain size.");
        }

        if (vramBudgetBytes > 0 && estimate.TotalBytes > warnBudgetBytes)
        {
            Debug.LogWarning(
                $"[LBM Memory Check][Case={caseTag}] Estimated GPU memory usage is high. " +
                $"Estimated total = {LbmGridMemoryEstimator.BytesToMiB(estimate.TotalBytes):F1} MiB, " +
                $"warning threshold = {LbmGridMemoryEstimator.BytesToMiB(warnBudgetBytes):F1} MiB. " +
                "You may avoid the 2 GB single-buffer error, but overall VRAM pressure, 3D texture allocation failure, or severe slowdown can still occur.");
        }

        if (vramBudgetBytes > 0 && estimate.TotalBytes > vramBudgetBytes)
        {
            Debug.LogError(
                $"[LBM Memory Check][Case={caseTag}] Estimated GPU memory usage exceeds the approximate VRAM budget. " +
                $"Estimated total = {LbmGridMemoryEstimator.BytesToMiB(estimate.TotalBytes):F1} MiB, " +
                $"VRAM budget = {LbmGridMemoryEstimator.BytesToMiB(vramBudgetBytes):F1} MiB. " +
                "Reduce grid resolution or domain size before running.");
        }
    }

    private long GetCellCount64()
    {
        return (long)nx * (long)ny * (long)nz;
    }

    private double EstimateRecommendedDxForBufferLimit()
    {
        double volume = (double)lx * (double)ly * (double)lz;
        if (volume <= 0.0)
            return dxPhys;

        double bytesPerLargestGroupCell =
            LbmGridMemoryEstimator.LargestDistributionGroupDirectionCount * sizeof(float);
        double maxCellCount = LbmGridMemoryEstimator.MaxGraphicsBufferBytes /
                              bytesPerLargestGroupCell;
        double dxRecommended = math.pow((float)(volume / maxCellCount), 1.0f / 3.0f);
        return math.max((float)dxRecommended, dxPhys);
    }
}
