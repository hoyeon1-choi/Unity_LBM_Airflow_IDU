using System;
using UnityEngine;
using UnityEngine.UIElements;

public enum ResultsSliceAxis
{
    X,
    Y,
    Z
}

public enum ResultsCameraInteraction
{
    Orbit,
    Pan
}

public sealed class ResultsSceneInteractionController : IDisposable
{
    private const string ThermalCameraName = "ThermalDisplayCamera";
    private const string VelocityCameraName = "VelocityDisplayCamera";
    private const float SearchIntervalSeconds = 1.0f;

    private readonly ResultsVariableSelectionState variableState;
    private readonly Image sceneImage;
    private readonly Toggle[] sliceToggles;
    private readonly Slider[] positionSliders;
    private readonly Label[] positionValues;
    private readonly Label sliceStatus;
    private readonly Button fitButton;
    private readonly Button orbitButton;
    private readonly Button panButton;
    private readonly Button resetButton;

    private RuntimeSliceContourController sliceController;
    private Camera thermalCamera;
    private Camera velocityCamera;
    private DisplayOrbitCameraController thermalCameraController;
    private DisplayOrbitCameraController velocityCameraController;
    private ResultsVariable selectedVariable;
    private ResultsCameraInteraction interaction = ResultsCameraInteraction.Orbit;
    private float nextSearchTime;
    private int activePointerId = -1;
    private int activePointerButton = -1;
    private Vector2 lastPointerPosition;
    private bool syncingSliceUi;

    private ResultsSceneInteractionController(
        VisualElement root,
        ResultsVariableSelectionState variableState)
    {
        this.variableState = variableState;
        sceneImage = root.Q<Image>("SceneViewImage");
        sliceToggles = new[]
        {
            root.Q<Toggle>("SliceXToggle"),
            root.Q<Toggle>("SliceYToggle"),
            root.Q<Toggle>("SliceZToggle")
        };
        positionSliders = new[]
        {
            root.Q<Slider>("SliceXPositionSlider"),
            root.Q<Slider>("SliceYPositionSlider"),
            root.Q<Slider>("SliceZPositionSlider")
        };
        positionValues = new[]
        {
            root.Q<Label>("SliceXPositionValue"),
            root.Q<Label>("SliceYPositionValue"),
            root.Q<Label>("SliceZPositionValue")
        };
        sliceStatus = root.Q<Label>("SliceControlStatus");
        fitButton = root.Q<Button>("SceneFitButton");
        orbitButton = root.Q<Button>("SceneOrbitButton");
        panButton = root.Q<Button>("ScenePanButton");
        resetButton = root.Q<Button>("SceneResetButton");
    }

    public ResultsCameraInteraction Interaction => interaction;
    public bool HasSliceController => sliceController != null && sliceController.IsInitialized;
    public bool HasCameraController => GetSelectedCameraController() != null;

    public static bool TryCreate(
        VisualElement root,
        ResultsVariableSelectionState variableState,
        out ResultsSceneInteractionController controller,
        out string issue)
    {
        controller = null;
        if (root == null || variableState == null || !variableState.HasSelection)
        {
            issue = "Results scene UI root or Result Variable state is missing.";
            return false;
        }

        var candidate = new ResultsSceneInteractionController(root, variableState);
        if (!candidate.HasRequiredElements())
        {
            issue = "One or more supported Results scene controls are missing.";
            return false;
        }

        candidate.selectedVariable = variableState.Current.Variable;
        candidate.sliceToggles[(int)ResultsSliceAxis.X]
            .RegisterValueChangedCallback(candidate.OnSliceXVisibilityChanged);
        candidate.sliceToggles[(int)ResultsSliceAxis.Y]
            .RegisterValueChangedCallback(candidate.OnSliceYVisibilityChanged);
        candidate.sliceToggles[(int)ResultsSliceAxis.Z]
            .RegisterValueChangedCallback(candidate.OnSliceZVisibilityChanged);
        candidate.positionSliders[(int)ResultsSliceAxis.X]
            .RegisterValueChangedCallback(candidate.OnSliceXPositionChanged);
        candidate.positionSliders[(int)ResultsSliceAxis.Y]
            .RegisterValueChangedCallback(candidate.OnSliceYPositionChanged);
        candidate.positionSliders[(int)ResultsSliceAxis.Z]
            .RegisterValueChangedCallback(candidate.OnSliceZPositionChanged);
        candidate.fitButton.clicked += candidate.FitView;
        candidate.orbitButton.clicked += candidate.SelectOrbit;
        candidate.panButton.clicked += candidate.SelectPan;
        candidate.resetButton.clicked += candidate.ResetView;
        candidate.sceneImage.RegisterCallback<PointerDownEvent>(candidate.OnPointerDown);
        candidate.sceneImage.RegisterCallback<PointerMoveEvent>(candidate.OnPointerMove);
        candidate.sceneImage.RegisterCallback<PointerUpEvent>(candidate.OnPointerUp);
        candidate.sceneImage.RegisterCallback<PointerCaptureOutEvent>(candidate.OnPointerCaptureOut);
        candidate.sceneImage.RegisterCallback<WheelEvent>(candidate.OnWheel);
        candidate.variableState.SelectionChanged += candidate.OnVariableChanged;
        candidate.ApplyInteractionVisuals();
        candidate.Refresh(true);

        controller = candidate;
        issue = string.Empty;
        return true;
    }

    public void Refresh(bool forceSearch = false)
    {
        bool canSearch = forceSearch || Time.unscaledTime >= nextSearchTime;
        if (canSearch)
        {
            nextSearchTime = Time.unscaledTime + SearchIntervalSeconds;
            ResolveSliceController();
            ResolveDisplayCameras();
        }

        bool hasSlice = HasSliceController;
        for (int i = 0; i < sliceToggles.Length; i++)
        {
            sliceToggles[i].SetEnabled(hasSlice);
            positionSliders[i].SetEnabled(hasSlice);
        }

        sliceStatus.EnableInClassList("scene-control-panel__status--ready", hasSlice);
        sliceStatus.text = hasSlice
            ? "Slice planes can be enabled and positioned independently."
            : "Waiting for the existing slice controller.";

        bool hasCamera = HasCameraController;
        fitButton.SetEnabled(hasCamera);
        orbitButton.SetEnabled(hasCamera);
        panButton.SetEnabled(hasCamera);
        resetButton.SetEnabled(hasCamera);
    }

    public bool SetSliceVisible(ResultsSliceAxis axis, bool visible)
    {
        if (!HasSliceController || !Enum.IsDefined(typeof(ResultsSliceAxis), axis))
            return false;

        switch (axis)
        {
            case ResultsSliceAxis.X:
                sliceController.SetVerticalVisible(visible);
                break;
            case ResultsSliceAxis.Y:
                sliceController.SetHorizontalVisible(visible);
                break;
            case ResultsSliceAxis.Z:
                sliceController.SetDepthVisible(visible);
                break;
        }

        sliceToggles[(int)axis].SetValueWithoutNotify(visible);
        return true;
    }

    public bool SetSlicePosition(ResultsSliceAxis axis, float normalizedPosition)
    {
        if (!HasSliceController || !Enum.IsDefined(typeof(ResultsSliceAxis), axis))
            return false;

        float value = Mathf.Clamp01(normalizedPosition);
        switch (axis)
        {
            case ResultsSliceAxis.X:
                sliceController.SetVerticalPositionNormalized(value);
                break;
            case ResultsSliceAxis.Y:
                sliceController.SetHorizontalPositionNormalized(value);
                break;
            case ResultsSliceAxis.Z:
                sliceController.SetDepthPositionNormalized(value);
                break;
        }

        positionSliders[(int)axis].SetValueWithoutNotify(value);
        positionValues[(int)axis].text = value.ToString("F2");
        return true;
    }

    public bool IsSliceVisible(ResultsSliceAxis axis)
    {
        return Enum.IsDefined(typeof(ResultsSliceAxis), axis) &&
               sliceToggles[(int)axis].value;
    }

    public void Dispose()
    {
        sliceToggles[(int)ResultsSliceAxis.X]
            .UnregisterValueChangedCallback(OnSliceXVisibilityChanged);
        sliceToggles[(int)ResultsSliceAxis.Y]
            .UnregisterValueChangedCallback(OnSliceYVisibilityChanged);
        sliceToggles[(int)ResultsSliceAxis.Z]
            .UnregisterValueChangedCallback(OnSliceZVisibilityChanged);
        positionSliders[(int)ResultsSliceAxis.X]
            .UnregisterValueChangedCallback(OnSliceXPositionChanged);
        positionSliders[(int)ResultsSliceAxis.Y]
            .UnregisterValueChangedCallback(OnSliceYPositionChanged);
        positionSliders[(int)ResultsSliceAxis.Z]
            .UnregisterValueChangedCallback(OnSliceZPositionChanged);
        fitButton.clicked -= FitView;
        orbitButton.clicked -= SelectOrbit;
        panButton.clicked -= SelectPan;
        resetButton.clicked -= ResetView;
        sceneImage.UnregisterCallback<PointerDownEvent>(OnPointerDown);
        sceneImage.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
        sceneImage.UnregisterCallback<PointerUpEvent>(OnPointerUp);
        sceneImage.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        sceneImage.UnregisterCallback<WheelEvent>(OnWheel);
        variableState.SelectionChanged -= OnVariableChanged;
        ReleasePointer();
    }

    private void ResolveSliceController()
    {
        if (sliceController != null && sliceController.IsInitialized)
            return;

        RuntimeSliceContourController[] controllers =
            UnityEngine.Object.FindObjectsByType<RuntimeSliceContourController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        sliceController = controllers.Length > 0 ? controllers[0] : null;
        if (HasSliceController)
        {
            ApplyVariableVisibility();
            SyncSliceControls();
        }
    }

    private void ResolveDisplayCameras()
    {
        if (thermalCamera != null && velocityCamera != null)
        {
            ResolveCameraControllers();
            return;
        }

        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate == null)
                continue;
            if (candidate.name == ThermalCameraName)
                thermalCamera = candidate;
            else if (candidate.name == VelocityCameraName)
                velocityCamera = candidate;
        }

        ResolveCameraControllers();
    }

    private void ResolveCameraControllers()
    {
        thermalCameraController = thermalCamera != null
            ? thermalCamera.GetComponent<DisplayOrbitCameraController>()
            : null;
        velocityCameraController = velocityCamera != null
            ? velocityCamera.GetComponent<DisplayOrbitCameraController>()
            : null;
    }

    private void OnVariableChanged(ResultsVariableSelection selection)
    {
        DisplayOrbitCameraController previous = GetSelectedCameraController();
        selectedVariable = selection.Variable;
        ResolveDisplayCameras();
        DisplayOrbitCameraController current = GetSelectedCameraController();
        if (previous != null && current != null && previous != current)
            current.CopyViewFrom(previous);
        ApplyVariableVisibility();
        Refresh(true);
    }

    private void ApplyVariableVisibility()
    {
        if (!HasSliceController)
            return;

        bool temperature = selectedVariable == ResultsVariable.Temperature;
        sliceController.SetTemperatureVisible(temperature);
        sliceController.SetVelocityVisible(!temperature);
    }

    private void SyncSliceControls()
    {
        if (!HasSliceController)
            return;

        syncingSliceUi = true;
        SetSliceUi(ResultsSliceAxis.X, sliceController.ShowVertical,
            sliceController.VerticalPositionNormalized);
        SetSliceUi(ResultsSliceAxis.Y, sliceController.ShowHorizontal,
            sliceController.HorizontalPositionNormalized);
        SetSliceUi(ResultsSliceAxis.Z, sliceController.ShowDepth,
            sliceController.DepthPositionNormalized);
        syncingSliceUi = false;
    }

    private void SetSliceUi(ResultsSliceAxis axis, bool visible, float position)
    {
        int index = (int)axis;
        sliceToggles[index].SetValueWithoutNotify(visible);
        positionSliders[index].SetValueWithoutNotify(position);
        positionValues[index].text = position.ToString("F2");
    }

    private DisplayOrbitCameraController GetSelectedCameraController()
    {
        return selectedVariable == ResultsVariable.Temperature
            ? thermalCameraController
            : velocityCameraController;
    }

    private void OnSliceXVisibilityChanged(ChangeEvent<bool> evt) =>
        ApplyVisibilityChange(ResultsSliceAxis.X, evt.newValue);

    private void OnSliceYVisibilityChanged(ChangeEvent<bool> evt) =>
        ApplyVisibilityChange(ResultsSliceAxis.Y, evt.newValue);

    private void OnSliceZVisibilityChanged(ChangeEvent<bool> evt) =>
        ApplyVisibilityChange(ResultsSliceAxis.Z, evt.newValue);

    private void ApplyVisibilityChange(ResultsSliceAxis axis, bool visible)
    {
        if (!syncingSliceUi)
            SetSliceVisible(axis, visible);
    }

    private void OnSliceXPositionChanged(ChangeEvent<float> evt) =>
        ApplyPositionChange(ResultsSliceAxis.X, evt.newValue);

    private void OnSliceYPositionChanged(ChangeEvent<float> evt) =>
        ApplyPositionChange(ResultsSliceAxis.Y, evt.newValue);

    private void OnSliceZPositionChanged(ChangeEvent<float> evt) =>
        ApplyPositionChange(ResultsSliceAxis.Z, evt.newValue);

    private void ApplyPositionChange(ResultsSliceAxis axis, float position)
    {
        if (!syncingSliceUi)
            SetSlicePosition(axis, position);
    }

    private void SelectOrbit()
    {
        interaction = ResultsCameraInteraction.Orbit;
        ApplyInteractionVisuals();
    }

    private void SelectPan()
    {
        interaction = ResultsCameraInteraction.Pan;
        ApplyInteractionVisuals();
    }

    private void FitView()
    {
        ResolveDisplayCameras();
        GetSelectedCameraController()?.FitView();
    }

    private void ResetView()
    {
        ResolveDisplayCameras();
        GetSelectedCameraController()?.ResetView();
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 && evt.button != 1)
            return;
        ResolveDisplayCameras();
        if (GetSelectedCameraController() == null)
            return;

        activePointerId = evt.pointerId;
        activePointerButton = evt.button;
        lastPointerPosition = new Vector2(evt.position.x, evt.position.y);
        sceneImage.CapturePointer(activePointerId);
        evt.StopPropagation();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (activePointerId < 0 || evt.pointerId != activePointerId)
            return;

        Vector2 current = new Vector2(evt.position.x, evt.position.y);
        Vector2 panelDelta = current - lastPointerPosition;
        lastPointerPosition = current;
        Vector2 cameraDelta = new Vector2(panelDelta.x, -panelDelta.y);
        float viewportHeight = Mathf.Max(1.0f, sceneImage.contentRect.height);
        DisplayOrbitCameraController controller = GetSelectedCameraController();
        bool pan = activePointerButton == 1 || interaction == ResultsCameraInteraction.Pan;
        if (pan)
            controller?.PanView(cameraDelta, viewportHeight);
        else
            controller?.OrbitView(cameraDelta, viewportHeight);
        evt.StopPropagation();
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (activePointerId >= 0 && evt.pointerId == activePointerId)
        {
            ReleasePointer();
            evt.StopPropagation();
        }
    }

    private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
    {
        activePointerId = -1;
        activePointerButton = -1;
    }

    private void OnWheel(WheelEvent evt)
    {
        ResolveDisplayCameras();
        DisplayOrbitCameraController controller = GetSelectedCameraController();
        if (controller == null || Mathf.Approximately(evt.delta.y, 0.0f))
            return;
        controller.ZoomView(-Mathf.Sign(evt.delta.y));
        evt.StopPropagation();
    }

    private void ReleasePointer()
    {
        if (activePointerId >= 0 && sceneImage.HasPointerCapture(activePointerId))
            sceneImage.ReleasePointer(activePointerId);
        activePointerId = -1;
        activePointerButton = -1;
    }

    private void ApplyInteractionVisuals()
    {
        orbitButton.EnableInClassList(
            "scene-toolbar-button--selected",
            interaction == ResultsCameraInteraction.Orbit);
        panButton.EnableInClassList(
            "scene-toolbar-button--selected",
            interaction == ResultsCameraInteraction.Pan);
    }

    private bool HasRequiredElements()
    {
        if (sceneImage == null || sliceStatus == null || fitButton == null ||
            orbitButton == null || panButton == null || resetButton == null)
        {
            return false;
        }

        for (int i = 0; i < sliceToggles.Length; i++)
        {
            if (sliceToggles[i] == null || positionSliders[i] == null || positionValues[i] == null)
                return false;
        }

        return true;
    }
}
