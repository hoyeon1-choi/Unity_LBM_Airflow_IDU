using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

[AddComponentMenu("Co-Simulation/Integrated Monitoring Dashboard")]
public sealed class CoSimulationMonitoringDashboard : MonoBehaviour
{
    [Header("Refresh")]
    [SerializeField, Min(0.05f)] private float refreshIntervalSeconds = 0.25f;
    [SerializeField, Min(0.1f)] private float historyFallbackIntervalSeconds = 1.0f;

    [Header("Display Quality")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(960.0f, 540.0f);
    [Tooltip("Used by world-space canvases; it does not affect TMP sharpness on this screen-space overlay dashboard.")]
    [SerializeField, Range(1.0f, 4.0f)] private float dynamicPixelsPerUnit = 2.0f;
    [Tooltip("SDF text is usually smoother with pixel snapping disabled at non-integer canvas scales.")]
    [SerializeField] private bool pixelPerfect = false;
    [SerializeField] private string preferredSystemFontFamily = "Malgun Gothic";
    [SerializeField, Range(48, 180)] private int sdfSamplingPointSize = 144;
    [SerializeField, Range(4, 32)] private int sdfAtlasPadding = 16;
    [Tooltip("Optional persistent TMP font asset. If empty, Windows builds a runtime Malgun Gothic SDF font.")]
    [SerializeField] private TMP_FontAsset monitoringFontAsset;

    [Header("Pages")]
    [SerializeField] private bool startOnSignalGraphPage = false;

    [Header("Hierarchy Authoring")]
    [Tooltip("Legacy compatibility fallback that builds the UI only when the stored hierarchy is missing.")]
    [SerializeField] private bool buildMissingHierarchyAtRuntime = true;

    [Header("Display Separation")]
    [SerializeField] private bool preferDedicatedMonitoringDisplay = true;
    [SerializeField, Range(0, 7)] private int monitoringDisplayIndex = 1;
    [SerializeField, Min(640)] private int monitoringRenderWidth = 1920;
    [SerializeField, Min(360)] private int monitoringRenderHeight = 1080;
    [SerializeField] private bool fallbackToPrimaryDisplay = true;
    [SerializeField] private bool enableF10VisibilityToggle = true;

    [Header("Optional References")]
    [SerializeField] private CoSimulationOrchestrator orchestrator;
    [SerializeField] private SimulationController simulationController;
    [SerializeField] private AirflowLbmSignalAdapter airflowAdapter;
    [SerializeField] private SimulationResultSampler resultSampler;

    private readonly Dictionary<string, TMP_Text> metricValues = new Dictionary<string, TMP_Text>(StringComparer.Ordinal);
    private readonly Dictionary<string, NodeView> nodeViews = new Dictionary<string, NodeView>(StringComparer.Ordinal);
    private readonly Dictionary<int, IndoorRowView> indoorRows = new Dictionary<int, IndoorRowView>();

    private TMP_FontAsset font;
    private TMP_Text headerStatus;
    private TMP_Text headerProfile;
    private TMP_Text footerStatus;
    private Canvas dashboardCanvas;
    private Camera displayBackdropCamera;
    private GameObject dashboardRoot;
    private GameObject overviewPageRoot;
    private GameObject signalGraphPageRoot;
    private Button overviewPageButton;
    private Button signalGraphPageButton;
    private Button setTemperatureDecreaseButton;
    private Button setTemperatureIncreaseButton;
    private Button indoorFanDecreaseButton;
    private Button indoorFanIncreaseButton;
    private TMP_Text overviewPageButtonLabel;
    private TMP_Text signalGraphPageButtonLabel;
    private CoSimulationSignalGraphPanel signalGraphPanel;
    private bool dashboardVisible = true;
    private bool showingSignalGraphPage;
    private bool allowVisibilityToggle = true;
    private bool ownsRuntimeFont;
    private CoSimulationMonitorChartGraphic temperatureChart;
    private CoSimulationMonitorChartGraphic pressureChart;
    private float nextRefreshTime;
    private float nextFallbackSampleTime;
    private ulong lastSampledCoSimStep = ulong.MaxValue;
    private int lastRuntimeControlInputFrame = -1;

    [Header("Theme")]
    [SerializeField] private Color windowColor = new Color(0.035f, 0.047f, 0.067f, 0.99f);
    [SerializeField] private Color panelColor = new Color(0.075f, 0.094f, 0.125f, 0.98f);
    [SerializeField] private Color panelHeaderColor = new Color(0.105f, 0.133f, 0.176f, 1.0f);
    [SerializeField] private Color textColor = new Color(0.91f, 0.94f, 0.97f, 1.0f);
    [SerializeField] private Color mutedColor = new Color(0.61f, 0.68f, 0.76f, 1.0f);
    [SerializeField] private Color greenColor = new Color(0.27f, 0.86f, 0.50f, 1.0f);
    [SerializeField] private Color yellowColor = new Color(1.00f, 0.70f, 0.24f, 1.0f);
    [SerializeField] private Color redColor = new Color(1.00f, 0.34f, 0.39f, 1.0f);
    [SerializeField] private Color accentColor = new Color(0.20f, 0.70f, 0.95f, 1.0f);

    private sealed class NodeView
    {
        public Image background;
        public TMP_Text indicator;
        public TMP_Text detail;
    }

    private sealed class IndoorRowView
    {
        public TMP_Text status;
        public Button powerButton;
        public TMP_Text powerLabel;
        public TMP_Text roomTemperature;
        public TMP_Text roomHumidity;
        public TMP_Text dischargeTemperature;
        public TMP_Text dischargeHumidity;
        public TMP_Text massFlow;
        public TMP_Text fanMode;
        public TMP_Text eevPulse;
    }

    private sealed class ChartAxisView
    {
        public TMP_Text minTime;
        public TMP_Text maxTime;
        public TMP_Text minValue;
        public TMP_Text midValue;
        public TMP_Text maxValue;
        public string valueUnit;
        public int valueDecimals;
    }

    private ChartAxisView temperatureAxis;
    private ChartAxisView pressureAxis;
    private Coroutine pendingLayoutRepair;

    private void Awake()
    {
        font = monitoringFontAsset != null ? monitoringFontAsset : CreateRuntimeFontAsset();
        // Authored labels still reference the default TMP font while the scene is loading.
        // Replace it before a canvas rebuild to avoid repeated missing-glyph work and warnings.
        ApplyFontToHierarchy();
        if (transform.Find("Background") == null)
        {
            if (buildMissingHierarchyAtRuntime)
                BuildInterface();
            else
            {
                Debug.LogError(
                    "[CoSim Monitor] 저장된 Monitoring UI 계층이 없습니다. " +
                    "Dashboard Inspector의 'Rebuild Monitoring Hierarchy'를 실행하세요.",
                    this);
                enabled = false;
                return;
            }
        }
        else if (!TryBindAuthoredInterface())
        {
            Debug.LogError(
                "[CoSim Monitor] 저장된 Monitoring UI 계층에 필수 오브젝트가 없습니다. " +
                "Dashboard Inspector의 'Rebuild Monitoring Hierarchy'를 실행하세요.",
                this);
            enabled = false;
            return;
        }

        ApplyFontToHierarchy();
        ScheduleMonitoringLayoutRepair();
        SetDashboardPage(startOnSignalGraphPage);
        ApplyDisplayConfiguration();
        ResolveReferences();
    }

    private void OnDestroy()
    {
        if (ownsRuntimeFont && font != null)
        {
            if (Application.isPlaying)
                Destroy(font);
            else
                DestroyImmediate(font);
        }
    }

    private void Update()
    {
        if (allowVisibilityToggle && Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
        {
            dashboardVisible = !dashboardVisible;
            if (dashboardRoot != null)
                dashboardRoot.SetActive(dashboardVisible);
            Debug.Log($"[CoSim Monitor] Dashboard visibility={(dashboardVisible ? "shown" : "hidden")} (F10).");
        }

        PollHeaderPageTabs();
        PollRuntimeControlButtons();
        PollRoomPowerButtons();

        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
        ResolveReferences();
        RefreshView();
    }

    private void ResolveReferences()
    {
        if (orchestrator == null)
            orchestrator = FindFirstObjectByType<CoSimulationOrchestrator>();
        if (simulationController == null)
            simulationController = SimulationController.Instance != null
                ? SimulationController.Instance
                : FindFirstObjectByType<SimulationController>();
        if (airflowAdapter == null)
            airflowAdapter = FindFirstObjectByType<AirflowLbmSignalAdapter>();
        if (resultSampler == null)
            resultSampler = FindFirstObjectByType<SimulationResultSampler>();
    }

    private void BuildInterface()
    {
        dashboardCanvas = GetComponent<Canvas>();
        if (dashboardCanvas == null)
            dashboardCanvas = gameObject.AddComponent<Canvas>();
        dashboardCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        dashboardCanvas.sortingOrder = 1000;
        dashboardCanvas.pixelPerfect = pixelPerfect;
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.dynamicPixelsPerUnit = dynamicPixelsPerUnit;
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        BuildDisplayBackdropCamera();

        RectTransform root = CreateRect(transform, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        dashboardRoot = root.gameObject;
        root.gameObject.AddComponent<Image>().color = windowColor;

        RectTransform overviewPage = CreateRect(root, "OverviewPage", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        overviewPageRoot = overviewPage.gameObject;
        RectTransform signalGraphPage = CreateRect(root, "SignalGraphPage", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        signalGraphPageRoot = signalGraphPage.gameObject;

        BuildHeader(root);
        BuildChainStrip(overviewPage);
        BuildEquipmentPanel(overviewPage);
        BuildIndoorPanel(overviewPage);
        BuildChartPanel(overviewPage, true);
        BuildChartPanel(overviewPage, false);
        BuildStatusPanel(overviewPage);

        signalGraphPanel = signalGraphPage.gameObject.AddComponent<CoSimulationSignalGraphPanel>();
        signalGraphPanel.Initialize(font);
        BindPageNavigation();
        SetDashboardPage(startOnSignalGraphPage);
    }

    /// <summary>
    /// Creates the complete editable UI hierarchy while authoring the Monitoring scene.
    /// Runtime builds use the same method only as a compatibility fallback for older scenes.
    /// </summary>
    public void BuildAuthoredInterface(TMP_FontAsset editorFont)
    {
        if (transform.Find("Background") != null)
            return;

        font = editorFont != null ? editorFont : TMP_Settings.defaultFontAsset;
        ownsRuntimeFont = false;
        BuildInterface();
        ApplyFontToHierarchy();
    }

    private bool TryBindAuthoredInterface()
    {
        dashboardCanvas = GetComponent<Canvas>();
        Transform background = transform.Find("Background");
        Transform overview = background != null ? background.Find("OverviewPage") : null;
        Transform signalGraphs = background != null ? background.Find("SignalGraphPage") : null;
        Transform header = background != null ? background.Find("Header") : null;
        if (dashboardCanvas == null || background == null || overview == null || signalGraphs == null || header == null)
            return false;

        dashboardRoot = background.gameObject;
        overviewPageRoot = overview.gameObject;
        signalGraphPageRoot = signalGraphs.gameObject;
        displayBackdropCamera = FindChildComponent<Camera>(transform, "MonitoringDisplayCamera");
        headerProfile = FindChildComponent<TMP_Text>(header, "Profile");
        headerStatus = FindChildComponent<TMP_Text>(header, "Status");
        overviewPageButton = FindChildComponent<Button>(header, "OverviewTab");
        signalGraphPageButton = FindChildComponent<Button>(header, "SignalGraphTab");
        overviewPageButtonLabel = FindChildComponent<TMP_Text>(header, "OverviewTab/Label");
        signalGraphPageButtonLabel = FindChildComponent<TMP_Text>(header, "SignalGraphTab/Label");
        if (headerProfile == null || headerStatus == null || overviewPageButton == null || signalGraphPageButton == null)
            return false;

        BindPageNavigation();

        nodeViews.Clear();
        string[] nodeKeys = { "Controller", "Product", "R1", "R2", "R3", "R4", "R5" };
        for (int i = 0; i < nodeKeys.Length; i++)
        {
            Transform node = overview.Find("Chain/" + nodeKeys[i]);
            if (node == null)
                return false;
            nodeViews[nodeKeys[i]] = new NodeView
            {
                background = node.GetComponent<Image>(),
                indicator = FindChildComponent<TMP_Text>(node, "Indicator"),
                detail = FindChildComponent<TMP_Text>(node, "Detail")
            };
        }

        metricValues.Clear();
        BindMetric(overview, "EquipmentPanel/ControlColumn/compTarget/Value", "compTarget");
        BindMetric(overview, "EquipmentPanel/ControlColumn/fanTarget/Value", "fanTarget");
        BindMetric(overview, "EquipmentPanel/ControlColumn/mainEev/Value", "mainEev");
        BindMetric(overview, "EquipmentPanel/ControlColumn/setTemp/Value", "setTemp");
        BindMetric(overview, "EquipmentPanel/ControlColumn/coSimTime/Value", "coSimTime");
        BindMetric(overview, "EquipmentPanel/ControlColumn/coSimStep/Value", "coSimStep");
        BindMetric(overview, "EquipmentPanel/ProductColumn/pressureHi/Value", "pressureHi");
        BindMetric(overview, "EquipmentPanel/ProductColumn/pressureLo/Value", "pressureLo");
        BindMetric(overview, "EquipmentPanel/ProductColumn/tempDischarge/Value", "tempDischarge");
        BindMetric(overview, "EquipmentPanel/ProductColumn/tempSuction/Value", "tempSuction");
        BindMetric(overview, "EquipmentPanel/ProductColumn/tempLiquid/Value", "tempLiquid");
        BindMetric(overview, "EquipmentPanel/ProductColumn/tempHex/Value", "tempHex");
        BindMetric(overview, "StatusPanel/LbmMetrics/lbmTime/Value", "lbmTime");
        BindMetric(overview, "StatusPanel/LbmMetrics/lbmStep/Value", "lbmStep");
        BindMetric(overview, "StatusPanel/LbmMetrics/lbmTemp/Value", "lbmTemp");
        BindMetric(overview, "StatusPanel/LbmMetrics/lbmFlow/Value", "lbmFlow");
        BindMetric(overview, "StatusPanel/LbmMetrics/lbmMach/Value", "lbmMach");
        BindMetric(overview, "StatusPanel/LbmMetrics/massResidual/Value", "massResidual");
        if (metricValues.Count != 18)
            return false;

        EnsureRuntimeControlInterface(overview.Find("EquipmentPanel/ControlColumn"));

        indoorRows.Clear();
        for (int room = 1; room <= 5; room++)
        {
            Transform row = overview.Find($"IndoorPanel/IndoorTable/Room{room}");
            if (row == null)
                return false;
            indoorRows[room] = new IndoorRowView
            {
                status = FindChildComponent<TMP_Text>(row, "State"),
                roomTemperature = FindChildComponent<TMP_Text>(row, "RoomTemperature"),
                roomHumidity = FindChildComponent<TMP_Text>(row, "RoomHumidity"),
                dischargeTemperature = FindChildComponent<TMP_Text>(row, "DischargeTemperature"),
                dischargeHumidity = FindChildComponent<TMP_Text>(row, "DischargeHumidity"),
                massFlow = FindChildComponent<TMP_Text>(row, "MassFlow"),
                fanMode = FindChildComponent<TMP_Text>(row, "FanEev")
            };
            indoorRows[room].eevPulse = indoorRows[room].fanMode;
        }
        TMP_Text indoorPanelTitle = FindChildComponent<TMP_Text>(overview, "IndoorPanel/PanelHeader/Title");
        if (indoorPanelTitle != null)
            indoorPanelTitle.text = "실내공간 연동 · R1 전원은 초기조건, R2~R5는 클릭 제어";
        EnsureIndoorPowerControls(overview.Find("IndoorPanel/IndoorTable"));

        Transform temperaturePanel = overview.Find("TemperatureChartPanel");
        Transform pressurePanel = overview.Find("PressureChartPanel");
        temperatureChart = FindChildComponent<CoSimulationMonitorChartGraphic>(temperaturePanel, "Chart");
        pressureChart = FindChildComponent<CoSimulationMonitorChartGraphic>(pressurePanel, "Chart");
        temperatureAxis = BindChartAxes(temperaturePanel, "°C", 1);
        pressureAxis = BindChartAxes(pressurePanel, "kPa", 0);
        footerStatus = FindChildComponent<TMP_Text>(overview, "StatusPanel/FooterStatus");

        signalGraphPanel = signalGraphs.GetComponent<CoSimulationSignalGraphPanel>();
        if (signalGraphPanel == null || temperatureChart == null || pressureChart == null || footerStatus == null)
            return false;
        signalGraphPanel.Initialize(font);
        return true;
    }

    private void BindMetric(Transform root, string path, string key)
    {
        TMP_Text value = FindChildComponent<TMP_Text>(root, path);
        if (value != null)
            metricValues[key] = value;
    }

    private ChartAxisView BindChartAxes(Transform panel, string valueUnit, int valueDecimals)
    {
        if (panel == null)
            return null;
        return new ChartAxisView
        {
            minTime = FindChildComponent<TMP_Text>(panel, "XMin"),
            maxTime = FindChildComponent<TMP_Text>(panel, "XMax"),
            minValue = FindChildComponent<TMP_Text>(panel, "YMin"),
            midValue = FindChildComponent<TMP_Text>(panel, "YMid"),
            maxValue = FindChildComponent<TMP_Text>(panel, "YMax"),
            valueUnit = valueUnit,
            valueDecimals = valueDecimals
        };
    }

    private void ApplyFontToHierarchy()
    {
        if (font == null)
            return;
        TMP_Text[] labels = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++)
            labels[i].font = font;
    }

    private void ScheduleMonitoringLayoutRepair()
    {
        if (overviewPageRoot == null)
            return;

        Transform overview = overviewPageRoot.transform;
        ConfigureHorizontalLayout(overview.Find("Chain"), true);
        ConfigureVerticalLayout(overview.Find("EquipmentPanel/ControlColumn"));
        ConfigureVerticalLayout(overview.Find("EquipmentPanel/ProductColumn"));
        ConfigureVerticalLayout(overview.Find("IndoorPanel/IndoorTable"));
        ConfigureVerticalLayout(overview.Find("StatusPanel/LbmMetrics"));

        if (!Application.isPlaying)
        {
            RebuildMonitoringLayoutNow();
            return;
        }

        if (pendingLayoutRepair == null && isActiveAndEnabled)
            pendingLayoutRepair = StartCoroutine(RebuildMonitoringLayoutNextFrame());
    }

    private IEnumerator RebuildMonitoringLayoutNextFrame()
    {
        yield return null;
        RebuildMonitoringLayoutNow();
        pendingLayoutRepair = null;
    }

    private void RebuildMonitoringLayoutNow()
    {
        if (overviewPageRoot == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(overviewPageRoot.transform as RectTransform);
    }

    private static void ConfigureVerticalLayout(Transform root)
    {
        if (root == null)
            return;

        VerticalLayoutGroup vertical = root.GetComponent<VerticalLayoutGroup>();
        if (vertical != null)
        {
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = true;
        }

        for (int i = 0; i < root.childCount; i++)
            ConfigureHorizontalLayout(root.GetChild(i), false);
    }

    private static void ConfigureHorizontalLayout(Transform root, bool forceExpandWidth)
    {
        if (root == null)
            return;

        HorizontalLayoutGroup horizontal = root.GetComponent<HorizontalLayoutGroup>();
        if (horizontal == null)
            return;

        horizontal.childControlWidth = true;
        horizontal.childControlHeight = true;
        horizontal.childForceExpandWidth = forceExpandWidth;
        horizontal.childForceExpandHeight = true;
    }

    private static T FindChildComponent<T>(Transform root, string path) where T : Component
    {
        if (root == null)
            return null;
        Transform child = root.Find(path);
        return child != null ? child.GetComponent<T>() : null;
    }

#if UNITY_EDITOR
    [ContextMenu("Rebuild Monitoring Hierarchy")]
    public void RebuildMonitoringHierarchy()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        metricValues.Clear();
        nodeViews.Clear();
        indoorRows.Clear();
        dashboardRoot = null;
        overviewPageRoot = null;
        signalGraphPageRoot = null;
        displayBackdropCamera = null;
        temperatureChart = null;
        pressureChart = null;
        signalGraphPanel = null;

        font = monitoringFontAsset != null ? monitoringFontAsset : TMP_Settings.defaultFontAsset;
        ownsRuntimeFont = false;
        BuildInterface();
        ApplyFontToHierarchy();
        UnityEditor.EditorUtility.SetDirty(gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }
#endif

    public void ConfigureDisplay(
        int requestedDisplayIndex,
        bool preferDedicatedDisplay,
        bool fallbackToPrimaryDisplay,
        bool enableVisibilityToggle,
        int requestedRenderWidth = 1920,
        int requestedRenderHeight = 1080)
    {
        monitoringDisplayIndex = Mathf.Clamp(requestedDisplayIndex, 0, 7);
        preferDedicatedMonitoringDisplay = preferDedicatedDisplay;
        this.fallbackToPrimaryDisplay = fallbackToPrimaryDisplay;
        enableF10VisibilityToggle = enableVisibilityToggle;
        monitoringRenderWidth = Mathf.Max(640, requestedRenderWidth);
        monitoringRenderHeight = Mathf.Max(360, requestedRenderHeight);

        if (dashboardCanvas != null)
            ApplyDisplayConfiguration();
    }

    private void ApplyDisplayConfiguration()
    {
        allowVisibilityToggle = enableF10VisibilityToggle;
        int requested = Mathf.Clamp(monitoringDisplayIndex, 0, 7);
        int resolved = 0;
        bool usedPrimaryFallback = false;

#if UNITY_EDITOR
        // The Editor can expose Display 2 through a second Game view even though
        // Display.displays only reports the primary physical monitor.
        if (preferDedicatedMonitoringDisplay)
            resolved = requested;
#else
        if (preferDedicatedMonitoringDisplay && requested < Display.displays.Length)
        {
            resolved = requested;
            if (requested > 0)
                Display.displays[requested].Activate(
                    monitoringRenderWidth,
                    monitoringRenderHeight,
                    Screen.currentResolution.refreshRateRatio);
        }
        else if (!fallbackToPrimaryDisplay)
        {
            resolved = requested;
        }
        else
        {
            usedPrimaryFallback = requested > 0;
        }
#endif

#if !UNITY_EDITOR
        if (resolved == 0)
        {
            Screen.SetResolution(
                monitoringRenderWidth,
                monitoringRenderHeight,
                FullScreenMode.FullScreenWindow);
        }
#endif

        dashboardCanvas.targetDisplay = resolved;
        if (displayBackdropCamera != null)
            displayBackdropCamera.targetDisplay = resolved;

        int renderingWidth = resolved < Display.displays.Length ? Display.displays[resolved].renderingWidth : 0;
        int renderingHeight = resolved < Display.displays.Length ? Display.displays[resolved].renderingHeight : 0;

        string fallback = usedPrimaryFallback ? " (single-display fallback)" : string.Empty;
        string shortcut = allowVisibilityToggle ? " F10 toggles the dashboard." : string.Empty;
        string resolution = renderingWidth > 0 && renderingHeight > 0
            ? $", render={renderingWidth}x{renderingHeight}"
            : string.Empty;
        Debug.Log(
            $"[CoSim Monitor] Dashboard target display={resolved + 1}{resolution}{fallback}, " +
            $"requested={monitoringRenderWidth}x{monitoringRenderHeight}.{shortcut}");
    }

    private void BuildDisplayBackdropCamera()
    {
        GameObject cameraObject = new GameObject("MonitoringDisplayCamera", typeof(Camera));
        cameraObject.transform.SetParent(transform, false);
        displayBackdropCamera = cameraObject.GetComponent<Camera>();
        displayBackdropCamera.clearFlags = CameraClearFlags.SolidColor;
        displayBackdropCamera.backgroundColor = windowColor;
        displayBackdropCamera.cullingMask = 0;
        displayBackdropCamera.depth = -1000.0f;
        displayBackdropCamera.allowHDR = false;
        displayBackdropCamera.allowMSAA = false;
        displayBackdropCamera.useOcclusionCulling = false;
    }

    private TMP_FontAsset CreateRuntimeFontAsset()
    {
        string[] fontFamilies =
        {
            preferredSystemFontFamily,
            "Malgun Gothic",
            "맑은 고딕"
        };

        HashSet<string> attemptedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < fontFamilies.Length; i++)
        {
            string family = fontFamilies[i];
            if (string.IsNullOrWhiteSpace(family) || !attemptedFamilies.Add(family))
                continue;

            try
            {
                // Use the font file directly. Going through the legacy dynamic Font API first can
                // rasterize a system font at a small intermediate size and defeats the benefit of
                // increasing the TMP atlas resolution. SDF8 supersampling keeps 12-15 px labels
                // readable without changing the dashboard's logical resolution or render target.
                TMP_FontAsset runtimeFont = null;
                if (TryGetWindowsFontFile(family, out string fontFilePath))
                {
                    runtimeFont = TMP_FontAsset.CreateFontAsset(
                        fontFilePath,
                        0,
                        sdfSamplingPointSize,
                        sdfAtlasPadding,
                        GlyphRenderMode.SDF8,
                        2048,
                        2048);
                }

                if (runtimeFont == null)
                    runtimeFont = TMP_FontAsset.CreateFontAsset(family, "Regular", sdfSamplingPointSize);
                if (runtimeFont == null)
                    continue;

                runtimeFont.name = $"{family} Runtime SDF";
#if UNITY_EDITOR
                // DontSave includes DontSaveInEditor. Assigning such a font to scene-authored TMP
                // components can trigger a Unity persistence assertion while the Inspector repaints.
                runtimeFont.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontUnloadUnusedAsset;
#else
                runtimeFont.hideFlags = HideFlags.DontSave;
#endif
                runtimeFont.isMultiAtlasTexturesEnabled = true;
                ownsRuntimeFont = true;

                const string dashboardGlyphs =
                    "가나다라마바사아자차카타파하연결대기실패초기화중없음센서동기화실행가능제어제품상태" +
                    "실내공간연동흡입온도추세냉매압력진단물리시간평균출구유량런타임객체찾는에러" +
                    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;+-/()[]<>%°³·●—→";
                if (!runtimeFont.TryAddCharacters(dashboardGlyphs, out string missingCharacters))
                {
                    Debug.LogWarning(
                        $"[CoSim Monitor] Runtime SDF font '{family}' is missing glyphs: {missingCharacters}");
                }

                Texture2D[] atlasTextures = runtimeFont.atlasTextures;
                for (int atlasIndex = 0; atlasIndex < atlasTextures.Length; atlasIndex++)
                {
                    if (atlasTextures[atlasIndex] != null)
                    {
                        atlasTextures[atlasIndex].filterMode = FilterMode.Bilinear;
                        atlasTextures[atlasIndex].wrapMode = TextureWrapMode.Clamp;
                    }
                }

                Debug.Log(
                    $"[CoSim Monitor] Runtime SDF font='{family}', pointSize={sdfSamplingPointSize}, " +
                    $"renderMode={runtimeFont.atlasRenderMode}, atlasMode={runtimeFont.atlasPopulationMode}.");
                return runtimeFont;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[CoSim Monitor] Failed to create runtime SDF font '{family}': {exception.Message}");
            }
        }

        TMP_FontAsset fallbackFont = TMP_Settings.defaultFontAsset;
        if (fallbackFont == null)
            Debug.LogError("[CoSim Monitor] No runtime system font or TMP default font asset is available.");
        else
            Debug.LogWarning("[CoSim Monitor] Malgun Gothic was unavailable. Using the TMP default font asset; Korean glyphs may be missing.");

        return fallbackFont;
    }

    private static bool TryGetWindowsFontFile(string family, out string fontFilePath)
    {
        fontFilePath = null;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        // This project targets Windows 11. Resolving Malgun Gothic's file directly lets TMP use
        // the public high-quality SDF overload instead of the legacy dynamic Font wrapper.
        if (!string.Equals(family, "Malgun Gothic", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(family, "맑은 고딕", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (string.IsNullOrWhiteSpace(fontsDirectory))
            return false;

        string candidate = Path.Combine(fontsDirectory, "malgun.ttf");
        if (!File.Exists(candidate))
            return false;

        fontFilePath = candidate;
        return true;
#else
        return false;
#endif
    }

    private void BuildHeader(RectTransform root)
    {
        RectTransform header = CreateRect(root, "Header", new Vector2(0.0f, 0.925f), Vector2.one, Vector2.zero, Vector2.zero);
        header.gameObject.AddComponent<Image>().color = new Color(0.055f, 0.075f, 0.105f, 1.0f);
        TMP_Text title = CreateText(header, "Title", "MULTI V · LBM MONITOR", 21, FontStyle.Bold,
            new Vector2(0.015f, 0.05f), new Vector2(0.31f, 0.95f), TextAnchor.MiddleLeft, textColor);
        title.overflowMode = TextOverflowModes.Ellipsis;

        overviewPageButton = CreateHeaderTab(header, "OverviewTab", "화면 1 · 통합", new Vector2(0.32f, 0.16f), new Vector2(0.435f, 0.84f),
            out overviewPageButtonLabel);
        signalGraphPageButton = CreateHeaderTab(header, "SignalGraphTab", "화면 2 · 그래프", new Vector2(0.44f, 0.16f), new Vector2(0.555f, 0.84f),
            out signalGraphPageButtonLabel);

        headerProfile = CreateText(header, "Profile", "Profile: waiting", 14, FontStyle.Normal,
            new Vector2(0.565f, 0.08f), new Vector2(0.84f, 0.92f), TextAnchor.MiddleRight, mutedColor);
        headerProfile.overflowMode = TextOverflowModes.Ellipsis;
        headerStatus = CreateText(header, "Status", "● 연결 대기", 16, FontStyle.Bold,
            new Vector2(0.85f, 0.08f), new Vector2(0.985f, 0.92f), TextAnchor.MiddleRight, yellowColor);
        headerStatus.overflowMode = TextOverflowModes.Ellipsis;
    }

    private Button CreateHeaderTab(
        Transform parent,
        string name,
        string label,
        Vector2 min,
        Vector2 max,
        out TMP_Text labelText)
    {
        RectTransform rect = CreateRect(parent, name, min, max, Vector2.zero, Vector2.zero);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = panelColor;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        labelText = CreateText(rect, "Label", label, 12, FontStyle.Bold,
            new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f), TextAnchor.MiddleCenter, textColor);
        labelText.enableAutoSizing = true;
        labelText.fontSizeMin = 9.0f;
        labelText.fontSizeMax = 12.0f;
        return button;
    }

    private void BindPageNavigation()
    {
        if (overviewPageButton == null || signalGraphPageButton == null)
            return;

        overviewPageButton.onClick.RemoveAllListeners();
        signalGraphPageButton.onClick.RemoveAllListeners();
        overviewPageButton.onClick.AddListener(ShowOverviewPage);
        signalGraphPageButton.onClick.AddListener(ShowSignalGraphPage);
        overviewPageButton.interactable = true;
        signalGraphPageButton.interactable = true;

        // The labels are visual only. Keeping them out of the raycast list makes the
        // full tab rectangle the unambiguous click target.
        if (overviewPageButtonLabel != null)
            overviewPageButtonLabel.raycastTarget = false;
        if (signalGraphPageButtonLabel != null)
            signalGraphPageButtonLabel.raycastTarget = false;

        // Both pages fill the canvas. Keep the shared header above them so an authored
        // hierarchy reorder cannot let a page background consume tab clicks.
        if (overviewPageButton.transform.parent != null)
            overviewPageButton.transform.parent.SetAsLastSibling();
    }

    public void ShowOverviewPage()
    {
        SetDashboardPage(false);
    }

    public void ShowSignalGraphPage()
    {
        SetDashboardPage(true);
    }

    private void SetDashboardPage(bool showSignalGraphs)
    {
        bool pageChanged = showingSignalGraphPage != showSignalGraphs;
        showingSignalGraphPage = showSignalGraphs;

        if (overviewPageRoot != null)
            overviewPageRoot.SetActive(!showSignalGraphs);
        if (signalGraphPageRoot != null)
            signalGraphPageRoot.SetActive(showSignalGraphs);

        SetHeaderTabState(overviewPageButton, overviewPageButtonLabel, !showSignalGraphs);
        SetHeaderTabState(signalGraphPageButton, signalGraphPageButtonLabel, showSignalGraphs);

        if (pageChanged && Application.isPlaying)
            Debug.Log($"[CoSim Monitor] Page switched to {(showSignalGraphs ? "custom graphs" : "integrated overview")}.", this);
    }

    private void PollHeaderPageTabs()
    {
        // Unity's EventSystem can miss overlay clicks on an auxiliary Game View/display.
        // This narrow fallback only handles the two shared header tabs; normal Button
        // events remain the primary path for all monitoring controls.
        if (!dashboardVisible || Mouse.current == null || !Mouse.current.leftButton.wasReleasedThisFrame)
            return;

        Vector2 pointerPosition = Mouse.current.position.ReadValue();
        if (ContainsScreenPoint(signalGraphPageButton, pointerPosition))
            ShowSignalGraphPage();
        else if (ContainsScreenPoint(overviewPageButton, pointerPosition))
            ShowOverviewPage();
    }

    private void PollRuntimeControlButtons()
    {
        // The monitoring canvas can be rendered on Display 2, where Unity's
        // EventSystem occasionally reports the pointer against Display 1. Keep a
        // narrow release-based fallback for these four controls as well.
        if (!dashboardVisible || showingSignalGraphPage || Mouse.current == null ||
            !Mouse.current.leftButton.wasReleasedThisFrame ||
            lastRuntimeControlInputFrame == Time.frameCount)
        {
            return;
        }

        Vector2 pointerPosition = Mouse.current.position.ReadValue();
        if (ContainsScreenPoint(setTemperatureDecreaseButton, pointerPosition))
            AdjustRuntimeControls(-0.5f, 0);
        else if (ContainsScreenPoint(setTemperatureIncreaseButton, pointerPosition))
            AdjustRuntimeControls(0.5f, 0);
        else if (ContainsScreenPoint(indoorFanDecreaseButton, pointerPosition))
            AdjustRuntimeControls(0.0f, -1);
        else if (ContainsScreenPoint(indoorFanIncreaseButton, pointerPosition))
            AdjustRuntimeControls(0.0f, 1);
    }

    private void PollRoomPowerButtons()
    {
        if (!dashboardVisible || showingSignalGraphPage || Mouse.current == null ||
            !Mouse.current.leftButton.wasReleasedThisFrame ||
            lastRuntimeControlInputFrame == Time.frameCount)
        {
            return;
        }

        Vector2 pointerPosition = Mouse.current.position.ReadValue();
        for (int room = 2; room <= 5; room++)
        {
            if (indoorRows.TryGetValue(room, out IndoorRowView row) &&
                ContainsScreenPoint(row.powerButton, pointerPosition))
            {
                ToggleRoomPower(room);
                return;
            }
        }
    }

    private void ToggleRoomPower(int room)
    {
        if (lastRuntimeControlInputFrame == Time.frameCount)
            return;
        lastRuntimeControlInputFrame = Time.frameCount;

        ResolveReferences();
        if (orchestrator == null)
            return;

        bool powerOn = !orchestrator.IsRuntimeIndoorUnitPowerOn(room);
        if (!orchestrator.TrySetRuntimeIndoorUnitPower(room, powerOn, out string message))
        {
            string experimentTag = simulationController != null
                ? simulationController.ActiveCaseName
                : orchestrator.ProfileName;
            Debug.LogWarning($"[CoSim Monitor][{experimentTag}] {message}", this);
            return;
        }

        RefreshRoomPowerControls();
    }

    private void AdjustRuntimeControls(float setTemperatureDelta, int indoorFanDelta)
    {
        // Prevent a Display 2 fallback click and the normal Button event from
        // applying the same increment twice in one frame.
        if (lastRuntimeControlInputFrame == Time.frameCount)
            return;
        lastRuntimeControlInputFrame = Time.frameCount;

        ResolveReferences();
        if (orchestrator == null)
            return;

        float setTemperature = Mathf.Clamp(
            orchestrator.RuntimeSetTemperatureDegC + setTemperatureDelta,
            -30.0f,
            60.0f);
        int fanMode = Mathf.Clamp(orchestrator.RuntimeIndoorFanMode + indoorFanDelta, 1, 5);
        if (!orchestrator.TryApplyRuntimeControls(setTemperature, fanMode, out string message))
        {
            string experimentTag = simulationController != null
                ? simulationController.ActiveCaseName
                : orchestrator.ProfileName;
            Debug.LogWarning($"[CoSim Monitor][{experimentTag}] {message}", this);
            return;
        }

        RefreshRuntimeControlMetrics();
    }

    private bool ContainsScreenPoint(Button button, Vector2 screenPoint)
    {
        if (button == null || !button.isActiveAndEnabled || !button.interactable)
            return false;

        Camera eventCamera = dashboardCanvas != null && dashboardCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? dashboardCanvas.worldCamera
            : null;
        return RectTransformUtility.RectangleContainsScreenPoint(
            button.transform as RectTransform,
            screenPoint,
            eventCamera);
    }

    private void SetHeaderTabState(Button button, TMP_Text label, bool selected)
    {
        if (button != null && button.targetGraphic is Image image)
            image.color = selected ? new Color(0.19f, 0.28f, 0.38f, 1.0f) : panelColor;
        if (label != null)
            label.color = selected ? Color.white : mutedColor;
    }

    private void BuildChainStrip(RectTransform root)
    {
        RectTransform strip = CreatePanel(root, "Chain", new Vector2(0.015f, 0.815f), new Vector2(0.985f, 0.915f));
        HorizontalLayoutGroup layout = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 4, 4);
        layout.spacing = 5.0f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        AddNode(strip, "Controller", "CONTROL", "초기화 대기");
        AddArrow(strip, "→");
        AddNode(strip, "Product", "PRODUCT", "초기화 대기");
        AddArrow(strip, "<->");
        AddNode(strip, "R1", "R1 · LBM", "센서 대기");
        AddArrow(strip, "|");
        AddNode(strip, "R2", "R2 · FMU", "초기화 대기");
        AddNode(strip, "R3", "R3 · FMU", "초기화 대기");
        AddNode(strip, "R4", "R4 · FMU", "초기화 대기");
        AddNode(strip, "R5", "R5 · FMU", "초기화 대기");
    }

    private void BuildEquipmentPanel(RectTransform root)
    {
        RectTransform panel = CreatePanel(root, "EquipmentPanel", new Vector2(0.015f, 0.36f), new Vector2(0.405f, 0.805f));
        AddPanelTitle(panel, "제어 · 제품 상태");

        RectTransform left = CreateMetricColumn(panel, "ControlColumn", new Vector2(0.02f, 0.04f), new Vector2(0.49f, 0.88f));
        AddSection(left, "CONTROLLER COMMAND");
        AddMetric(left, "compTarget", "Compressor", "Hz");
        AddMetric(left, "fanTarget", "Outdoor fan", "rpm");
        AddMetric(left, "mainEev", "Main EEV", "pulse");
        AddMetric(left, "setTemp", "Indoor set temp", "°C");
        AddMetric(left, "coSimTime", "Co-sim time", "s");
        AddMetric(left, "coSimStep", "Completed step", "step");

        RectTransform right = CreateMetricColumn(panel, "ProductColumn", new Vector2(0.51f, 0.04f), new Vector2(0.98f, 0.88f));
        AddSection(right, "PRODUCT SENSOR");
        AddMetric(right, "pressureHi", "High pressure", "kPa");
        AddMetric(right, "pressureLo", "Low pressure", "kPa");
        AddMetric(right, "tempDischarge", "Discharge temperature", "°C");
        AddMetric(right, "tempSuction", "Suction temperature", "°C");
        AddMetric(right, "tempLiquid", "Liquid temperature", "°C");
        AddMetric(right, "tempHex", "HEX pipe temperature", "°C");

        EnsureRuntimeControlInterface(left);
    }

    private void EnsureRuntimeControlInterface(Transform controlColumn)
    {
        if (controlColumn == null)
            return;

        Transform setTemperatureRow = controlColumn.Find("setTemp");
        if (setTemperatureRow == null)
            return;

        Transform indoorFanRow = controlColumn.Find("indoorFanMode");
        if (indoorFanRow == null)
        {
            AddMetric((RectTransform)controlColumn, "indoorFanMode", "실내팬", "단");
            indoorFanRow = controlColumn.Find("indoorFanMode");
            if (indoorFanRow != null)
                indoorFanRow.SetSiblingIndex(setTemperatureRow.GetSiblingIndex() + 1);
        }
        else
        {
            TMP_Text fanValue = FindChildComponent<TMP_Text>(indoorFanRow, "Value");
            if (fanValue != null)
                metricValues["indoorFanMode"] = fanValue;
        }

        ConfigureRuntimeControlRow(
            setTemperatureRow,
            "설정온도",
            out setTemperatureDecreaseButton,
            out setTemperatureIncreaseButton);
        ConfigureRuntimeControlRow(
            indoorFanRow,
            "실내팬",
            out indoorFanDecreaseButton,
            out indoorFanIncreaseButton);

        BindRuntimeControlButton(setTemperatureDecreaseButton, () => AdjustRuntimeControls(-0.5f, 0));
        BindRuntimeControlButton(setTemperatureIncreaseButton, () => AdjustRuntimeControls(0.5f, 0));
        BindRuntimeControlButton(indoorFanDecreaseButton, () => AdjustRuntimeControls(0.0f, -1));
        BindRuntimeControlButton(indoorFanIncreaseButton, () => AdjustRuntimeControls(0.0f, 1));
    }

    private void ConfigureRuntimeControlRow(
        Transform row,
        string label,
        out Button decreaseButton,
        out Button increaseButton)
    {
        decreaseButton = null;
        increaseButton = null;
        if (row == null)
            return;

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
            layout.spacing = 2.0f;

        TMP_Text labelText = FindChildComponent<TMP_Text>(row, "Label");
        if (labelText != null)
        {
            labelText.text = label;
            labelText.fontSize = 12.0f;
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = 9.0f;
            labelText.fontSizeMax = 12.0f;
        }

        TMP_Text valueText = FindChildComponent<TMP_Text>(row, "Value");
        LayoutElement valueLayout = valueText != null ? valueText.GetComponent<LayoutElement>() : null;
        if (valueLayout != null)
        {
            valueLayout.preferredWidth = 58.0f;
            valueLayout.minWidth = 50.0f;
        }

        decreaseButton = EnsureRuntimeControlButton(row, "Decrease", "-");
        increaseButton = EnsureRuntimeControlButton(row, "Increase", "+");
    }

    private Button EnsureRuntimeControlButton(Transform row, string name, string label)
    {
        Transform existing = row.Find(name);
        Button button = existing != null ? existing.GetComponent<Button>() : null;
        if (button != null)
            return button;

        RectTransform rect = CreateRect(row, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image image = rect.gameObject.AddComponent<Image>();
        Color normal = new Color(0.16f, 0.23f, 0.31f, 1.0f);
        image.color = normal;
        button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = new Color(0.24f, 0.39f, 0.52f, 1.0f);
        colors.pressedColor = accentColor;
        colors.selectedColor = normal;
        colors.disabledColor = new Color(0.12f, 0.14f, 0.17f, 0.55f);
        button.colors = colors;

        LayoutElement buttonLayout = rect.gameObject.AddComponent<LayoutElement>();
        buttonLayout.preferredWidth = 28.0f;
        buttonLayout.minWidth = 25.0f;
        buttonLayout.flexibleWidth = 0.0f;

        TMP_Text buttonLabel = CreateText(
            rect,
            "Label",
            label,
            15,
            FontStyle.Bold,
            Vector2.zero,
            Vector2.one,
            TextAnchor.MiddleCenter,
            textColor);
        buttonLabel.raycastTarget = false;
        return button;
    }

    private static void BindRuntimeControlButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private void BuildIndoorPanel(RectTransform root)
    {
        RectTransform panel = CreatePanel(root, "IndoorPanel", new Vector2(0.415f, 0.36f), new Vector2(0.985f, 0.805f));
        AddPanelTitle(panel, "실내공간 연동 · R1 전원은 초기조건, R2~R5는 클릭 제어");

        RectTransform table = CreateRect(panel, "IndoorTable", new Vector2(0.015f, 0.04f), new Vector2(0.985f, 0.88f), Vector2.zero, Vector2.zero);
        VerticalLayoutGroup vertical = table.gameObject.AddComponent<VerticalLayoutGroup>();
        vertical.spacing = 4.0f;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = true;

        RectTransform header = AddIndoorRowBase(table, panelHeaderColor);
        header.name = "Header";
        AddCell(header, "Room", 0.10f, TextAnchor.MiddleLeft, true);
        AddCell(header, "State", 0.12f, TextAnchor.MiddleCenter, true);
        AddCell(header, "In °C", 0.13f, TextAnchor.MiddleRight, true);
        AddCell(header, "In RH", 0.13f, TextAnchor.MiddleRight, true);
        AddCell(header, "Out °C", 0.14f, TextAnchor.MiddleRight, true);
        AddCell(header, "Out RH", 0.14f, TextAnchor.MiddleRight, true);
        AddCell(header, "kg/s", 0.12f, TextAnchor.MiddleRight, true);
        AddCell(header, "Fan/EEV", 0.12f, TextAnchor.MiddleRight, true);

        for (int room = 1; room <= 5; room++)
            AddIndoorDataRow(table, room);

        EnsureIndoorPowerControls(table);
    }

    private void EnsureIndoorPowerControls(Transform table)
    {
        if (table == null)
            return;

        Transform header = table.Find("Header");
        if (header != null && header.Find("Power") == null)
        {
            TMP_Text powerHeader = AddCell(
                (RectTransform)header,
                "전원",
                0.10f,
                TextAnchor.MiddleCenter,
                true);
            powerHeader.name = "Power";
            powerHeader.transform.SetSiblingIndex(2);
        }

        for (int room = 1; room <= 5; room++)
        {
            if (!indoorRows.TryGetValue(room, out IndoorRowView view))
                continue;

            Transform row = table.Find($"Room{room}");
            if (row == null)
                continue;

            Button powerButton = EnsureRoomPowerButton(row, out TMP_Text powerLabel);
            view.powerButton = powerButton;
            view.powerLabel = powerLabel;
            if (powerButton == null)
                continue;

            powerButton.transform.SetSiblingIndex(2);
            powerButton.onClick.RemoveAllListeners();
            if (room >= 2)
            {
                int capturedRoom = room;
                powerButton.onClick.AddListener(() => ToggleRoomPower(capturedRoom));
            }
        }
    }

    private Button EnsureRoomPowerButton(Transform row, out TMP_Text label)
    {
        Transform existing = row.Find("PowerControl");
        if (existing != null)
        {
            label = FindChildComponent<TMP_Text>(existing, "Label");
            return existing.GetComponent<Button>();
        }

        RectTransform rect = CreateRect(row, "PowerControl", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = panelHeaderColor;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        LayoutElement buttonLayout = rect.gameObject.AddComponent<LayoutElement>();
        buttonLayout.flexibleWidth = 10.0f;
        buttonLayout.minWidth = 44.0f;

        label = CreateText(
            rect,
            "Label",
            "OFF",
            11,
            FontStyle.Bold,
            Vector2.zero,
            Vector2.one,
            TextAnchor.MiddleCenter,
            mutedColor);
        label.enableAutoSizing = true;
        label.fontSizeMin = 8.0f;
        label.fontSizeMax = 11.0f;
        label.raycastTarget = false;
        return button;
    }

    private void BuildChartPanel(RectTransform root, bool temperature)
    {
        Vector2 min = temperature ? new Vector2(0.015f, 0.045f) : new Vector2(0.49f, 0.045f);
        Vector2 max = temperature ? new Vector2(0.48f, 0.345f) : new Vector2(0.755f, 0.345f);
        RectTransform panel = CreatePanel(root, temperature ? "TemperatureChartPanel" : "PressureChartPanel", min, max);
        AddPanelTitle(panel, temperature ? "실내 흡입온도 추세 · R1~R5" : "냉매 압력 추세", 0.84f, 14);
        CoSimulationMonitorChartGraphic chart = CreateRect(panel, "Chart", new Vector2(0.105f, 0.23f), new Vector2(0.975f, 0.82f), Vector2.zero, Vector2.zero)
            .gameObject.AddComponent<CoSimulationMonitorChartGraphic>();

        int seriesCount = temperature ? 5 : 2;
        chart.ConfigureSeries(seriesCount);
        TMP_Text legend = CreateText(panel, "Legend", string.Empty, 12, FontStyle.Normal,
            new Vector2(0.105f, 0.01f), new Vector2(0.975f, 0.12f), TextAnchor.MiddleLeft, mutedColor);
        legend.richText = true;
        legend.enableAutoSizing = true;
        legend.fontSizeMin = 9.0f;
        legend.fontSizeMax = 12.0f;
        legend.text = temperature
            ? BuildLegend(chart, new[] { "R1", "R2", "R3", "R4", "R5" })
            : BuildLegend(chart, new[] { "High kPa", "Low kPa" });

        ChartAxisView axis = BuildChartAxes(panel, temperature ? "°C" : "kPa", temperature ? 1 : 0);

        if (temperature)
        {
            temperatureChart = chart;
            temperatureAxis = axis;
        }
        else
        {
            pressureChart = chart;
            pressureAxis = axis;
        }
    }

    private ChartAxisView BuildChartAxes(RectTransform panel, string valueUnit, int valueDecimals)
    {
        ChartAxisView axis = new ChartAxisView
        {
            maxValue = CreateAxisText(panel, "YMax", new Vector2(0.005f, 0.70f), new Vector2(0.095f, 0.84f), TextAnchor.MiddleRight),
            midValue = CreateAxisText(panel, "YMid", new Vector2(0.005f, 0.455f), new Vector2(0.095f, 0.595f), TextAnchor.MiddleRight),
            minValue = CreateAxisText(panel, "YMin", new Vector2(0.005f, 0.21f), new Vector2(0.095f, 0.35f), TextAnchor.MiddleRight),
            minTime = CreateAxisText(panel, "XMin", new Vector2(0.105f, 0.12f), new Vector2(0.32f, 0.23f), TextAnchor.MiddleLeft),
            maxTime = CreateAxisText(panel, "XMax", new Vector2(0.76f, 0.12f), new Vector2(0.975f, 0.23f), TextAnchor.MiddleRight),
            valueUnit = valueUnit,
            valueDecimals = valueDecimals
        };

        CreateAxisText(panel, "XAxisTitle", new Vector2(0.38f, 0.12f), new Vector2(0.70f, 0.23f), TextAnchor.MiddleCenter).text = "Time (s)";
        axis.minTime.text = "0 s";
        axis.maxTime.text = "1 s";
        axis.minValue.text = $"0 {valueUnit}";
        axis.midValue.text = $"0.5 {valueUnit}";
        axis.maxValue.text = $"1 {valueUnit}";
        return axis;
    }

    private TMP_Text CreateAxisText(RectTransform panel, string name, Vector2 min, Vector2 max, TextAnchor alignment)
    {
        TMP_Text text = CreateText(panel, name, "—", 10, FontStyle.Normal, min, max, alignment, mutedColor);
        text.enableAutoSizing = true;
        text.fontSizeMin = 8.0f;
        text.fontSizeMax = 10.0f;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    private static void UpdateChartAxes(CoSimulationMonitorChartGraphic chart, ChartAxisView axis)
    {
        if (chart == null || axis == null)
            return;

        chart.GetDisplayRanges(out float minTime, out float maxTime, out float minValue, out float maxValue);
        float midValue = (minValue + maxValue) * 0.5f;
        axis.minTime.text = $"{minTime:F0} s";
        axis.maxTime.text = $"{maxTime:F0} s";
        axis.minValue.text = $"{minValue.ToString("F" + axis.valueDecimals, CultureInfo.InvariantCulture)} {axis.valueUnit}";
        axis.midValue.text = $"{midValue.ToString("F" + axis.valueDecimals, CultureInfo.InvariantCulture)} {axis.valueUnit}";
        axis.maxValue.text = $"{maxValue.ToString("F" + axis.valueDecimals, CultureInfo.InvariantCulture)} {axis.valueUnit}";
    }

    private void BuildStatusPanel(RectTransform root)
    {
        RectTransform panel = CreatePanel(root, "StatusPanel", new Vector2(0.765f, 0.045f), new Vector2(0.985f, 0.345f));
        AddPanelTitle(panel, "LBM · 연동 진단", 0.84f, 14);
        RectTransform metrics = CreateMetricColumn(panel, "LbmMetrics", new Vector2(0.04f, 0.30f), new Vector2(0.96f, 0.82f), 1.0f);
        AddMetric(metrics, "lbmTime", "Physical time", "s", true);
        AddMetric(metrics, "lbmStep", "LBM step", "step", true);
        AddMetric(metrics, "lbmTemp", "Room average", "°C", true);
        AddMetric(metrics, "lbmFlow", "Outlet flow", "m³/s", true);
        AddMetric(metrics, "lbmMach", "Max Mach", string.Empty, true);
        AddMetric(metrics, "massResidual", "Mass residual", string.Empty, true);
        footerStatus = CreateText(panel, "FooterStatus", "런타임 객체 연결 대기", 11, FontStyle.Normal,
            new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.30f), TextAnchor.UpperLeft, mutedColor);
        footerStatus.textWrappingMode = TextWrappingModes.Normal;
        footerStatus.overflowMode = TextOverflowModes.Truncate;
    }

    private void RefreshView()
    {
        bool connected = orchestrator != null && simulationController != null;
        bool failed = orchestrator != null && orchestrator.CoSimFailureObserved;
        headerStatus.text = failed ? "● 연동 실패" : connected ? "● LIVE" : "● 연결 대기";
        headerStatus.color = failed ? redColor : connected ? greenColor : yellowColor;
        headerProfile.text = orchestrator == null
            ? "Profile: -"
            : $"{orchestrator.ProfileName}  |  t={orchestrator.CurrentCoSimTime:F1}s  |  step={orchestrator.CompletedCoSimStepCount}";

        RefreshModelNode("Controller", "Multi_V_S__Set_CFMU");
        RefreshModelNode("Product", "MULTIV_FMU_WARPPER");
        RefreshLbmNode();
        for (int room = 2; room <= 5; room++)
            RefreshModelNode($"R{room}", $"Simple_Chamber_R{room}");

        double comp = Read("Multi_V_S__Set_CFMU", "Multi_V_S.Comp__TarFreq");
        double fan = Read("Multi_V_S__Set_CFMU", "Multi_V_S.Fan1__TarRPM");
        double eev = Read("Multi_V_S__Set_CFMU", "Multi_V_S.MAIN_EEV__TarPulse");
        double pressureHi = Read("MULTIV_FMU_WARPPER", "ODU_Sensor_Pressure_HI");
        double pressureLo = Read("MULTIV_FMU_WARPPER", "ODU_Sensor_Pressure_LO");

        SetMetric("compTarget", comp, "Hz", 1);
        SetMetric("fanTarget", fan, "rpm", 0);
        SetMetric("mainEev", eev, "pulse", 0);
        RefreshRuntimeControlMetrics();
        SetMetric("coSimTime", orchestrator != null ? orchestrator.CurrentCoSimTime : double.NaN, "s", 2);
        SetMetric("coSimStep", orchestrator != null ? orchestrator.CompletedCoSimStepCount : double.NaN, "step", 0);
        SetMetric("pressureHi", pressureHi, "kPa", 1);
        SetMetric("pressureLo", pressureLo, "kPa", 1);
        SetMetric("tempDischarge", Read("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Discharge"), "°C", 1);
        SetMetric("tempSuction", Read("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Suction"), "°C", 1);
        SetMetric("tempLiquid", Read("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Liquid"), "°C", 1);
        SetMetric("tempHex", Read("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_HEXPipe"), "°C", 1);

        float[] roomTemperatures = new float[5];
        RefreshIndoorRows(roomTemperatures);
        RefreshLbmMetrics();
        SampleCharts(roomTemperatures, pressureHi, pressureLo);
        signalGraphPanel?.Refresh(
            orchestrator,
            simulationController != null ? simulationController.SimulatedTimeSeconds : Time.time);

        if (orchestrator == null)
            footerStatus.text = "CoSimulationOrchestrator를 찾는 중입니다.";
        else if (orchestrator.CoSimFailureObserved)
        {
            footerStatus.text = "FAIL: " + orchestrator.LastCoSimFailure;
            footerStatus.color = redColor;
        }
        else
        {
            string pause = simulationController != null && simulationController.IsExternallyPaused ? "FMU step 중 · LBM pause" : "LBM 실행 가능";
            footerStatus.text = $"{pause}\n{orchestrator.RuntimeModeSummary}\n{orchestrator.LastStatus}";
            footerStatus.color = mutedColor;
        }
    }

    private void RefreshRuntimeControlMetrics()
    {
        double setTemperature = orchestrator != null
            ? orchestrator.RuntimeSetTemperatureDegC
            : Read("Multi_V_S__Set_CFMU", "IDU_01.SetTemp");
        double indoorFanMode = orchestrator != null
            ? orchestrator.RuntimeIndoorFanMode
            : Read("Multi_V_S__Set_CFMU", "IDU_01.SetFan");

        SetMetric("setTemp", setTemperature, "°C", 1);
        SetMetric("indoorFanMode", indoorFanMode, "단", 0);

        bool interactable = orchestrator != null;
        if (setTemperatureDecreaseButton != null)
            setTemperatureDecreaseButton.interactable = interactable && setTemperature > -30.0;
        if (setTemperatureIncreaseButton != null)
            setTemperatureIncreaseButton.interactable = interactable && setTemperature < 60.0;
        if (indoorFanDecreaseButton != null)
            indoorFanDecreaseButton.interactable = interactable && indoorFanMode > 1.0;
        if (indoorFanIncreaseButton != null)
            indoorFanIncreaseButton.interactable = interactable && indoorFanMode < 5.0;
    }

    private void RefreshModelNode(string viewKey, string modelId)
    {
        NodeView view = nodeViews[viewKey];
        FmuCoSimulationModel model = FindModel(modelId);
        if (model == null)
        {
            SetNode(view, "●", "모델 없음", mutedColor);
            return;
        }

        if (orchestrator != null && orchestrator.CoSimFailureObserved)
            SetNode(view, "●", "실패", redColor);
        else if (model.NativeFallbackActive)
            SetNode(view, "●", "Mock fallback", yellowColor);
        else if (model.IsInitializationPending)
            SetNode(view, "●", "초기화 중", yellowColor);
        else if (model.IsInitialized)
            SetNode(view, "●", ShortRuntimeMode(model.RuntimeMode), greenColor);
        else
            SetNode(view, "●", "초기화 대기", mutedColor);
    }

    private void RefreshLbmNode()
    {
        NodeView view = nodeViews["R1"];
        if (simulationController == null)
            SetNode(view, "●", "Solver 없음", mutedColor);
        else if (simulationController.ReadinessStatus == SimulationHealthStatus.Invalid)
            SetNode(view, "●", "Readiness invalid", redColor);
        else if (simulationController.IsExternallyPaused)
            SetNode(view, "●", "FMU 동기화 중", yellowColor);
        else if (simulationController.IsSimulationRunning)
            SetNode(view, "●", "LBM running", greenColor);
        else
            SetNode(view, "●", "LBM paused", yellowColor);
    }

    private void RefreshIndoorRows(float[] roomTemperatures)
    {
        RefreshRoomPowerControls();
        SimulationResultMetrics metrics = resultSampler != null ? resultSampler.LatestMetrics : null;
        for (int room = 1; room <= 5; room++)
        {
            IndoorRowView row = indoorRows[room];
            double suctionTemperature;
            double suctionHumidity;
            bool roomHealthy;
            if (room == 1)
            {
                suctionTemperature = metrics != null && metrics.hasValidRoomAverage ? metrics.avgRoomTemperatureDegC : double.NaN;
                suctionHumidity = airflowAdapter != null ? airflowAdapter.LatestRelativeHumidityPercent : double.NaN;
                roomHealthy = simulationController != null && simulationController.ReadinessStatus != SimulationHealthStatus.Invalid;
            }
            else
            {
                suctionTemperature = Read($"Simple_Chamber_R{room}", "T_air_suc");
                suctionHumidity = Read($"Simple_Chamber_R{room}", "RH_air_suc");
                FmuCoSimulationModel chamber = FindModel($"Simple_Chamber_R{room}");
                roomHealthy = chamber != null && chamber.IsInitialized && !chamber.NativeFallbackActive;
            }

            double dischargeTemperature = Read("MULTIV_FMU_WARPPER", $"IDU_{room:00}_Air_Temp_Discharge");
            double dischargeHumidity = Read("MULTIV_FMU_WARPPER", $"IDU_{room:00}_Air_RH_Discharge");
            double massFlow = Read("MULTIV_FMU_WARPPER", $"IDU_{room:00}_Air_mfr_Discharge");
            double fanMode = Read("Multi_V_S__Set_CFMU", $"IDU_{room:00}.CurSetFan");
            double eevPulse = Read("Multi_V_S__Set_CFMU", $"IDU_{room:00}.EEV_TarPulse");

            roomTemperatures[room - 1] = IsFinite(suctionTemperature) ? (float)suctionTemperature : float.NaN;
            bool requestedPowerOn = orchestrator != null && orchestrator.IsRuntimeIndoorUnitPowerOn(room);
            if (orchestrator != null && !requestedPowerOn)
                SetCell(row.status, "● OFF", mutedColor);
            else
                SetCell(row.status, roomHealthy ? "● OK" : "● WAIT", roomHealthy ? greenColor : yellowColor);
            SetCell(row.roomTemperature, Format(suctionTemperature, 2));
            SetCell(row.roomHumidity, Format(suctionHumidity, 1), IsFinite(suctionHumidity) && (suctionHumidity < 0.0 || suctionHumidity > 100.0) ? yellowColor : textColor);
            SetCell(row.dischargeTemperature, Format(dischargeTemperature, 2));
            SetCell(row.dischargeHumidity, Format(dischargeHumidity, 1));
            SetCell(row.massFlow, Format(massFlow, 3));
            SetCell(row.fanMode, $"{Format(fanMode, 0)} / {Format(eevPulse, 0)}");
        }
    }

    private void RefreshRoomPowerControls()
    {
        for (int room = 1; room <= 5; room++)
        {
            if (!indoorRows.TryGetValue(room, out IndoorRowView row) ||
                row.powerButton == null || row.powerLabel == null)
            {
                continue;
            }

            bool powerOn = orchestrator != null && orchestrator.IsRuntimeIndoorUnitPowerOn(room);
            row.powerLabel.text = powerOn ? "ON" : "OFF";
            row.powerLabel.color = powerOn ? Color.white : mutedColor;
            row.powerButton.interactable = orchestrator != null && room >= 2;

            Color normal = powerOn
                ? new Color(0.10f, 0.42f, 0.25f, 1.0f)
                : new Color(0.16f, 0.20f, 0.27f, 1.0f);
            ColorBlock colors = row.powerButton.colors;
            colors.normalColor = normal;
            colors.highlightedColor = powerOn
                ? new Color(0.14f, 0.58f, 0.33f, 1.0f)
                : new Color(0.24f, 0.32f, 0.42f, 1.0f);
            colors.pressedColor = accentColor;
            colors.selectedColor = normal;
            colors.disabledColor = normal;
            row.powerButton.colors = colors;
            if (row.powerButton.targetGraphic != null)
                row.powerButton.targetGraphic.color = normal;
        }
    }

    private void RefreshLbmMetrics()
    {
        SimulationResultMetrics metrics = resultSampler != null ? resultSampler.LatestMetrics : null;
        SetMetric("lbmTime", simulationController != null ? simulationController.SimulatedTimeSeconds : double.NaN, "s", 2);
        SetMetric("lbmStep", simulationController != null ? simulationController.StepCount : double.NaN, "step", 0);
        SetMetric("lbmTemp", metrics != null && metrics.hasValidRoomAverage ? metrics.avgRoomTemperatureDegC : double.NaN, "°C", 2);
        SetMetric("lbmFlow", metrics != null && metrics.hasValidFlowDiagnostic ? metrics.outletFlowRatePhysAbs : double.NaN, "m³/s", 4);
        SetMetric("lbmMach", metrics != null && metrics.hasValidVelocityDiagnostic ? metrics.maxMach : double.NaN, string.Empty, 3);
        SetMetric("massResidual", metrics != null && metrics.hasValidDensityDiagnostic ? metrics.massResidualNormalized : double.NaN, string.Empty, 4);
    }

    private void SampleCharts(float[] roomTemperatures, double pressureHi, double pressureLo)
    {
        float time = simulationController != null ? simulationController.SimulatedTimeSeconds : Time.time;
        bool coSimAdvanced = orchestrator != null && orchestrator.CompletedCoSimStepCount != lastSampledCoSimStep;
        bool fallbackDue = orchestrator == null && Time.unscaledTime >= nextFallbackSampleTime;
        if (!coSimAdvanced && !fallbackDue)
            return;

        if (orchestrator != null)
            lastSampledCoSimStep = orchestrator.CompletedCoSimStepCount;
        nextFallbackSampleTime = Time.unscaledTime + historyFallbackIntervalSeconds;
        temperatureChart.AddSample(time, roomTemperatures);
        pressureChart.AddSample(time,
            IsFinite(pressureHi) ? (float)pressureHi : float.NaN,
            IsFinite(pressureLo) ? (float)pressureLo : float.NaN);
        UpdateChartAxes(temperatureChart, temperatureAxis);
        UpdateChartAxes(pressureChart, pressureAxis);
    }

    private double Read(string modelId, string variableName)
    {
        return orchestrator != null && orchestrator.TryReadRealSignal(modelId, variableName, out double value)
            ? value
            : double.NaN;
    }

    private FmuCoSimulationModel FindModel(string modelId)
    {
        if (orchestrator == null || orchestrator.FmuModels == null)
            return null;
        for (int i = 0; i < orchestrator.FmuModels.Count; i++)
        {
            FmuCoSimulationModel model = orchestrator.FmuModels[i];
            if (model != null && string.Equals(model.ModelId, modelId, StringComparison.Ordinal))
                return model;
        }
        return null;
    }

    private void SetMetric(string key, double value, string unit, int decimals)
    {
        if (!metricValues.TryGetValue(key, out TMP_Text text))
            return;
        text.text = IsFinite(value) ? $"{value.ToString("F" + decimals, CultureInfo.InvariantCulture)} {unit}".TrimEnd() : "—";
        text.color = IsFinite(value) ? textColor : mutedColor;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string Format(double value, int decimals)
    {
        return IsFinite(value) ? value.ToString("F" + decimals, CultureInfo.InvariantCulture) : "—";
    }

    private void SetCell(TMP_Text text, string value, Color? color = null)
    {
        text.text = value;
        text.color = color ?? textColor;
    }

    private void SetNode(NodeView view, string indicator, string detail, Color color)
    {
        view.indicator.text = indicator;
        view.indicator.color = color;
        view.detail.text = detail;
        view.background.color = new Color(color.r, color.g, color.b, 0.09f);
    }

    private static string ShortRuntimeMode(string runtimeMode)
    {
        if (string.IsNullOrWhiteSpace(runtimeMode))
            return "Initialized";
        if (runtimeMode.IndexOf("External", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Native external";
        if (runtimeMode.IndexOf("Native", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Native";
        return runtimeMode.Length <= 20 ? runtimeMode : runtimeMode.Substring(0, 20);
    }

    private void AddNode(RectTransform parent, string key, string title, string detail)
    {
        RectTransform node = CreateRect(parent, key, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image image = node.gameObject.AddComponent<Image>();
        image.color = new Color(0.3f, 0.35f, 0.42f, 0.09f);
        LayoutElement element = node.gameObject.AddComponent<LayoutElement>();
        element.flexibleWidth = 1.0f;
        element.minWidth = 0.0f;
        element.preferredWidth = 105.0f;
        TMP_Text indicator = CreateText(node, "Indicator", "●", 18, FontStyle.Bold,
            new Vector2(0.04f, 0.1f), new Vector2(0.22f, 0.9f), TextAnchor.MiddleCenter, mutedColor);
        TMP_Text titleText = CreateText(node, "Title", title, 14, FontStyle.Bold,
            new Vector2(0.22f, 0.48f), new Vector2(0.97f, 0.9f), TextAnchor.MiddleLeft, textColor);
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 10.0f;
        titleText.fontSizeMax = 14.0f;
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        TMP_Text detailText = CreateText(node, "Detail", detail, 12, FontStyle.Normal,
            new Vector2(0.22f, 0.08f), new Vector2(0.97f, 0.5f), TextAnchor.MiddleLeft, mutedColor);
        detailText.enableAutoSizing = true;
        detailText.fontSizeMin = 9.0f;
        detailText.fontSizeMax = 12.0f;
        detailText.overflowMode = TextOverflowModes.Ellipsis;
        nodeViews[key] = new NodeView { background = image, indicator = indicator, detail = detailText };
    }

    private void AddArrow(RectTransform parent, string arrow)
    {
        TMP_Text text = CreateText(parent, "Arrow", arrow, 18, FontStyle.Bold, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, accentColor);
        LayoutElement element = text.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = 18.0f;
        element.flexibleWidth = 0.0f;
    }

    private RectTransform CreateMetricColumn(RectTransform parent, string name, Vector2 min, Vector2 max, float spacing = 3.0f)
    {
        RectTransform column = CreateRect(parent, name, min, max, Vector2.zero, Vector2.zero);
        VerticalLayoutGroup layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        return column;
    }

    private void AddSection(RectTransform parent, string title)
    {
        TMP_Text text = CreateText(parent, "Section", title, 14, FontStyle.Bold, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, accentColor);
        text.gameObject.AddComponent<LayoutElement>().preferredHeight = 25.0f;
    }

    private void AddMetric(RectTransform parent, string key, string label, string unit, bool compact = false)
    {
        RectTransform row = CreateRect(parent, key, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        row.gameObject.AddComponent<Image>().color = new Color(1.0f, 1.0f, 1.0f, 0.025f);
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = compact ? new RectOffset(5, 5, 0, 0) : new RectOffset(8, 8, 0, 0);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        int labelSize = compact ? 10 : 14;
        int valueSize = compact ? 11 : 15;
        TMP_Text labelText = CreateText(row, "Label", label, labelSize, FontStyle.Normal, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, mutedColor);
        labelText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1.0f;
        labelText.overflowMode = TextOverflowModes.Ellipsis;
        TMP_Text valueText = CreateText(row, "Value", "— " + unit, valueSize, FontStyle.Bold, Vector2.zero, Vector2.one, TextAnchor.MiddleRight, textColor);
        LayoutElement valueLayout = valueText.gameObject.AddComponent<LayoutElement>();
        valueLayout.preferredWidth = compact ? 68.0f : 82.0f;
        valueLayout.minWidth = compact ? 58.0f : 72.0f;
        valueText.overflowMode = TextOverflowModes.Ellipsis;
        if (compact)
        {
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = 8.0f;
            labelText.fontSizeMax = labelSize;
            valueText.enableAutoSizing = true;
            valueText.fontSizeMin = 8.0f;
            valueText.fontSizeMax = valueSize;
        }
        metricValues[key] = valueText;
    }

    private void AddIndoorDataRow(RectTransform table, int room)
    {
        RectTransform row = AddIndoorRowBase(table, room % 2 == 0
            ? new Color(1.0f, 1.0f, 1.0f, 0.035f)
            : new Color(1.0f, 1.0f, 1.0f, 0.015f));
        row.name = $"Room{room}";
        AddCell(row, room == 1 ? "R1 · LBM" : $"R{room} · FMU", 0.10f, TextAnchor.MiddleLeft, true);
        IndoorRowView view = new IndoorRowView
        {
            status = AddCell(row, "● WAIT", 0.12f, TextAnchor.MiddleCenter),
            roomTemperature = AddCell(row, "—", 0.13f, TextAnchor.MiddleRight),
            roomHumidity = AddCell(row, "—", 0.13f, TextAnchor.MiddleRight),
            dischargeTemperature = AddCell(row, "—", 0.14f, TextAnchor.MiddleRight),
            dischargeHumidity = AddCell(row, "—", 0.14f, TextAnchor.MiddleRight),
            massFlow = AddCell(row, "—", 0.12f, TextAnchor.MiddleRight),
            fanMode = AddCell(row, "— / —", 0.12f, TextAnchor.MiddleRight)
        };
        view.status.name = "State";
        view.roomTemperature.name = "RoomTemperature";
        view.roomHumidity.name = "RoomHumidity";
        view.dischargeTemperature.name = "DischargeTemperature";
        view.dischargeHumidity.name = "DischargeHumidity";
        view.massFlow.name = "MassFlow";
        view.fanMode.name = "FanEev";
        view.eevPulse = view.fanMode;
        indoorRows[room] = view;
    }

    private RectTransform AddIndoorRowBase(RectTransform parent, Color color)
    {
        RectTransform row = CreateRect(parent, "Row", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        row.gameObject.AddComponent<Image>().color = color;
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(7, 7, 0, 0);
        layout.spacing = 2.0f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childAlignment = TextAnchor.MiddleCenter;
        return row;
    }

    private TMP_Text AddCell(RectTransform row, string value, float ratio, TextAnchor alignment, bool bold = false)
    {
        TMP_Text text = CreateText(row, "Cell", value, 13, bold ? FontStyle.Bold : FontStyle.Normal,
            Vector2.zero, Vector2.one, alignment, bold ? mutedColor : textColor);
        LayoutElement layout = text.gameObject.AddComponent<LayoutElement>();
        layout.flexibleWidth = ratio * 100.0f;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private void AddPanelTitle(RectTransform panel, string title, float headerMinY = 0.88f, int fontSize = 16)
    {
        RectTransform header = CreateRect(panel, "PanelHeader", new Vector2(0.0f, headerMinY), Vector2.one, Vector2.zero, Vector2.zero);
        header.gameObject.AddComponent<Image>().color = panelHeaderColor;
        TMP_Text titleText = CreateText(header, "Title", title, fontSize, FontStyle.Bold,
            new Vector2(0.025f, 0.0f), new Vector2(0.98f, 1.0f), TextAnchor.MiddleLeft, textColor);
        titleText.overflowMode = TextOverflowModes.Ellipsis;
    }

    private RectTransform CreatePanel(RectTransform parent, string name, Vector2 min, Vector2 max)
    {
        RectTransform panel = CreateRect(parent, name, min, max, Vector2.zero, Vector2.zero);
        panel.gameObject.AddComponent<Image>().color = panelColor;
        return panel;
    }

    private TMP_Text CreateText(
        Transform parent,
        string name,
        string value,
        int size,
        FontStyle style,
        Vector2 min,
        Vector2 max,
        TextAnchor alignment,
        Color color)
    {
        RectTransform rect = CreateRect(parent, name, min, max, Vector2.zero, Vector2.zero);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = ConvertFontStyle(style);
        text.alignment = ConvertAlignment(alignment);
        text.color = color;
        text.raycastTarget = false;
        text.extraPadding = true;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Truncate;
        return text;
    }

    private static FontStyles ConvertFontStyle(FontStyle style)
    {
        switch (style)
        {
            case FontStyle.Bold:
                return FontStyles.Bold;
            case FontStyle.Italic:
                return FontStyles.Italic;
            case FontStyle.BoldAndItalic:
                return FontStyles.Bold | FontStyles.Italic;
            default:
                return FontStyles.Normal;
        }
    }

    private static TextAlignmentOptions ConvertAlignment(TextAnchor alignment)
    {
        switch (alignment)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.Midline;
        }
    }

    private static RectTransform CreateRect(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static string BuildLegend(CoSimulationMonitorChartGraphic chart, string[] labels)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < labels.Length; i++)
        {
            if (i > 0)
                builder.Append("    ");
            builder.Append("<color=#")
                .Append(ColorUtility.ToHtmlStringRGB(chart.GetSeriesColor(i)))
                .Append(">●</color> ")
                .Append(labels[i]);
        }
        return builder.ToString();
    }
}
