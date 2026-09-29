using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Monitoring page for composing up to three FMU signal graphs.
/// The hierarchy can be stored in the scene and edited with standard Unity UI components.
/// It only reads the orchestrator's diagnostic signal API and never changes FMU inputs.
/// </summary>
public sealed class CoSimulationSignalGraphPanel : MonoBehaviour
{
    private const int GraphCount = 3;

    [Serializable]
    public sealed class SignalDefinition
    {
        public string id;
        public string category;
        public string title;
        public string modelId;
        public string variableName;
        public string unit;
        public int decimals;
        public Color color;
    }

    private sealed class SignalRow
    {
        public Toggle toggle;
        public TMP_Text trace;
    }

    private sealed class GraphView
    {
        public GameObject root;
        public CoSimulationMonitorChartGraphic chart;
        public TMP_Text title;
        public TMP_Text legend;
        public TMP_Text minTime;
        public TMP_Text maxTime;
        public TMP_Text minValue;
        public TMP_Text midValue;
        public TMP_Text maxValue;
        public TMP_Text axisUnit;
    }

    [Header("Signal Catalog")]
    [Tooltip("FMU/LBM signals exposed by the graph, including labels, units, and colors.")]
    [SerializeField] private List<SignalDefinition> signals = new List<SignalDefinition>();

    [Header("Default Graph Selections")]
    [SerializeField] private List<string> firstGraphSignalIds = new List<string>
    {
        "pressure.high", "pressure.low"
    };
    [SerializeField] private List<string> secondGraphSignalIds = new List<string>
    {
        "comp.target", "comp.current", "fan.target", "fan.current", "eev.target", "eev.current"
    };
    [SerializeField] private List<string> thirdGraphSignalIds = new List<string>
    {
        "temp.discharge", "temp.suction", "temp.liquid", "temp.hex"
    };

    private readonly List<SignalRow> rows = new List<SignalRow>();
    private readonly List<RaycastResult> eventSystemHits = new List<RaycastResult>();
    private readonly GraphView[] graphs = new GraphView[GraphCount];
    private readonly Button[] graphTabButtons = new Button[GraphCount];
    private readonly TMP_Text[] graphTabLabels = new TMP_Text[GraphCount];
    private Button resetSelectionButton;
    private Button clearHistoryButton;
    private ScrollRect signalScrollRect;
    private Scrollbar signalScrollbar;
    private RectTransform signalViewport;
    private RectTransform signalContent;
    private bool[,] selected;
    private double[] currentValues;

    private TMP_FontAsset font;
    private TMP_Text selectionSummary;
    private TMP_Text statusText;
    private int activeGraphIndex;
    private ulong lastSampledCoSimStep = ulong.MaxValue;
    private float nextFallbackSampleTime;
    private bool hasSampledValidSignal;
    private bool manualScrollbarDrag;
    private CoSimulationOrchestrator lastOrchestrator;
    private bool initialized;
    private Coroutine pendingLayoutRepair;
    private bool resetScrollOnPendingLayoutRepair;

    public int SignalCount
    {
        get
        {
            EnsureDefaultSignalCatalog();
            return signals.Count;
        }
    }

    [Header("Theme")]
    [SerializeField] private Color panelColor = new Color(0.075f, 0.094f, 0.125f, 0.98f);
    [SerializeField] private Color panelHeaderColor = new Color(0.105f, 0.133f, 0.176f, 1.0f);
    [SerializeField] private Color textColor = new Color(0.91f, 0.94f, 0.97f, 1.0f);
    [SerializeField] private Color mutedColor = new Color(0.61f, 0.68f, 0.76f, 1.0f);
    [SerializeField] private Color accentColor = new Color(0.20f, 0.70f, 0.95f, 1.0f);
    [SerializeField] private Color selectedTabColor = new Color(0.19f, 0.28f, 0.38f, 1.0f);
    [SerializeField] private Color normalTabColor = new Color(0.10f, 0.13f, 0.18f, 1.0f);

    public void Initialize(TMP_FontAsset dashboardFont)
    {
        if (initialized)
            return;

        font = dashboardFont != null ? dashboardFont : TMP_Settings.defaultFontAsset;
        EnsureDefaultSignalCatalog();
        selected = new bool[GraphCount, signals.Count];
        currentValues = new double[signals.Count];
        for (int i = 0; i < currentValues.Length; i++)
            currentValues[i] = double.NaN;

        if (transform.Find("GraphTabs") == null)
            BuildInterface();
        else if (!TryBindAuthoredInterface())
        {
            Debug.LogError(
                "[CoSim Monitor] 저장된 Signal Graph 계층 또는 Signal Catalog가 일치하지 않습니다. " +
                "SignalGraphPage Inspector의 'Rebuild Signal Graph Hierarchy'를 실행하세요.",
                this);
            enabled = false;
            return;
        }

        ApplyFontToHierarchy();
        ApplyDefaultSelection(0);
        ApplyDefaultSelection(1);
        ApplyDefaultSelection(2);
        SelectGraph(0);
        RepairSignalSelectorLayout();
        initialized = true;
    }

    private void Update()
    {
        if (!initialized)
            return;

        PollAuxiliaryDisplayInput();
    }

    private void OnEnable()
    {
        if (initialized)
            RepairSignalSelectorLayout(false);
    }

    private void OnDisable()
    {
        if (pendingLayoutRepair == null)
            return;

        StopCoroutine(pendingLayoutRepair);
        pendingLayoutRepair = null;
    }

    public void Refresh(CoSimulationOrchestrator orchestrator, float simulationTimeSeconds)
    {
        if (!initialized)
            return;

        if (orchestrator != lastOrchestrator)
        {
            lastOrchestrator = orchestrator;
            lastSampledCoSimStep = ulong.MaxValue;
            hasSampledValidSignal = false;
        }

        bool coSimAdvanced = orchestrator != null && orchestrator.CompletedCoSimStepCount != lastSampledCoSimStep;
        bool fallbackDue = orchestrator == null && Time.unscaledTime >= nextFallbackSampleTime;
        bool initializationRetryDue = orchestrator != null && !hasSampledValidSignal && Time.unscaledTime >= nextFallbackSampleTime;
        if (coSimAdvanced || fallbackDue || initializationRetryDue)
        {
            // Sample only at a completed co-simulation boundary. This avoids displaying a
            // partially transferred Controller -> Product state and keeps FMU reads inexpensive.
            bool anyValid = false;
            for (int i = 0; i < signals.Count; i++)
            {
                SignalDefinition signal = signals[i];
                currentValues[i] = orchestrator != null &&
                                   orchestrator.TryReadRealSignal(signal.modelId, signal.variableName, out double value)
                    ? value
                    : double.NaN;

                SignalRow row = rows[i];
                row.trace.text = Format(currentValues[i], signal.decimals, signal.unit);
                row.trace.color = IsFinite(currentValues[i]) ? textColor : mutedColor;
                anyValid |= IsFinite(currentValues[i]);
            }
            hasSampledValidSignal |= anyValid;

            float sampleTime = orchestrator != null
                ? (float)orchestrator.CurrentCoSimTime
                : simulationTimeSeconds;
            float[] samples = new float[currentValues.Length];
            for (int i = 0; i < currentValues.Length; i++)
                samples[i] = IsFinite(currentValues[i]) ? (float)currentValues[i] : float.NaN;

            for (int graph = 0; graph < GraphCount; graph++)
                graphs[graph].chart.AddSample(sampleTime, samples);

            if (orchestrator != null)
                lastSampledCoSimStep = orchestrator.CompletedCoSimStepCount;
            nextFallbackSampleTime = Time.unscaledTime + 1.0f;
        }

        for (int graph = 0; graph < GraphCount; graph++)
            UpdateAxes(graphs[graph], GetAxisUnit(graph));

        if (orchestrator == null)
        {
            statusText.text = "CoSimulationOrchestrator 연결 대기";
            statusText.color = mutedColor;
        }
        else if (orchestrator.CoSimFailureObserved)
        {
            statusText.text = "FMU 오류 · 마지막 정상 trace를 확인하세요";
            statusText.color = new Color(1.00f, 0.34f, 0.39f, 1.0f);
        }
        else
        {
            statusText.text = $"LIVE · t={orchestrator.CurrentCoSimTime:F1}s · step={orchestrator.CompletedCoSimStepCount}";
            statusText.color = new Color(0.27f, 0.86f, 0.50f, 1.0f);
        }
    }

    private void EnsureDefaultSignalCatalog()
    {
        if (signals == null)
            signals = new List<SignalDefinition>();
        if (signals.Count > 0)
            return;

        Color[] palette =
        {
            new Color(0.96f, 0.31f, 0.55f), new Color(0.29f, 0.72f, 0.91f),
            new Color(0.31f, 0.84f, 0.49f), new Color(0.96f, 0.39f, 0.28f),
            new Color(0.72f, 0.48f, 0.95f), new Color(0.98f, 0.72f, 0.23f),
            new Color(0.17f, 0.82f, 0.77f), new Color(0.98f, 0.52f, 0.20f),
            new Color(0.55f, 0.72f, 0.26f), new Color(0.42f, 0.54f, 0.94f),
            new Color(0.91f, 0.42f, 0.75f), new Color(0.75f, 0.76f, 0.80f)
        };
        int colorIndex = 0;

        AddSignal("comp.target", "A", "압축기 목표 주파수", "Multi_V_S__Set_CFMU", "Multi_V_S.Comp__TarFreq", "Hz", 1, palette[colorIndex++ % palette.Length]);
        AddSignal("comp.current", "A", "압축기 운전 주파수", "MULTIV_FMU_WARPPER", "Comp_CurFreq", "Hz", 1, palette[colorIndex++ % palette.Length]);
        AddSignal("fan.target", "A", "실외팬 목표 RPM", "Multi_V_S__Set_CFMU", "Multi_V_S.Fan1__TarRPM", "rpm", 0, palette[colorIndex++ % palette.Length]);
        AddSignal("fan.current", "A", "실외팬 운전 RPM", "MULTIV_FMU_WARPPER", "Fan_CurRPM", "rpm", 0, palette[colorIndex++ % palette.Length]);
        AddSignal("eev.target", "A", "메인 EEV 목표", "Multi_V_S__Set_CFMU", "Multi_V_S.MAIN_EEV__TarPulse", "pulse", 0, palette[colorIndex++ % palette.Length]);
        AddSignal("eev.current", "A", "메인 EEV 운전", "MULTIV_FMU_WARPPER", "MAIN_EEV_CurPulse", "pulse", 0, palette[colorIndex++ % palette.Length]);

        for (int room = 1; room <= 5; room++)
        {
            AddSignal($"idu{room}.fan", "A", $"R{room} 실내팬 모드", "Multi_V_S__Set_CFMU", $"IDU_{room:00}.CurSetFan", string.Empty, 0, palette[colorIndex++ % palette.Length]);
            AddSignal($"idu{room}.eev", "A", $"R{room} EEV 목표", "Multi_V_S__Set_CFMU", $"IDU_{room:00}.EEV_TarPulse", "pulse", 0, palette[colorIndex++ % palette.Length]);
        }

        AddSignal("pressure.high", "S", "고압 센서", "MULTIV_FMU_WARPPER", "ODU_Sensor_Pressure_HI", "kPa", 1, palette[colorIndex++ % palette.Length]);
        AddSignal("pressure.low", "S", "저압 센서", "MULTIV_FMU_WARPPER", "ODU_Sensor_Pressure_LO", "kPa", 1, palette[colorIndex++ % palette.Length]);
        AddSignal("temp.discharge", "S", "토출 온도", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Discharge", "°C", 1, palette[colorIndex++ % palette.Length]);
        AddSignal("temp.suction", "S", "흡입 온도", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Suction", "°C", 1, palette[colorIndex++ % palette.Length]);
        AddSignal("temp.liquid", "S", "액관 온도", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Liquid", "°C", 1, palette[colorIndex++ % palette.Length]);
        AddSignal("temp.hex", "S", "열교환기 배관 온도", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_HEXPipe", "°C", 1, palette[colorIndex++ % palette.Length]);
        AddSignal("temp.sc.in", "S", "과냉각기 입구 온도", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_SC_In", "°C", 1, palette[colorIndex++ % palette.Length]);
        AddSignal("temp.sc.out", "S", "과냉각기 출구 온도", "MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_SC_Out", "°C", 1, palette[colorIndex++ % palette.Length]);

        AddSignal("idu1.suction", "S", "R1 흡입 온도 (LBM)", "airflow", "T_sensor", "°C", 2, palette[colorIndex++ % palette.Length]);
        for (int room = 2; room <= 5; room++)
            AddSignal($"idu{room}.suction", "S", $"R{room} 흡입 온도", $"Simple_Chamber_R{room}", "T_air_suc", "°C", 2, palette[colorIndex++ % palette.Length]);
        for (int room = 1; room <= 5; room++)
            AddSignal($"idu{room}.discharge", "S", $"R{room} 토출 온도", "MULTIV_FMU_WARPPER", $"IDU_{room:00}_Air_Temp_Discharge", "°C", 2, palette[colorIndex++ % palette.Length]);
    }

    private void AddSignal(
        string id,
        string category,
        string title,
        string modelId,
        string variableName,
        string unit,
        int decimals,
        Color color)
    {
        signals.Add(new SignalDefinition
        {
            id = id,
            category = category,
            title = title,
            modelId = modelId,
            variableName = variableName,
            unit = unit,
            decimals = decimals,
            color = color
        });
    }

    private void BuildInterface()
    {
        RectTransform tabStrip = CreatePanel(transform, "GraphTabs", new Vector2(0.015f, 0.84f), new Vector2(0.73f, 0.915f));
        HorizontalLayoutGroup tabs = tabStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabs.padding = new RectOffset(8, 8, 6, 6);
        tabs.spacing = 6.0f;
        tabs.childControlWidth = true;
        tabs.childForceExpandWidth = true;
        string[] labels = { "1st Graph · 압력", "2nd Graph · Actuator", "3rd Graph · 온도" };
        for (int graph = 0; graph < GraphCount; graph++)
        {
            int captured = graph;
            graphTabButtons[graph] = CreateButton(tabStrip, $"GraphTab{graph + 1}", labels[graph], 14, () => SelectGraph(captured), out graphTabLabels[graph]);
        }

        RectTransform graphPanel = CreatePanel(transform, "GraphPanel", new Vector2(0.015f, 0.045f), new Vector2(0.73f, 0.83f));
        for (int graph = 0; graph < GraphCount; graph++)
            graphs[graph] = BuildGraph(graphPanel, graph);

        BuildSignalSelector();
    }

    private bool TryBindAuthoredInterface()
    {
        Transform tabStrip = transform.Find("GraphTabs");
        Transform graphPanel = transform.Find("GraphPanel");
        Transform selector = transform.Find("SignalSelector");
        signalViewport = selector != null ? selector.Find("Viewport") as RectTransform : null;
        signalContent = signalViewport != null ? signalViewport.Find("Content") as RectTransform : null;
        if (tabStrip == null || graphPanel == null || selector == null || signalContent == null)
            return false;

        signalScrollRect = selector.GetComponent<ScrollRect>();
        signalScrollbar = signalScrollRect != null ? signalScrollRect.verticalScrollbar : null;

        for (int graph = 0; graph < GraphCount; graph++)
        {
            int captured = graph;
            Transform tab = tabStrip.Find($"GraphTab{graph + 1}");
            Transform graphRoot = graphPanel.Find($"Graph{graph + 1}");
            if (tab == null || graphRoot == null)
                return false;

            graphTabButtons[graph] = tab.GetComponent<Button>();
            graphTabLabels[graph] = FindChildComponent<TMP_Text>(tab, "Label");
            graphs[graph] = new GraphView
            {
                root = graphRoot.gameObject,
                chart = FindChildComponent<CoSimulationMonitorChartGraphic>(graphRoot, "Chart"),
                title = FindChildComponent<TMP_Text>(graphRoot, "Title"),
                legend = FindChildComponent<TMP_Text>(graphRoot, "Legend"),
                minTime = FindChildComponent<TMP_Text>(graphRoot, "XMin"),
                maxTime = FindChildComponent<TMP_Text>(graphRoot, "XMax"),
                minValue = FindChildComponent<TMP_Text>(graphRoot, "YMin"),
                midValue = FindChildComponent<TMP_Text>(graphRoot, "YMid"),
                maxValue = FindChildComponent<TMP_Text>(graphRoot, "YMax"),
                axisUnit = FindChildComponent<TMP_Text>(graphRoot, "AxisUnit")
            };

            if (graphTabButtons[graph] == null || graphTabLabels[graph] == null || !IsComplete(graphs[graph]))
                return false;
            graphTabButtons[graph].onClick.AddListener(() => SelectGraph(captured));
            graphs[graph].chart.ConfigureSeries(signals.Count);
            for (int signal = 0; signal < signals.Count; signal++)
            {
                graphs[graph].chart.SetSeriesColor(signal, signals[signal].color);
                graphs[graph].chart.SetSeriesVisible(signal, false);
            }
        }

        rows.Clear();
        for (int signal = 0; signal < signals.Count; signal++)
        {
            int captured = signal;
            Transform row = signalContent.Find(signals[signal].id);
            Toggle toggle = row != null ? row.GetComponent<Toggle>() : null;
            TMP_Text trace = FindChildComponent<TMP_Text>(row, "Trace");
            if (toggle == null || trace == null)
                return false;
            toggle.onValueChanged.AddListener(value => SetSignalSelected(captured, value));
            rows.Add(new SignalRow { toggle = toggle, trace = trace });
        }

        selectionSummary = FindChildComponent<TMP_Text>(selector, "SelectionSummary");
        statusText = FindChildComponent<TMP_Text>(selector, "Status");
        resetSelectionButton = FindChildComponent<Button>(selector, "ResetSelection");
        clearHistoryButton = FindChildComponent<Button>(selector, "ClearHistory");
        if (selectionSummary == null || statusText == null || resetSelectionButton == null || clearHistoryButton == null)
            return false;
        resetSelectionButton.onClick.AddListener(ResetCurrentSelection);
        clearHistoryButton.onClick.AddListener(ClearAllHistory);
        return true;
    }

    private static bool IsComplete(GraphView graph)
    {
        return graph != null && graph.root != null && graph.chart != null && graph.title != null &&
               graph.legend != null && graph.minTime != null && graph.maxTime != null &&
               graph.minValue != null && graph.midValue != null && graph.maxValue != null &&
               graph.axisUnit != null;
    }

    private void ApplyFontToHierarchy()
    {
        if (font == null)
            return;
        TMP_Text[] labels = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++)
            labels[i].font = font;
    }

    private void RepairSignalSelectorLayout(bool resetScrollPosition = true)
    {
        if (signalContent == null)
            signalContent = transform.Find("SignalSelector/Viewport/Content") as RectTransform;
        if (signalViewport == null)
            signalViewport = transform.Find("SignalSelector/Viewport") as RectTransform;
        if (signalScrollRect == null)
            signalScrollRect = transform.Find("SignalSelector")?.GetComponent<ScrollRect>();
        if (signalScrollbar == null && signalScrollRect != null)
            signalScrollbar = signalScrollRect.verticalScrollbar;
        if (signalContent == null)
            return;

        VerticalLayoutGroup layout = signalContent.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
        {
            // Authored scenes created with the previous defaults left this false,
            // collapsing every signal row to a width of zero.
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        if (Application.isPlaying)
        {
            resetScrollOnPendingLayoutRepair |= resetScrollPosition;
            if (pendingLayoutRepair == null && isActiveAndEnabled)
                pendingLayoutRepair = StartCoroutine(RebuildSignalSelectorLayoutNextFrame());
            return;
        }

        RebuildSignalSelectorLayoutNow(resetScrollPosition);
    }

    private IEnumerator RebuildSignalSelectorLayoutNextFrame()
    {
        yield return null;
        bool resetScrollPosition = resetScrollOnPendingLayoutRepair;
        resetScrollOnPendingLayoutRepair = false;
        RebuildSignalSelectorLayoutNow(resetScrollPosition);
        pendingLayoutRepair = null;
    }

    private void RebuildSignalSelectorLayoutNow(bool resetScrollPosition)
    {
        if (signalContent == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(signalContent);
        if (resetScrollPosition && signalScrollRect != null)
        {
            signalScrollRect.StopMovement();
            signalScrollRect.verticalNormalizedPosition = 1.0f;
        }
    }

    private void PollAuxiliaryDisplayInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        Vector2 pointerPosition = mouse.position.ReadValue();

        // These actions are idempotent, so handle them directly as well as through
        // their Button listeners. This guarantees the authored buttons work on
        // Display 2 even when the shared EventSystem is still reporting Display 1.
        if (mouse.leftButton.wasReleasedThisFrame)
        {
            for (int graph = 0; graph < graphTabButtons.Length; graph++)
            {
                if (!ContainsScreenPoint(graphTabButtons[graph], pointerPosition))
                    continue;
                SelectGraph(graph);
                return;
            }

            if (ContainsScreenPoint(resetSelectionButton, pointerPosition))
            {
                ResetCurrentSelection();
                return;
            }

            if (ContainsScreenPoint(clearHistoryButton, pointerPosition))
            {
                ClearAllHistory();
                return;
            }
        }

        // When the normal EventSystem can raycast this panel, let Unity UI own the
        // event. On the auxiliary monitoring display it currently returns no panel
        // hit, so the narrow fallback below handles only these existing controls.
        if (CanEventSystemReachPanel(pointerPosition))
        {
            manualScrollbarDrag = false;
            return;
        }

        Vector2 scroll = mouse.scroll.ReadValue();
        if (signalScrollRect != null && signalViewport != null &&
            ContainsScreenPoint(signalViewport, pointerPosition) && Mathf.Abs(scroll.y) > 0.01f)
        {
            float scrollableHeight = Mathf.Max(1.0f, signalContent.rect.height - signalViewport.rect.height);
            signalScrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                signalScrollRect.verticalNormalizedPosition + scroll.y / scrollableHeight);
        }

        RectTransform scrollbarRect = signalScrollbar != null
            ? signalScrollbar.transform as RectTransform
            : null;
        if (mouse.leftButton.wasPressedThisFrame && ContainsScreenPoint(scrollbarRect, pointerPosition))
            manualScrollbarDrag = true;
        if (manualScrollbarDrag && mouse.leftButton.isPressed)
            SetManualScrollbarPosition(scrollbarRect, pointerPosition);
        if (manualScrollbarDrag && mouse.leftButton.wasReleasedThisFrame)
        {
            SetManualScrollbarPosition(scrollbarRect, pointerPosition);
            manualScrollbarDrag = false;
            return;
        }

        if (!mouse.leftButton.wasReleasedThisFrame)
            return;

        if (!ContainsScreenPoint(signalViewport, pointerPosition))
            return;

        for (int signal = 0; signal < rows.Count; signal++)
        {
            Toggle toggle = rows[signal].toggle;
            if (!ContainsScreenPoint(toggle, pointerPosition))
                continue;
            toggle.isOn = !toggle.isOn;
            return;
        }
    }

    private void SetManualScrollbarPosition(RectTransform scrollbarRect, Vector2 screenPoint)
    {
        if (signalScrollRect == null || scrollbarRect == null ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                scrollbarRect,
                screenPoint,
                GetEventCamera(),
                out Vector2 localPoint))
        {
            return;
        }

        float normalized = Mathf.InverseLerp(scrollbarRect.rect.yMin, scrollbarRect.rect.yMax, localPoint.y);
        signalScrollRect.verticalNormalizedPosition = normalized;
    }

    private bool ContainsScreenPoint(Selectable selectable, Vector2 screenPoint)
    {
        return selectable != null && selectable.isActiveAndEnabled && selectable.interactable &&
               ContainsScreenPoint(selectable.transform as RectTransform, screenPoint);
    }

    private bool ContainsScreenPoint(RectTransform rect, Vector2 screenPoint)
    {
        return rect != null && rect.gameObject.activeInHierarchy &&
               RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, GetEventCamera());
    }

    private Camera GetEventCamera()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
    }

    private bool CanEventSystemReachPanel(Vector2 screenPoint)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null || !eventSystem.IsPointerOverGameObject())
            return false;

        PointerEventData pointer = new PointerEventData(eventSystem)
        {
            position = screenPoint
        };
        eventSystemHits.Clear();
        eventSystem.RaycastAll(pointer, eventSystemHits);
        for (int i = 0; i < eventSystemHits.Count; i++)
        {
            Transform hit = eventSystemHits[i].gameObject != null
                ? eventSystemHits[i].gameObject.transform
                : null;
            if (hit == transform || (hit != null && hit.IsChildOf(transform)))
                return true;
        }

        return false;
    }

    private static T FindChildComponent<T>(Transform root, string path) where T : Component
    {
        if (root == null)
            return null;
        Transform child = root.Find(path);
        return child != null ? child.GetComponent<T>() : null;
    }

#if UNITY_EDITOR
    [ContextMenu("Rebuild Signal Graph Hierarchy")]
    public void RebuildSignalGraphHierarchy()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        rows.Clear();
        font = TMP_Settings.defaultFontAsset;
        EnsureDefaultSignalCatalog();
        selected = new bool[GraphCount, signals.Count];
        currentValues = new double[signals.Count];
        for (int i = 0; i < currentValues.Length; i++)
            currentValues[i] = double.NaN;
        BuildInterface();
        ApplyDefaultSelection(0);
        ApplyDefaultSelection(1);
        ApplyDefaultSelection(2);
        SelectGraph(0);
        ApplyFontToHierarchy();
        initialized = true;
        UnityEditor.EditorUtility.SetDirty(gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }
#endif

    private GraphView BuildGraph(RectTransform parent, int graphIndex)
    {
        RectTransform root = CreateRect(parent, $"Graph{graphIndex + 1}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        TMP_Text title = CreateText(root, "Title", $"Graph {graphIndex + 1}", 18, FontStyle.Bold,
            new Vector2(0.025f, 0.91f), new Vector2(0.98f, 0.985f), TextAnchor.MiddleLeft, textColor);
        TMP_Text legend = CreateText(root, "Legend", string.Empty, 11, FontStyle.Normal,
            new Vector2(0.11f, 0.03f), new Vector2(0.985f, 0.145f), TextAnchor.UpperLeft, mutedColor);
        legend.richText = true;
        legend.textWrappingMode = TextWrappingModes.Normal;
        legend.overflowMode = TextOverflowModes.Truncate;

        CoSimulationMonitorChartGraphic chart = CreateRect(root, "Chart", new Vector2(0.11f, 0.19f), new Vector2(0.985f, 0.90f), Vector2.zero, Vector2.zero)
            .gameObject.AddComponent<CoSimulationMonitorChartGraphic>();
        chart.ConfigureSeries(signals.Count);
        for (int i = 0; i < signals.Count; i++)
        {
            chart.SetSeriesColor(i, signals[i].color);
            chart.SetSeriesVisible(i, false);
        }

        GraphView view = new GraphView
        {
            root = root.gameObject,
            chart = chart,
            title = title,
            legend = legend,
            maxValue = CreateText(root, "YMax", "1", 10, FontStyle.Normal, new Vector2(0.005f, 0.80f), new Vector2(0.10f, 0.90f), TextAnchor.MiddleRight, mutedColor),
            midValue = CreateText(root, "YMid", "0.5", 10, FontStyle.Normal, new Vector2(0.005f, 0.50f), new Vector2(0.10f, 0.60f), TextAnchor.MiddleRight, mutedColor),
            minValue = CreateText(root, "YMin", "0", 10, FontStyle.Normal, new Vector2(0.005f, 0.19f), new Vector2(0.10f, 0.29f), TextAnchor.MiddleRight, mutedColor),
            minTime = CreateText(root, "XMin", "0 s", 10, FontStyle.Normal, new Vector2(0.11f, 0.145f), new Vector2(0.30f, 0.19f), TextAnchor.MiddleLeft, mutedColor),
            maxTime = CreateText(root, "XMax", "1 s", 10, FontStyle.Normal, new Vector2(0.79f, 0.145f), new Vector2(0.985f, 0.19f), TextAnchor.MiddleRight, mutedColor),
            axisUnit = CreateText(root, "AxisUnit", "자동 범위", 10, FontStyle.Normal, new Vector2(0.40f, 0.145f), new Vector2(0.69f, 0.19f), TextAnchor.MiddleCenter, mutedColor)
        };
        return view;
    }

    private void BuildSignalSelector()
    {
        RectTransform panel = CreatePanel(transform, "SignalSelector", new Vector2(0.74f, 0.045f), new Vector2(0.985f, 0.915f));
        TMP_Text title = CreateText(panel, "Title", "그래프 항목", 16, FontStyle.Bold,
            new Vector2(0.035f, 0.945f), new Vector2(0.96f, 0.995f), TextAnchor.MiddleLeft, textColor);
        title.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform header = CreateRect(panel, "ListHeader", new Vector2(0.025f, 0.885f), new Vector2(0.975f, 0.942f), Vector2.zero, Vector2.zero);
        header.gameObject.AddComponent<Image>().color = panelHeaderColor;
        CreateText(header, "Enable", "선택", 10, FontStyle.Bold, new Vector2(0.00f, 0.0f), new Vector2(0.16f, 1.0f), TextAnchor.MiddleCenter, textColor);
        CreateText(header, "Color", "색상", 10, FontStyle.Bold, new Vector2(0.16f, 0.0f), new Vector2(0.29f, 1.0f), TextAnchor.MiddleCenter, textColor);
        CreateText(header, "Title", "항목", 10, FontStyle.Bold, new Vector2(0.29f, 0.0f), new Vector2(0.74f, 1.0f), TextAnchor.MiddleLeft, textColor);
        CreateText(header, "Trace", "현재값", 10, FontStyle.Bold, new Vector2(0.74f, 0.0f), new Vector2(0.98f, 1.0f), TextAnchor.MiddleRight, textColor);

        signalViewport = CreateRect(panel, "Viewport", new Vector2(0.025f, 0.25f), new Vector2(0.94f, 0.883f), Vector2.zero, Vector2.zero);
        signalViewport.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.047f, 0.067f, 0.55f);
        signalViewport.gameObject.AddComponent<RectMask2D>();
        signalContent = CreateRect(signalViewport, "Content", new Vector2(0.0f, 1.0f), new Vector2(1.0f, 1.0f), Vector2.zero, Vector2.zero);
        signalContent.pivot = new Vector2(0.5f, 1.0f);
        VerticalLayoutGroup layout = signalContent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 1.0f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = signalContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        signalScrollbar = BuildScrollbar(panel);
        signalScrollRect = panel.gameObject.AddComponent<ScrollRect>();
        signalScrollRect.viewport = signalViewport;
        signalScrollRect.content = signalContent;
        signalScrollRect.horizontal = false;
        signalScrollRect.vertical = true;
        signalScrollRect.movementType = ScrollRect.MovementType.Clamped;
        signalScrollRect.scrollSensitivity = 22.0f;
        signalScrollRect.verticalScrollbar = signalScrollbar;
        signalScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        for (int i = 0; i < signals.Count; i++)
            rows.Add(BuildSignalRow(signalContent, i));

        selectionSummary = CreateText(panel, "SelectionSummary", "", 11, FontStyle.Normal,
            new Vector2(0.035f, 0.195f), new Vector2(0.965f, 0.245f), TextAnchor.MiddleLeft, mutedColor);
        selectionSummary.overflowMode = TextOverflowModes.Ellipsis;
        statusText = CreateText(panel, "Status", "FMU 연결 대기", 10, FontStyle.Normal,
            new Vector2(0.035f, 0.15f), new Vector2(0.965f, 0.195f), TextAnchor.MiddleLeft, mutedColor);

        resetSelectionButton = CreateButton(panel, "ResetSelection", "그래프 항목 재설정", 11, ResetCurrentSelection,
            out _, new Vector2(0.035f, 0.075f), new Vector2(0.62f, 0.145f));
        clearHistoryButton = CreateButton(panel, "ClearHistory", "기록 초기화", 11, ClearAllHistory,
            out _, new Vector2(0.64f, 0.075f), new Vector2(0.965f, 0.145f));
        TMP_Text help = CreateText(panel, "Help", "A=Actuator · S=Sensor\n탭별 선택 저장 · 혼합 단위는 공통 Y축", 9, FontStyle.Normal,
            new Vector2(0.035f, 0.005f), new Vector2(0.965f, 0.068f), TextAnchor.UpperLeft, mutedColor);
        help.textWrappingMode = TextWrappingModes.Normal;
    }

    private SignalRow BuildSignalRow(RectTransform content, int signalIndex)
    {
        SignalDefinition signal = signals[signalIndex];
        RectTransform row = CreateRect(content, signal.id, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
        rowLayout.preferredHeight = 29.0f;
        rowLayout.minHeight = 29.0f;
        Image background = row.gameObject.AddComponent<Image>();
        background.color = signalIndex % 2 == 0
            ? new Color(1.0f, 1.0f, 1.0f, 0.045f)
            : new Color(1.0f, 1.0f, 1.0f, 0.018f);

        Toggle toggle = row.gameObject.AddComponent<Toggle>();
        toggle.transition = Selectable.Transition.None;
        toggle.targetGraphic = background;
        RectTransform box = CreateRect(row, "CheckBox", new Vector2(0.035f, 0.18f), new Vector2(0.115f, 0.82f), Vector2.zero, Vector2.zero);
        box.gameObject.AddComponent<Image>().color = new Color(0.45f, 0.50f, 0.56f, 1.0f);
        RectTransform check = CreateRect(box, "Check", new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.82f), Vector2.zero, Vector2.zero);
        Image checkImage = check.gameObject.AddComponent<Image>();
        checkImage.color = accentColor;
        toggle.graphic = checkImage;

        CreateText(row, "Category", signal.category, 9, FontStyle.Bold,
            new Vector2(0.12f, 0.0f), new Vector2(0.17f, 1.0f), TextAnchor.MiddleCenter,
            signal.category == "A" ? new Color(1.0f, 0.70f, 0.24f) : accentColor);
        RectTransform swatch = CreateRect(row, "Color", new Vector2(0.185f, 0.20f), new Vector2(0.265f, 0.80f), Vector2.zero, Vector2.zero);
        swatch.gameObject.AddComponent<Image>().color = signal.color;
        TMP_Text label = CreateText(row, "Label", signal.title, 10, FontStyle.Normal,
            new Vector2(0.285f, 0.0f), new Vector2(0.73f, 1.0f), TextAnchor.MiddleLeft, textColor);
        label.enableAutoSizing = true;
        label.fontSizeMin = 8.0f;
        label.fontSizeMax = 10.0f;
        label.overflowMode = TextOverflowModes.Ellipsis;
        TMP_Text trace = CreateText(row, "Trace", "—", 10, FontStyle.Bold,
            new Vector2(0.735f, 0.0f), new Vector2(0.98f, 1.0f), TextAnchor.MiddleRight, mutedColor);
        trace.enableAutoSizing = true;
        trace.fontSizeMin = 8.0f;
        trace.fontSizeMax = 10.0f;

        int captured = signalIndex;
        toggle.onValueChanged.AddListener(value => SetSignalSelected(captured, value));
        return new SignalRow { toggle = toggle, trace = trace };
    }

    private Scrollbar BuildScrollbar(RectTransform parent)
    {
        RectTransform root = CreateRect(parent, "Scrollbar", new Vector2(0.945f, 0.25f), new Vector2(0.975f, 0.883f), Vector2.zero, Vector2.zero);
        Image background = root.gameObject.AddComponent<Image>();
        background.color = new Color(1.0f, 1.0f, 1.0f, 0.08f);
        Scrollbar scrollbar = root.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        RectTransform sliding = CreateRect(root, "SlidingArea", new Vector2(0.15f, 0.015f), new Vector2(0.85f, 0.985f), Vector2.zero, Vector2.zero);
        RectTransform handle = CreateRect(sliding, "Handle", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.49f, 0.57f, 0.67f, 0.85f);
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        return scrollbar;
    }

    private void SelectGraph(int graphIndex)
    {
        activeGraphIndex = Mathf.Clamp(graphIndex, 0, GraphCount - 1);
        for (int graph = 0; graph < GraphCount; graph++)
        {
            graphs[graph].root.SetActive(graph == activeGraphIndex);
            Image tabImage = graphTabButtons[graph].targetGraphic as Image;
            if (tabImage != null)
                tabImage.color = graph == activeGraphIndex ? selectedTabColor : normalTabColor;
            graphTabLabels[graph].color = graph == activeGraphIndex ? Color.white : mutedColor;
        }

        for (int signal = 0; signal < signals.Count; signal++)
            rows[signal].toggle.SetIsOnWithoutNotify(selected[activeGraphIndex, signal]);
        UpdateGraphPresentation(activeGraphIndex);
    }

    private void SetSignalSelected(int signalIndex, bool value)
    {
        if (!initialized && selected == null)
            return;

        selected[activeGraphIndex, signalIndex] = value;
        graphs[activeGraphIndex].chart.SetSeriesVisible(signalIndex, value);
        UpdateGraphPresentation(activeGraphIndex);
    }

    private void ResetCurrentSelection()
    {
        ApplyDefaultSelection(activeGraphIndex);
        SelectGraph(activeGraphIndex);
    }

    private void ClearAllHistory()
    {
        for (int graph = 0; graph < GraphCount; graph++)
            graphs[graph].chart.ClearSamples();
        lastSampledCoSimStep = ulong.MaxValue;
        statusText.text = "3개 그래프의 기록을 초기화했습니다.";
        statusText.color = accentColor;
    }

    private void ApplyDefaultSelection(int graphIndex)
    {
        for (int signal = 0; signal < signals.Count; signal++)
            selected[graphIndex, signal] = false;

        List<string> defaults = graphIndex == 0
            ? firstGraphSignalIds
            : graphIndex == 1 ? secondGraphSignalIds : thirdGraphSignalIds;
        if (defaults != null)
            SelectById(graphIndex, defaults.ToArray());

        for (int signal = 0; signal < signals.Count; signal++)
            graphs[graphIndex].chart.SetSeriesVisible(signal, selected[graphIndex, signal]);
        UpdateGraphPresentation(graphIndex);
    }

    private void SelectById(int graphIndex, params string[] ids)
    {
        for (int id = 0; id < ids.Length; id++)
        {
            for (int signal = 0; signal < signals.Count; signal++)
            {
                if (string.Equals(signals[signal].id, ids[id], StringComparison.Ordinal))
                {
                    selected[graphIndex, signal] = true;
                    break;
                }
            }
        }
    }

    private void UpdateGraphPresentation(int graphIndex)
    {
        GraphView graph = graphs[graphIndex];
        int selectedCount = 0;
        System.Text.StringBuilder legend = new System.Text.StringBuilder();
        for (int i = 0; i < signals.Count; i++)
        {
            if (!selected[graphIndex, i])
                continue;
            selectedCount++;
            if (legend.Length > 0)
                legend.Append("    ");
            legend.Append("<color=#")
                .Append(ColorUtility.ToHtmlStringRGB(signals[i].color))
                .Append(">■</color> ")
                .Append(signals[i].title);
        }

        graph.title.text = $"Graph {graphIndex + 1} · {GetAxisUnit(graphIndex)} 자동 범위";
        graph.legend.text = selectedCount == 0 ? "오른쪽 목록에서 표시할 항목을 선택하세요." : legend.ToString();
        graph.axisUnit.text = $"Time (s) · Y: {GetAxisUnit(graphIndex)}";
        if (graphIndex == activeGraphIndex && selectionSummary != null)
            selectionSummary.text = $"Graph {graphIndex + 1} 선택 항목: {selectedCount}개";
    }

    private string GetAxisUnit(int graphIndex)
    {
        string unit = null;
        for (int i = 0; i < signals.Count; i++)
        {
            if (!selected[graphIndex, i])
                continue;
            string candidate = string.IsNullOrEmpty(signals[i].unit) ? "unitless" : signals[i].unit;
            if (unit == null)
                unit = candidate;
            else if (!string.Equals(unit, candidate, StringComparison.Ordinal))
                return "mixed";
        }
        return unit ?? "-";
    }

    private static void UpdateAxes(GraphView view, string unit)
    {
        if (view == null || view.chart == null)
            return;
        view.chart.GetDisplayRanges(out float minTime, out float maxTime, out float minValue, out float maxValue);
        float mid = (minValue + maxValue) * 0.5f;
        view.minTime.text = $"{minTime:F0} s";
        view.maxTime.text = $"{maxTime:F0} s";
        view.minValue.text = minValue.ToString("G5", CultureInfo.InvariantCulture);
        view.midValue.text = mid.ToString("G5", CultureInfo.InvariantCulture);
        view.maxValue.text = maxValue.ToString("G5", CultureInfo.InvariantCulture);
        view.axisUnit.text = $"Time (s) · Y: {unit}";
    }

    private RectTransform CreatePanel(Transform parent, string name, Vector2 min, Vector2 max)
    {
        RectTransform panel = CreateRect(parent, name, min, max, Vector2.zero, Vector2.zero);
        panel.gameObject.AddComponent<Image>().color = panelColor;
        return panel;
    }

    private Button CreateButton(Transform parent, string name, string label, int size, Action onClick, out TMP_Text labelText)
    {
        return CreateButton(parent, name, label, size, onClick, out labelText, Vector2.zero, Vector2.one);
    }

    private Button CreateButton(
        Transform parent,
        string name,
        string label,
        int size,
        Action onClick,
        out TMP_Text labelText,
        Vector2 min,
        Vector2 max)
    {
        RectTransform rect = CreateRect(parent, name, min, max, Vector2.zero, Vector2.zero);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = normalTabColor;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());
        labelText = CreateText(rect, "Label", label, size, FontStyle.Bold,
            new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.95f), TextAnchor.MiddleCenter, textColor);
        labelText.overflowMode = TextOverflowModes.Ellipsis;
        return button;
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
        text.fontStyle = style == FontStyle.Bold ? FontStyles.Bold : FontStyles.Normal;
        text.alignment = ToAlignment(alignment);
        text.color = color;
        text.raycastTarget = false;
        text.extraPadding = true;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Truncate;
        return text;
    }

    private static TextAlignmentOptions ToAlignment(TextAnchor alignment)
    {
        switch (alignment)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
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

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string Format(double value, int decimals, string unit)
    {
        if (!IsFinite(value))
            return "—";
        string suffix = string.IsNullOrEmpty(unit) ? string.Empty : " " + unit;
        return value.ToString("F" + decimals, CultureInfo.InvariantCulture) + suffix;
    }
}
