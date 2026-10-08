using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class MainDashboardSceneViewController : IDisposable
{
    private enum ContourMode
    {
        Temperature,
        Velocity
    }

    private const string ThermalDisplayCameraName = "ThermalDisplayCamera";
    private const string VelocityDisplayCameraName = "VelocityDisplayCamera";
    private const float CameraSearchIntervalSeconds = 1.0f;
    private const float VisualizerSearchIntervalSeconds = 1.0f;
    private const float DefaultTemperatureMin = 20.0f;
    private const float DefaultTemperatureMax = 35.0f;
    private const float DefaultVelocityMin = 0.0f;
    private const float DefaultVelocityMax = 2.0f;

    private readonly Func<Texture> textureSource;
    private readonly bool usesSceneCameraSource;
    private Image sceneImage;
    private Label sourceStatus;
    private Button fitButton;
    private Button resetViewButton;
    private Button variableButton;
    private Button realtimeButton;
    private Label legendTitle;
    private Label legendUnit;
    private Label[] legendValueLabels;
    private Camera thermalDisplayCamera;
    private Camera velocityDisplayCamera;
    private DisplayOrbitCameraController thermalCameraController;
    private DisplayOrbitCameraController velocityCameraController;
    private ThermalVisualizer[] thermalVisualizers;
    private VelocityVisualizer[] velocityVisualizers;
    private Texture boundTexture;
    private RenderTexture frozenPreview;
    private float nextCameraSearchTime;
    private float nextVisualizerSearchTime;
    private bool isRealtime = true;
    private ContourMode contourMode = ContourMode.Temperature;
    private int activePointerId = -1;
    private int activePointerButton = -1;
    private Vector2 lastPointerPosition;

    public MainDashboardSceneViewController()
    {
        usesSceneCameraSource = true;
        textureSource = ResolveSelectedDisplayTexture;
    }

    public MainDashboardSceneViewController(Func<Texture> textureSource)
    {
        this.textureSource = textureSource ?? throw new ArgumentNullException(nameof(textureSource));
        usesSceneCameraSource = false;
    }

    public bool IsBound => boundTexture != null;
    public Texture BoundTexture => boundTexture;
    public bool IsRealtime => isRealtime;

    public bool Initialize(VisualElement documentRoot, out string issue)
    {
        if (documentRoot == null)
        {
            issue = "UIDocument 루트가 없습니다.";
            return false;
        }

        sceneImage = documentRoot.Q<Image>("SceneViewImage");
        sourceStatus = documentRoot.Q<Label>("SceneSourceStatus");
        if (sceneImage == null || sourceStatus == null)
        {
            issue = "Dashboard Scene View 요소가 없습니다.";
            return false;
        }

        fitButton = documentRoot.Q<Button>("SceneFitButton");
        resetViewButton = documentRoot.Q<Button>("SceneResetViewButton");
        variableButton = documentRoot.Q<Button>("SceneVariableButton");
        realtimeButton = documentRoot.Q<Button>("SceneRealtimeButton");
        legendTitle = documentRoot.Q<Label>("TemperatureLegendTitle");
        legendUnit = documentRoot.Q<Label>("ResultLegendUnit");
        legendValueLabels = new[]
        {
            documentRoot.Q<Label>("LegendMaxLabel"),
            documentRoot.Q<Label>("LegendUpperMidLabel"),
            documentRoot.Q<Label>("LegendMiddleHighLabel"),
            documentRoot.Q<Label>("LegendMiddleLowLabel"),
            documentRoot.Q<Label>("LegendLowerMidLabel"),
            documentRoot.Q<Label>("LegendMinLabel")
        };
        if (fitButton == null || resetViewButton == null || variableButton == null || realtimeButton == null ||
            legendTitle == null || legendUnit == null || HasMissingLegendLabel())
        {
            issue = "Scene Toolbar 또는 Result Legend 구성이 올바르지 않습니다.";
            return false;
        }

        sceneImage.scaleMode = ScaleMode.ScaleToFit;
        sceneImage.RegisterCallback<PointerDownEvent>(OnScenePointerDown);
        sceneImage.RegisterCallback<PointerMoveEvent>(OnScenePointerMove);
        sceneImage.RegisterCallback<PointerUpEvent>(OnScenePointerUp);
        sceneImage.RegisterCallback<PointerCaptureOutEvent>(OnScenePointerCaptureOut);
        sceneImage.RegisterCallback<WheelEvent>(OnSceneWheel);
        fitButton.clicked += FitView;
        resetViewButton.clicked += ResetView;
        variableButton.clicked += ToggleContour;
        realtimeButton.clicked += ToggleRealtime;
        variableButton.tooltip = "Temperature와 Velocity 컨투어를 전환합니다.";
        realtimeButton.tooltip = "Dashboard 미리보기의 실시간 갱신을 고정하거나 재개합니다.";
        ResolveContourVisualizers(true);
        ApplyContourMode();
        UpdateRealtimeButton();
        Refresh();
        issue = string.Empty;
        return true;
    }

    public void Refresh()
    {
        if (sceneImage == null)
            return;

        Texture texture = textureSource();
        if (texture != boundTexture)
        {
            boundTexture = texture;
            if (isRealtime)
                sceneImage.image = boundTexture;
        }

        bool hasTexture = boundTexture != null;
        sourceStatus.text = hasTexture
            ? string.Empty
            : $"Waiting for {GetSelectedCameraName()}...";
        sourceStatus.style.display = hasTexture ? DisplayStyle.None : DisplayStyle.Flex;
        ResolveContourVisualizers(false);
        UpdateLegend();
        UpdateCameraButtonAvailability();
    }

    public void Dispose()
    {
        if (sceneImage != null)
        {
            sceneImage.UnregisterCallback<PointerDownEvent>(OnScenePointerDown);
            sceneImage.UnregisterCallback<PointerMoveEvent>(OnScenePointerMove);
            sceneImage.UnregisterCallback<PointerUpEvent>(OnScenePointerUp);
            sceneImage.UnregisterCallback<PointerCaptureOutEvent>(OnScenePointerCaptureOut);
            sceneImage.UnregisterCallback<WheelEvent>(OnSceneWheel);
            if (activePointerId >= 0 && sceneImage.HasPointerCapture(activePointerId))
                sceneImage.ReleasePointer(activePointerId);
        }

        if (fitButton != null)
            fitButton.clicked -= FitView;
        if (resetViewButton != null)
            resetViewButton.clicked -= ResetView;
        if (variableButton != null)
            variableButton.clicked -= ToggleContour;
        if (realtimeButton != null)
            realtimeButton.clicked -= ToggleRealtime;

        ReleaseFrozenPreview();
        sceneImage = null;
        sourceStatus = null;
        fitButton = null;
        resetViewButton = null;
        variableButton = null;
        realtimeButton = null;
        legendTitle = null;
        legendUnit = null;
        legendValueLabels = null;
        thermalDisplayCamera = null;
        velocityDisplayCamera = null;
        thermalCameraController = null;
        velocityCameraController = null;
        thermalVisualizers = null;
        velocityVisualizers = null;
        boundTexture = null;
        activePointerId = -1;
        activePointerButton = -1;
    }

    private Texture ResolveSelectedDisplayTexture()
    {
        ResolveDisplayCameras(false);
        Camera selectedCamera = GetSelectedCamera();
        return selectedCamera != null ? selectedCamera.targetTexture : null;
    }

    private void ResolveDisplayCameras(bool forceSearch)
    {
        if (thermalDisplayCamera != null && velocityDisplayCamera != null)
        {
            ResolveCameraControllers();
            return;
        }

        if (!forceSearch && Time.unscaledTime < nextCameraSearchTime)
            return;

        nextCameraSearchTime = Time.unscaledTime + CameraSearchIntervalSeconds;
        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate == null)
                continue;

            if (candidate.name == ThermalDisplayCameraName)
                thermalDisplayCamera = candidate;
            else if (candidate.name == VelocityDisplayCameraName)
                velocityDisplayCamera = candidate;
        }

        ResolveCameraControllers();
    }

    private void FitView()
    {
        ResolveDisplayCameras(true);
        GetSelectedCameraController()?.FitView();
        UpdateCameraButtonAvailability();
    }

    private void ResetView()
    {
        ResolveDisplayCameras(true);
        GetSelectedCameraController()?.ResetView();
        UpdateCameraButtonAvailability();
    }

    private void ToggleContour()
    {
        if (!isRealtime)
            ResumeRealtime();

        DisplayOrbitCameraController previousController = GetSelectedCameraController();
        contourMode = contourMode == ContourMode.Temperature
            ? ContourMode.Velocity
            : ContourMode.Temperature;
        ResolveDisplayCameras(true);
        DisplayOrbitCameraController selectedController = GetSelectedCameraController();
        if (previousController != null && selectedController != null)
            selectedController.CopyViewFrom(previousController);
        ResolveContourVisualizers(true);
        ApplyContourMode();
        Refresh();
    }

    private void OnScenePointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 && evt.button != 1)
            return;

        ResolveDisplayCameras(true);
        if (GetSelectedCameraController() == null)
            return;
        if (!isRealtime)
            ResumeRealtime();

        activePointerId = evt.pointerId;
        activePointerButton = evt.button;
        lastPointerPosition = new Vector2(evt.position.x, evt.position.y);
        sceneImage.CapturePointer(activePointerId);
        evt.StopPropagation();
    }

    private void OnScenePointerMove(PointerMoveEvent evt)
    {
        if (activePointerId < 0 || evt.pointerId != activePointerId)
            return;

        Vector2 currentPosition = new Vector2(evt.position.x, evt.position.y);
        Vector2 panelDelta = currentPosition - lastPointerPosition;
        lastPointerPosition = currentPosition;
        Vector2 cameraDelta = new Vector2(panelDelta.x, -panelDelta.y);
        float viewportHeight = Mathf.Max(1.0f, sceneImage.contentRect.height);
        DisplayOrbitCameraController selectedController = GetSelectedCameraController();
        if (activePointerButton == 0)
            selectedController?.OrbitView(cameraDelta, viewportHeight);
        else if (activePointerButton == 1)
            selectedController?.PanView(cameraDelta, viewportHeight);

        evt.StopPropagation();
    }

    private void OnScenePointerUp(PointerUpEvent evt)
    {
        if (activePointerId < 0 || evt.pointerId != activePointerId)
            return;

        if (sceneImage.HasPointerCapture(activePointerId))
            sceneImage.ReleasePointer(activePointerId);
        activePointerId = -1;
        activePointerButton = -1;
        evt.StopPropagation();
    }

    private void OnScenePointerCaptureOut(PointerCaptureOutEvent evt)
    {
        activePointerId = -1;
        activePointerButton = -1;
    }

    private void OnSceneWheel(WheelEvent evt)
    {
        ResolveDisplayCameras(true);
        DisplayOrbitCameraController selectedController = GetSelectedCameraController();
        if (selectedController == null || Mathf.Approximately(evt.delta.y, 0f))
            return;
        if (!isRealtime)
            ResumeRealtime();

        // UI Toolkit reports positive Y when scrolling down. The camera controller uses
        // positive notches for zoom-in, so reverse and normalize the event direction.
        selectedController.ZoomView(-Mathf.Sign(evt.delta.y));
        evt.StopPropagation();
    }

    private void ToggleRealtime()
    {
        if (isRealtime)
            FreezePreview();
        else
            ResumeRealtime();
    }

    private void FreezePreview()
    {
        if (sceneImage == null || boundTexture == null)
            return;

        ReleaseFrozenPreview();
        frozenPreview = RenderTexture.GetTemporary(
            Mathf.Max(1, boundTexture.width),
            Mathf.Max(1, boundTexture.height),
            0,
            RenderTextureFormat.ARGB32);
        frozenPreview.name = "UX01_FrozenScenePreview";
        Graphics.Blit(boundTexture, frozenPreview);
        sceneImage.image = frozenPreview;
        isRealtime = false;
        UpdateRealtimeButton();
    }

    private void ResumeRealtime()
    {
        isRealtime = true;
        if (sceneImage != null)
            sceneImage.image = boundTexture;
        ReleaseFrozenPreview();
        UpdateRealtimeButton();
    }

    private void ReleaseFrozenPreview()
    {
        if (frozenPreview == null)
            return;

        RenderTexture.ReleaseTemporary(frozenPreview);
        frozenPreview = null;
    }

    private void UpdateCameraButtonAvailability()
    {
        DisplayOrbitCameraController selectedController = GetSelectedCameraController();
        bool hasController = selectedController != null;
        if (fitButton != null)
        {
            fitButton.SetEnabled(hasController);
            fitButton.tooltip = hasController
                ? "현재 방향을 유지하면서 해석 도메인을 화면에 맞춥니다."
                : $"{GetSelectedCameraName()}의 Camera Controller를 기다리는 중입니다.";
        }

        if (resetViewButton != null)
        {
            resetViewButton.SetEnabled(hasController);
            resetViewButton.tooltip = hasController
                ? "카메라를 시작 시점의 위치와 배율로 되돌립니다."
                : $"{GetSelectedCameraName()}의 Camera Controller를 기다리는 중입니다.";
        }

        bool canToggleContour = usesSceneCameraSource
            ? thermalDisplayCamera != null && thermalDisplayCamera.targetTexture != null &&
              velocityDisplayCamera != null && velocityDisplayCamera.targetTexture != null &&
              HasContourVisualizers()
            : HasContourVisualizers();
        variableButton?.SetEnabled(boundTexture != null && canToggleContour);
        realtimeButton?.SetEnabled(boundTexture != null);
    }

    private void ResolveContourVisualizers(bool forceSearch)
    {
        if (!forceSearch && HasContourVisualizers())
            return;
        if (!forceSearch && Time.unscaledTime < nextVisualizerSearchTime)
            return;

        nextVisualizerSearchTime = Time.unscaledTime + VisualizerSearchIntervalSeconds;
        thermalVisualizers = UnityEngine.Object.FindObjectsByType<ThermalVisualizer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        velocityVisualizers = UnityEngine.Object.FindObjectsByType<VelocityVisualizer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        if (HasContourVisualizers())
        {
            SetVisualizerRenderers(thermalVisualizers, true);
            SetVisualizerRenderers(velocityVisualizers, true);
        }
    }

    private bool HasContourVisualizers()
    {
        return HasLiveVisualizer(thermalVisualizers) && HasLiveVisualizer(velocityVisualizers);
    }

    private static bool HasLiveVisualizer<T>(T[] visualizers) where T : Component
    {
        if (visualizers == null || visualizers.Length == 0)
            return false;

        for (int i = 0; i < visualizers.Length; i++)
        {
            if (visualizers[i] != null)
                return true;
        }

        return false;
    }

    private void ApplyContourMode()
    {
        bool showTemperature = contourMode == ContourMode.Temperature;
        SetVisualizerRenderers(thermalVisualizers, true);
        SetVisualizerRenderers(velocityVisualizers, true);

        if (variableButton != null)
        {
            variableButton.text = showTemperature ? "Temperature" : "Velocity";
            variableButton.EnableInClassList("scene-toolbar-button--selected", true);
        }

        UpdateLegend();
    }

    private Camera GetSelectedCamera()
    {
        return contourMode == ContourMode.Temperature
            ? thermalDisplayCamera
            : velocityDisplayCamera;
    }

    private DisplayOrbitCameraController GetSelectedCameraController()
    {
        return contourMode == ContourMode.Temperature
            ? thermalCameraController
            : velocityCameraController;
    }

    private string GetSelectedCameraName()
    {
        return contourMode == ContourMode.Temperature
            ? ThermalDisplayCameraName
            : VelocityDisplayCameraName;
    }

    private void ResolveCameraControllers()
    {
        if (thermalCameraController == null && thermalDisplayCamera != null)
            thermalCameraController = thermalDisplayCamera.GetComponent<DisplayOrbitCameraController>();
        if (velocityCameraController == null && velocityDisplayCamera != null)
            velocityCameraController = velocityDisplayCamera.GetComponent<DisplayOrbitCameraController>();
    }

    private static void SetVisualizerRenderers<T>(T[] visualizers, bool visible) where T : Component
    {
        if (visualizers == null)
            return;

        for (int i = 0; i < visualizers.Length; i++)
        {
            T visualizer = visualizers[i];
            if (visualizer == null)
                continue;

            Renderer contourRenderer = visualizer.GetComponent<Renderer>();
            if (contourRenderer != null)
                contourRenderer.enabled = visible;
        }
    }

    private void UpdateLegend()
    {
        if (legendTitle == null || legendUnit == null || HasMissingLegendLabel())
            return;

        bool showTemperature = contourMode == ContourMode.Temperature;
        float minimum = showTemperature ? DefaultTemperatureMin : DefaultVelocityMin;
        float maximum = showTemperature ? DefaultTemperatureMax : DefaultVelocityMax;
        SimulationController simulationController = SimulationController.Instance;
        if (simulationController != null)
        {
            minimum = showTemperature ? simulationController.TempPhysMinDegC : DefaultVelocityMin;
            maximum = showTemperature
                ? simulationController.TempPhysMaxDegC
                : simulationController.MaxWindSpeedPhys;
        }

        if (!float.IsFinite(minimum))
            minimum = showTemperature ? DefaultTemperatureMin : DefaultVelocityMin;
        if (!float.IsFinite(maximum) || maximum <= minimum)
            maximum = showTemperature ? DefaultTemperatureMax : DefaultVelocityMax;

        legendTitle.text = showTemperature ? "Temperature" : "Velocity";
        legendUnit.text = showTemperature ? "(°C)" : "(m/s)";

        string format = showTemperature ? "F0" : "F1";
        for (int i = 0; i < legendValueLabels.Length; i++)
        {
            float ratio = i / (float)(legendValueLabels.Length - 1);
            float value = Mathf.Lerp(maximum, minimum, ratio);
            legendValueLabels[i].text = value.ToString(format);
        }
    }

    private bool HasMissingLegendLabel()
    {
        if (legendValueLabels == null || legendValueLabels.Length == 0)
            return true;

        for (int i = 0; i < legendValueLabels.Length; i++)
        {
            if (legendValueLabels[i] == null)
                return true;
        }

        return false;
    }

    private void UpdateRealtimeButton()
    {
        if (realtimeButton == null)
            return;

        realtimeButton.text = isRealtime ? "Real-time" : "Frozen";
        realtimeButton.EnableInClassList("scene-toolbar-button--selected", isRealtime);
    }
}
