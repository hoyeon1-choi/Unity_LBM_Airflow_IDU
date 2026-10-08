using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[AddComponentMenu("UI/UX-02 Simulation Setup Preview")]
public sealed class SimulationSetupRuntimeDocument : MonoBehaviour
{
    private const string LogTag = "[UX02][F01][T05][Case=LayoutPreview]";
    private const string RuntimeLogTag = "[UX02][F10][Case=RuntimeSetup]";
    private const string RuntimeAssetPath = "SimulationSetupRuntime";
    private const string RuntimePanelSettingsPath = "MainDashboardPanelSettings";
    private const int RuntimeSortingOrder = 100;
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap/ApplicationBootstrap.unity";
    private const string SimulationScenePath = "Assets/Scenes/LBMScenes/LBM_1wayCST.unity";

    private static SimulationSetupRuntimeDocument runtimeInstance;

    private static readonly string[] RequiredElementNames =
    {
        "SimulationSetup",
        "SimulationSetupTopBar",
        "SimulationSetupProjectValue",
        "SimulationSetupCaseValue",
        "SimulationSetupClockValue",
        "SimulationSetupTopBarState",
        "SimulationSetupStateValue",
        "SimulationSetupNavigation",
        "SimulationNavHome",
        "SimulationNavSimulation",
        "SimulationNavResults",
        "SimulationNavFmuMonitor",
        "SimulationNavCompare",
        "SimulationNavReport",
        "SimulationNavSettings",
        "SimulationPage",
        "SimulationWorkspace",
        "SimulationStepRail",
        "CaseStepItem",
        "ModelStepItem",
        "MeshStepItem",
        "PhysicsStepItem",
        "BoundaryStepItem",
        "RunStepItem",
        "SimulationStepContent",
        "CasePanel",
        "CaseActionBar",
        "NewCaseButton",
        "DuplicateCaseButton",
        "DeleteCaseButton",
        "CaseListPanel",
        "CaseCountValue",
        "CaseListScrollView",
        "SelectedCasePanel",
        "SelectedCaseNameValue",
        "SelectedCaseDescriptionValue",
        "SelectedCaseMetadata",
        "SelectedCaseCreatedValue",
        "CaseInitialConditions",
        "CaseIndoorTemperatureField",
        "CaseIndoorHumidityField",
        "CaseOutdoorTemperatureField",
        "CaseOutdoorHumidityField",
        "CaseSetTemperatureField",
        "CaseTargetSimulationTimeField",
        "CaseSelectionStatus",
        "ModelPanel",
        "RefreshModelListButton",
        "ModelListPanel",
        "ModelCountValue",
        "ModelListScrollView",
        "SelectedModelPanel",
        "SelectedModelStateValue",
        "SelectedModelNameValue",
        "SelectedModelDescriptionValue",
        "SelectedModelMetadataScrollView",
        "SelectedModelMetadata",
        "SelectedModelSceneValue",
        "SelectedModelSourceValue",
        "SelectedModelDomainValue",
        "SelectedModelSizeValue",
        "SelectedModelCenterValue",
        "SelectedModelGridValue",
        "SelectedModelGeometryValue",
        "SelectedModelTopologyValue",
        "SelectedModelComponentsValue",
        "ModelSelectionStatus",
        "MeshPanel",
        "MeshGridPanel",
        "MeshModelNameValue",
        "MeshDomainSizeValue",
        "MeshCellSizeField",
        "MeshCellSizeSourceValue",
        "MeshNxValue",
        "MeshNyValue",
        "MeshNzValue",
        "MeshTotalCellsValue",
        "MeshMemoryPanel",
        "MeshDistributionMemoryValue",
        "MeshStateMemoryValue",
        "MeshTextureMemoryValue",
        "MeshTotalMemoryValue",
        "MeshLargestBufferMemoryValue",
        "MeshBufferLimitValue",
        "MeshMemoryStatusValue",
        "MeshEstimateStatus",
        "PhysicsPanel",
        "PhysicsFlowPanel",
        "PhysicsCollisionModelField",
        "PhysicsFluidLatticeValue",
        "PhysicsThermalLatticeValue",
        "PhysicsTurbulenceModelField",
        "PhysicsTurbulenceConstantField",
        "PhysicsTurbulentPrandtlField",
        "PhysicsThermalPanel",
        "PhysicsThermalEnabledToggle",
        "PhysicsTemperatureMinField",
        "PhysicsTemperatureMaxField",
        "PhysicsReferenceTemperatureField",
        "PhysicsPrandtlTargetField",
        "PhysicsTauThermalMinValue",
        "PhysicsBuoyancyEnabledToggle",
        "PhysicsBuoyancyModelField",
        "PhysicsThermalExpansionBetaField",
        "PhysicsGravityPhysicalValue",
        "PhysicsGravityLatticeValue",
        "PhysicsConfigurationStatus",
        "BoundaryPanel",
        "BoundaryInletTabButton",
        "BoundaryOutletTabButton",
        "BoundaryWallTabButton",
        "BoundaryInletCountValue",
        "BoundaryOutletCountValue",
        "BoundaryWallCountValue",
        "BoundaryListPanel",
        "BoundaryItemListScrollView",
        "BoundaryParameterPanel",
        "SelectedBoundaryNameValue",
        "SelectedBoundarySourceValue",
        "SelectedBoundaryTypeValue",
        "BoundaryPatchTypeField",
        "BoundaryEnabledToggle",
        "BoundaryGridPatchValue",
        "BoundaryInletFields",
        "BoundaryInletModeField",
        "BoundaryInletVelocityField",
        "BoundaryInletFlowRateField",
        "BoundaryInletTemperatureField",
        "BoundaryInletDischargeAngleField",
        "BoundaryOutletFields",
        "BoundaryOutletModeValue",
        "BoundaryMassFluxToggle",
        "BoundaryOutletDensityField",
        "BoundaryOutletBlendField",
        "BoundaryOutletAnchorField",
        "BoundaryWallFields",
        "BoundaryWallMomentumValue",
        "BoundaryWallThermalField",
        "BoundaryWallTemperatureField",
        "BoundaryConfigurationStatus",
        "RunPanel",
        "RunReviewPanel",
        "RunReviewCaseValue",
        "RunReviewModelValue",
        "RunReviewGridValue",
        "RunReviewPhysicsValue",
        "RunReviewBoundaryValue",
        "RunReviewReadinessValue",
        "SimulationOperations",
        "RunControlPanel",
        "SimulationRunStateValue",
        "SimulationRunStateDetail",
        "SimulationStartButton",
        "SimulationPauseButton",
        "SimulationResumeButton",
        "SimulationStopButton",
        "SimulationRunCommandStatus",
        "SimulationProgressPanel",
        "SimulationProgressPercentValue",
        "SimulationProgressFill",
        "SimulationTimeValue",
        "SimulationTargetTimeValue",
        "SimulationTimeStepValue",
        "SimulationStepCountValue",
        "SimulationEstimatedRemainingValue",
        "SimulationProgressStatus",
        "GpuSystemMonitorPanel",
        "SystemMonitorStateValue",
        "SystemMonitorGpuNameValue",
        "SystemMonitorGraphicsApiValue",
        "SystemMonitorGpuUsageValue",
        "SystemMonitorGpuMemoryValue",
        "SystemMonitorSystemMemoryValue",
        "SystemMonitorFpsValue",
        "SystemMonitorStatus",
        "SimulationConsolePanel",
        "SimulationConsoleTabs",
        "SimulationConsoleScrollView"
    };

    private UIDocument document;
    private PanelSettings panelSettings;
    private VisualElement setupRoot;
    private DashboardHeaderController headerController;
    private SimulationSetupStepNavigationController stepNavigation;
    private SimulationSetupCaseController caseSelection;
    private SimulationSetupModelController modelSelection;
    private SimulationSetupMeshController meshConfiguration;
    private SimulationSetupPhysicsController physicsConfiguration;
    private SimulationSetupBoundaryController boundaryConfiguration;
    private SimulationSetupRunControlController runControl;
    private SimulationSetupProgressController progressController;
    private SimulationSetupSystemMonitorController systemMonitor;
    private ISimulationSetupSolverStateSource solverStateSource;
    private ISimulationSetupCaseStore caseStoreOverride;
    private ISimulationSetupModelSource modelSourceOverride;
    private ISimulationSetupBoundarySource boundarySourceOverride;
    private ISimulationSetupRunTarget runTargetOverride;
    private ISimulationSetupProgressSource progressSourceOverride;
    private ISimulationSetupSystemMonitorSource systemMonitorSourceOverride;
    private Label dashboardNavHome;
    private Label dashboardNavSimulation;
    private Label dashboardNavResults;
    private Label dashboardNavFmuMonitor;
    private Label dashboardNavCompare;
    private Label dashboardNavReport;
    private Label dashboardNavSettings;
    private bool startupHost;

    public UIDocument Document => document;
    public SimulationSetupStepNavigationController StepNavigation => stepNavigation;
    public SimulationSetupCaseController CaseSelection => caseSelection;
    public SimulationSetupModelController ModelSelection => modelSelection;
    public SimulationSetupMeshController MeshConfiguration => meshConfiguration;
    public SimulationSetupPhysicsController PhysicsConfiguration => physicsConfiguration;
    public SimulationSetupBoundaryController BoundaryConfiguration => boundaryConfiguration;
    public SimulationSetupRunControlController RunControl => runControl;
    public SimulationSetupProgressController ProgressController => progressController;
    public SimulationSetupSystemMonitorController SystemMonitor => systemMonitor;
    public static bool IsRuntimeAvailable => runtimeInstance != null && runtimeInstance.IsReady;
    public bool IsReady => setupRoot != null && stepNavigation != null &&
                           caseSelection != null && modelSelection != null &&
                           meshConfiguration != null && physicsConfiguration != null &&
                           boundaryConfiguration != null && runControl != null &&
                           progressController != null && systemMonitor != null;

    public static bool TrySetRuntimeVisible(bool visible)
    {
        if (!IsRuntimeAvailable || runtimeInstance.document == null)
            return false;

        runtimeInstance.document.rootVisualElement.style.display = visible
            ? DisplayStyle.Flex
            : DisplayStyle.None;
        if (visible)
        {
            runtimeInstance.SetDashboardNavigationSelection(
                MainDashboardNavigationController.DashboardPage.Simulation);
            runtimeInstance.headerController?.Refresh(true);
        }

        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Application.isBatchMode)
            return;

        string activeScenePath = SceneManager.GetActiveScene().path;
        if (activeScenePath != BootstrapScenePath && activeScenePath != SimulationScenePath)
            return;
        if (FindFirstObjectByType<SimulationSetupRuntimeDocument>() != null)
            return;

        GameObject host = new GameObject("SimulationSetupRuntime");
        // UX02 remains the live run/progress surface after the startup gate is released.
        // Destroy it only with the application lifecycle, not when Run is pressed.
        DontDestroyOnLoad(host);
        SimulationSetupRuntimeDocument runtime = host.AddComponent<SimulationSetupRuntimeDocument>();
        runtime.startupHost = true;
        CoSimulationStartupGate.SetToolkitSetupActive(true);
    }

    private void Awake()
    {
        if (runtimeInstance != null && runtimeInstance != this)
        {
            Destroy(gameObject);
            return;
        }

        runtimeInstance = this;
    }

    private void Start()
    {
        double startedAt = Time.realtimeSinceStartupAsDouble;
        if (!CreateDocument())
        {
            if (startupHost)
                CoSimulationStartupGate.SetToolkitSetupActive(false);
            return;
        }

        Debug.Log(
            $"{RuntimeLogTag} Simulation Set-up became available in " +
            $"{Time.realtimeSinceStartupAsDouble - startedAt:F3}s.");
    }

    private void Update()
    {
        headerController?.Refresh();
    }

    public void ConfigureCaseStore(ISimulationSetupCaseStore store)
    {
        if (document != null)
            throw new InvalidOperationException("Case store must be configured before the document is created.");

        caseStoreOverride = store;
    }

    public void ConfigureModelSource(ISimulationSetupModelSource source)
    {
        if (document != null)
            throw new InvalidOperationException("Model source must be configured before the document is created.");

        modelSourceOverride = source;
    }

    public void ConfigureBoundarySource(ISimulationSetupBoundarySource source)
    {
        if (document != null)
            throw new InvalidOperationException("Boundary source must be configured before the document is created.");

        boundarySourceOverride = source;
    }

    public void ConfigureRunTarget(ISimulationSetupRunTarget target)
    {
        if (document != null)
            throw new InvalidOperationException("Run target must be configured before the document is created.");

        runTargetOverride = target;
    }

    public void ConfigureProgressSource(ISimulationSetupProgressSource source)
    {
        if (document != null)
            throw new InvalidOperationException("Progress source must be configured before the document is created.");

        progressSourceOverride = source;
    }

    public void ConfigureSystemMonitorSource(ISimulationSetupSystemMonitorSource source)
    {
        if (document != null)
            throw new InvalidOperationException("System monitor source must be configured before the document is created.");

        systemMonitorSourceOverride = source;
    }

    public bool TryValidateCurrentLayout(out string issue)
    {
        if (!IsReady)
        {
            issue = "Simulation setup document is not ready.";
            return false;
        }

        for (int i = 0; i < RequiredElementNames.Length; i++)
        {
            if (document.rootVisualElement.Q<VisualElement>(RequiredElementNames[i]) == null)
            {
                issue = $"Required VisualElement is missing: {RequiredElementNames[i]}.";
                return false;
            }
        }

        if (!HasUsableBounds(setupRoot))
        {
            issue = "SimulationSetup has invalid resolved bounds.";
            return false;
        }

        VisualElement topBar = setupRoot.Q<VisualElement>("SimulationSetupTopBar");
        VisualElement navigation = setupRoot.Q<VisualElement>("SimulationSetupNavigation");
        VisualElement page = setupRoot.Q<VisualElement>("SimulationPage");
        VisualElement workspace = setupRoot.Q<VisualElement>("SimulationWorkspace");
        VisualElement stepRail = setupRoot.Q<VisualElement>("SimulationStepRail");
        VisualElement content = setupRoot.Q<VisualElement>("SimulationStepContent");
        VisualElement operations = setupRoot.Q<VisualElement>("SimulationOperations");
        VisualElement console = setupRoot.Q<VisualElement>("SimulationConsolePanel");

        if (!HasUsableBounds(topBar) || !HasUsableBounds(navigation) || !HasUsableBounds(page) ||
            !HasUsableBounds(workspace) || !HasUsableBounds(stepRail) || !HasUsableBounds(content) ||
            !HasUsableBounds(operations) || !HasUsableBounds(console))
        {
            issue = "One or more primary layout regions have invalid resolved bounds.";
            return false;
        }

        const float tolerance = 1.0f;
        if (topBar.worldBound.yMax > navigation.worldBound.yMin + tolerance ||
            navigation.worldBound.yMax > page.worldBound.yMin + tolerance ||
            workspace.worldBound.yMax > console.worldBound.yMin + tolerance)
        {
            issue = "Vertical layout regions overlap.";
            return false;
        }

        if (stepRail.worldBound.xMax > content.worldBound.xMin + tolerance ||
            content.worldBound.xMax > operations.worldBound.xMin + tolerance)
        {
            issue = "Workspace columns overlap.";
            return false;
        }

        if (!Contains(setupRoot.worldBound, topBar.worldBound, tolerance) ||
            !Contains(setupRoot.worldBound, navigation.worldBound, tolerance) ||
            !Contains(setupRoot.worldBound, page.worldBound, tolerance))
        {
            issue = "A primary layout region extends outside SimulationSetup.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    public bool TryValidateModelLayout(out string issue)
    {
        if (!IsReady || stepNavigation.CurrentStep !=
            SimulationSetupStepNavigationController.SimulationStep.Model)
        {
            issue = "Model step must be active before validating its layout.";
            return false;
        }

        VisualElement modelPanel = setupRoot.Q<VisualElement>("ModelPanel");
        VisualElement modelHeader = modelPanel?.Q<VisualElement>(className: "model-selection__header");
        VisualElement modelBody = modelPanel?.Q<VisualElement>(className: "model-selection__body");
        VisualElement listPanel = setupRoot.Q<VisualElement>("ModelListPanel");
        VisualElement metadataPanel = setupRoot.Q<VisualElement>("SelectedModelPanel");
        if (!HasUsableBounds(modelPanel) || !HasUsableBounds(modelHeader) ||
            !HasUsableBounds(modelBody) || !HasUsableBounds(listPanel) ||
            !HasUsableBounds(metadataPanel))
        {
            issue = "One or more Model Selection regions have invalid resolved bounds.";
            return false;
        }

        const float tolerance = 1.0f;
        if (modelHeader.worldBound.yMax > modelBody.worldBound.yMin + tolerance ||
            listPanel.worldBound.xMax > metadataPanel.worldBound.xMin + tolerance ||
            !Contains(modelPanel.worldBound, modelHeader.worldBound, tolerance) ||
            !Contains(modelPanel.worldBound, modelBody.worldBound, tolerance))
        {
            issue = "Model Selection regions overlap or extend outside the Model panel.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    public bool TryValidateMeshLayout(out string issue)
    {
        if (!IsReady || stepNavigation.CurrentStep !=
            SimulationSetupStepNavigationController.SimulationStep.Mesh)
        {
            issue = "Mesh step must be active before validating its layout.";
            return false;
        }

        VisualElement meshPanel = setupRoot.Q<VisualElement>("MeshPanel");
        VisualElement meshHeader = meshPanel?.Q<VisualElement>(className: "mesh-setup__header");
        VisualElement meshBody = meshPanel?.Q<VisualElement>(className: "mesh-setup__body");
        VisualElement gridPanel = setupRoot.Q<VisualElement>("MeshGridPanel");
        VisualElement memoryPanel = setupRoot.Q<VisualElement>("MeshMemoryPanel");
        if (!HasUsableBounds(meshPanel) || !HasUsableBounds(meshHeader) ||
            !HasUsableBounds(meshBody) || !HasUsableBounds(gridPanel) ||
            !HasUsableBounds(memoryPanel))
        {
            issue = "One or more Mesh regions have invalid resolved bounds.";
            return false;
        }

        const float tolerance = 1.0f;
        if (meshHeader.worldBound.yMax > meshBody.worldBound.yMin + tolerance ||
            gridPanel.worldBound.xMax > memoryPanel.worldBound.xMin + tolerance ||
            !Contains(meshPanel.worldBound, meshHeader.worldBound, tolerance) ||
            !Contains(meshPanel.worldBound, meshBody.worldBound, tolerance))
        {
            issue = "Mesh regions overlap or extend outside the Mesh panel.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    public bool TryValidatePhysicsLayout(out string issue)
    {
        if (!IsReady || stepNavigation.CurrentStep !=
            SimulationSetupStepNavigationController.SimulationStep.Physics)
        {
            issue = "Physics step must be active before validating its layout.";
            return false;
        }

        VisualElement physicsPanel = setupRoot.Q<VisualElement>("PhysicsPanel");
        VisualElement physicsHeader = physicsPanel?.Q<VisualElement>(className: "physics-setup__header");
        VisualElement physicsBody = physicsPanel?.Q<VisualElement>(className: "physics-setup__body");
        VisualElement flowPanel = setupRoot.Q<VisualElement>("PhysicsFlowPanel");
        VisualElement thermalPanel = setupRoot.Q<VisualElement>("PhysicsThermalPanel");
        if (!HasUsableBounds(physicsPanel) || !HasUsableBounds(physicsHeader) ||
            !HasUsableBounds(physicsBody) || !HasUsableBounds(flowPanel) ||
            !HasUsableBounds(thermalPanel))
        {
            issue = "One or more Physics regions have invalid resolved bounds.";
            return false;
        }

        const float tolerance = 1.0f;
        if (physicsHeader.worldBound.yMax > physicsBody.worldBound.yMin + tolerance ||
            flowPanel.worldBound.xMax > thermalPanel.worldBound.xMin + tolerance ||
            !Contains(physicsPanel.worldBound, physicsHeader.worldBound, tolerance) ||
            !Contains(physicsPanel.worldBound, physicsBody.worldBound, tolerance))
        {
            issue = "Physics regions overlap or extend outside the Physics panel.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    public bool TryValidateBoundaryLayout(out string issue)
    {
        if (!IsReady || stepNavigation.CurrentStep !=
            SimulationSetupStepNavigationController.SimulationStep.Boundary)
        {
            issue = "Boundary step must be active before validating its layout.";
            return false;
        }

        VisualElement boundaryPanel = setupRoot.Q<VisualElement>("BoundaryPanel");
        VisualElement header = boundaryPanel?.Q<VisualElement>(className: "boundary-setup__header");
        VisualElement summary = boundaryPanel?.Q<VisualElement>(className: "boundary-summary");
        VisualElement body = boundaryPanel?.Q<VisualElement>(className: "boundary-setup__body");
        VisualElement listPanel = setupRoot.Q<VisualElement>("BoundaryListPanel");
        VisualElement parameterPanel = setupRoot.Q<VisualElement>("BoundaryParameterPanel");
        if (!HasUsableBounds(boundaryPanel) || !HasUsableBounds(header) ||
            !HasUsableBounds(summary) || !HasUsableBounds(body) ||
            !HasUsableBounds(listPanel) || !HasUsableBounds(parameterPanel))
        {
            issue = "One or more Boundary regions have invalid resolved bounds.";
            return false;
        }

        const float tolerance = 1.0f;
        if (header.worldBound.yMax > summary.worldBound.yMin + tolerance ||
            summary.worldBound.yMax > body.worldBound.yMin + tolerance ||
            listPanel.worldBound.xMax > parameterPanel.worldBound.xMin + tolerance ||
            !Contains(boundaryPanel.worldBound, header.worldBound, tolerance) ||
            !Contains(boundaryPanel.worldBound, summary.worldBound, tolerance) ||
            !Contains(boundaryPanel.worldBound, body.worldBound, tolerance))
        {
            issue = "Boundary regions overlap or extend outside the Boundary panel.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    public bool TryValidateRunLayout(out string issue)
    {
        if (!IsReady || stepNavigation.CurrentStep !=
            SimulationSetupStepNavigationController.SimulationStep.Run)
        {
            issue = "Run step must be active before validating its layout.";
            return false;
        }

        VisualElement runPanel = setupRoot.Q<VisualElement>("RunPanel");
        VisualElement header = runPanel?.Q<VisualElement>(className: "run-review__header");
        VisualElement review = setupRoot.Q<VisualElement>("RunReviewPanel");
        VisualElement controls = setupRoot.Q<VisualElement>("RunControlPanel");
        VisualElement progress = setupRoot.Q<VisualElement>("SimulationProgressPanel");
        VisualElement monitor = setupRoot.Q<VisualElement>("GpuSystemMonitorPanel");
        if (!HasUsableBounds(runPanel) || !HasUsableBounds(header) ||
            !HasUsableBounds(review) || !HasUsableBounds(controls) ||
            !HasUsableBounds(progress) || !HasUsableBounds(monitor))
        {
            issue = "One or more Run, Progress, or GPU/System Monitor regions have invalid resolved bounds.";
            return false;
        }

        const float tolerance = 1.0f;
        if (header.worldBound.yMax > review.worldBound.yMin + tolerance ||
            !Contains(runPanel.worldBound, header.worldBound, tolerance) ||
            !Contains(runPanel.worldBound, review.worldBound, tolerance))
        {
            issue = "Run Control regions overlap or extend outside the Run panel.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private bool CreateDocument()
    {
        VisualTreeAsset visualTreeAsset = Resources.Load<VisualTreeAsset>(RuntimeAssetPath);
        if (visualTreeAsset == null)
        {
            Debug.LogError($"{LogTag} Runtime UXML not found: Resources/{RuntimeAssetPath}", this);
            return false;
        }

        panelSettings = Resources.Load<PanelSettings>(RuntimePanelSettingsPath);
        if (panelSettings == null)
        {
            Debug.LogError(
                $"{LogTag} Runtime PanelSettings not found: Resources/{RuntimePanelSettingsPath}",
                this);
            return false;
        }

        GameObject documentObject = new GameObject("SimulationSetupUIDocument");
        documentObject.transform.SetParent(transform, false);
        documentObject.SetActive(false);

        document = documentObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;
        document.visualTreeAsset = visualTreeAsset;
        // UX01 is created after the startup gate is released and uses sorting order 0.
        // Keep the live UX02 run/progress surface above its placeholder Simulation page.
        document.sortingOrder = RuntimeSortingOrder;
        documentObject.SetActive(true);
        document.rootVisualElement.style.flexGrow = 1.0f;
        document.rootVisualElement.style.flexShrink = 1.0f;

        setupRoot = document.rootVisualElement.Q<VisualElement>("SimulationSetup");
        if (setupRoot == null)
        {
            Debug.LogError($"{LogTag} Initialization failed: SimulationSetup root is missing.", this);
            return false;
        }

        if (!DashboardHeaderController.TryCreate(
                setupRoot,
                "SimulationSetup",
                out headerController,
                out string headerIssue))
        {
            Debug.LogError($"{RuntimeLogTag} Header initialization failed: {headerIssue}", this);
            setupRoot = null;
            return false;
        }

        headerController.Refresh(true);

        if (!BindDashboardNavigation(out string dashboardNavigationIssue))
        {
            Debug.LogError($"{RuntimeLogTag} Navigation initialization failed: {dashboardNavigationIssue}", this);
            setupRoot = null;
            return false;
        }

        stepNavigation = new SimulationSetupStepNavigationController();
        if (!stepNavigation.Initialize(document.rootVisualElement, out string issue))
        {
            Debug.LogError($"{LogTag} Initialization failed: {issue}", this);
            stepNavigation.Dispose();
            stepNavigation = null;
            setupRoot = null;
            return false;
        }

        SimulationController simulationController = FindFirstObjectByType<SimulationController>();
        CaseStudyPreset initialCase = simulationController != null
            ? simulationController.SelectedCaseStudy
            : CaseStudyPreset.A0_Baseline;
        caseSelection = new SimulationSetupCaseController();
        ISimulationSetupCaseStore caseStore = caseStoreOverride ?? new JsonSimulationSetupCaseStore();
        if (!caseSelection.Initialize(document.rootVisualElement, initialCase, caseStore, out issue))
        {
            Debug.LogError($"{LogTag} Case Selection initialization failed: {issue}", this);
            caseSelection.Dispose();
            caseSelection = null;
            stepNavigation.Dispose();
            stepNavigation = null;
            setupRoot = null;
            return false;
        }

        modelSelection = new SimulationSetupModelController();
        ISimulationSetupModelSource modelSource =
            modelSourceOverride ?? new LoadedSimulationSceneModelSource();
        if (!modelSelection.Initialize(document.rootVisualElement, modelSource, out issue))
        {
            Debug.LogError($"[UX02][F03][Case=LayoutPreview] Model Selection initialization failed: {issue}", this);
            modelSelection.Dispose();
            modelSelection = null;
            caseSelection.Dispose();
            caseSelection = null;
            stepNavigation.Dispose();
            stepNavigation = null;
            setupRoot = null;
            return false;
        }

        solverStateSource = new SimulationControllerStateAdapter(
            () => modelSelection?.SelectedController);

        meshConfiguration = new SimulationSetupMeshController();
        if (!meshConfiguration.Initialize(
                document.rootVisualElement, caseSelection, modelSelection, out issue))
        {
            Debug.LogError($"[UX02][F04][Case=LayoutPreview] Mesh initialization failed: {issue}", this);
            meshConfiguration.Dispose();
            meshConfiguration = null;
            modelSelection.Dispose();
            modelSelection = null;
            caseSelection.Dispose();
            caseSelection = null;
            stepNavigation.Dispose();
            stepNavigation = null;
            setupRoot = null;
            return false;
        }

        physicsConfiguration = new SimulationSetupPhysicsController();
        if (!physicsConfiguration.Initialize(
                document.rootVisualElement, caseSelection, modelSelection, out issue,
                solverStateSource))
        {
            Debug.LogError($"[UX02][F05][Case=LayoutPreview] Physics initialization failed: {issue}", this);
            physicsConfiguration.Dispose();
            physicsConfiguration = null;
            meshConfiguration.Dispose();
            meshConfiguration = null;
            modelSelection.Dispose();
            modelSelection = null;
            caseSelection.Dispose();
            caseSelection = null;
            stepNavigation.Dispose();
            stepNavigation = null;
            setupRoot = null;
            return false;
        }

        boundaryConfiguration = new SimulationSetupBoundaryController();
        ISimulationSetupBoundarySource boundarySource =
            boundarySourceOverride ?? new LoadedSimulationBoundarySource();
        if (!boundaryConfiguration.Initialize(
                document.rootVisualElement, modelSelection, boundarySource, out issue))
        {
            Debug.LogError($"[UX02][F06][Case=LayoutPreview] Boundary initialization failed: {issue}", this);
            boundaryConfiguration.Dispose();
            boundaryConfiguration = null;
            physicsConfiguration.Dispose();
            physicsConfiguration = null;
            meshConfiguration.Dispose();
            meshConfiguration = null;
            modelSelection.Dispose();
            modelSelection = null;
            caseSelection.Dispose();
            caseSelection = null;
            stepNavigation.Dispose();
            stepNavigation = null;
            setupRoot = null;
            return false;
        }

        ISimulationSetupRunTarget runTarget = runTargetOverride ??
            new SimulationSetupSolverCommandAdapter(
                () => modelSelection?.SelectedController,
                () => new SimulationSetupRunRequest(
                    caseSelection.SelectedDefinition,
                    caseSelection.SelectedInitialConditions,
                    meshConfiguration.CellSizeMeters,
                    physicsConfiguration.CurrentConfiguration,
                    boundaryConfiguration.GetStagedConfigurations()),
                solverStateSource);
        if (!SimulationSetupRunControlController.TryCreate(
                document.rootVisualElement, caseSelection, modelSelection, meshConfiguration,
                physicsConfiguration, boundaryConfiguration, runTarget, out runControl, out issue))
        {
            Debug.LogError($"[UX02][F07][Case=LayoutPreview] Run Control initialization failed: {issue}", this);
            runControl?.Dispose();
            runControl = null;
            boundaryConfiguration.Dispose();
            boundaryConfiguration = null;
            physicsConfiguration.Dispose();
            physicsConfiguration = null;
            meshConfiguration.Dispose();
            meshConfiguration = null;
            modelSelection.Dispose();
            modelSelection = null;
            caseSelection.Dispose();
            caseSelection = null;
            stepNavigation.Dispose();
            stepNavigation = null;
            setupRoot = null;
            return false;
        }

        ISimulationSetupProgressSource progressSource = progressSourceOverride ??
            new SimulationSetupSolverProgressSource(solverStateSource);
        if (!SimulationSetupProgressController.TryCreate(
                document.rootVisualElement, progressSource, out progressController, out issue))
        {
            Debug.LogError($"[UX02][F08][Case=LayoutPreview] Progress initialization failed: {issue}", this);
            progressController?.Dispose();
            progressController = null;
            runControl.Dispose();
            runControl = null;
            boundaryConfiguration.Dispose();
            boundaryConfiguration = null;
            physicsConfiguration.Dispose();
            physicsConfiguration = null;
            meshConfiguration.Dispose();
            meshConfiguration = null;
            modelSelection.Dispose();
            modelSelection = null;
            caseSelection.Dispose();
            caseSelection = null;
            stepNavigation.Dispose();
            stepNavigation = null;
            setupRoot = null;
            return false;
        }

        ISimulationSetupSystemMonitorSource monitorSource = systemMonitorSourceOverride ??
            new UnitySimulationSetupSystemMonitorSource(solverStateSource);
        if (!SimulationSetupSystemMonitorController.TryCreate(
                document.rootVisualElement, monitorSource, out systemMonitor, out issue))
        {
            Debug.LogError($"[UX02][F09][Case=LayoutPreview] GPU/System Monitor initialization failed: {issue}", this);
            systemMonitor?.Dispose();
            systemMonitor = null;
            progressController.Dispose();
            progressController = null;
            runControl.Dispose();
            runControl = null;
            boundaryConfiguration.Dispose();
            boundaryConfiguration = null;
            physicsConfiguration.Dispose();
            physicsConfiguration = null;
            meshConfiguration.Dispose();
            meshConfiguration = null;
            modelSelection.Dispose();
            modelSelection = null;
            caseSelection.Dispose();
            caseSelection = null;
            stepNavigation.Dispose();
            stepNavigation = null;
            setupRoot = null;
            return false;
        }

        stepNavigation.CurrentStepChanged += OnCurrentStepChanged;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        return true;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (stepNavigation != null)
            stepNavigation.CurrentStepChanged -= OnCurrentStepChanged;
        UnbindDashboardNavigation();
        systemMonitor?.Dispose();
        systemMonitor = null;
        progressController?.Dispose();
        progressController = null;
        runControl?.Dispose();
        runControl = null;
        boundaryConfiguration?.Dispose();
        boundaryConfiguration = null;
        physicsConfiguration?.Dispose();
        physicsConfiguration = null;
        meshConfiguration?.Dispose();
        meshConfiguration = null;
        modelSelection?.Dispose();
        modelSelection = null;
        caseSelection?.Dispose();
        caseSelection = null;
        stepNavigation?.Dispose();
        stepNavigation = null;
        headerController = null;
        setupRoot = null;
        document = null;
        panelSettings = null;
        caseStoreOverride = null;
        modelSourceOverride = null;
        boundarySourceOverride = null;
        runTargetOverride = null;
        progressSourceOverride = null;
        systemMonitorSourceOverride = null;
        solverStateSource = null;
        if (runtimeInstance == this)
            runtimeInstance = null;
        if (startupHost)
            CoSimulationStartupGate.SetToolkitSetupActive(false);
    }

    private bool BindDashboardNavigation(out string issue)
    {
        dashboardNavHome = setupRoot.Q<Label>("SimulationNavHome");
        dashboardNavSimulation = setupRoot.Q<Label>("SimulationNavSimulation");
        dashboardNavResults = setupRoot.Q<Label>("SimulationNavResults");
        dashboardNavFmuMonitor = setupRoot.Q<Label>("SimulationNavFmuMonitor");
        dashboardNavCompare = setupRoot.Q<Label>("SimulationNavCompare");
        dashboardNavReport = setupRoot.Q<Label>("SimulationNavReport");
        dashboardNavSettings = setupRoot.Q<Label>("SimulationNavSettings");

        if (dashboardNavHome == null || dashboardNavSimulation == null ||
            dashboardNavResults == null || dashboardNavFmuMonitor == null ||
            dashboardNavCompare == null || dashboardNavReport == null ||
            dashboardNavSettings == null)
        {
            issue = "One or more dashboard navigation items are missing.";
            return false;
        }

        dashboardNavHome.RegisterCallback<ClickEvent>(OnDashboardNavHomeClicked);
        dashboardNavSimulation.RegisterCallback<ClickEvent>(OnDashboardNavSimulationClicked);
        dashboardNavResults.RegisterCallback<ClickEvent>(OnDashboardNavResultsClicked);
        dashboardNavFmuMonitor.RegisterCallback<ClickEvent>(OnDashboardNavFmuMonitorClicked);
        dashboardNavReport.RegisterCallback<ClickEvent>(OnDashboardNavReportClicked);
        dashboardNavSettings.RegisterCallback<ClickEvent>(OnDashboardNavSettingsClicked);

        dashboardNavCompare.SetEnabled(MainDashboardNavigationController.ComparePageEnabled);
        if (MainDashboardNavigationController.ComparePageEnabled)
            dashboardNavCompare.RegisterCallback<ClickEvent>(OnDashboardNavCompareClicked);

        dashboardNavHome.pickingMode = PickingMode.Position;
        dashboardNavSimulation.pickingMode = PickingMode.Position;
        dashboardNavResults.pickingMode = PickingMode.Position;
        dashboardNavFmuMonitor.pickingMode = PickingMode.Position;
        dashboardNavCompare.pickingMode = MainDashboardNavigationController.ComparePageEnabled
            ? PickingMode.Position
            : PickingMode.Ignore;
        dashboardNavReport.pickingMode = PickingMode.Position;
        dashboardNavSettings.pickingMode = PickingMode.Position;

        issue = string.Empty;
        return true;
    }

    private void UnbindDashboardNavigation()
    {
        dashboardNavHome?.UnregisterCallback<ClickEvent>(OnDashboardNavHomeClicked);
        dashboardNavSimulation?.UnregisterCallback<ClickEvent>(OnDashboardNavSimulationClicked);
        dashboardNavResults?.UnregisterCallback<ClickEvent>(OnDashboardNavResultsClicked);
        dashboardNavFmuMonitor?.UnregisterCallback<ClickEvent>(OnDashboardNavFmuMonitorClicked);
        dashboardNavCompare?.UnregisterCallback<ClickEvent>(OnDashboardNavCompareClicked);
        dashboardNavReport?.UnregisterCallback<ClickEvent>(OnDashboardNavReportClicked);
        dashboardNavSettings?.UnregisterCallback<ClickEvent>(OnDashboardNavSettingsClicked);
        dashboardNavHome = null;
        dashboardNavSimulation = null;
        dashboardNavResults = null;
        dashboardNavFmuMonitor = null;
        dashboardNavCompare = null;
        dashboardNavReport = null;
        dashboardNavSettings = null;
    }

    private void OnDashboardNavHomeClicked(ClickEvent evt) =>
        NavigateToDashboardPage(MainDashboardNavigationController.DashboardPage.Home, evt);

    private void OnDashboardNavSimulationClicked(ClickEvent evt) =>
        NavigateToDashboardPage(MainDashboardNavigationController.DashboardPage.Simulation, evt);

    private void OnDashboardNavResultsClicked(ClickEvent evt) =>
        NavigateToDashboardPage(MainDashboardNavigationController.DashboardPage.Results, evt);

    private void OnDashboardNavFmuMonitorClicked(ClickEvent evt) =>
        NavigateToDashboardPage(MainDashboardNavigationController.DashboardPage.FmuMonitor, evt);

    private void OnDashboardNavCompareClicked(ClickEvent evt) =>
        NavigateToDashboardPage(MainDashboardNavigationController.DashboardPage.Compare, evt);

    private void OnDashboardNavReportClicked(ClickEvent evt) =>
        NavigateToDashboardPage(MainDashboardNavigationController.DashboardPage.Report, evt);

    private void OnDashboardNavSettingsClicked(ClickEvent evt) =>
        NavigateToDashboardPage(MainDashboardNavigationController.DashboardPage.Settings, evt);

    private void NavigateToDashboardPage(
        MainDashboardNavigationController.DashboardPage page, ClickEvent evt)
    {
        evt.StopPropagation();
        SetDashboardNavigationSelection(page);
        if (page == MainDashboardNavigationController.DashboardPage.Simulation)
            return;

        if (!MainDashboardRuntimeDocument.TryShowPage(page))
        {
            Debug.LogWarning(
                $"{RuntimeLogTag} '{page}' is not available until the runtime dashboard is ready.",
                this);
        }
    }

    private void SetDashboardNavigationSelection(
        MainDashboardNavigationController.DashboardPage page)
    {
        const string selectedClass = "simulation-navigation__item--selected";
        dashboardNavHome?.EnableInClassList(selectedClass,
            page == MainDashboardNavigationController.DashboardPage.Home);
        dashboardNavSimulation?.EnableInClassList(selectedClass,
            page == MainDashboardNavigationController.DashboardPage.Simulation);
        dashboardNavResults?.EnableInClassList(selectedClass,
            page == MainDashboardNavigationController.DashboardPage.Results);
        dashboardNavFmuMonitor?.EnableInClassList(selectedClass,
            page == MainDashboardNavigationController.DashboardPage.FmuMonitor);
        dashboardNavCompare?.EnableInClassList(selectedClass,
            MainDashboardNavigationController.ComparePageEnabled &&
            page == MainDashboardNavigationController.DashboardPage.Compare);
        dashboardNavReport?.EnableInClassList(selectedClass,
            page == MainDashboardNavigationController.DashboardPage.Report);
        dashboardNavSettings?.EnableInClassList(selectedClass,
            page == MainDashboardNavigationController.DashboardPage.Settings);
    }

    private void OnCurrentStepChanged(SimulationSetupStepNavigationController.SimulationStep step)
    {
        if (step == SimulationSetupStepNavigationController.SimulationStep.Model)
            modelSelection?.RefreshIfNeeded();
        else if (step == SimulationSetupStepNavigationController.SimulationStep.Mesh)
        {
            modelSelection?.RefreshIfNeeded();
            meshConfiguration?.RefreshIfNeeded();
        }
        else if (step == SimulationSetupStepNavigationController.SimulationStep.Physics)
        {
            modelSelection?.RefreshIfNeeded();
            physicsConfiguration?.RefreshIfNeeded();
        }
        else if (step == SimulationSetupStepNavigationController.SimulationStep.Boundary)
        {
            modelSelection?.RefreshIfNeeded();
            boundaryConfiguration?.RefreshIfNeeded();
        }
        else if (step == SimulationSetupStepNavigationController.SimulationStep.Run)
        {
            modelSelection?.RefreshIfNeeded();
            meshConfiguration?.RefreshIfNeeded();
            physicsConfiguration?.RefreshIfNeeded();
            boundaryConfiguration?.RefreshIfNeeded();
            runControl?.Refresh();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshModelSelectionForSceneChange();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        RefreshModelSelectionForSceneChange();
    }

    private void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        RefreshModelSelectionForSceneChange();
    }

    private void RefreshModelSelectionForSceneChange()
    {
        modelSelection?.MarkDirty();
        meshConfiguration?.MarkDirty();
        physicsConfiguration?.MarkDirty();
        boundaryConfiguration?.MarkDirty();
        if (stepNavigation != null &&
            stepNavigation.CurrentStep == SimulationSetupStepNavigationController.SimulationStep.Model)
        {
            modelSelection?.RefreshIfNeeded();
        }
        else if (stepNavigation != null &&
                 stepNavigation.CurrentStep == SimulationSetupStepNavigationController.SimulationStep.Mesh)
        {
            modelSelection?.RefreshIfNeeded();
            meshConfiguration?.RefreshIfNeeded();
        }
        else if (stepNavigation != null &&
                 stepNavigation.CurrentStep == SimulationSetupStepNavigationController.SimulationStep.Physics)
        {
            modelSelection?.RefreshIfNeeded();
            physicsConfiguration?.RefreshIfNeeded();
        }
        else if (stepNavigation != null &&
                 stepNavigation.CurrentStep == SimulationSetupStepNavigationController.SimulationStep.Boundary)
        {
            modelSelection?.RefreshIfNeeded();
            boundaryConfiguration?.RefreshIfNeeded();
        }
    }

    private static bool HasUsableBounds(VisualElement element)
    {
        if (element == null)
            return false;

        Rect bounds = element.worldBound;
        return IsFinite(bounds.x) && IsFinite(bounds.y) &&
               IsFinite(bounds.width) && IsFinite(bounds.height) &&
               bounds.width > 0.0f && bounds.height > 0.0f;
    }

    private static bool Contains(Rect outer, Rect inner, float tolerance)
    {
        return inner.xMin >= outer.xMin - tolerance &&
               inner.yMin >= outer.yMin - tolerance &&
               inner.xMax <= outer.xMax + tolerance &&
               inner.yMax <= outer.yMax + tolerance;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
