using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[DefaultExecutionOrder(-31000)]
[DisallowMultipleComponent]
public sealed class MainDashboardRuntimeDocument : MonoBehaviour
{
    private const string LogTag = "[UX01][F01][T04]";
    private const string RuntimeAssetPath = "MainDashboardRuntime";
    private const string RuntimePanelSettingsPath = "MainDashboardPanelSettings";
    private const string DashboardRootName = "MainDashboard";
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap/ApplicationBootstrap.unity";
    private const string SimulationScenePath = "Assets/Scenes/LBMScenes/LBM_1wayCST.unity";
    private const float SimulationStatusRefreshIntervalSeconds = 0.5f;
    private const float GpuUsageRefreshIntervalSeconds = 1.0f;
    private const float HeaderRefreshIntervalSeconds = 1.0f;
    private const float UserControlRefreshIntervalSeconds = 0.5f;
    private const float SystemMonitorRefreshIntervalSeconds = 0.5f;
    private const float TimeHistoryRefreshIntervalSeconds = 0.2f;
    private const float MinimumSetTemperatureDegC = -30.0f;
    private const float MaximumSetTemperatureDegC = 60.0f;
    private const int MinimumFanMode = 1;
    private const int MaximumFanMode = 5;
    private const int MinimumWindDirectionPosition = 1;
    private const int MaximumWindDirectionPosition = 6;
    private const int IndoorUnitCount = 5;

    private static readonly string[] HeaderStateClasses =
    {
        "top-bar__state--idle",
        "top-bar__state--running",
        "top-bar__state--paused",
        "top-bar__state--completed",
        "top-bar__state--error"
    };

    private static readonly string[] RequiredElementNames =
    {
        DashboardRootName,
        "TopBar",
        "TopBarProjectValue",
        "TopBarCaseValue",
        "TopBarClockValue",
        "TopBarState",
        "TopBarStateValue",
        "NavigationPanel",
        "PageHost",
        "HomePage",
        "SystemMonitorPage",
        "TimeHistoryPage",
        "TimeHistoryContent",
        "PlaceholderPage",
        "MainContent",
        "KPIContainer",
        "RoomAverageKpiCard",
        "TemperatureDeltaKpiCard",
        "MaxVelocityKpiCard",
        "MassErrorKpiCard",
        "GpuUsageKpiCard",
        "ViewPanel",
        "SceneContainer",
        "SceneViewport",
        "SceneViewImage",
        "SceneToolbar",
        "SceneFitButton",
        "SceneResetViewButton",
        "SceneVariableButton",
        "SceneRealtimeButton",
        "ResultLegendContainer",
        "TemperatureLegendTitle",
        "ResultLegendUnit",
        "TemperatureLegendScale",
        "TemperatureLegendBar",
        "LegendMaxLabel",
        "LegendUpperMidLabel",
        "LegendMiddleHighLabel",
        "LegendMiddleLowLabel",
        "LegendLowerMidLabel",
        "LegendMinLabel",
        "UserControlPanel",
        "BottomContentRow",
        "SimulationStatusPanel",
        "SimulationStatusIndicator",
        "SimulationStatusValue",
        "SimulationTimeValue",
        "SimulationTimeStepValue",
        "SimulationGridValue",
        "SimulationFpsValue",
        "SimulationGpuValue",
        "SimulationMemoryValue",
        "BottomAnalyticsPanel",
        "TemperatureTrendTab",
        "GpuPerformanceTrendTab",
        "TemperatureTrendPanel",
        "GpuPerformanceTrendPanel",
        "TrendSamplingNote",
        "TemperatureTrendChartHost",
        "GpuPerformanceTrendChartHost"
    };

    private static MainDashboardRuntimeDocument instance;

    private UIDocument document;
    private PanelSettings runtimePanelSettings;
    private VisualElement dashboardRoot;
    private MainDashboardNavigationController navigationController;
    private MainDashboardKpiController kpiController;
    private IMainDashboardKpiViewModel kpiViewModel;
    private MainDashboardSceneViewController sceneViewController;
    private MainDashboardSimulationStatusController simulationStatusController;
    private IMainDashboardSimulationStatusViewModel simulationStatusViewModel;
    private MainDashboardTrendController trendController;
    private MainDashboardGpuUsageMonitor gpuUsageMonitor;
    private SystemMonitorActuatorController systemMonitorActuatorController;
    private SystemMonitorSensorController systemMonitorSensorController;
    private SystemMonitorElectricalController systemMonitorElectricalController;
    private SystemMonitorGraphController systemMonitorGraphController;
    private SystemMonitorIndoorUnitController systemMonitorIndoorUnitController;
    private TimeHistoryController timeHistoryController;
    private MainDashboardViewModel dashboardViewModel;
    private Label topBarProjectValue;
    private Label topBarCaseValue;
    private Label topBarClockValue;
    private VisualElement topBarState;
    private Label topBarStateValue;
    private readonly Button[] userControlPowerButtons = new Button[IndoorUnitCount];
    private readonly DropdownField[] userControlOperationModeFields =
        new DropdownField[IndoorUnitCount];
    private readonly FloatField[] userControlTargetTemperatureFields =
        new FloatField[IndoorUnitCount];
    private readonly Button[] userControlFanDecreaseButtons = new Button[IndoorUnitCount];
    private readonly Label[] userControlFanValues = new Label[IndoorUnitCount];
    private readonly Button[] userControlFanIncreaseButtons = new Button[IndoorUnitCount];
    private Button userControlAirflowDecreaseButton;
    private Label userControlAirflowValue;
    private Button userControlAirflowIncreaseButton;
    private CoSimulationOrchestrator userControlOrchestrator;
    private ulong lastPresentedRuntimeControlRevision = ulong.MaxValue;
    private SimulationController headerSimulationController;
    private float nextSimulationStatusRefreshTime;
    private float nextGpuUsageRefreshTime;
    private float nextHeaderRefreshTime;
    private float nextUserControlRefreshTime;
    private float nextSystemMonitorRefreshTime;
    private float nextTimeHistoryRefreshTime;
    private double initializationStartedAt;

    public MainDashboardNavigationController Navigation => navigationController;
    public MainDashboardKpiController KpiCards => kpiController;
    public MainDashboardSceneViewController SceneView => sceneViewController;
    public MainDashboardSimulationStatusController SimulationStatus => simulationStatusController;
    public MainDashboardTrendController Trends => trendController;
    public MainDashboardViewModel ViewModel => dashboardViewModel;
    public TimeHistoryController TimeHistory => timeHistoryController;

    public static bool TryShowPage(MainDashboardNavigationController.DashboardPage page)
    {
        return instance != null && instance.navigationController != null &&
               instance.navigationController.ShowPage(page);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        string activeScenePath = SceneManager.GetActiveScene().path;
        if (activeScenePath != BootstrapScenePath && activeScenePath != SimulationScenePath)
            return;

        if (FindFirstObjectByType<MainDashboardRuntimeDocument>() != null)
            return;

        GameObject host = new GameObject("MainDashboardRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<MainDashboardRuntimeDocument>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        gpuUsageMonitor = new MainDashboardGpuUsageMonitor();
    }

    private IEnumerator Start()
    {
        // Keep the Simulation Set-up window on the startup critical path by itself.
        yield return null;
        while (CoSimulationStartupGate.IsWaitingForConfirmation)
            yield return null;

        initializationStartedAt = Time.realtimeSinceStartupAsDouble;
        if (!CreateDocument())
            yield break;

        // UI Toolkit resolves geometry on the following panel update.
        yield return null;
        ValidateRuntimeLayout();

        navigationController.CurrentPageChanged += OnNavigationPageChanged;
        if (SimulationSetupRuntimeDocument.IsRuntimeAvailable)
            navigationController.ShowPage(MainDashboardNavigationController.DashboardPage.Simulation);
    }

    private bool CreateDocument()
    {
        VisualTreeAsset visualTreeAsset = Resources.Load<VisualTreeAsset>(RuntimeAssetPath);
        if (visualTreeAsset == null)
        {
            Debug.LogError($"{LogTag} Runtime UXML을 찾을 수 없습니다: Resources/{RuntimeAssetPath}", this);
            return false;
        }

        runtimePanelSettings = Resources.Load<PanelSettings>(RuntimePanelSettingsPath);
        if (runtimePanelSettings == null)
        {
            Debug.LogError(
                $"{LogTag} Runtime PanelSettings를 찾을 수 없습니다: Resources/{RuntimePanelSettingsPath}",
                this);
            return false;
        }

        GameObject documentObject = new GameObject("MainDashboardUIDocument");
        documentObject.transform.SetParent(transform, false);
        documentObject.SetActive(false);

        document = documentObject.AddComponent<UIDocument>();
        document.panelSettings = runtimePanelSettings;
        document.visualTreeAsset = visualTreeAsset;
        document.sortingOrder = 0;
        documentObject.SetActive(true);
        document.rootVisualElement.style.flexGrow = 1.0f;
        document.rootVisualElement.style.flexShrink = 1.0f;

        if (!InitializeTopBar(document.rootVisualElement, out string topBarIssue))
        {
            Debug.LogError($"[UX01][TopBar] 초기화 실패: {topBarIssue}", this);
            return false;
        }

        if (!InitializeUserControlPresentation(document.rootVisualElement, out string userControlIssue))
        {
            Debug.LogError($"[UX01][UserControl] 초기화 실패: {userControlIssue}", this);
            return false;
        }

        navigationController = new MainDashboardNavigationController();
        if (!navigationController.Initialize(document.rootVisualElement, out string navigationIssue))
        {
            Debug.LogError($"{LogTag} Navigation 초기화 실패: {navigationIssue}", this);
            navigationController.Dispose();
            navigationController = null;
            return false;
        }

        kpiController = new MainDashboardKpiController();
        if (!kpiController.Initialize(document.rootVisualElement, out string kpiIssue))
        {
            Debug.LogError($"[UX01][F03] KPI 초기화 실패: {kpiIssue}", this);
            kpiController.Dispose();
            kpiController = null;
            navigationController.Dispose();
            navigationController = null;
            return false;
        }

        sceneViewController = new MainDashboardSceneViewController();
        if (!sceneViewController.Initialize(document.rootVisualElement, out string sceneViewIssue))
        {
            Debug.LogError($"[UX01][F04] Scene View 초기화 실패: {sceneViewIssue}", this);
            sceneViewController.Dispose();
            sceneViewController = null;
            kpiController.Dispose();
            kpiController = null;
            kpiViewModel = null;
            navigationController.Dispose();
            navigationController = null;
            return false;
        }

        simulationStatusController = new MainDashboardSimulationStatusController();
        if (!simulationStatusController.Initialize(document.rootVisualElement, out string statusIssue))
        {
            Debug.LogError($"[UX01][F05] Simulation Status 초기화 실패: {statusIssue}", this);
            simulationStatusController.Dispose();
            simulationStatusController = null;
            sceneViewController.Dispose();
            sceneViewController = null;
            kpiController.Dispose();
            kpiController = null;
            kpiViewModel = null;
            navigationController.Dispose();
            navigationController = null;
            return false;
        }

        trendController = new MainDashboardTrendController(
            new SimulationMainDashboardPerformanceDataSource(gpuUsageMonitor));
        if (!trendController.Initialize(document.rootVisualElement, out string trendIssue))
        {
            Debug.LogError($"[UX01][F06] Trend Chart 초기화 실패: {trendIssue}", this);
            trendController.Dispose();
            trendController = null;
            simulationStatusController.Dispose();
            simulationStatusController = null;
            simulationStatusViewModel = null;
            sceneViewController.Dispose();
            sceneViewController = null;
            kpiController.Dispose();
            kpiController = null;
            kpiViewModel = null;
            navigationController.Dispose();
            navigationController = null;
            return false;
        }

        systemMonitorActuatorController = new SystemMonitorActuatorController();
        if (!systemMonitorActuatorController.Initialize(
                document.rootVisualElement,
                out string systemMonitorIssue))
        {
            Debug.LogError($"[UX04][Actuator] 초기화 실패: {systemMonitorIssue}", this);
            systemMonitorActuatorController.Dispose();
            systemMonitorActuatorController = null;
            trendController.Dispose();
            trendController = null;
            simulationStatusController.Dispose();
            simulationStatusController = null;
            simulationStatusViewModel = null;
            sceneViewController.Dispose();
            sceneViewController = null;
            kpiController.Dispose();
            kpiController = null;
            kpiViewModel = null;
            navigationController.Dispose();
            navigationController = null;
            return false;
        }

        systemMonitorSensorController = new SystemMonitorSensorController();
        if (!systemMonitorSensorController.Initialize(
                document.rootVisualElement,
                out string systemMonitorSensorIssue))
        {
            Debug.LogError($"[UX04][F02] Sensor 초기화 실패: {systemMonitorSensorIssue}", this);
            systemMonitorSensorController.Dispose();
            systemMonitorSensorController = null;
            systemMonitorActuatorController.Dispose();
            systemMonitorActuatorController = null;
            trendController.Dispose();
            trendController = null;
            simulationStatusController.Dispose();
            simulationStatusController = null;
            simulationStatusViewModel = null;
            sceneViewController.Dispose();
            sceneViewController = null;
            kpiController.Dispose();
            kpiController = null;
            kpiViewModel = null;
            navigationController.Dispose();
            navigationController = null;
            return false;
        }

        systemMonitorElectricalController = new SystemMonitorElectricalController();
        if (!systemMonitorElectricalController.Initialize(
                document.rootVisualElement,
                out string systemMonitorElectricalIssue))
        {
            Debug.LogError($"[UX04][F03] Electrical 초기화 실패: {systemMonitorElectricalIssue}", this);
            systemMonitorElectricalController.Dispose();
            systemMonitorElectricalController = null;
            systemMonitorSensorController.Dispose();
            systemMonitorSensorController = null;
            systemMonitorActuatorController.Dispose();
            systemMonitorActuatorController = null;
            trendController.Dispose();
            trendController = null;
            simulationStatusController.Dispose();
            simulationStatusController = null;
            simulationStatusViewModel = null;
            sceneViewController.Dispose();
            sceneViewController = null;
            kpiController.Dispose();
            kpiController = null;
            kpiViewModel = null;
            navigationController.Dispose();
            navigationController = null;
            return false;
        }

        systemMonitorGraphController = new SystemMonitorGraphController();
        if (!systemMonitorGraphController.Initialize(
                document.rootVisualElement,
                out string systemMonitorGraphIssue))
        {
            Debug.LogError($"[UX04][F04] Primary Graph initialization failed: {systemMonitorGraphIssue}", this);
            systemMonitorGraphController.Dispose();
            systemMonitorGraphController = null;
            systemMonitorElectricalController.Dispose();
            systemMonitorElectricalController = null;
            systemMonitorSensorController.Dispose();
            systemMonitorSensorController = null;
            systemMonitorActuatorController.Dispose();
            systemMonitorActuatorController = null;
            trendController.Dispose();
            trendController = null;
            simulationStatusController.Dispose();
            simulationStatusController = null;
            simulationStatusViewModel = null;
            sceneViewController.Dispose();
            sceneViewController = null;
            kpiController.Dispose();
            kpiController = null;
            kpiViewModel = null;
            navigationController.Dispose();
            navigationController = null;
            return false;
        }

        systemMonitorIndoorUnitController = new SystemMonitorIndoorUnitController();
        if (!systemMonitorIndoorUnitController.Initialize(
                document.rootVisualElement,
                out string systemMonitorIndoorUnitIssue))
        {
            Debug.LogError($"[UX04][F05] Indoor Unit initialization failed: {systemMonitorIndoorUnitIssue}", this);
            systemMonitorIndoorUnitController.Dispose();
            systemMonitorIndoorUnitController = null;
            systemMonitorGraphController.Dispose();
            systemMonitorGraphController = null;
            systemMonitorElectricalController.Dispose();
            systemMonitorElectricalController = null;
            systemMonitorSensorController.Dispose();
            systemMonitorSensorController = null;
            systemMonitorActuatorController.Dispose();
            systemMonitorActuatorController = null;
            trendController.Dispose();
            trendController = null;
            simulationStatusController.Dispose();
            simulationStatusController = null;
            simulationStatusViewModel = null;
            sceneViewController.Dispose();
            sceneViewController = null;
            kpiController.Dispose();
            kpiController = null;
            kpiViewModel = null;
            navigationController.Dispose();
            navigationController = null;
            return false;
        }

        timeHistoryController = new TimeHistoryController();
        if (!timeHistoryController.Initialize(document.rootVisualElement, out string timeHistoryIssue))
        {
            Debug.LogError($"[UX05][TimeHistory][Case=Initialization] initialization failed: {timeHistoryIssue}", this);
            timeHistoryController.Dispose();
            timeHistoryController = null;
            systemMonitorIndoorUnitController.Dispose();
            systemMonitorIndoorUnitController = null;
            systemMonitorGraphController.Dispose();
            systemMonitorGraphController = null;
            systemMonitorElectricalController.Dispose();
            systemMonitorElectricalController = null;
            systemMonitorSensorController.Dispose();
            systemMonitorSensorController = null;
            systemMonitorActuatorController.Dispose();
            systemMonitorActuatorController = null;
            trendController.Dispose();
            trendController = null;
            simulationStatusController.Dispose();
            simulationStatusController = null;
            simulationStatusViewModel = null;
            sceneViewController.Dispose();
            sceneViewController = null;
            kpiController.Dispose();
            kpiController = null;
            kpiViewModel = null;
            navigationController.Dispose();
            navigationController = null;
            return false;
        }

        dashboardViewModel = new MainDashboardViewModel(
            new SimulationMainDashboardDataModel(),
            gpuUsageMonitor);
        kpiViewModel = dashboardViewModel.Kpi;
        simulationStatusViewModel = dashboardViewModel.SimulationStatus;
        dashboardViewModel.MetricsUpdated += OnDashboardMetricsUpdated;
        dashboardViewModel.TemperatureTrendSampled += OnTemperatureTrendSampled;
        dashboardViewModel.Tick();
        RefreshKpiCards();
        RefreshSimulationStatus();
        RefreshTopBar();
        RefreshUserControlPresentation(true);

        return true;
    }

    private void Update()
    {
        if (dashboardViewModel != null)
            gpuUsageMonitor?.Tick();

        sceneViewController?.Refresh();
        trendController?.Tick();
        dashboardViewModel?.Tick();

        if (kpiController != null && kpiViewModel != null &&
            Time.unscaledTime >= nextGpuUsageRefreshTime)
        {
            RefreshKpiCards();
            nextGpuUsageRefreshTime = Time.unscaledTime + GpuUsageRefreshIntervalSeconds;
        }

        if (Time.unscaledTime >= nextTimeHistoryRefreshTime)
        {
            if (userControlOrchestrator == null)
                userControlOrchestrator = FindFirstObjectByType<CoSimulationOrchestrator>();
            timeHistoryController?.Refresh(userControlOrchestrator);
            nextTimeHistoryRefreshTime = Time.unscaledTime + TimeHistoryRefreshIntervalSeconds;
        }

        if (Time.unscaledTime >= nextHeaderRefreshTime)
            RefreshTopBar();

        if (simulationStatusController != null && simulationStatusViewModel != null &&
            Time.unscaledTime >= nextSimulationStatusRefreshTime)
        {
            RefreshSimulationStatus();
        }

        if (Time.unscaledTime >= nextUserControlRefreshTime)
            RefreshUserControlPresentation();

        if (navigationController != null &&
            navigationController.CurrentPage == MainDashboardNavigationController.DashboardPage.FmuMonitor &&
            Time.unscaledTime >= nextSystemMonitorRefreshTime)
        {
            RefreshSystemMonitor();
        }
    }

    private void OnNavigationPageChanged(MainDashboardNavigationController.DashboardPage page)
    {
        bool showSimulationSetup =
            page == MainDashboardNavigationController.DashboardPage.Simulation &&
            SimulationSetupRuntimeDocument.TrySetRuntimeVisible(true);
        bool showResults =
            page == MainDashboardNavigationController.DashboardPage.Results &&
            ResultsRuntimeDocument.TrySetRuntimeVisible(true);

        if (!showSimulationSetup)
            SimulationSetupRuntimeDocument.TrySetRuntimeVisible(false);
        if (!showResults)
            ResultsRuntimeDocument.TrySetRuntimeVisible(false);

        if (document != null)
        {
            document.rootVisualElement.style.display = showSimulationSetup || showResults
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        if (page == MainDashboardNavigationController.DashboardPage.FmuMonitor)
            RefreshSystemMonitor();
    }

    private void RefreshSystemMonitor()
    {
        nextSystemMonitorRefreshTime = Time.unscaledTime + SystemMonitorRefreshIntervalSeconds;

        if (userControlOrchestrator == null)
            userControlOrchestrator = FindFirstObjectByType<CoSimulationOrchestrator>();

        // External FMU runtimes must not receive diagnostic reads while an asynchronous
        // communication step is pending. Keep the last completed values visible until
        // the orchestrator reaches the next safe communication boundary.
        bool canReadMonitoringSignals = userControlOrchestrator == null ||
                                        !userControlOrchestrator.IsCoSimStepInProgress;
        if (canReadMonitoringSignals)
        {
            systemMonitorActuatorController?.Refresh(userControlOrchestrator);
            systemMonitorSensorController?.Refresh(userControlOrchestrator);
            systemMonitorElectricalController?.Refresh(userControlOrchestrator);
            systemMonitorIndoorUnitController?.Refresh(userControlOrchestrator);
        }

        systemMonitorGraphController?.Refresh(userControlOrchestrator);
    }

    private void RefreshKpiCards()
    {
        kpiController.Render(kpiViewModel);
    }

    private void RefreshSimulationStatus()
    {
        simulationStatusController.Render(simulationStatusViewModel);
        RenderTopBarState(simulationStatusViewModel.State);
        nextSimulationStatusRefreshTime = Time.unscaledTime + SimulationStatusRefreshIntervalSeconds;
    }

    private bool InitializeTopBar(VisualElement documentRoot, out string issue)
    {
        topBarProjectValue = documentRoot?.Q<Label>("TopBarProjectValue");
        topBarCaseValue = documentRoot?.Q<Label>("TopBarCaseValue");
        topBarClockValue = documentRoot?.Q<Label>("TopBarClockValue");
        topBarState = documentRoot?.Q<VisualElement>("TopBarState");
        topBarStateValue = documentRoot?.Q<Label>("TopBarStateValue");

        if (topBarProjectValue == null || topBarCaseValue == null || topBarClockValue == null ||
            topBarState == null || topBarStateValue == null)
        {
            issue = "TopBar 정보 표시 요소가 없습니다.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private bool InitializeUserControlPresentation(VisualElement documentRoot, out string issue)
    {
        List<string> operationModeChoices = new List<string>
        {
            CoSimulationInitialConditionsPanel.OperationMode.Cooling.ToString(),
            CoSimulationInitialConditionsPanel.OperationMode.Dehumidification.ToString(),
            CoSimulationInitialConditionsPanel.OperationMode.Heating.ToString()
        };

        for (int index = 0; index < IndoorUnitCount; index++)
        {
            int room = index + 1;
            userControlPowerButtons[index] =
                documentRoot?.Q<Button>($"UserControlPowerButtonR{room}");
            userControlOperationModeFields[index] =
                documentRoot?.Q<DropdownField>($"UserControlOperationModeFieldR{room}");
            userControlTargetTemperatureFields[index] =
                documentRoot?.Q<FloatField>($"UserControlTargetTemperatureFieldR{room}");
            userControlFanDecreaseButtons[index] =
                documentRoot?.Q<Button>($"UserControlFanDecreaseButtonR{room}");
            userControlFanValues[index] =
                documentRoot?.Q<Label>($"UserControlFanValueR{room}");
            userControlFanIncreaseButtons[index] =
                documentRoot?.Q<Button>($"UserControlFanIncreaseButtonR{room}");

            if (userControlPowerButtons[index] == null ||
                userControlOperationModeFields[index] == null ||
                userControlTargetTemperatureFields[index] == null ||
                userControlFanDecreaseButtons[index] == null ||
                userControlFanValues[index] == null ||
                userControlFanIncreaseButtons[index] == null)
            {
                issue = $"R{room} User Control UI is incomplete.";
                return false;
            }

            userControlPowerButtons[index].userData = room;
            userControlOperationModeFields[index].userData = room;
            userControlTargetTemperatureFields[index].userData = room;
            userControlFanDecreaseButtons[index].userData = -room;
            userControlFanIncreaseButtons[index].userData = room;

            userControlOperationModeFields[index].choices =
                new List<string>(operationModeChoices);
            userControlOperationModeFields[index].SetValueWithoutNotify(
                CoSimulationInitialConditionsPanel.OperationMode.Cooling.ToString());
            userControlTargetTemperatureFields[index].isDelayed = true;
            userControlTargetTemperatureFields[index].formatString = "0.0";
            userControlTargetTemperatureFields[index].tooltip =
                $"Allowed range: {MinimumSetTemperatureDegC:F0} to " +
                $"{MaximumSetTemperatureDegC:F0} °C";
            ExpandUserControlField(userControlOperationModeFields[index]);
            ExpandUserControlField(userControlTargetTemperatureFields[index]);

            userControlPowerButtons[index].RegisterCallback<ClickEvent>(OnUserControlPowerClicked);
            userControlOperationModeFields[index].RegisterValueChangedCallback(
                OnUserControlOperationModeChanged);
            userControlTargetTemperatureFields[index].RegisterValueChangedCallback(
                OnUserControlTargetTemperatureChanged);
            userControlFanDecreaseButtons[index].RegisterCallback<ClickEvent>(
                OnUserControlFanClicked);
            userControlFanIncreaseButtons[index].RegisterCallback<ClickEvent>(
                OnUserControlFanClicked);
        }

        userControlAirflowDecreaseButton =
            documentRoot?.Q<Button>("UserControlAirflowDecreaseButtonR1");
        userControlAirflowValue = documentRoot?.Q<Label>("UserControlAirflowValueR1");
        userControlAirflowIncreaseButton =
            documentRoot?.Q<Button>("UserControlAirflowIncreaseButtonR1");
        if (userControlAirflowDecreaseButton == null || userControlAirflowValue == null ||
            userControlAirflowIncreaseButton == null)
        {
            issue = "R1 Airflow Direction UI is incomplete.";
            return false;
        }

        userControlAirflowDecreaseButton.userData = -1;
        userControlAirflowIncreaseButton.userData = 1;
        userControlAirflowDecreaseButton.RegisterCallback<ClickEvent>(OnUserControlAirflowClicked);
        userControlAirflowIncreaseButton.RegisterCallback<ClickEvent>(OnUserControlAirflowClicked);
        SetUserControlAvailability(false);

        issue = string.Empty;
        return true;
    }

    private static void ExpandUserControlField(VisualElement field)
    {
        if (field == null)
            return;

        field.style.flexBasis = 0;
        field.style.flexGrow = 1;
        field.style.flexShrink = 1;
        field.style.minWidth = 0;

        VisualElement generatedLabel = field.Q<VisualElement>(
            className: "unity-base-field__label");
        if (generatedLabel != null)
        {
            generatedLabel.style.display = DisplayStyle.None;
            generatedLabel.style.width = 0;
            generatedLabel.style.minWidth = 0;
        }

        VisualElement input = field.Q<VisualElement>(className: "unity-base-field__input");
        if (input != null)
        {
            input.style.flexBasis = 0;
            input.style.flexGrow = 1;
            input.style.flexShrink = 1;
            input.style.minWidth = 0;
        }

        VisualElement valueText = field.Q<VisualElement>(
            className: "unity-base-popup-field__text") ??
            field.Q<VisualElement>(className: "unity-text-input");
        if (valueText != null)
        {
            valueText.style.flexBasis = 0;
            valueText.style.flexGrow = 1;
            valueText.style.flexShrink = 1;
            valueText.style.minWidth = 0;
        }
    }

    private void RefreshUserControlPresentation(bool force = false)
    {
        nextUserControlRefreshTime = Time.unscaledTime + UserControlRefreshIntervalSeconds;

        if (userControlOperationModeFields[0] == null)
            return;

        if (userControlOrchestrator == null)
        {
            userControlOrchestrator = FindFirstObjectByType<CoSimulationOrchestrator>();
            lastPresentedRuntimeControlRevision = ulong.MaxValue;
        }

        if (userControlOrchestrator == null)
        {
            SetUserControlAvailability(false);
            return;
        }

        if (!force &&
            lastPresentedRuntimeControlRevision == userControlOrchestrator.RuntimeControlRevision)
        {
            return;
        }

        for (int index = 0; index < IndoorUnitCount; index++)
        {
            int room = index + 1;
            int operationMode = Mathf.Clamp(
                userControlOrchestrator.GetRuntimeOperationMode(room), 0, 2);
            int fanMode = Mathf.Clamp(
                userControlOrchestrator.GetRuntimeIndoorFanMode(room),
                MinimumFanMode,
                MaximumFanMode);

            userControlOperationModeFields[index].SetValueWithoutNotify(
                ((CoSimulationInitialConditionsPanel.OperationMode)operationMode).ToString());
            userControlTargetTemperatureFields[index].SetValueWithoutNotify(
                userControlOrchestrator.GetRuntimeSetTemperatureDegC(room));
            userControlFanValues[index].text = $"L{fanMode}";
            userControlFanValues[index].tooltip = FanModeName(fanMode);
            SetPowerButtonPresentation(
                userControlPowerButtons[index],
                userControlOrchestrator.IsRuntimeIndoorUnitPowerOn(room));
        }

        int windDirection = Mathf.Clamp(
            userControlOrchestrator.RuntimeWindDirectionPosition,
            MinimumWindDirectionPosition,
            MaximumWindDirectionPosition);
        userControlAirflowValue.text =
            $"P{windDirection} {userControlOrchestrator.RuntimeDischargeAngleDeg:F0}°";
        SetUserControlAvailability(true);
        lastPresentedRuntimeControlRevision = userControlOrchestrator.RuntimeControlRevision;
    }

    private void SetUserControlAvailability(bool available)
    {
        for (int index = 0; index < IndoorUnitCount; index++)
        {
            int room = index + 1;
            int fanMode = userControlOrchestrator != null
                ? userControlOrchestrator.GetRuntimeIndoorFanMode(room)
                : MinimumFanMode;
            userControlPowerButtons[index]?.SetEnabled(available);
            userControlOperationModeFields[index]?.SetEnabled(available);
            userControlTargetTemperatureFields[index]?.SetEnabled(available);
            userControlFanDecreaseButtons[index]?.SetEnabled(
                available && fanMode > MinimumFanMode);
            userControlFanIncreaseButtons[index]?.SetEnabled(
                available && fanMode < MaximumFanMode);
        }

        int windDirection = userControlOrchestrator != null
            ? userControlOrchestrator.RuntimeWindDirectionPosition
            : MinimumWindDirectionPosition;
        userControlAirflowDecreaseButton?.SetEnabled(
            available && windDirection > MinimumWindDirectionPosition);
        userControlAirflowIncreaseButton?.SetEnabled(
            available && windDirection < MaximumWindDirectionPosition);
    }

    private static void SetPowerButtonPresentation(Button button, bool powerOn)
    {
        if (button == null)
            return;

        button.text = powerOn ? "On" : "Off";
        button.EnableInClassList("user-control__power-button--on", powerOn);
    }

    private void OnUserControlPowerClicked(ClickEvent evt)
    {
        if (userControlOrchestrator == null)
        {
            RefreshUserControlPresentation(true);
            return;
        }

        int room = GetRoomFromElement(evt.currentTarget as VisualElement);
        if (room == 0)
            return;

        bool powerOn = !userControlOrchestrator.IsRuntimeIndoorUnitPowerOn(room);
        if (!userControlOrchestrator.TrySetRuntimeIndoorUnitPower(room, powerOn, out string message))
            LogUserControlWarning(message);

        RefreshUserControlPresentation(true);
    }

    private void OnUserControlOperationModeChanged(ChangeEvent<string> evt)
    {
        if (userControlOrchestrator == null)
        {
            RefreshUserControlPresentation(true);
            return;
        }

        DropdownField field = evt.currentTarget as DropdownField;
        int room = GetRoomFromElement(field);
        int operationMode = field != null ? field.choices.IndexOf(evt.newValue) : -1;
        if (operationMode < 0 || operationMode > 2)
        {
            RefreshUserControlPresentation(true);
            return;
        }

        ApplyRuntimeControls(
            room,
            userControlOrchestrator.GetRuntimeSetTemperatureDegC(room),
            operationMode,
            userControlOrchestrator.GetRuntimeIndoorFanMode(room),
            userControlOrchestrator.RuntimeWindDirectionPosition);
    }

    private void OnUserControlTargetTemperatureChanged(ChangeEvent<float> evt)
    {
        if (userControlOrchestrator == null)
        {
            RefreshUserControlPresentation(true);
            return;
        }

        int room = GetRoomFromElement(evt.currentTarget as VisualElement);
        float value = evt.newValue;
        if (room == 0 || !float.IsFinite(value) || value < MinimumSetTemperatureDegC ||
            value > MaximumSetTemperatureDegC)
        {
            LogUserControlWarning(
                $"Target Temperature must be between {MinimumSetTemperatureDegC:F0} and " +
                $"{MaximumSetTemperatureDegC:F0} °C. Keeping the current runtime value.");
            RefreshUserControlPresentation(true);
            return;
        }

        ApplyRuntimeControls(
            room,
            value,
            userControlOrchestrator.GetRuntimeOperationMode(room),
            userControlOrchestrator.GetRuntimeIndoorFanMode(room),
            userControlOrchestrator.RuntimeWindDirectionPosition);
    }

    private void OnUserControlFanClicked(ClickEvent evt)
    {
        if (!(evt.currentTarget is VisualElement element) || !(element.userData is int encodedRoom))
            return;

        int room = Mathf.Abs(encodedRoom);
        AdjustUserControlFan(room, encodedRoom < 0 ? -1 : 1);
    }

    private void AdjustUserControlFan(int room, int delta)
    {
        if (userControlOrchestrator == null)
        {
            RefreshUserControlPresentation(true);
            return;
        }

        int fanMode = Mathf.Clamp(
            userControlOrchestrator.GetRuntimeIndoorFanMode(room) + delta,
            MinimumFanMode,
            MaximumFanMode);
        if (fanMode == userControlOrchestrator.GetRuntimeIndoorFanMode(room))
            return;

        ApplyRuntimeControls(
            room,
            userControlOrchestrator.GetRuntimeSetTemperatureDegC(room),
            userControlOrchestrator.GetRuntimeOperationMode(room),
            fanMode,
            userControlOrchestrator.RuntimeWindDirectionPosition);
    }

    private void OnUserControlAirflowClicked(ClickEvent evt)
    {
        if (!(evt.currentTarget is VisualElement element) || !(element.userData is int delta))
            return;

        AdjustUserControlAirflow(delta);
    }

    private void AdjustUserControlAirflow(int delta)
    {
        if (userControlOrchestrator == null)
        {
            RefreshUserControlPresentation(true);
            return;
        }

        int windDirection = Mathf.Clamp(
            userControlOrchestrator.RuntimeWindDirectionPosition + delta,
            MinimumWindDirectionPosition,
            MaximumWindDirectionPosition);
        if (windDirection == userControlOrchestrator.RuntimeWindDirectionPosition)
            return;

        ApplyRuntimeControls(
            1,
            userControlOrchestrator.GetRuntimeSetTemperatureDegC(1),
            userControlOrchestrator.GetRuntimeOperationMode(1),
            userControlOrchestrator.GetRuntimeIndoorFanMode(1),
            windDirection);
    }

    private void ApplyRuntimeControls(
        int room,
        float setTemperatureDegC,
        int operationMode,
        int indoorFanMode,
        int windDirectionPosition)
    {
        if (userControlOrchestrator == null)
            return;

        if (!userControlOrchestrator.TryApplyRuntimeControlsForRoom(
                room,
                setTemperatureDegC,
                operationMode,
                indoorFanMode,
                windDirectionPosition,
                out string message))
        {
            LogUserControlWarning(message);
        }

        RefreshUserControlPresentation(true);
    }

    private static int GetRoomFromElement(VisualElement element)
    {
        return element != null && element.userData is int room &&
               room >= 1 && room <= IndoorUnitCount
            ? room
            : 0;
    }

    private void LogUserControlWarning(string message)
    {
        string experimentTag = headerSimulationController != null &&
                               !string.IsNullOrWhiteSpace(headerSimulationController.ActiveCaseName)
            ? headerSimulationController.ActiveCaseName
            : userControlOrchestrator != null &&
              !string.IsNullOrWhiteSpace(userControlOrchestrator.ProfileName)
                ? userControlOrchestrator.ProfileName
                : "Runtime";
        Debug.LogWarning($"[UX01][UserControl][{experimentTag}] {message}", this);
    }

    private static string FanModeName(int fanMode)
    {
        switch (fanMode)
        {
            case 1: return CoSimulationInitialConditionsPanel.FanStrength.VeryLow.ToString();
            case 2: return CoSimulationInitialConditionsPanel.FanStrength.Low.ToString();
            case 3: return CoSimulationInitialConditionsPanel.FanStrength.Medium.ToString();
            case 4: return CoSimulationInitialConditionsPanel.FanStrength.High.ToString();
            case 5: return CoSimulationInitialConditionsPanel.FanStrength.SuperHigh.ToString();
            default: return string.Empty;
        }
    }

    private void RefreshTopBar()
    {
        if (topBarProjectValue == null)
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        topBarProjectValue.text = string.IsNullOrWhiteSpace(activeScene.name)
            ? "—"
            : activeScene.name;
        topBarProjectValue.tooltip = activeScene.path;

        if (headerSimulationController == null)
            headerSimulationController = FindFirstObjectByType<SimulationController>();

        topBarCaseValue.text = headerSimulationController != null &&
                               !string.IsNullOrWhiteSpace(headerSimulationController.ActiveCaseName)
            ? headerSimulationController.ActiveCaseName
            : "Waiting";
        topBarCaseValue.tooltip = topBarCaseValue.text;
        topBarClockValue.text = System.DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
        nextHeaderRefreshTime = Time.unscaledTime + HeaderRefreshIntervalSeconds;
    }

    private void RenderTopBarState(MainDashboardSimulationState state)
    {
        if (topBarState == null || topBarStateValue == null)
            return;

        for (int i = 0; i < HeaderStateClasses.Length; i++)
            topBarState.RemoveFromClassList(HeaderStateClasses[i]);

        string suffix;
        string label;
        switch (state)
        {
            case MainDashboardSimulationState.Running:
                suffix = "running";
                label = "RUNNING";
                break;
            case MainDashboardSimulationState.Paused:
                suffix = "paused";
                label = "PAUSED";
                break;
            case MainDashboardSimulationState.Completed:
                suffix = "completed";
                label = "DONE";
                break;
            case MainDashboardSimulationState.Error:
                suffix = "error";
                label = "ERROR";
                break;
            default:
                suffix = "idle";
                label = "IDLE";
                break;
        }

        topBarState.AddToClassList($"top-bar__state--{suffix}");
        topBarStateValue.text = label;
    }

    private void OnDashboardMetricsUpdated()
    {
        if (kpiController != null && kpiViewModel != null)
            RefreshKpiCards();
    }

    private void OnTemperatureTrendSampled(MainDashboardTemperatureTrendSample sample)
    {
        trendController?.AddTemperatureSample(sample);
    }

    private void ValidateRuntimeLayout()
    {
        VisualElement documentRoot = document != null ? document.rootVisualElement : null;
        dashboardRoot = documentRoot?.Q<VisualElement>(DashboardRootName);
        if (!TryValidateCurrentLayout(out string layoutIssue))
        {
            Debug.LogError($"{LogTag} 레이아웃 검증 실패: {layoutIssue}", this);
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        dashboardRoot.RegisterCallback<GeometryChangedEvent>(OnDashboardGeometryChanged);
#endif

        double elapsed = Time.realtimeSinceStartupAsDouble - initializationStartedAt;
        Debug.Log(
            $"{LogTag} Runtime UIDocument 표시 및 계층 검증 완료. " +
            $"size={dashboardRoot.resolvedStyle.width:F0}x{dashboardRoot.resolvedStyle.height:F0}, elapsed={elapsed:F3}s.",
            this);
    }

    public bool TryValidateCurrentLayout(out string issue)
    {
        VisualElement documentRoot = document != null ? document.rootVisualElement : null;
        if (documentRoot == null)
        {
            issue = "UIDocument 루트가 없습니다.";
            return false;
        }

        dashboardRoot = documentRoot.Q<VisualElement>(DashboardRootName);
        if (dashboardRoot == null)
        {
            issue = $"Runtime UIDocument에 '{DashboardRootName}' 루트가 없습니다.";
            return false;
        }

        for (int i = 0; i < RequiredElementNames.Length; i++)
        {
            if (documentRoot.Q<VisualElement>(RequiredElementNames[i]) == null)
            {
                issue = $"필수 VisualElement가 없습니다: {RequiredElementNames[i]}";
                return false;
            }
        }

        return TryValidateLayoutBounds(documentRoot, dashboardRoot, out issue);
    }

    private static bool TryValidateLayoutBounds(
        VisualElement documentRoot,
        VisualElement dashboard,
        out string issue)
    {
        if (!HasArea(dashboard.worldBound))
        {
            issue = "MainDashboard 크기가 유효하지 않습니다.";
            return false;
        }

        VisualElement panelRoot = documentRoot.panel?.visualTree;
        if (panelRoot == null || !HasArea(panelRoot.worldBound))
        {
            issue = "UI Toolkit Panel 크기가 유효하지 않습니다.";
            return false;
        }

        Rect panelBounds = panelRoot.worldBound;
        Rect dashboardBounds = dashboard.worldBound;
        if (Mathf.Abs(panelBounds.width - dashboardBounds.width) > 1.0f ||
            Mathf.Abs(panelBounds.height - dashboardBounds.height) > 1.0f)
        {
            issue =
                $"MainDashboard가 Panel 전체를 채우지 않습니다. " +
                $"panel={panelBounds.width:F0}x{panelBounds.height:F0}, " +
                $"dashboard={dashboardBounds.width:F0}x{dashboardBounds.height:F0}.";
            return false;
        }

        string[] alwaysVisibleNames = { "TopBar", "NavigationPanel", "PageHost" };
        for (int i = 0; i < alwaysVisibleNames.Length; i++)
        {
            if (!ValidateContainedElement(documentRoot, dashboard, alwaysVisibleNames[i], out issue))
                return false;
        }

        VisualElement topBar = documentRoot.Q<VisualElement>("TopBar");
        VisualElement navigationPanel = documentRoot.Q<VisualElement>("NavigationPanel");
        VisualElement pageHost = documentRoot.Q<VisualElement>("PageHost");
        if (Overlaps(topBar, navigationPanel) || Overlaps(navigationPanel, pageHost))
        {
            issue = "상단 영역 또는 PageHost가 겹칩니다.";
            return false;
        }

        VisualElement homePage = documentRoot.Q<VisualElement>("HomePage");
        if (homePage.resolvedStyle.display == DisplayStyle.None)
        {
            VisualElement systemMonitorPage = documentRoot.Q<VisualElement>("SystemMonitorPage");
            if (systemMonitorPage != null && systemMonitorPage.resolvedStyle.display != DisplayStyle.None)
                return ValidateContainedElement(documentRoot, dashboard, "SystemMonitorPage", out issue);

            VisualElement timeHistoryPage = documentRoot.Q<VisualElement>("TimeHistoryPage");
            if (timeHistoryPage != null && timeHistoryPage.resolvedStyle.display != DisplayStyle.None)
                return ValidateContainedElement(documentRoot, dashboard, "TimeHistoryPage", out issue);

            return ValidateContainedElement(documentRoot, dashboard, "PlaceholderPage", out issue);
        }

        string[] homeElementNames =
        {
            "HomePage",
            "MainContent",
            "KPIContainer",
            "RoomAverageKpiCard",
            "TemperatureDeltaKpiCard",
            "MaxVelocityKpiCard",
            "MassErrorKpiCard",
            "GpuUsageKpiCard",
            "ViewPanel",
            "SceneContainer",
            "SceneViewport",
            "SceneViewImage",
            "SceneToolbar",
            "ResultLegendContainer",
            "TemperatureLegendScale",
            "UserControlPanel",
            "UserControlPowerButtonR1",
            "UserControlPowerButtonR5",
            "UserControlOperationModeFieldR1",
            "UserControlOperationModeFieldR5",
            "UserControlTargetTemperatureFieldR1",
            "UserControlTargetTemperatureFieldR5",
            "UserControlFanDecreaseButtonR1",
            "UserControlFanIncreaseButtonR5",
            "UserControlAirflowValueR1",
            "SimulationStatusPanel",
            "SimulationStatusValue",
            "SimulationTimeValue",
            "SimulationTimeStepValue",
            "SimulationGridValue",
            "SimulationFpsValue",
            "SimulationGpuValue",
            "SimulationMemoryValue",
            "BottomContentRow",
            "BottomAnalyticsPanel",
            "TemperatureTrendTab",
            "GpuPerformanceTrendTab",
            "TemperatureTrendPanel",
            "TemperatureTrendChartHost"
        };
        for (int i = 0; i < homeElementNames.Length; i++)
        {
            if (!ValidateContainedElement(documentRoot, dashboard, homeElementNames[i], out issue))
                return false;
        }

        VisualElement visualizationRow = dashboard.Q<VisualElement>(className: "main-dashboard__visualization-row");
        if (visualizationRow == null || !HasArea(visualizationRow.worldBound))
        {
            issue = "시각화 행의 크기가 유효하지 않습니다.";
            return false;
        }

        if (Overlaps(documentRoot.Q<VisualElement>("MainContent"), documentRoot.Q<VisualElement>("BottomContentRow")) ||
            Overlaps(documentRoot.Q<VisualElement>("KPIContainer"), visualizationRow) ||
            Overlaps(documentRoot.Q<VisualElement>("SceneContainer"), documentRoot.Q<VisualElement>("ResultLegendContainer")) ||
            Overlaps(documentRoot.Q<VisualElement>("ViewPanel"), documentRoot.Q<VisualElement>("UserControlPanel")) ||
            Overlaps(documentRoot.Q<VisualElement>("BottomAnalyticsPanel"), documentRoot.Q<VisualElement>("SimulationStatusPanel")))
        {
            issue = "Home 화면의 인접 패널이 겹칩니다.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private static bool ValidateContainedElement(
        VisualElement documentRoot,
        VisualElement dashboard,
        string elementName,
        out string issue)
    {
        VisualElement element = documentRoot.Q<VisualElement>(elementName);
        if (!HasArea(element.worldBound))
        {
            issue = $"{elementName} 크기가 유효하지 않습니다.";
            return false;
        }

        if (!Contains(dashboard.worldBound, element.worldBound))
        {
            issue = $"{elementName} 영역이 MainDashboard 밖으로 잘렸습니다.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private static bool HasArea(Rect rect)
    {
        return float.IsFinite(rect.width) && float.IsFinite(rect.height) &&
               rect.width > 0.5f && rect.height > 0.5f;
    }

    private static bool Contains(Rect outer, Rect inner)
    {
        const float tolerance = 0.5f;
        return inner.xMin >= outer.xMin - tolerance &&
               inner.yMin >= outer.yMin - tolerance &&
               inner.xMax <= outer.xMax + tolerance &&
               inner.yMax <= outer.yMax + tolerance;
    }

    private static bool Overlaps(VisualElement first, VisualElement second)
    {
        Rect a = first.worldBound;
        Rect b = second.worldBound;
        float overlapWidth = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        float overlapHeight = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return overlapWidth > 0.5f && overlapHeight > 0.5f;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnDashboardGeometryChanged(GeometryChangedEvent changeEvent)
    {
        if (changeEvent.oldRect.size == changeEvent.newRect.size || document == null)
            return;

        VisualElement documentRoot = document.rootVisualElement;
        if (!TryValidateLayoutBounds(documentRoot, dashboardRoot, out string issue))
        {
            Debug.LogWarning(
                $"{LogTag} resize 레이아웃 검증 실패 " +
                $"({changeEvent.newRect.width:F0}x{changeEvent.newRect.height:F0}): {issue}",
                this);
        }
    }
#endif

    private void OnDestroy()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        dashboardRoot?.UnregisterCallback<GeometryChangedEvent>(OnDashboardGeometryChanged);
#endif

        for (int index = 0; index < IndoorUnitCount; index++)
        {
            userControlPowerButtons[index]?.UnregisterCallback<ClickEvent>(
                OnUserControlPowerClicked);
            userControlOperationModeFields[index]?.UnregisterValueChangedCallback(
                OnUserControlOperationModeChanged);
            userControlTargetTemperatureFields[index]?.UnregisterValueChangedCallback(
                OnUserControlTargetTemperatureChanged);
            userControlFanDecreaseButtons[index]?.UnregisterCallback<ClickEvent>(
                OnUserControlFanClicked);
            userControlFanIncreaseButtons[index]?.UnregisterCallback<ClickEvent>(
                OnUserControlFanClicked);
        }

        userControlAirflowDecreaseButton?.UnregisterCallback<ClickEvent>(
            OnUserControlAirflowClicked);
        userControlAirflowIncreaseButton?.UnregisterCallback<ClickEvent>(
            OnUserControlAirflowClicked);
        userControlOrchestrator = null;

        if (navigationController != null)
            navigationController.CurrentPageChanged -= OnNavigationPageChanged;
        navigationController?.Dispose();
        navigationController = null;
        kpiController?.Dispose();
        kpiController = null;
        kpiViewModel = null;
        sceneViewController?.Dispose();
        sceneViewController = null;
        simulationStatusController?.Dispose();
        simulationStatusController = null;
        simulationStatusViewModel = null;
        trendController?.Dispose();
        trendController = null;
        gpuUsageMonitor?.Dispose();
        gpuUsageMonitor = null;
        systemMonitorActuatorController?.Dispose();
        systemMonitorActuatorController = null;
        systemMonitorSensorController?.Dispose();
        systemMonitorSensorController = null;
        systemMonitorElectricalController?.Dispose();
        systemMonitorElectricalController = null;
        systemMonitorGraphController?.Dispose();
        systemMonitorGraphController = null;
        systemMonitorIndoorUnitController?.Dispose();
        systemMonitorIndoorUnitController = null;
        timeHistoryController?.Dispose();
        timeHistoryController = null;
        if (dashboardViewModel != null)
        {
            dashboardViewModel.MetricsUpdated -= OnDashboardMetricsUpdated;
            dashboardViewModel.TemperatureTrendSampled -= OnTemperatureTrendSampled;
            dashboardViewModel.Dispose();
            dashboardViewModel = null;
        }

        if (instance == this)
            instance = null;
    }
}
