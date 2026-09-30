using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class RuntimeSliceContourController : MonoBehaviour
{
    public enum SliceOrientation
    {
        Horizontal,
        VerticalYZ
    }

    [Header("Domain and Existing Planes")]
    [SerializeField] private Transform domainTransform;
    [SerializeField] private Renderer velocityHorizontalRenderer;
    [SerializeField] private Renderer velocityVerticalRenderer;
    [SerializeField] private Renderer temperatureHorizontalRenderer;
    [SerializeField] private Renderer temperatureVerticalRenderer;

    [Header("Existing Color Bars")]
    [SerializeField] private GameObject velocityColorBar;
    [SerializeField] private GameObject temperatureColorBar;

    [Header("Display Overlay Parents")]
    [SerializeField] private RectTransform velocityOverlayParent;
    [SerializeField] private RectTransform temperatureOverlayParent;
    [SerializeField] private bool buildRuntimeOverlays = true;

    [Header("Initial State")]
    [SerializeField] private bool showVelocity = true;
    [SerializeField] private bool showTemperature = true;
    [SerializeField] private bool showHorizontal = true;
    [SerializeField] private bool showVertical = true;

    [Header("Overlay Layout")]
    [SerializeField] private Vector2 overlaySize = new Vector2(440f, 74f);
    [Tooltip("Anchored position relative to the middle-right of each display panel.")]
    [SerializeField] private Vector2 overlayAnchoredPosition = new Vector2(-100f, 180f);

    private static readonly Color PanelColor = new Color(0.055f, 0.065f, 0.08f, 0.88f);
    private static readonly Color NormalButtonColor = new Color(0.18f, 0.21f, 0.25f, 0.96f);
    private static readonly Color SelectedButtonColor = new Color(0.10f, 0.48f, 0.82f, 0.98f);
    private static readonly Color SliderBackgroundColor = new Color(0.11f, 0.13f, 0.16f, 1f);
    private static readonly Color SliderFillColor = new Color(0.10f, 0.58f, 0.92f, 1f);

    private readonly List<OverlayControls> overlays = new List<OverlayControls>(2);

    private float horizontalPositionNormalized;
    private float verticalPositionNormalized;

    public bool ShowVelocity => showVelocity;
    public bool ShowTemperature => showTemperature;
    public bool ShowHorizontal => showHorizontal;
    public bool ShowVertical => showVertical;

    private void Start()
    {
        if (!HasRequiredReferences())
        {
            Debug.LogWarning(
                $"[Slice Contour][Case={GetCaseName()}] Required domain or contour plane references are missing.",
                this);
            enabled = false;
            return;
        }

        horizontalPositionNormalized = ReadNormalizedPosition(velocityHorizontalRenderer, SliceOrientation.Horizontal);
        verticalPositionNormalized = ReadNormalizedPosition(velocityVerticalRenderer, SliceOrientation.VerticalYZ);

        ApplyPlanePosition(SliceOrientation.Horizontal, horizontalPositionNormalized);
        ApplyPlanePosition(SliceOrientation.VerticalYZ, verticalPositionNormalized);

        if (buildRuntimeOverlays)
        {
            BuildOverlay(velocityOverlayParent);
            if (temperatureOverlayParent != velocityOverlayParent)
                BuildOverlay(temperatureOverlayParent);
        }

        ApplyVisibility();
        RefreshOverlays();
    }

    public void ToggleVelocity()
    {
        showVelocity = !showVelocity;
        ApplyVisibility();
        RefreshOverlays();
    }

    public void ToggleTemperature()
    {
        showTemperature = !showTemperature;
        ApplyVisibility();
        RefreshOverlays();
    }

    public void ToggleHorizontal()
    {
        showHorizontal = !showHorizontal;
        ApplyVisibility();
        RefreshOverlays();
    }

    public void ToggleVertical()
    {
        showVertical = !showVertical;
        ApplyVisibility();
        RefreshOverlays();
    }

    public void SetVelocityVisible(bool visible)
    {
        showVelocity = visible;
        ApplyVisibility();
        RefreshOverlays();
    }

    public void SetTemperatureVisible(bool visible)
    {
        showTemperature = visible;
        ApplyVisibility();
        RefreshOverlays();
    }

    public void SetHorizontalVisible(bool visible)
    {
        showHorizontal = visible;
        ApplyVisibility();
        RefreshOverlays();
    }

    public void SetVerticalVisible(bool visible)
    {
        showVertical = visible;
        ApplyVisibility();
        RefreshOverlays();
    }

    public void SetHorizontalPositionNormalized(float normalizedPosition)
    {
        horizontalPositionNormalized = Mathf.Clamp01(normalizedPosition);
        ApplyPlanePosition(SliceOrientation.Horizontal, horizontalPositionNormalized);
        RefreshOverlays();
    }

    public void SetVerticalPositionNormalized(float normalizedPosition)
    {
        verticalPositionNormalized = Mathf.Clamp01(normalizedPosition);
        ApplyPlanePosition(SliceOrientation.VerticalYZ, verticalPositionNormalized);
        RefreshOverlays();
    }

    private bool HasRequiredReferences()
    {
        return domainTransform != null
            && velocityHorizontalRenderer != null
            && velocityVerticalRenderer != null
            && temperatureHorizontalRenderer != null
            && temperatureVerticalRenderer != null;
    }

    private float ReadNormalizedPosition(Renderer planeRenderer, SliceOrientation orientation)
    {
        Vector3 domainLocalPosition = domainTransform.InverseTransformPoint(planeRenderer.transform.position);
        float coordinate = orientation == SliceOrientation.Horizontal
            ? domainLocalPosition.y
            : domainLocalPosition.x;
        return Mathf.Clamp01(coordinate + 0.5f);
    }

    private void ApplyPlanePosition(SliceOrientation orientation, float normalizedPosition)
    {
        float domainLocalCoordinate = Mathf.Clamp01(normalizedPosition) - 0.5f;

        if (orientation == SliceOrientation.Horizontal)
        {
            SetPlaneDomainCoordinate(velocityHorizontalRenderer.transform, orientation, domainLocalCoordinate);
            SetPlaneDomainCoordinate(temperatureHorizontalRenderer.transform, orientation, domainLocalCoordinate);
        }
        else
        {
            SetPlaneDomainCoordinate(velocityVerticalRenderer.transform, orientation, domainLocalCoordinate);
            SetPlaneDomainCoordinate(temperatureVerticalRenderer.transform, orientation, domainLocalCoordinate);
        }
    }

    private void SetPlaneDomainCoordinate(
        Transform planeTransform,
        SliceOrientation orientation,
        float domainLocalCoordinate)
    {
        Vector3 domainLocalPosition = domainTransform.InverseTransformPoint(planeTransform.position);
        if (orientation == SliceOrientation.Horizontal)
            domainLocalPosition.y = domainLocalCoordinate;
        else
            domainLocalPosition.x = domainLocalCoordinate;

        planeTransform.position = domainTransform.TransformPoint(domainLocalPosition);
    }

    private void ApplyVisibility()
    {
        velocityHorizontalRenderer.enabled = showVelocity && showHorizontal;
        velocityVerticalRenderer.enabled = showVelocity && showVertical;
        temperatureHorizontalRenderer.enabled = showTemperature && showHorizontal;
        temperatureVerticalRenderer.enabled = showTemperature && showVertical;

        if (velocityColorBar != null)
            velocityColorBar.SetActive(showVelocity && (showHorizontal || showVertical));
        if (temperatureColorBar != null)
            temperatureColorBar.SetActive(showTemperature && (showHorizontal || showVertical));
    }

    private void BuildOverlay(RectTransform parent)
    {
        if (parent == null)
            return;

        GameObject panelObject = CreateUiObject("RuntimeSliceContourOverlay", parent);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 0.5f);
        panelRect.anchorMax = new Vector2(1f, 0.5f);
        panelRect.pivot = new Vector2(1f, 0.5f);
        panelRect.anchoredPosition = overlayAnchoredPosition;
        panelRect.sizeDelta = overlaySize;

        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = PanelColor;
        panelImage.raycastTarget = true;

        Button velocityButton = CreateButton(panelRect, "VelocityButton", "Vel On", new Vector2(8f, 7f), new Vector2(100f, 24f));
        Button temperatureButton = CreateButton(panelRect, "TemperatureButton", "Temp On", new Vector2(116f, 7f), new Vector2(100f, 24f));
        Button horizontalButton = CreateButton(panelRect, "HorizontalButton", "H(XZ) On", new Vector2(224f, 7f), new Vector2(100f, 24f));
        Button verticalButton = CreateButton(panelRect, "VerticalButton", "V(YZ) On", new Vector2(332f, 7f), new Vector2(100f, 24f));

        TextMeshProUGUI horizontalPositionText = CreateText(
            panelRect,
            "HorizontalPositionText",
            string.Empty,
            new Vector2(8f, 39f),
            new Vector2(76f, 22f),
            12f,
            TextAlignmentOptions.MidlineLeft);
        Slider horizontalPositionSlider = CreateSlider(
            panelRect,
            "HorizontalPositionSlider",
            new Vector2(82f, 40f),
            new Vector2(126f, 20f));

        TextMeshProUGUI verticalPositionText = CreateText(
            panelRect,
            "VerticalPositionText",
            string.Empty,
            new Vector2(224f, 39f),
            new Vector2(76f, 22f),
            12f,
            TextAlignmentOptions.MidlineLeft);
        Slider verticalPositionSlider = CreateSlider(
            panelRect,
            "VerticalPositionSlider",
            new Vector2(298f, 40f),
            new Vector2(134f, 20f));

        velocityButton.onClick.AddListener(ToggleVelocity);
        temperatureButton.onClick.AddListener(ToggleTemperature);
        horizontalButton.onClick.AddListener(ToggleHorizontal);
        verticalButton.onClick.AddListener(ToggleVertical);
        horizontalPositionSlider.onValueChanged.AddListener(SetHorizontalPositionNormalized);
        verticalPositionSlider.onValueChanged.AddListener(SetVerticalPositionNormalized);

        overlays.Add(new OverlayControls
        {
            velocityButton = velocityButton,
            velocityButtonText = velocityButton.GetComponentInChildren<TextMeshProUGUI>(),
            temperatureButton = temperatureButton,
            temperatureButtonText = temperatureButton.GetComponentInChildren<TextMeshProUGUI>(),
            horizontalButton = horizontalButton,
            horizontalButtonText = horizontalButton.GetComponentInChildren<TextMeshProUGUI>(),
            verticalButton = verticalButton,
            verticalButtonText = verticalButton.GetComponentInChildren<TextMeshProUGUI>(),
            horizontalPositionSlider = horizontalPositionSlider,
            horizontalPositionText = horizontalPositionText,
            verticalPositionSlider = verticalPositionSlider,
            verticalPositionText = verticalPositionText
        });
    }

    private void RefreshOverlays()
    {
        float domainHeightMeters = domainTransform.TransformVector(Vector3.up).magnitude;
        float domainWidthMeters = domainTransform.TransformVector(Vector3.right).magnitude;
        float horizontalPositionMeters = horizontalPositionNormalized * domainHeightMeters;
        float verticalPositionMeters = verticalPositionNormalized * domainWidthMeters;

        foreach (OverlayControls overlay in overlays)
        {
            SetButtonState(overlay.velocityButton, overlay.velocityButtonText, "Vel", showVelocity);
            SetButtonState(overlay.temperatureButton, overlay.temperatureButtonText, "Temp", showTemperature);
            SetButtonState(overlay.horizontalButton, overlay.horizontalButtonText, "H(XZ)", showHorizontal);
            SetButtonState(overlay.verticalButton, overlay.verticalButtonText, "V(YZ)", showVertical);

            overlay.horizontalPositionText.text = $"Y {horizontalPositionMeters:F2}m";
            overlay.verticalPositionText.text = $"X {verticalPositionMeters:F2}m";
            overlay.horizontalPositionSlider.SetValueWithoutNotify(horizontalPositionNormalized);
            overlay.verticalPositionSlider.SetValueWithoutNotify(verticalPositionNormalized);
        }
    }

    private static GameObject CreateUiObject(string name, RectTransform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        return child;
    }

    private static TextMeshProUGUI CreateText(
        RectTransform parent,
        string name,
        string value,
        Vector2 topLeftPosition,
        Vector2 size,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateUiObject(name, parent);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        SetTopLeftRect(rect, topLeftPosition, size);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(
        RectTransform parent,
        string name,
        string label,
        Vector2 topLeftPosition,
        Vector2 size)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        SetTopLeftRect(rect, topLeftPosition, size);

        Image image = buttonObject.AddComponent<Image>();
        image.color = NormalButtonColor;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        TextMeshProUGUI text = CreateText(
            rect,
            "Label",
            label,
            Vector2.zero,
            size,
            14f,
            TextAlignmentOptions.Center);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;
        return button;
    }

    private static Slider CreateSlider(
        RectTransform parent,
        string name,
        Vector2 topLeftPosition,
        Vector2 size)
    {
        GameObject sliderObject = CreateUiObject(name, parent);
        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        SetTopLeftRect(sliderRect, topLeftPosition, size);

        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.direction = Slider.Direction.LeftToRight;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };

        GameObject backgroundObject = CreateUiObject("Background", sliderRect);
        RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
        SetStretchedRect(backgroundRect, 0f, 0f, 6f, 6f);
        Image backgroundImage = backgroundObject.AddComponent<Image>();
        backgroundImage.color = SliderBackgroundColor;
        backgroundImage.raycastTarget = false;

        GameObject fillAreaObject = CreateUiObject("Fill Area", sliderRect);
        RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
        SetStretchedRect(fillAreaRect, 8f, 8f, 6f, 6f);

        GameObject fillObject = CreateUiObject("Fill", fillAreaRect);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = Vector2.zero;
        Image fillImage = fillObject.AddComponent<Image>();
        fillImage.color = SliderFillColor;
        fillImage.raycastTarget = false;

        GameObject handleAreaObject = CreateUiObject("Handle Slide Area", sliderRect);
        RectTransform handleAreaRect = handleAreaObject.GetComponent<RectTransform>();
        SetStretchedRect(handleAreaRect, 8f, 8f, 0f, 0f);

        GameObject handleObject = CreateUiObject("Handle", handleAreaRect);
        RectTransform handleRect = handleObject.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(16f, 20f);
        Image handleImage = handleObject.AddComponent<Image>();
        handleImage.color = Color.white;

        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        return slider;
    }

    private static void SetTopLeftRect(RectTransform rect, Vector2 topLeftPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(topLeftPosition.x, -topLeftPosition.y);
        rect.sizeDelta = size;
    }

    private static void SetStretchedRect(
        RectTransform rect,
        float left,
        float right,
        float bottom,
        float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void SetButtonState(Button button, TextMeshProUGUI label, string name, bool enabled)
    {
        if (button != null && button.image != null)
            button.image.color = enabled ? SelectedButtonColor : NormalButtonColor;
        if (label != null)
            label.text = $"{name} {(enabled ? "On" : "Off")}";
    }

    private static string GetCaseName()
    {
        SimulationController controller = SimulationController.Instance;
        return controller != null && !string.IsNullOrWhiteSpace(controller.ActiveCaseName)
            ? controller.ActiveCaseName
            : "Manual";
    }

    private void OnValidate()
    {
        overlaySize.x = Mathf.Max(420f, overlaySize.x);
        overlaySize.y = Mathf.Max(68f, overlaySize.y);
    }

    private sealed class OverlayControls
    {
        public Button velocityButton;
        public TextMeshProUGUI velocityButtonText;
        public Button temperatureButton;
        public TextMeshProUGUI temperatureButtonText;
        public Button horizontalButton;
        public TextMeshProUGUI horizontalButtonText;
        public Button verticalButton;
        public TextMeshProUGUI verticalButtonText;
        public Slider horizontalPositionSlider;
        public TextMeshProUGUI horizontalPositionText;
        public Slider verticalPositionSlider;
        public TextMeshProUGUI verticalPositionText;
    }
}
