using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

public enum ResultsVariable
{
    Temperature,
    Velocity
}

public readonly struct ResultsVariableMetadata
{
    public ResultsVariable Variable { get; }
    public string DisplayName { get; }
    public string Unit { get; }
    public float Minimum { get; }
    public float Maximum { get; }
    public string DataSource { get; }

    public ResultsVariableMetadata(
        ResultsVariable variable,
        string displayName,
        string unit,
        float minimum,
        float maximum,
        string dataSource)
    {
        Variable = variable;
        DisplayName = displayName ?? string.Empty;
        Unit = unit ?? string.Empty;
        Minimum = minimum;
        Maximum = maximum;
        DataSource = dataSource ?? string.Empty;
    }

    public string FormatValue(float value)
    {
        string format = Variable == ResultsVariable.Temperature ? "0.0" : "0.00";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    public string FormatDataRange()
    {
        return $"{FormatValue(Minimum)} — {FormatValue(Maximum)} {Unit}";
    }
}

public readonly struct ResultsVariableSelection
{
    public ResultsVariable Variable { get; }
    public ResultsVariableMetadata Metadata { get; }

    public ResultsVariableSelection(ResultsVariable variable, ResultsVariableMetadata metadata)
    {
        Variable = variable;
        Metadata = metadata;
    }
}

public sealed class ResultsVariableSelectionState
{
    public event Action<ResultsVariableSelection> SelectionChanged;

    public bool HasSelection { get; private set; }
    public ResultsVariableSelection Current { get; private set; }

    internal void Set(ResultsVariableSelection selection)
    {
        Current = selection;
        HasSelection = true;
        SelectionChanged?.Invoke(selection);
    }
}

public interface IResultsVariableMetadataSource
{
    ResultsVariableMetadata Read(ResultsVariable variable);
}

public interface IResultsVisualizationTarget
{
    bool TryApply(
        ResultsVariableSelection selection,
        out Texture texture,
        out string sourceName,
        out string issue);
}

public sealed class SimulationResultsVariableMetadataSource : IResultsVariableMetadataSource
{
    private const float DefaultTemperatureMinDegC = 20.0f;
    private const float DefaultTemperatureMaxDegC = 35.0f;
    private const float DefaultVelocityMinMetersPerSecond = 0.0f;
    private const float DefaultVelocityMaxMetersPerSecond = 2.0f;

    private readonly Func<SimulationController> controllerSource;

    public SimulationResultsVariableMetadataSource(Func<SimulationController> source = null)
    {
        controllerSource = source ?? (() => SimulationController.Instance);
    }

    public ResultsVariableMetadata Read(ResultsVariable variable)
    {
        SimulationController controller = controllerSource();
        switch (variable)
        {
            case ResultsVariable.Temperature:
            {
                float minimum = controller != null
                    ? controller.TempPhysMinDegC
                    : DefaultTemperatureMinDegC;
                float maximum = controller != null
                    ? controller.TempPhysMaxDegC
                    : DefaultTemperatureMaxDegC;
                NormalizeRange(
                    ref minimum, ref maximum,
                    DefaultTemperatureMinDegC, DefaultTemperatureMaxDegC);
                return new ResultsVariableMetadata(
                    variable, "Temperature", "°C", minimum, maximum,
                    "ThermalSolver.ThermalTexture (RFloat, GPU)");
            }
            case ResultsVariable.Velocity:
            {
                float minimum = DefaultVelocityMinMetersPerSecond;
                float maximum = controller != null
                    ? controller.MaxWindSpeedPhys
                    : DefaultVelocityMaxMetersPerSecond;
                NormalizeRange(
                    ref minimum, ref maximum,
                    DefaultVelocityMinMetersPerSecond, DefaultVelocityMaxMetersPerSecond);
                return new ResultsVariableMetadata(
                    variable, "Velocity", "m/s", minimum, maximum,
                    "ThermalSolver.VelocityTexture (ARGBFloat, GPU)");
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(variable), variable, null);
        }
    }

    private static void NormalizeRange(
        ref float minimum,
        ref float maximum,
        float fallbackMinimum,
        float fallbackMaximum)
    {
        if (!IsFinite(minimum))
            minimum = fallbackMinimum;
        if (!IsFinite(maximum) || maximum <= minimum)
            maximum = Mathf.Max(fallbackMaximum, minimum + 0.01f);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

public sealed class SceneCameraResultsVisualizationTarget : IResultsVisualizationTarget
{
    private const string ThermalCameraName = "ThermalDisplayCamera";
    private const string VelocityCameraName = "VelocityDisplayCamera";

    public bool TryApply(
        ResultsVariableSelection selection,
        out Texture texture,
        out string sourceName,
        out string issue)
    {
        string cameraName = selection.Variable == ResultsVariable.Temperature
            ? ThermalCameraName
            : VelocityCameraName;
        Camera camera = FindCamera(cameraName);
        if (camera == null)
        {
            texture = null;
            sourceName = cameraName;
            issue = $"Waiting for {cameraName}.";
            return false;
        }

        texture = camera.targetTexture;
        sourceName = cameraName;
        if (texture == null)
        {
            issue = $"{cameraName} has no target texture.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private static Camera FindCamera(string cameraName)
    {
        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera != null && camera.name == cameraName)
                return camera;
        }

        return null;
    }
}

public sealed class ResultsVariableController : IDisposable
{
    private static readonly string[] UnsupportedButtonNames =
    {
        "PressureVariableButton",
        "StreamlineVariableButton",
        "ComfortVariableButton",
        "MassFluxVariableButton"
    };

    private readonly IResultsVariableMetadataSource metadataSource;
    private readonly IResultsVisualizationTarget visualizationTarget;
    private readonly Button temperatureButton;
    private readonly Button velocityButton;
    private readonly Image sceneImage;
    private readonly VisualElement scenePlaceholder;
    private readonly Label sceneSourceStatus;
    private readonly Label legendTitle;
    private readonly Label legendUnit;
    private readonly Label metadataUnit;
    private readonly Label metadataMin;
    private readonly Label metadataMax;
    private readonly Label metadataRange;
    private readonly Label variableStatus;
    private readonly Label[] legendValues;

    private ResultsVariableController(
        VisualElement root,
        IResultsVariableMetadataSource metadataSource,
        IResultsVisualizationTarget visualizationTarget)
    {
        this.metadataSource = metadataSource;
        this.visualizationTarget = visualizationTarget;
        temperatureButton = root.Q<Button>("TemperatureVariableButton");
        velocityButton = root.Q<Button>("VelocityVariableButton");
        sceneImage = root.Q<Image>("SceneViewImage");
        scenePlaceholder = root.Q<VisualElement>("ScenePlaceholder");
        sceneSourceStatus = root.Q<Label>("SceneSourceStatus");
        legendTitle = root.Q<Label>("ResultLegendTitle");
        legendUnit = root.Q<Label>("ResultLegendUnit");
        metadataUnit = root.Q<Label>("ResultMetadataUnitValue");
        metadataMin = root.Q<Label>("ResultMetadataMinValue");
        metadataMax = root.Q<Label>("ResultMetadataMaxValue");
        metadataRange = root.Q<Label>("ResultMetadataRangeValue");
        variableStatus = root.Q<Label>("ResultVariableStatus");
        legendValues = new[]
        {
            root.Q<Label>("ResultLegendMaxValue"),
            root.Q<Label>("ResultLegendUpperMidValue"),
            root.Q<Label>("ResultLegendMiddleHighValue"),
            root.Q<Label>("ResultLegendMiddleLowValue"),
            root.Q<Label>("ResultLegendLowerMidValue"),
            root.Q<Label>("ResultLegendMinValue")
        };

        for (int i = 0; i < UnsupportedButtonNames.Length; i++)
        {
            Button unsupported = root.Q<Button>(UnsupportedButtonNames[i]);
            unsupported?.SetEnabled(false);
        }
    }

    public ResultsVariableSelectionState State { get; } = new ResultsVariableSelectionState();
    public bool LastVisualizationApplied { get; private set; }
    public string LastVisualizationIssue { get; private set; } = string.Empty;

    public static bool TryCreate(
        VisualElement root,
        IResultsVariableMetadataSource metadataSource,
        IResultsVisualizationTarget visualizationTarget,
        out ResultsVariableController controller,
        out string issue)
    {
        controller = null;
        if (root == null || metadataSource == null || visualizationTarget == null)
        {
            issue = "Result variable UI root or data source is missing.";
            return false;
        }

        var candidate = new ResultsVariableController(root, metadataSource, visualizationTarget);
        if (!candidate.HasRequiredElements())
        {
            issue = "One or more Result Variable UI elements are missing.";
            return false;
        }

        candidate.temperatureButton.clicked += candidate.SelectTemperature;
        candidate.velocityButton.clicked += candidate.SelectVelocity;
        // The display cameras render to square textures. Crop the surplus edges so the
        // visualization fills the wide Results viewport instead of remaining letterboxed.
        candidate.sceneImage.scaleMode = ScaleMode.ScaleAndCrop;
        candidate.Select(ResultsVariable.Temperature);
        controller = candidate;
        issue = string.Empty;
        return true;
    }

    public bool Select(ResultsVariable variable)
    {
        ResultsVariableMetadata metadata = metadataSource.Read(variable);
        var selection = new ResultsVariableSelection(variable, metadata);
        UpdateSelectionVisuals(selection);
        State.Set(selection);
        ApplyVisualization(selection);
        return true;
    }

    public void Refresh()
    {
        if (!State.HasSelection)
            return;

        ResultsVariable variable = State.Current.Variable;
        ResultsVariableMetadata metadata = metadataSource.Read(variable);
        var selection = new ResultsVariableSelection(variable, metadata);
        UpdateSelectionVisuals(selection);
        State.Set(selection);
        ApplyVisualization(selection);
    }

    public void Dispose()
    {
        temperatureButton.clicked -= SelectTemperature;
        velocityButton.clicked -= SelectVelocity;
    }

    private void SelectTemperature()
    {
        Select(ResultsVariable.Temperature);
    }

    private void SelectVelocity()
    {
        Select(ResultsVariable.Velocity);
    }

    private void UpdateSelectionVisuals(ResultsVariableSelection selection)
    {
        ResultsVariableMetadata metadata = selection.Metadata;
        bool temperatureSelected = selection.Variable == ResultsVariable.Temperature;
        temperatureButton.EnableInClassList("result-variable-tab--selected", temperatureSelected);
        velocityButton.EnableInClassList("result-variable-tab--selected", !temperatureSelected);
        legendTitle.text = metadata.DisplayName;
        legendUnit.text = $"({metadata.Unit})";
        metadataUnit.text = metadata.Unit;
        metadataMin.text = metadata.FormatValue(metadata.Minimum);
        metadataMax.text = metadata.FormatValue(metadata.Maximum);
        metadataRange.text = metadata.FormatDataRange();

        for (int i = 0; i < legendValues.Length; i++)
        {
            float ratio = i / (float)(legendValues.Length - 1);
            float value = Mathf.Lerp(metadata.Maximum, metadata.Minimum, ratio);
            legendValues[i].text = metadata.FormatValue(value);
        }
    }

    private void ApplyVisualization(ResultsVariableSelection selection)
    {
        LastVisualizationApplied = visualizationTarget.TryApply(
            selection,
            out Texture texture,
            out string sourceName,
            out string issue);
        LastVisualizationIssue = issue ?? string.Empty;

        sceneImage.image = LastVisualizationApplied ? texture : null;
        bool hasTexture = LastVisualizationApplied && texture != null;
        scenePlaceholder.style.display = hasTexture ? DisplayStyle.None : DisplayStyle.Flex;
        sceneSourceStatus.text = hasTexture
            ? string.Empty
            : string.IsNullOrWhiteSpace(LastVisualizationIssue)
                ? $"Waiting for {sourceName}."
                : LastVisualizationIssue;
        variableStatus.text = hasTexture
            ? $"{selection.Metadata.DisplayName} · {sourceName}"
            : $"{selection.Metadata.DisplayName} · {sceneSourceStatus.text}";
        variableStatus.EnableInClassList("result-variable-status--ready", hasTexture);
    }

    private bool HasRequiredElements()
    {
        if (temperatureButton == null || velocityButton == null || sceneImage == null ||
            scenePlaceholder == null || sceneSourceStatus == null || legendTitle == null ||
            legendUnit == null || metadataUnit == null || metadataMin == null ||
            metadataMax == null || metadataRange == null || variableStatus == null)
        {
            return false;
        }

        for (int i = 0; i < legendValues.Length; i++)
        {
            if (legendValues[i] == null)
                return false;
        }

        return true;
    }
}
