using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public enum ResultsColorMapPreset
{
    Jet,
    Viridis,
    Grayscale
}

public enum ResultsColorRangeMode
{
    Auto,
    Manual
}

public readonly struct ResultsColorMapSettings
{
    public ResultsColorMapSettings(
        ResultsColorMapPreset preset,
        ResultsColorRangeMode rangeMode,
        float minimum,
        float maximum)
    {
        Preset = preset;
        RangeMode = rangeMode;
        Minimum = minimum;
        Maximum = maximum;
    }

    public ResultsColorMapPreset Preset { get; }
    public ResultsColorRangeMode RangeMode { get; }
    public float Minimum { get; }
    public float Maximum { get; }
}

public interface IResultsColorMapTarget : IDisposable
{
    bool TryApply(
        ResultsVariableSelection selection,
        ResultsColorMapSettings settings,
        Texture2D replacementColorMap,
        out string targetName,
        out string issue);
}

public sealed class ExistingSliceColorMapTarget : IResultsColorMapTarget
{
    private const string ColorMapProperty = "_ColormapTex";
    private readonly Dictionary<Material, Texture> originalColorMaps =
        new Dictionary<Material, Texture>();

    public bool TryApply(
        ResultsVariableSelection selection,
        ResultsColorMapSettings settings,
        Texture2D replacementColorMap,
        out string targetName,
        out string issue)
    {
        bool manual = settings.RangeMode == ResultsColorRangeMode.Manual;
        int appliedCount = selection.Variable == ResultsVariable.Temperature
            ? ApplyTemperature(manual, settings, replacementColorMap)
            : ApplyVelocity(manual, settings, replacementColorMap);

        targetName = selection.Variable == ResultsVariable.Temperature
            ? nameof(ThermalVisualizer)
            : nameof(VelocityVisualizer);
        if (appliedCount == 0)
        {
            issue = $"Waiting for {targetName}.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    public void Dispose()
    {
        foreach (KeyValuePair<Material, Texture> entry in originalColorMaps)
        {
            if (entry.Key != null && entry.Key.HasProperty(ColorMapProperty))
                entry.Key.SetTexture(ColorMapProperty, entry.Value);
        }

        ThermalVisualizer[] thermalVisualizers = UnityEngine.Object.FindObjectsByType<ThermalVisualizer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < thermalVisualizers.Length; i++)
            thermalVisualizers[i]?.SetDisplayRangeOverride(false, 0.0f, 1.0f);

        VelocityVisualizer[] velocityVisualizers = UnityEngine.Object.FindObjectsByType<VelocityVisualizer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < velocityVisualizers.Length; i++)
            velocityVisualizers[i]?.SetDisplayRangeOverride(false, 0.0f, 1.0f);

        originalColorMaps.Clear();
    }

    private int ApplyTemperature(
        bool manual,
        ResultsColorMapSettings settings,
        Texture2D replacementColorMap)
    {
        ThermalVisualizer[] visualizers = UnityEngine.Object.FindObjectsByType<ThermalVisualizer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        int appliedCount = 0;
        for (int i = 0; i < visualizers.Length; i++)
        {
            ThermalVisualizer visualizer = visualizers[i];
            if (visualizer == null)
                continue;

            visualizer.SetDisplayRangeOverride(manual, settings.Minimum, settings.Maximum);
            if (ApplyColorMap(visualizer.GetComponent<Renderer>(), settings.Preset, replacementColorMap))
                appliedCount++;
        }

        return appliedCount;
    }

    private int ApplyVelocity(
        bool manual,
        ResultsColorMapSettings settings,
        Texture2D replacementColorMap)
    {
        VelocityVisualizer[] visualizers = UnityEngine.Object.FindObjectsByType<VelocityVisualizer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        int appliedCount = 0;
        for (int i = 0; i < visualizers.Length; i++)
        {
            VelocityVisualizer visualizer = visualizers[i];
            if (visualizer == null)
                continue;

            visualizer.SetDisplayRangeOverride(manual, settings.Minimum, settings.Maximum);
            if (ApplyColorMap(visualizer.GetComponent<Renderer>(), settings.Preset, replacementColorMap))
                appliedCount++;
        }

        return appliedCount;
    }

    private bool ApplyColorMap(
        Renderer renderer,
        ResultsColorMapPreset preset,
        Texture2D replacementColorMap)
    {
        if (renderer == null)
            return false;

        Material material = renderer.material;
        if (material == null || !material.HasProperty(ColorMapProperty))
            return false;

        if (!originalColorMaps.ContainsKey(material))
            originalColorMaps.Add(material, material.GetTexture(ColorMapProperty));

        Texture texture = preset == ResultsColorMapPreset.Jet
            ? originalColorMaps[material]
            : replacementColorMap;
        if (texture != null)
            material.SetTexture(ColorMapProperty, texture);
        return true;
    }
}

public sealed class ResultsColorMapController : IDisposable
{
    private static readonly string[] PresetChoices =
    {
        "Jet (Existing)",
        "Viridis",
        "Grayscale"
    };

    private readonly VisualElement root;
    private readonly ResultsVariableSelectionState variableState;
    private readonly IResultsColorMapTarget target;
    private readonly Dictionary<ResultsVariable, ResultsColorMapSettings> settingsByVariable =
        new Dictionary<ResultsVariable, ResultsColorMapSettings>();
    private readonly Dictionary<ResultsColorMapPreset, Texture2D> generatedColorMaps =
        new Dictionary<ResultsColorMapPreset, Texture2D>();
    private readonly DropdownField presetField;
    private readonly Button autoButton;
    private readonly Button manualButton;
    private readonly FloatField minimumField;
    private readonly FloatField maximumField;
    private readonly Label statusLabel;
    private readonly Label[] legendValues;
    private readonly VisualElement[] legendSegments;
    private bool updatingUi;

    private ResultsColorMapController(
        VisualElement root,
        ResultsVariableSelectionState variableState,
        IResultsColorMapTarget target)
    {
        this.root = root;
        this.variableState = variableState;
        this.target = target;
        presetField = root.Q<DropdownField>("ColorMapPresetField");
        autoButton = root.Q<Button>("ColorRangeAutoButton");
        manualButton = root.Q<Button>("ColorRangeManualButton");
        minimumField = root.Q<FloatField>("ColorRangeMinField");
        maximumField = root.Q<FloatField>("ColorRangeMaxField");
        statusLabel = root.Q<Label>("ColorMapStatus");
        legendValues = new[]
        {
            root.Q<Label>("ResultLegendMaxValue"),
            root.Q<Label>("ResultLegendUpperMidValue"),
            root.Q<Label>("ResultLegendMiddleHighValue"),
            root.Q<Label>("ResultLegendMiddleLowValue"),
            root.Q<Label>("ResultLegendLowerMidValue"),
            root.Q<Label>("ResultLegendMinValue")
        };
        legendSegments = new[]
        {
            root.Q<VisualElement>("ResultLegendColor0"),
            root.Q<VisualElement>("ResultLegendColor1"),
            root.Q<VisualElement>("ResultLegendColor2"),
            root.Q<VisualElement>("ResultLegendColor3"),
            root.Q<VisualElement>("ResultLegendColor4"),
            root.Q<VisualElement>("ResultLegendColor5")
        };
    }

    public ResultsColorMapSettings CurrentSettings { get; private set; }
    public bool LastApplySucceeded { get; private set; }
    public string LastApplyIssue { get; private set; } = string.Empty;

    public static bool TryCreate(
        VisualElement root,
        ResultsVariableSelectionState variableState,
        IResultsColorMapTarget target,
        out ResultsColorMapController controller,
        out string issue)
    {
        controller = null;
        if (root == null || variableState == null || target == null || !variableState.HasSelection)
        {
            issue = "Color Map UI root, Result Variable state, or visualization target is missing.";
            return false;
        }

        var candidate = new ResultsColorMapController(root, variableState, target);
        if (!candidate.HasRequiredElements())
        {
            issue = "One or more Color Map UI elements are missing.";
            return false;
        }

        candidate.presetField.choices = new List<string>(PresetChoices);
        candidate.presetField.RegisterValueChangedCallback(candidate.OnPresetChanged);
        candidate.minimumField.RegisterValueChangedCallback(candidate.OnMinimumChanged);
        candidate.maximumField.RegisterValueChangedCallback(candidate.OnMaximumChanged);
        candidate.autoButton.clicked += candidate.SelectAutoRange;
        candidate.manualButton.clicked += candidate.SelectManualRange;
        candidate.variableState.SelectionChanged += candidate.OnVariableSelectionChanged;
        candidate.SyncToSelection(candidate.variableState.Current, true);
        controller = candidate;
        issue = string.Empty;
        return true;
    }

    public bool SelectPreset(ResultsColorMapPreset preset)
    {
        if (!variableState.HasSelection || !Enum.IsDefined(typeof(ResultsColorMapPreset), preset))
            return false;

        ResultsVariableSelection selection = variableState.Current;
        ResultsColorMapSettings settings = GetOrCreateSettings(selection);
        settings = new ResultsColorMapSettings(
            preset, settings.RangeMode, settings.Minimum, settings.Maximum);
        settingsByVariable[selection.Variable] = settings;
        SyncToSelection(selection, true);
        return true;
    }

    public bool SetRangeMode(ResultsColorRangeMode mode)
    {
        if (!variableState.HasSelection || !Enum.IsDefined(typeof(ResultsColorRangeMode), mode))
            return false;

        ResultsVariableSelection selection = variableState.Current;
        ResultsColorMapSettings settings = GetOrCreateSettings(selection);
        float minimum = settings.Minimum;
        float maximum = settings.Maximum;
        if (mode == ResultsColorRangeMode.Auto)
        {
            minimum = selection.Metadata.Minimum;
            maximum = selection.Metadata.Maximum;
        }
        else if (!TryReadManualRange(out minimum, out maximum))
        {
            SetStatus("Manual range requires finite values with Max greater than Min.", false, true);
            return false;
        }

        settingsByVariable[selection.Variable] = new ResultsColorMapSettings(
            settings.Preset, mode, minimum, maximum);
        SyncToSelection(selection, true);
        return true;
    }

    public bool TrySetManualRange(float minimum, float maximum)
    {
        if (!variableState.HasSelection || !IsValidRange(minimum, maximum))
        {
            SetStatus("Manual range requires finite values with Max greater than Min.", false, true);
            return false;
        }

        ResultsVariableSelection selection = variableState.Current;
        ResultsColorMapSettings settings = GetOrCreateSettings(selection);
        settingsByVariable[selection.Variable] = new ResultsColorMapSettings(
            settings.Preset, ResultsColorRangeMode.Manual, minimum, maximum);
        SyncToSelection(selection, true);
        return true;
    }

    public ResultsColorMapSettings GetSettings(ResultsVariable variable)
    {
        return settingsByVariable.TryGetValue(variable, out ResultsColorMapSettings settings)
            ? settings
            : default;
    }

    public void Dispose()
    {
        presetField.UnregisterValueChangedCallback(OnPresetChanged);
        minimumField.UnregisterValueChangedCallback(OnMinimumChanged);
        maximumField.UnregisterValueChangedCallback(OnMaximumChanged);
        autoButton.clicked -= SelectAutoRange;
        manualButton.clicked -= SelectManualRange;
        variableState.SelectionChanged -= OnVariableSelectionChanged;
        target.Dispose();

        foreach (Texture2D texture in generatedColorMaps.Values)
        {
            if (texture == null)
                continue;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }

        generatedColorMaps.Clear();
    }

    private void OnVariableSelectionChanged(ResultsVariableSelection selection)
    {
        SyncToSelection(selection, true);
    }

    private void OnPresetChanged(ChangeEvent<string> evt)
    {
        if (updatingUi)
            return;

        int index = Array.IndexOf(PresetChoices, evt.newValue);
        if (index >= 0)
            SelectPreset((ResultsColorMapPreset)index);
    }

    private void OnMinimumChanged(ChangeEvent<float> evt)
    {
        ApplyRangeFieldEdit();
    }

    private void OnMaximumChanged(ChangeEvent<float> evt)
    {
        ApplyRangeFieldEdit();
    }

    private void ApplyRangeFieldEdit()
    {
        if (updatingUi || !variableState.HasSelection)
            return;

        ResultsColorMapSettings settings = GetOrCreateSettings(variableState.Current);
        if (settings.RangeMode == ResultsColorRangeMode.Manual)
            TrySetManualRange(minimumField.value, maximumField.value);
    }

    private void SelectAutoRange()
    {
        SetRangeMode(ResultsColorRangeMode.Auto);
    }

    private void SelectManualRange()
    {
        SetRangeMode(ResultsColorRangeMode.Manual);
    }

    private void SyncToSelection(ResultsVariableSelection selection, bool applyTarget)
    {
        ResultsColorMapSettings settings = GetOrCreateSettings(selection);
        CurrentSettings = settings;
        updatingUi = true;
        presetField.SetValueWithoutNotify(PresetChoices[(int)settings.Preset]);
        minimumField.SetValueWithoutNotify(settings.Minimum);
        maximumField.SetValueWithoutNotify(settings.Maximum);
        bool manual = settings.RangeMode == ResultsColorRangeMode.Manual;
        minimumField.SetEnabled(manual);
        maximumField.SetEnabled(manual);
        autoButton.EnableInClassList("color-range-mode-button--selected", !manual);
        manualButton.EnableInClassList("color-range-mode-button--selected", manual);
        UpdateLegend(selection.Metadata, settings);
        updatingUi = false;

        if (applyTarget)
            ApplyTarget(selection, settings);
    }

    private ResultsColorMapSettings GetOrCreateSettings(ResultsVariableSelection selection)
    {
        if (settingsByVariable.TryGetValue(selection.Variable, out ResultsColorMapSettings settings))
            return settings;

        settings = new ResultsColorMapSettings(
            ResultsColorMapPreset.Jet,
            ResultsColorRangeMode.Auto,
            selection.Metadata.Minimum,
            selection.Metadata.Maximum);
        settingsByVariable.Add(selection.Variable, settings);
        return settings;
    }

    private void ApplyTarget(
        ResultsVariableSelection selection,
        ResultsColorMapSettings settings)
    {
        Texture2D colorMap = settings.Preset == ResultsColorMapPreset.Jet
            ? null
            : GetOrCreateColorMap(settings.Preset);
        LastApplySucceeded = target.TryApply(
            selection,
            settings,
            colorMap,
            out string targetName,
            out string issue);
        LastApplyIssue = issue ?? string.Empty;

        if (!LastApplySucceeded)
        {
            SetStatus(string.IsNullOrWhiteSpace(LastApplyIssue)
                ? $"Waiting for {targetName}."
                : LastApplyIssue, false, false);
            return;
        }

        string rangeDescription = settings.RangeMode == ResultsColorRangeMode.Auto
            ? "Auto uses configured range; live data reduction is deferred."
            : $"Manual range applied to {targetName}.";
        SetStatus(rangeDescription, true, false);
    }

    private void UpdateLegend(
        ResultsVariableMetadata metadata,
        ResultsColorMapSettings settings)
    {
        for (int i = 0; i < legendValues.Length; i++)
        {
            float ratio = i / (float)(legendValues.Length - 1);
            float value = Mathf.Lerp(settings.Maximum, settings.Minimum, ratio);
            legendValues[i].text = metadata.FormatValue(value);
            legendSegments[i].style.backgroundColor = EvaluateColor(
                settings.Preset,
                1.0f - ratio);
        }
    }

    private Texture2D GetOrCreateColorMap(ResultsColorMapPreset preset)
    {
        if (generatedColorMaps.TryGetValue(preset, out Texture2D texture) && texture != null)
            return texture;

        const int width = 256;
        texture = new Texture2D(width, 1, TextureFormat.RGBA32, false, true)
        {
            name = $"UX03_{preset}_ColorMap",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
        var colors = new Color[width];
        for (int i = 0; i < width; i++)
            colors[i] = EvaluateColor(preset, i / (float)(width - 1));
        texture.SetPixels(colors);
        texture.Apply(false, true);
        generatedColorMaps[preset] = texture;
        return texture;
    }

    private void SetStatus(string message, bool ready, bool error)
    {
        statusLabel.text = message ?? string.Empty;
        statusLabel.EnableInClassList("color-map-status--ready", ready);
        statusLabel.EnableInClassList("color-map-status--error", error);
    }

    private bool TryReadManualRange(out float minimum, out float maximum)
    {
        minimum = minimumField.value;
        maximum = maximumField.value;
        return IsValidRange(minimum, maximum);
    }

    private bool HasRequiredElements()
    {
        if (root.Q<VisualElement>("ColorMapControls") == null ||
            presetField == null || autoButton == null || manualButton == null ||
            minimumField == null || maximumField == null || statusLabel == null)
        {
            return false;
        }

        for (int i = 0; i < legendValues.Length; i++)
        {
            if (legendValues[i] == null || legendSegments[i] == null)
                return false;
        }

        return true;
    }

    private static bool IsValidRange(float minimum, float maximum)
    {
        return IsFinite(minimum) && IsFinite(maximum) && maximum > minimum;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static Color EvaluateColor(ResultsColorMapPreset preset, float value)
    {
        value = Mathf.Clamp01(value);
        switch (preset)
        {
            case ResultsColorMapPreset.Viridis:
                return InterpolateStops(value, ViridisStops);
            case ResultsColorMapPreset.Grayscale:
                return new Color(value, value, value, 1.0f);
            default:
                return InterpolateStops(value, JetStops);
        }
    }

    private static Color InterpolateStops(float value, Color[] stops)
    {
        float scaled = value * (stops.Length - 1);
        int lower = Mathf.Min(Mathf.FloorToInt(scaled), stops.Length - 2);
        return Color.Lerp(stops[lower], stops[lower + 1], scaled - lower);
    }

    private static readonly Color[] JetStops =
    {
        new Color32(37, 61, 230, 255),
        new Color32(21, 188, 231, 255),
        new Color32(49, 215, 91, 255),
        new Color32(255, 235, 54, 255),
        new Color32(255, 126, 30, 255),
        new Color32(255, 35, 46, 255)
    };

    private static readonly Color[] ViridisStops =
    {
        new Color32(68, 1, 84, 255),
        new Color32(59, 82, 139, 255),
        new Color32(33, 145, 140, 255),
        new Color32(94, 201, 98, 255),
        new Color32(170, 220, 50, 255),
        new Color32(253, 231, 37, 255)
    };
}
