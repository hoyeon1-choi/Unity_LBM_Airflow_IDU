using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class ResultsLayoutValidation
{
    private const string LogTag = "[UX03][F01][T07][Case=LayoutValidation]";
    private const string UxmlAssetPath = "Assets/UI/UX03/Results.uxml";
    private const string RuntimeUxmlAssetPath = "Assets/UI/UX03/Resources/ResultsRuntime.uxml";
    private const string UssAssetPath = "Assets/UI/UX03/Results.uss";
    private const string PanelSettingsAssetPath =
        "Assets/UI/UX01/Resources/MainDashboardPanelSettings.asset";
    private const string RunningKey = "UX03.F01.T07.ValidationRunning";
    private const string BatchKey = "UX03.F01.T07.ValidationBatch";
    private const double TimeoutSeconds = 45.0;
    private const int SettleFrameCount = 5;

    private static readonly Vector2Int[] TestResolutions =
    {
        new Vector2Int(960, 540),
        new Vector2Int(1280, 720),
        new Vector2Int(1920, 1080)
    };

    private static ResultsRuntimeDocument runtimeDocument;
    private static UIDocument uiDocument;
    private static RenderTexture validationTarget;
    private static double deadline;
    private static int resolutionIndex = -1;
    private static int settleFrames;
    private static int exitCode;
    private static bool runtimeMountValidated;
    private static bool variableSelectionValidated;
    private static RecordingVisualizationTarget visualizationTarget;
    private static RecordingColorMapTarget colorMapTarget;

    [InitializeOnLoadMethod]
    private static void ResumeAfterDomainReload()
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;

        SubscribeToPlayMode();
        if (EditorApplication.isPlaying)
            EditorApplication.delayCall += BeginPlayModeValidation;
    }

    [MenuItem("Tools/UX-03/Validate F01 Results Layout")]
    public static void RunFromMenu()
    {
        StartValidation(false);
    }

    [MenuItem("Tools/UX-03/Validate F02 Result Variables")]
    public static void RunF02FromMenu()
    {
        StartValidation(false);
    }

    [MenuItem("Tools/UX-03/Validate F03 Color Map")]
    public static void RunF03FromMenu()
    {
        StartValidation(false);
    }

    public static void RunBatchValidation()
    {
        StartValidation(true);
    }

    private static void StartValidation(bool exitWhenFinished)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{LogTag} Validation requires Edit Mode.");
            if (exitWhenFinished)
                EditorApplication.Exit(2);
            return;
        }

        if (!ValidateAssets(out string issue))
        {
            Debug.LogError($"{LogTag} {issue}");
            if (exitWhenFinished)
                EditorApplication.Exit(3);
            return;
        }

        SessionState.SetBool(RunningKey, true);
        SessionState.SetBool(BatchKey, exitWhenFinished);
        exitCode = 0;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SubscribeToPlayMode();
        EditorApplication.EnterPlaymode();
    }

    private static bool ValidateAssets(out string issue)
    {
        VisualTreeAsset source = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlAssetPath);
        if (source == null)
        {
            issue = $"UXML asset is missing or invalid: {UxmlAssetPath}";
            return false;
        }

        if (AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RuntimeUxmlAssetPath) == null)
        {
            issue = $"Runtime UXML asset is missing or invalid: {RuntimeUxmlAssetPath}";
            return false;
        }

        if (AssetDatabase.LoadAssetAtPath<StyleSheet>(UssAssetPath) == null)
        {
            issue = $"USS asset is missing or invalid: {UssAssetPath}";
            return false;
        }

        if (AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsAssetPath) == null)
        {
            issue = $"PanelSettings asset is missing: {PanelSettingsAssetPath}";
            return false;
        }

        var cloneRoot = new VisualElement();
        source.CloneTree(cloneRoot);
        string[] structuralNames =
        {
            "ResultsDashboard", "ResultVariableTabs", "SceneViewport",
            "SceneControlPanel", "SliceXToggle", "SliceYToggle", "SliceZToggle",
            "SliceControlStatus", "SceneToolbar",
            "ResultsAnalyticsSidebar", "ResultLegendPanel"
        };

        for (int i = 0; i < structuralNames.Length; i++)
        {
            if (cloneRoot.Q<VisualElement>(structuralNames[i]) == null)
            {
                issue = $"UXML structural element is missing: {structuralNames[i]}.";
                return false;
            }
        }

        issue = string.Empty;
        return true;
    }

    private static void SubscribeToPlayMode()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
            BeginPlayModeValidation();
        else if (state == PlayModeStateChange.EnteredEditMode &&
                 SessionState.GetBool(RunningKey, false))
            FinishInEditMode();
    }

    private static void BeginPlayModeValidation()
    {
        if (!EditorApplication.isPlaying || runtimeDocument != null)
            return;

        PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsAssetPath);
        BeginResolution(0, settings);

        GameObject host = new GameObject("UX03_F01_ResultsLayoutValidationHost");
        host.SetActive(false);
        runtimeDocument = host.AddComponent<ResultsRuntimeDocument>();
        visualizationTarget = new RecordingVisualizationTarget();
        colorMapTarget = new RecordingColorMapTarget();
        runtimeDocument.ConfigureVariableSources(
            new ValidationMetadataSource(),
            visualizationTarget);
        runtimeDocument.ConfigureColorMapTarget(colorMapTarget);
        host.SetActive(true);
        deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
        runtimeMountValidated = false;
        variableSelectionValidated = false;
        EditorApplication.update -= PollValidation;
        EditorApplication.update += PollValidation;
    }

    private static void PollValidation()
    {
        if (!EditorApplication.isPlaying)
            return;

        if (EditorApplication.timeSinceStartup >= deadline)
        {
            Fail("Validation timed out.");
            return;
        }

        if (runtimeDocument == null || !runtimeDocument.IsReady)
            return;

        if (uiDocument == null)
            uiDocument = runtimeDocument.Document;

        if (!runtimeMountValidated)
        {
            if (!ValidateRuntimeMount(out string runtimeMountIssue))
            {
                Fail(runtimeMountIssue);
                return;
            }

            runtimeMountValidated = true;
            settleFrames = SettleFrameCount;
            return;
        }

        if (!variableSelectionValidated)
        {
            if (!ValidateVariableSelection(out string variableIssue))
            {
                Fail(variableIssue);
                return;
            }

            if (!ValidateColorMap(out string colorMapIssue))
            {
                Fail(colorMapIssue);
                return;
            }

            variableSelectionValidated = true;
            settleFrames = SettleFrameCount;
            return;
        }

        if (settleFrames-- > 0)
            return;

        if (!runtimeDocument.TryValidateCurrentLayout(out string issue))
        {
            Vector2Int failedResolution = TestResolutions[resolutionIndex];
            Fail($"{failedResolution.x}x{failedResolution.y}: {issue}");
            return;
        }

        VisualElement root = uiDocument.rootVisualElement.Q<VisualElement>("ResultsDashboard");
        Vector2Int resolution = TestResolutions[resolutionIndex];
        Debug.Log(
            $"{LogTag} PASS target={resolution.x}x{resolution.y}, " +
            $"resolved={root.resolvedStyle.width:F0}x{root.resolvedStyle.height:F0}.");

        int nextIndex = resolutionIndex + 1;
        if (nextIndex < TestResolutions.Length)
        {
            BeginResolution(nextIndex, uiDocument.panelSettings);
            return;
        }

        Debug.Log($"{LogTag} PASS all {TestResolutions.Length} resolutions.");
        CompletePlayModeValidation();
    }

    private static bool ValidateVariableSelection(out string issue)
    {
        ResultsVariableController controller = runtimeDocument.VariableController;
        if (controller == null || !controller.State.HasSelection ||
            controller.State.Current.Variable != ResultsVariable.Temperature)
        {
            issue = "Temperature was not established as the initial Result Variable state.";
            return false;
        }

        VisualElement root = uiDocument.rootVisualElement;
        Button pressure = root.Q<Button>("PressureVariableButton");
        Button streamline = root.Q<Button>("StreamlineVariableButton");
        Button comfort = root.Q<Button>("ComfortVariableButton");
        Button massFlux = root.Q<Button>("MassFluxVariableButton");
        if (pressure == null || pressure.enabledSelf ||
            streamline == null || streamline.enabledSelf ||
            comfort == null || comfort.enabledSelf ||
            massFlux == null || massFlux.enabledSelf)
        {
            issue = "Unsupported Result Variable buttons are not disabled.";
            return false;
        }

        int eventCount = 0;
        controller.State.SelectionChanged += _ => eventCount++;
        if (!controller.Select(ResultsVariable.Velocity) ||
            controller.State.Current.Variable != ResultsVariable.Velocity ||
            visualizationTarget.LastVariable != ResultsVariable.Velocity ||
            eventCount != 1)
        {
            issue = "Velocity selection state or visualization dispatch is inconsistent.";
            return false;
        }

        Label title = root.Q<Label>("ResultLegendTitle");
        Label unit = root.Q<Label>("ResultMetadataUnitValue");
        Label minimum = root.Q<Label>("ResultMetadataMinValue");
        Label maximum = root.Q<Label>("ResultMetadataMaxValue");
        Label range = root.Q<Label>("ResultMetadataRangeValue");
        Label status = root.Q<Label>("ResultVariableStatus");
        if (title == null || title.text != "Velocity" ||
            unit == null || unit.text != "m/s" ||
            minimum == null || minimum.text != "0.00" ||
            maximum == null || maximum.text != "4.00" ||
            range == null || range.text != "0.00 — 4.00 m/s" ||
            status == null || !status.ClassListContains("result-variable-status--ready"))
        {
            issue = "Velocity metadata was not mapped to the Result UI.";
            return false;
        }

        controller.Select(ResultsVariable.Temperature);
        if (controller.State.Current.Variable != ResultsVariable.Temperature ||
            visualizationTarget.LastVariable != ResultsVariable.Temperature)
        {
            issue = "Unable to restore the Temperature selection.";
            return false;
        }

        Debug.Log("[UX03][F02][Case=VariableValidation] PASS supported variables, metadata, state, and visualization dispatch.");
        issue = string.Empty;
        return true;
    }

    private static bool ValidateRuntimeMount(out string issue)
    {
        VisualElement root = uiDocument.rootVisualElement;
        if (!ResultsRuntimeDocument.TrySetRuntimeVisible(false) ||
            root.resolvedStyle.display != DisplayStyle.None)
        {
            issue = "Results runtime document did not hide when its route was deactivated.";
            return false;
        }

        if (!ResultsRuntimeDocument.TrySetRuntimeVisible(true) ||
            root.resolvedStyle.display != DisplayStyle.Flex)
        {
            issue = "Results runtime document did not replace the dashboard placeholder route.";
            return false;
        }

        Label selected = root.Q<Label>("ResultsNavResults");
        if (selected == null ||
            !selected.ClassListContains("results-navigation__item--selected"))
        {
            issue = "Results runtime navigation does not reflect the active Results route.";
            return false;
        }

        string[] removedPlaceholderNames =
        {
            "SceneToolRail",
            "ProbeAnalyticsPanel",
            "ResultInformationPanel",
            "StatisticsPanel",
            "SceneSnapshotButton",
            "SceneUpdateModeField",
            "SceneViewModeField",
            "SliceAxisZButton"
        };
        for (int i = 0; i < removedPlaceholderNames.Length; i++)
        {
            if (root.Q<VisualElement>(removedPlaceholderNames[i]) != null)
            {
                issue = $"Unsupported placeholder control is still present: {removedPlaceholderNames[i]}.";
                return false;
            }
        }

        ResultsSceneInteractionController sceneController =
            runtimeDocument.SceneInteractionController;
        if (sceneController == null ||
            sceneController.Interaction != ResultsCameraInteraction.Orbit ||
            !sceneController.IsSliceVisible(ResultsSliceAxis.X) ||
            !sceneController.IsSliceVisible(ResultsSliceAxis.Y) ||
            sceneController.IsSliceVisible(ResultsSliceAxis.Z))
        {
            issue = "Independent X/Y/Z Slice controls or Orbit mode did not establish their initial state.";
            return false;
        }

        Debug.Log("[UX03][F03][Case=RuntimeMountValidation] PASS route visibility and Results navigation selection.");
        issue = string.Empty;
        return true;
    }

    private static void BeginResolution(int index, PanelSettings settings)
    {
        ReleaseValidationTarget(settings);

        resolutionIndex = index;
        Vector2Int resolution = TestResolutions[index];
        validationTarget = new RenderTexture(resolution.x, resolution.y, 24)
        {
            name = $"UX03_F01_{resolution.x}x{resolution.y}"
        };
        validationTarget.Create();
        settings.targetTexture = validationTarget;
        settleFrames = SettleFrameCount;
    }

    private static bool ValidateColorMap(out string issue)
    {
        ResultsColorMapController controller = runtimeDocument.ColorMapController;
        if (controller == null ||
            controller.CurrentSettings.Preset != ResultsColorMapPreset.Jet ||
            controller.CurrentSettings.RangeMode != ResultsColorRangeMode.Auto)
        {
            issue = "Jet and Auto were not established as the initial Color Map state.";
            return false;
        }

        if (!controller.SelectPreset(ResultsColorMapPreset.Viridis) ||
            colorMapTarget.LastPreset != ResultsColorMapPreset.Viridis ||
            colorMapTarget.LastReplacementColorMap == null)
        {
            issue = "Viridis preset was not dispatched to the existing visualization target.";
            return false;
        }

        if (!controller.TrySetManualRange(19.5f, 31.5f) ||
            controller.CurrentSettings.RangeMode != ResultsColorRangeMode.Manual ||
            !Mathf.Approximately(colorMapTarget.LastMinimum, 19.5f) ||
            !Mathf.Approximately(colorMapTarget.LastMaximum, 31.5f))
        {
            issue = "Manual Color Map range was not dispatched correctly.";
            return false;
        }

        if (controller.TrySetManualRange(5.0f, 5.0f))
        {
            issue = "An invalid Color Map range was accepted.";
            return false;
        }

        VisualElement root = uiDocument.rootVisualElement;
        Button manual = root.Q<Button>("ColorRangeManualButton");
        FloatField minimum = root.Q<FloatField>("ColorRangeMinField");
        FloatField maximum = root.Q<FloatField>("ColorRangeMaxField");
        if (manual == null || !manual.ClassListContains("color-range-mode-button--selected") ||
            minimum == null || !minimum.enabledSelf ||
            maximum == null || !maximum.enabledSelf)
        {
            issue = "Manual Color Map controls do not reflect their state.";
            return false;
        }

        controller.SelectPreset(ResultsColorMapPreset.Jet);
        controller.SetRangeMode(ResultsColorRangeMode.Auto);
        if (colorMapTarget.LastPreset != ResultsColorMapPreset.Jet ||
            colorMapTarget.LastReplacementColorMap != null ||
            colorMapTarget.LastRangeMode != ResultsColorRangeMode.Auto)
        {
            issue = "Color Map controls could not return to existing Jet and Auto range.";
            return false;
        }

        Debug.Log("[UX03][F03][Case=ColorMapValidation] PASS presets, legend, Auto/Manual range, and target dispatch without GPU readback.");
        issue = string.Empty;
        return true;
    }

    private static void Fail(string message)
    {
        Debug.LogError($"{LogTag} {message}");
        exitCode = 4;
        CompletePlayModeValidation();
    }

    private static void CompletePlayModeValidation()
    {
        EditorApplication.update -= PollValidation;
        PanelSettings settings = uiDocument != null ? uiDocument.panelSettings :
            AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsAssetPath);
        ReleaseValidationTarget(settings);
        runtimeDocument = null;
        uiDocument = null;
        visualizationTarget = null;
        colorMapTarget = null;
        EditorApplication.ExitPlaymode();
    }

    private static void ReleaseValidationTarget(PanelSettings settings)
    {
        if (settings != null && settings.targetTexture == validationTarget)
            settings.targetTexture = null;

        if (validationTarget == null)
            return;

        validationTarget.Release();
        Object.DestroyImmediate(validationTarget);
        validationTarget = null;
    }

    private static void FinishInEditMode()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.update -= PollValidation;
        bool exitWhenFinished = SessionState.GetBool(BatchKey, false);
        SessionState.SetBool(RunningKey, false);
        SessionState.SetBool(BatchKey, false);

        if (exitCode == 0)
            Debug.Log($"{LogTag} Validation completed successfully.");

        if (exitWhenFinished)
            EditorApplication.Exit(exitCode);
    }

    private sealed class ValidationMetadataSource : IResultsVariableMetadataSource
    {
        public ResultsVariableMetadata Read(ResultsVariable variable)
        {
            return variable == ResultsVariable.Temperature
                ? new ResultsVariableMetadata(
                    variable,
                    "Temperature",
                    "°C",
                    18f,
                    32f,
                    "Validation temperature texture")
                : new ResultsVariableMetadata(
                    variable,
                    "Velocity",
                    "m/s",
                    0f,
                    4f,
                    "Validation velocity texture");
        }
    }

    private sealed class RecordingVisualizationTarget : IResultsVisualizationTarget
    {
        public ResultsVariable LastVariable { get; private set; } = ResultsVariable.Temperature;

        public bool TryApply(
            ResultsVariableSelection selection,
            out Texture texture,
            out string sourceName,
            out string issue)
        {
            LastVariable = selection.Variable;
            texture = Texture2D.blackTexture;
            sourceName = "ValidationTexture";
            issue = string.Empty;
            return true;
        }
    }

    private sealed class RecordingColorMapTarget : IResultsColorMapTarget
    {
        public ResultsColorMapPreset LastPreset { get; private set; }
        public ResultsColorRangeMode LastRangeMode { get; private set; }
        public float LastMinimum { get; private set; }
        public float LastMaximum { get; private set; }
        public Texture2D LastReplacementColorMap { get; private set; }

        public bool TryApply(
            ResultsVariableSelection selection,
            ResultsColorMapSettings settings,
            Texture2D replacementColorMap,
            out string targetName,
            out string issue)
        {
            LastPreset = settings.Preset;
            LastRangeMode = settings.RangeMode;
            LastMinimum = settings.Minimum;
            LastMaximum = settings.Maximum;
            LastReplacementColorMap = replacementColorMap;
            targetName = "ValidationSliceMaterial";
            issue = string.Empty;
            return true;
        }

        public void Dispose()
        {
        }
    }
}
