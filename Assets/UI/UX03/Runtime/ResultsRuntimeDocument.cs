using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[DefaultExecutionOrder(-30900)]
[DisallowMultipleComponent]
[AddComponentMenu("UI/UX-03 Results Layout Preview")]
public sealed class ResultsRuntimeDocument : MonoBehaviour
{
    private const string LogTag = "[UX03][F01][Case=LayoutPreview]";
    private const string RuntimeLogTag = "[UX03][F03][Case=RuntimeResults]";
    private const string RuntimeAssetPath = "ResultsRuntime";
    private const string PanelSettingsPath = "MainDashboardPanelSettings";
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap/ApplicationBootstrap.unity";
    private const string SimulationScenePath = "Assets/Scenes/LBMScenes/LBM_1wayCST.unity";

    private static readonly string[] RequiredElementNames =
    {
        "ResultsDashboard",
        "ResultsTopBar",
        "ResultsProjectValue",
        "ResultsCaseValue",
        "ResultsClockValue",
        "ResultsTopBarState",
        "ResultsStateValue",
        "ResultsNavigation",
        "ResultsNavHome",
        "ResultsNavSimulation",
        "ResultsNavResults",
        "ResultsNavFmuMonitor",
        "ResultsNavCompare",
        "ResultsNavReport",
        "ResultsNavSettings",
        "ResultsPage",
        "ResultVariableTabs",
        "TemperatureVariableButton",
        "VelocityVariableButton",
        "PressureVariableButton",
        "StreamlineVariableButton",
        "ComfortVariableButton",
        "MassFluxVariableButton",
        "ResultsWorkspace",
        "ResultsMainColumn",
        "SceneArea",
        "ScenePanel",
        "SceneViewport",
        "SceneViewImage",
        "ScenePlaceholder",
        "SceneControlPanel",
        "SliceXRow",
        "SliceXToggle",
        "SliceXPositionSlider",
        "SliceXPositionValue",
        "SliceYRow",
        "SliceYToggle",
        "SliceYPositionSlider",
        "SliceYPositionValue",
        "SliceZRow",
        "SliceZToggle",
        "SliceZPositionSlider",
        "SliceZPositionValue",
        "SliceControlStatus",
        "SceneToolbar",
        "SceneCameraToolbar",
        "SceneFitButton",
        "SceneOrbitButton",
        "ScenePanButton",
        "SceneResetButton",
        "ResultsAnalyticsSidebar",
        "ResultLegendPanel",
        "ResultLegendTitle",
        "ResultLegendUnit",
        "ResultLegendBar",
        "ResultVariableMetadata",
        "ResultMetadataUnitValue",
        "ResultMetadataMinValue",
        "ResultMetadataMaxValue",
        "ResultMetadataRangeValue",
        "ColorMapControls",
        "ColorMapPresetField",
        "ColorRangeModeToolbar",
        "ColorRangeAutoButton",
        "ColorRangeManualButton",
        "ColorRangeMinField",
        "ColorRangeMaxField",
        "ColorMapStatus",
        "ResultVariableStatus",
        "ResultLegendColor0",
        "ResultLegendColor1",
        "ResultLegendColor2",
        "ResultLegendColor3",
        "ResultLegendColor4",
        "ResultLegendColor5",
        "ResultLegendMaxValue",
        "ResultLegendUpperMidValue",
        "ResultLegendMiddleHighValue",
        "ResultLegendMiddleLowValue",
        "ResultLegendLowerMidValue",
        "ResultLegendMinValue"
    };

    private static readonly string[] VariableButtonNames =
    {
        "TemperatureVariableButton",
        "VelocityVariableButton",
        "PressureVariableButton",
        "StreamlineVariableButton",
        "ComfortVariableButton",
        "MassFluxVariableButton"
    };

    private static readonly string[] CameraModeButtonNames =
    {
        "SceneOrbitButton",
        "ScenePanButton"
    };

    private static ResultsRuntimeDocument runtimeInstance;

    private UIDocument document;
    private PanelSettings panelSettings;
    private VisualElement resultsRoot;
    private DashboardHeaderController headerController;
    private ResultsVariableController variableController;
    private ResultsColorMapController colorMapController;
    private ResultsSceneInteractionController sceneInteractionController;
    private IResultsVariableMetadataSource metadataSourceOverride;
    private IResultsVisualizationTarget visualizationTargetOverride;
    private IResultsColorMapTarget colorMapTargetOverride;
    private Label dashboardNavHome;
    private Label dashboardNavSimulation;
    private Label dashboardNavResults;
    private Label dashboardNavFmuMonitor;
    private Label dashboardNavCompare;
    private Label dashboardNavReport;
    private Label dashboardNavSettings;
    private bool startupHost;
    private bool visibilityRequested;
    private bool initializationFailed;

    public UIDocument Document => document;
    public ResultsVariableController VariableController => variableController;
    public ResultsColorMapController ColorMapController => colorMapController;
    public ResultsSceneInteractionController SceneInteractionController => sceneInteractionController;
    public static bool IsRuntimeAvailable => runtimeInstance != null && runtimeInstance.IsReady;
    public bool IsReady => document != null && resultsRoot != null &&
                           variableController != null && colorMapController != null &&
                           sceneInteractionController != null;

    public static bool TrySetRuntimeVisible(bool visible)
    {
        if (runtimeInstance == null || runtimeInstance.initializationFailed)
            return false;

        runtimeInstance.visibilityRequested = visible;
        if (runtimeInstance.document != null)
        {
            runtimeInstance.document.rootVisualElement.style.display = visible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            if (visible)
            {
                runtimeInstance.SetDashboardNavigationSelection(
                    MainDashboardNavigationController.DashboardPage.Results);
                runtimeInstance.headerController?.Refresh(true);
                runtimeInstance.variableController?.Refresh();
                runtimeInstance.sceneInteractionController?.Refresh(true);
            }
        }

        // A startup host can still be waiting for the setup gate or its first Start frame.
        // Treat the route as handled; CreateDocument applies visibilityRequested when ready.
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
        if (FindFirstObjectByType<ResultsRuntimeDocument>() != null)
            return;

        GameObject host = new GameObject("ResultsRuntime");
        DontDestroyOnLoad(host);
        ResultsRuntimeDocument runtime = host.AddComponent<ResultsRuntimeDocument>();
        runtime.startupHost = true;
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

    private IEnumerator Start()
    {
        if (startupHost)
        {
            // Results is not part of the first interactive setup frame.
            while (CoSimulationStartupGate.IsWaitingForConfirmation)
                yield return null;
            yield return null;
        }

        double startedAt = Time.realtimeSinceStartupAsDouble;
        if (!CreateDocument())
        {
            initializationFailed = true;
            yield break;
        }

        if (startupHost)
        {
            document.rootVisualElement.style.display = visibilityRequested
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        Debug.Log(
            $"{RuntimeLogTag} Results screen became available in " +
            $"{Time.realtimeSinceStartupAsDouble - startedAt:F3}s; visible={visibilityRequested}.",
            this);
    }

    private bool CreateDocument()
    {
        VisualTreeAsset visualTreeAsset = Resources.Load<VisualTreeAsset>(RuntimeAssetPath);
        if (visualTreeAsset == null)
        {
            Debug.LogError($"{LogTag} Runtime UXML을 찾을 수 없습니다: Resources/{RuntimeAssetPath}", this);
            return false;
        }

        panelSettings = Resources.Load<PanelSettings>(PanelSettingsPath);
        if (panelSettings == null)
        {
            Debug.LogError($"{LogTag} PanelSettings를 찾을 수 없습니다: Resources/{PanelSettingsPath}", this);
            return false;
        }

        document = gameObject.GetComponent<UIDocument>();
        if (document == null)
            document = gameObject.AddComponent<UIDocument>();

        document.panelSettings = panelSettings;
        document.visualTreeAsset = visualTreeAsset;
        document.sortingOrder = 120;
        document.rootVisualElement.style.flexGrow = 1;
        document.rootVisualElement.style.flexShrink = 1;

        resultsRoot = document.rootVisualElement.Q<VisualElement>("ResultsDashboard");
        if (resultsRoot == null)
        {
            Debug.LogError($"{LogTag} ResultsDashboard 루트를 만들지 못했습니다.", this);
            return false;
        }

        if (!DashboardHeaderController.TryCreate(
                resultsRoot,
                "Results",
                out headerController,
                out string headerIssue))
        {
            Debug.LogError($"{RuntimeLogTag} Header initialization failed: {headerIssue}", this);
            return false;
        }

        headerController.Refresh(true);

        if (!BindDashboardNavigation(out string navigationIssue))
        {
            Debug.LogError($"{RuntimeLogTag} Navigation initialization failed: {navigationIssue}", this);
            return false;
        }

        IResultsVariableMetadataSource metadataSource = metadataSourceOverride ??
            new SimulationResultsVariableMetadataSource();
        IResultsVisualizationTarget visualizationTarget = visualizationTargetOverride ??
            new SceneCameraResultsVisualizationTarget();
        if (!ResultsVariableController.TryCreate(
                resultsRoot,
                metadataSource,
                visualizationTarget,
                out variableController,
                out string variableIssue))
        {
            UnbindDashboardNavigation();
            Debug.LogError($"{LogTag} {variableIssue}", this);
            return false;
        }

        IResultsColorMapTarget colorMapTarget = colorMapTargetOverride ??
            new ExistingSliceColorMapTarget();
        if (!ResultsColorMapController.TryCreate(
                resultsRoot,
                variableController.State,
                colorMapTarget,
                out colorMapController,
                out string colorMapIssue))
        {
            colorMapTarget.Dispose();
            variableController.Dispose();
            variableController = null;
            UnbindDashboardNavigation();
            Debug.LogError($"{LogTag} {colorMapIssue}", this);
            return false;
        }

        if (!ResultsSceneInteractionController.TryCreate(
                resultsRoot,
                variableController.State,
                out sceneInteractionController,
                out string sceneInteractionIssue))
        {
            colorMapController.Dispose();
            colorMapController = null;
            variableController.Dispose();
            variableController = null;
            UnbindDashboardNavigation();
            Debug.LogError($"{RuntimeLogTag} {sceneInteractionIssue}", this);
            return false;
        }

        Debug.Log($"{LogTag} Results layout preview가 준비되었습니다.", this);
        return true;
    }

    public void ConfigureVariableSources(
        IResultsVariableMetadataSource metadataSource,
        IResultsVisualizationTarget visualizationTarget)
    {
        if (document != null)
            throw new InvalidOperationException("Variable sources must be configured before document creation.");

        metadataSourceOverride = metadataSource ??
            throw new ArgumentNullException(nameof(metadataSource));
        visualizationTargetOverride = visualizationTarget ??
            throw new ArgumentNullException(nameof(visualizationTarget));
    }

    public void ConfigureColorMapTarget(IResultsColorMapTarget colorMapTarget)
    {
        if (document != null)
            throw new InvalidOperationException("Color Map target must be configured before document creation.");

        colorMapTargetOverride = colorMapTarget ??
            throw new ArgumentNullException(nameof(colorMapTarget));
    }

    private void Update()
    {
        if (!startupHost || visibilityRequested)
        {
            headerController?.Refresh();
            sceneInteractionController?.Refresh();
        }
    }

    private void OnDestroy()
    {
        UnbindDashboardNavigation();
        sceneInteractionController?.Dispose();
        sceneInteractionController = null;
        colorMapController?.Dispose();
        colorMapController = null;
        variableController?.Dispose();
        variableController = null;
        headerController = null;
        if (runtimeInstance == this)
            runtimeInstance = null;
    }

    private bool BindDashboardNavigation(out string issue)
    {
        dashboardNavHome = resultsRoot.Q<Label>("ResultsNavHome");
        dashboardNavSimulation = resultsRoot.Q<Label>("ResultsNavSimulation");
        dashboardNavResults = resultsRoot.Q<Label>("ResultsNavResults");
        dashboardNavFmuMonitor = resultsRoot.Q<Label>("ResultsNavFmuMonitor");
        dashboardNavCompare = resultsRoot.Q<Label>("ResultsNavCompare");
        dashboardNavReport = resultsRoot.Q<Label>("ResultsNavReport");
        dashboardNavSettings = resultsRoot.Q<Label>("ResultsNavSettings");

        if (dashboardNavHome == null || dashboardNavSimulation == null ||
            dashboardNavResults == null || dashboardNavFmuMonitor == null ||
            dashboardNavCompare == null || dashboardNavReport == null ||
            dashboardNavSettings == null)
        {
            issue = "One or more Results dashboard navigation items are missing.";
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
        SetDashboardNavigationSelection(MainDashboardNavigationController.DashboardPage.Results);

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
        MainDashboardNavigationController.DashboardPage page,
        ClickEvent evt)
    {
        evt.StopPropagation();
        SetDashboardNavigationSelection(page);
        if (page == MainDashboardNavigationController.DashboardPage.Results)
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
        const string selectedClass = "results-navigation__item--selected";
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

    public bool TryValidateCurrentLayout(out string issue)
    {
        if (!IsReady)
        {
            issue = "Results document is not ready.";
            return false;
        }

        for (int i = 0; i < RequiredElementNames.Length; i++)
        {
            if (resultsRoot.Q<VisualElement>(RequiredElementNames[i]) == null)
            {
                issue = $"Required VisualElement is missing: {RequiredElementNames[i]}.";
                return false;
            }
        }

        VisualElement topBar = resultsRoot.Q<VisualElement>("ResultsTopBar");
        VisualElement navigation = resultsRoot.Q<VisualElement>("ResultsNavigation");
        VisualElement page = resultsRoot.Q<VisualElement>("ResultsPage");
        VisualElement variableTabs = resultsRoot.Q<VisualElement>("ResultVariableTabs");
        VisualElement workspace = resultsRoot.Q<VisualElement>("ResultsWorkspace");
        VisualElement mainColumn = resultsRoot.Q<VisualElement>("ResultsMainColumn");
        VisualElement sidebar = resultsRoot.Q<VisualElement>("ResultsAnalyticsSidebar");
        VisualElement sceneArea = resultsRoot.Q<VisualElement>("SceneArea");
        VisualElement scenePanel = resultsRoot.Q<VisualElement>("ScenePanel");
        VisualElement sceneViewport = resultsRoot.Q<VisualElement>("SceneViewport");
        VisualElement sceneControls = resultsRoot.Q<VisualElement>("SceneControlPanel");
        VisualElement sceneToolbar = resultsRoot.Q<VisualElement>("SceneToolbar");
        VisualElement legend = resultsRoot.Q<VisualElement>("ResultLegendPanel");

        VisualElement[] primaryRegions =
        {
            resultsRoot, topBar, navigation, page, variableTabs, workspace,
            mainColumn, sidebar, sceneArea, scenePanel, sceneViewport,
            sceneControls, sceneToolbar, legend
        };

        for (int i = 0; i < primaryRegions.Length; i++)
        {
            if (!HasUsableBounds(primaryRegions[i]))
            {
                issue = "One or more primary layout regions have invalid resolved bounds.";
                return false;
            }
        }

        const float tolerance = 1.0f;
        if (topBar.worldBound.yMax > navigation.worldBound.yMin + tolerance ||
            navigation.worldBound.yMax > page.worldBound.yMin + tolerance ||
            variableTabs.worldBound.yMax > workspace.worldBound.yMin + tolerance)
        {
            issue = "Top-level vertical regions overlap.";
            return false;
        }

        if (mainColumn.worldBound.xMax > sidebar.worldBound.xMin + tolerance)
        {
            issue = "Workspace columns overlap.";
            return false;
        }

        if (!Contains(resultsRoot.worldBound, topBar.worldBound, tolerance) ||
            !Contains(resultsRoot.worldBound, workspace.worldBound, tolerance) ||
            !Contains(scenePanel.worldBound, sceneViewport.worldBound, tolerance) ||
            !Contains(scenePanel.worldBound, sceneToolbar.worldBound, tolerance) ||
            !Contains(sceneViewport.worldBound, sceneControls.worldBound, tolerance) ||
            !Contains(sidebar.worldBound, legend.worldBound, tolerance))
        {
            issue = "A primary region is clipped outside its intended container.";
            return false;
        }

        if (!HasExactlyOneSelected(VariableButtonNames, "result-variable-tab--selected") ||
            !HasExactlyOneSelected(CameraModeButtonNames, "scene-toolbar-button--selected"))
        {
            issue = "Variable or camera interaction state is ambiguous.";
            return false;
        }

        if (!variableController.State.HasSelection ||
            !IsSupportedVariableButtonState())
        {
            issue = "Result variable state or supported-button availability is inconsistent.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private bool HasExactlyOneSelected(IReadOnlyList<string> names, string selectedClass)
    {
        int selectedCount = 0;
        for (int i = 0; i < names.Count; i++)
        {
            Button button = resultsRoot.Q<Button>(names[i]);
            if (button != null && button.ClassListContains(selectedClass))
                selectedCount++;
        }

        return selectedCount == 1;
    }

    private bool IsSupportedVariableButtonState()
    {
        Button temperature = resultsRoot.Q<Button>("TemperatureVariableButton");
        Button velocity = resultsRoot.Q<Button>("VelocityVariableButton");
        Button pressure = resultsRoot.Q<Button>("PressureVariableButton");
        Button streamline = resultsRoot.Q<Button>("StreamlineVariableButton");
        Button comfort = resultsRoot.Q<Button>("ComfortVariableButton");
        Button massFlux = resultsRoot.Q<Button>("MassFluxVariableButton");
        return temperature != null && temperature.enabledSelf &&
               velocity != null && velocity.enabledSelf &&
               pressure != null && !pressure.enabledSelf &&
               streamline != null && !streamline.enabledSelf &&
               comfort != null && !comfort.enabledSelf &&
               massFlux != null && !massFlux.enabledSelf;
    }

    private static bool HasUsableBounds(VisualElement element)
    {
        if (element == null)
            return false;

        Rect bounds = element.worldBound;
        return IsFinite(bounds.xMin) && IsFinite(bounds.yMin) &&
               IsFinite(bounds.width) && IsFinite(bounds.height) &&
               bounds.width > 1.0f && bounds.height > 1.0f;
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
