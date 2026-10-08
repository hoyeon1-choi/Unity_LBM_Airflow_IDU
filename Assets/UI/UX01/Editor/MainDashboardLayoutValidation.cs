using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class MainDashboardLayoutValidation
{
    private const string LogTag = "[UX01][F01][T04][Validation]";
    private const string NavigationLogTag = "[UX01][F02][Validation]";
    private const string KpiLogTag = "[UX01][F03][Validation]";
    private const string SceneViewLogTag = "[UX01][F04][Validation]";
    private const string SimulationStatusLogTag = "[UX01][F05][Validation]";
    private const string TrendLogTag = "[UX01][F06][Validation]";
    private const string DataIntegrationLogTag = "[UX01][F07][Validation]";
    private const string PanelSettingsAssetPath = "Assets/UI/UX01/Resources/MainDashboardPanelSettings.asset";
    private const string ThemeStyleSheetAssetPath = "Assets/UI/UX01/MainDashboardTheme.tss";
    private const string RunningKey = "UX01.F01.T04.ValidationRunning";
    private const string BatchKey = "UX01.F01.T04.ValidationBatch";
    private const double TimeoutSeconds = 60.0;
    private const int SettleFrameCount = 4;
    private const string TemperatureCaptureRelativePath = "Logs/UX01_F06_TemperatureTrend.png";
    private const string PerformanceCaptureRelativePath = "Logs/UX01_F06_GpuPerformanceTrend.png";

    private static readonly Vector2Int[] TestResolutions =
    {
        new Vector2Int(640, 360),
        new Vector2Int(960, 540),
        new Vector2Int(1024, 768),
        new Vector2Int(1280, 720),
        new Vector2Int(1920, 1080)
    };

    private static MainDashboardRuntimeDocument runtimeDocument;
    private static UIDocument uiDocument;
    private static RenderTexture validationTarget;
    private static double deadline;
    private static int resolutionIndex = -1;
    private static int settleFrames;
    private static int exitCode;
    private static int visualCaptureStage;
    private static bool navigationValidated;

    [InitializeOnLoadMethod]
    private static void ResumeAfterDomainReload()
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;

        SubscribeToPlayMode();
        if (EditorApplication.isPlaying)
            EditorApplication.delayCall += BeginPlayModeValidation;
    }

    [MenuItem("Tools/UX-01/Validate Main Dashboard Layout")]
    public static void RunFromMenu()
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
            Debug.LogError($"{LogTag} Play Mode가 이미 실행 중입니다.");
            if (exitWhenFinished)
                EditorApplication.Exit(2);
            return;
        }

        if (!EnsurePanelSettingsAsset())
        {
            if (exitWhenFinished)
                EditorApplication.Exit(3);
            return;
        }

        SessionState.SetBool(RunningKey, true);
        SessionState.SetBool(BatchKey, exitWhenFinished);
        exitCode = 0;
        visualCaptureStage = 0;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SubscribeToPlayMode();
        EditorApplication.EnterPlaymode();
    }

    private static bool EnsurePanelSettingsAsset()
    {
        PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsAssetPath);
        if (settings == null)
        {
            ThemeStyleSheet dashboardTheme =
                AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemeStyleSheetAssetPath);
            if (dashboardTheme == null)
            {
                Debug.LogError($"{LogTag} Theme Style Sheet를 찾을 수 없습니다: {ThemeStyleSheetAssetPath}");
                return false;
            }

            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "MainDashboardPanelSettings";
            settings.themeStyleSheet = dashboardTheme;
            AssetDatabase.CreateAsset(settings, PanelSettingsAssetPath);
        }

        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1920, 1080);
        settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        settings.match = 0.5f;
        settings.targetDisplay = 0;
        settings.sortingOrder = 100.0f;
        if (settings.themeStyleSheet == null)
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemeStyleSheetAssetPath);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
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
        else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(RunningKey, false))
            FinishInEditMode();
    }

    private static void BeginPlayModeValidation()
    {
        if (!EditorApplication.isPlaying || runtimeDocument != null)
            return;

        GameObject host = new GameObject("MainDashboardValidationHost");
        runtimeDocument = host.AddComponent<MainDashboardRuntimeDocument>();
        deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
        resolutionIndex = -1;
        settleFrames = 0;
        navigationValidated = false;
        EditorApplication.update -= PollValidation;
        EditorApplication.update += PollValidation;
    }

    private static void PollValidation()
    {
        if (!EditorApplication.isPlaying)
            return;

        if (EditorApplication.timeSinceStartup >= deadline)
        {
            Fail("검증 시간이 초과되었습니다.");
            return;
        }

        if (runtimeDocument == null)
            return;

        if (uiDocument == null)
        {
            uiDocument = runtimeDocument.GetComponentInChildren<UIDocument>();
            if (uiDocument == null || uiDocument.rootVisualElement.Q<VisualElement>("MainDashboard") == null)
                return;

            if (!ValidateNavigation(out string navigationIssue))
            {
                Fail(navigationIssue);
                return;
            }

            if (!ValidateKpiCards(out string kpiIssue))
            {
                Fail(kpiIssue);
                return;
            }

            if (!ValidateSceneView(out string sceneViewIssue))
            {
                Fail(sceneViewIssue);
                return;
            }

            if (!ValidateSimulationStatus(out string statusIssue))
            {
                Fail(statusIssue);
                return;
            }

            if (!ValidateTrendCharts(out string trendIssue))
            {
                Fail(trendIssue);
                return;
            }

            if (!ValidateDataIntegration(out string integrationIssue))
            {
                Fail(integrationIssue);
                return;
            }

            navigationValidated = true;
            BeginResolution(0);
            return;
        }

        if (!navigationValidated)
            return;

        if (settleFrames-- > 0)
            return;

        if (visualCaptureStage == 1)
        {
            CaptureVisualValidationImage(PerformanceCaptureRelativePath);
            runtimeDocument.Trends.ShowTemperatureTrend();
            Debug.Log($"{LogTag} PASS all {TestResolutions.Length} resolutions.");
            CompletePlayModeValidation();
            return;
        }

        if (!runtimeDocument.TryValidateCurrentLayout(out string issue))
        {
            Fail($"{TestResolutions[resolutionIndex].x}x{TestResolutions[resolutionIndex].y}: {issue}");
            return;
        }

        VisualElement dashboard = uiDocument.rootVisualElement.Q<VisualElement>("MainDashboard");
        Vector2Int resolution = TestResolutions[resolutionIndex];
        Debug.Log(
            $"{LogTag} PASS target={resolution.x}x{resolution.y}, " +
            $"resolved={dashboard.resolvedStyle.width:F0}x{dashboard.resolvedStyle.height:F0}.");

        int nextIndex = resolutionIndex + 1;
        if (nextIndex < TestResolutions.Length)
        {
            BeginResolution(nextIndex);
            return;
        }

        CaptureVisualValidationImage(TemperatureCaptureRelativePath);
        runtimeDocument.Trends.ShowGpuPerformanceTrend();
        visualCaptureStage = 1;
        settleFrames = 2;
    }

    private static bool ValidateNavigation(out string issue)
    {
        MainDashboardNavigationController navigation = runtimeDocument.Navigation;
        if (navigation == null)
        {
            issue = "Navigation Controller가 생성되지 않았습니다.";
            return false;
        }

        VisualElement root = uiDocument.rootVisualElement;
        Button homeButton = root.Q<Button>("NavHomeButton");
        Button compareButton = root.Q<Button>("NavCompareButton");
        Button reportButton = root.Q<Button>("NavReportButton");
        VisualElement homePage = root.Q<VisualElement>("HomePage");
        VisualElement placeholderPage = root.Q<VisualElement>("PlaceholderPage");
        Label placeholderTitle = root.Q<Label>("PlaceholderPageTitle");

        if (homeButton == null || compareButton == null || reportButton == null || homePage == null ||
            placeholderPage == null || placeholderTitle == null)
        {
            issue = "Navigation 검증에 필요한 VisualElement가 없습니다.";
            return false;
        }

        if (navigation.CurrentPage != MainDashboardNavigationController.DashboardPage.Home ||
            !homeButton.ClassListContains("navigation-button--selected") ||
            homePage.resolvedStyle.display == DisplayStyle.None)
        {
            issue = "초기 Home 선택 상태가 올바르지 않습니다.";
            return false;
        }

        if (!navigation.ShowPage(MainDashboardNavigationController.DashboardPage.Simulation) ||
            navigation.CurrentPage != MainDashboardNavigationController.DashboardPage.Simulation ||
            homePage.style.display.value != DisplayStyle.None ||
            placeholderPage.style.display.value != DisplayStyle.Flex ||
            placeholderTitle.text != "Simulation Setup")
        {
            issue = "Simulation Placeholder 전환 상태가 올바르지 않습니다.";
            return false;
        }

        if (navigation.IsPageEnabled(MainDashboardNavigationController.DashboardPage.Compare) ||
            compareButton.enabledSelf ||
            navigation.ShowPage(MainDashboardNavigationController.DashboardPage.Compare) ||
            navigation.CurrentPage != MainDashboardNavigationController.DashboardPage.Simulation)
        {
            issue = "Compare Navigation must remain disabled without changing the current page.";
            return false;
        }

        if (!navigation.SetPageEnabled(MainDashboardNavigationController.DashboardPage.Report, false) ||
            navigation.IsPageEnabled(MainDashboardNavigationController.DashboardPage.Report) ||
            reportButton.enabledSelf)
        {
            issue = "Navigation Disabled 상태가 올바르지 않습니다.";
            return false;
        }

        navigation.SetPageEnabled(MainDashboardNavigationController.DashboardPage.Report, true);
        navigation.ShowPage(MainDashboardNavigationController.DashboardPage.Home);
        if (navigation.CurrentPage != MainDashboardNavigationController.DashboardPage.Home ||
            homePage.style.display.value != DisplayStyle.Flex ||
            placeholderPage.style.display.value != DisplayStyle.None)
        {
            issue = "Home 화면 복귀 상태가 올바르지 않습니다.";
            return false;
        }

        Debug.Log($"{NavigationLogTag} PASS: Home, Placeholder, Selected, Disabled.");
        issue = string.Empty;
        return true;
    }

    private static bool ValidateKpiCards(out string issue)
    {
        VisualElement root = uiDocument.rootVisualElement;
        if (root.Q<Label>("TopBarProjectValue") == null ||
            root.Q<Label>("TopBarCaseValue") == null ||
            root.Q<Label>("TopBarClockValue") == null ||
            root.Q<VisualElement>("TopBarState") == null ||
            root.Q<Label>("TopBarStateValue") == null)
        {
            issue = "TopBar 정보 표시 요소가 없습니다.";
            return false;
        }

        var controller = new MainDashboardKpiController();
        if (!controller.Initialize(root, out issue))
            return false;

        try
        {
            controller.Render(new MainDashboardMockKpiViewModel());
            if (!TryValidateCard(root, "RoomAverageKpiCard", "Room Avg", "24.8", "°C", out issue) ||
                !TryValidateCard(root, "TemperatureDeltaKpiCard", "ΔT", "6.2", "°C", out issue) ||
                !TryValidateCard(root, "MaxVelocityKpiCard", "Max Velocity", "3.8", "m/s", out issue) ||
                !TryValidateCard(root, "MassErrorKpiCard", "Mass Error", "0.02", "%", out issue) ||
                !TryValidateCard(root, "GpuUsageKpiCard", "GPU Usage", "92", "%", out issue))
            {
                return false;
            }

            var sourceMetrics = new SimulationResultMetrics
            {
                hasValidRoomAverage = true,
                avgRoomTemperatureDegC = 24.8f,
                hasValidInletAverage = true,
                inletAverageTemperatureDegC = 30.1f,
                hasValidOutletAverage = true,
                outletAverageTemperatureDegC = 23.9f,
                hasValidVelocityDiagnostic = true,
                maxSpeedPhys = 3.8f,
                hasValidDensityDiagnostic = true,
                massResidualNormalized = 0.0002f,
                massConservationStatus = "OK"
            };
            var actualViewModel = new SimulationMainDashboardKpiViewModel(() => sourceMetrics);
            actualViewModel.Refresh();
            if (!actualViewModel.RoomAverage.HasValue ||
                Mathf.Abs((float)actualViewModel.RoomAverage.Value - 24.8f) > 0.001f ||
                Mathf.Abs((float)actualViewModel.TemperatureDelta.Value - 6.2f) > 0.001f ||
                !actualViewModel.MaxVelocity.HasValue ||
                Mathf.Abs((float)actualViewModel.MaxVelocity.Value - 3.8f) > 0.001f ||
                Mathf.Abs((float)actualViewModel.MassError.Value - 0.02f) > 0.001f ||
                actualViewModel.GpuUsage.HasValue)
            {
                issue = "실제 Metrics KPI 매핑 또는 GPU 미지원 상태가 올바르지 않습니다.";
                return false;
            }

            Debug.Log(
                $"{KpiLogTag} PASS: common card, mock data, ViewModel interface, actual metrics mapping, GPU unavailable fallback.");
            issue = string.Empty;
            return true;
        }
        finally
        {
            controller.Dispose();
        }
    }

    private static bool TryValidateCard(
        VisualElement root,
        string instanceName,
        string expectedLabel,
        string expectedValue,
        string expectedUnit,
        out string issue)
    {
        VisualElement instance = root.Q<VisualElement>(instanceName);
        VisualElement card = instance?.Q<VisualElement>("KpiCard");
        Label label = card?.Q<Label>("KpiLabel");
        Label value = card?.Q<Label>("KpiValue");
        Label unit = card?.Q<Label>("KpiUnit");
        Label status = card?.Q<Label>("KpiStatus");
        if (card == null || label == null || value == null || unit == null || status == null)
        {
            issue = $"KPI Card 검증 요소가 없습니다: {instanceName}";
            return false;
        }

        if (label.text != expectedLabel || value.text != expectedValue || unit.text != expectedUnit ||
            status.text != "Mock data" || !card.ClassListContains("kpi-card--mock"))
        {
            issue = $"KPI Mock 표시가 올바르지 않습니다: {instanceName}";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private static bool ValidateSceneView(out string issue)
    {
        VisualElement root = uiDocument.rootVisualElement;
        Texture previewTexture = Texture2D.blackTexture;
        var controller = new MainDashboardSceneViewController(() => previewTexture);
        if (!controller.Initialize(root, out issue))
            return false;

        try
        {
            Image image = root.Q<Image>("SceneViewImage");
            Label sourceStatus = root.Q<Label>("SceneSourceStatus");
            VisualElement legendBar = root.Q<VisualElement>("TemperatureLegendBar");
            Label legendMax = root.Q<Label>("LegendMaxLabel");
            Label legendMin = root.Q<Label>("LegendMinLabel");
            if (image == null || sourceStatus == null || legendBar == null ||
                legendMax == null || legendMin == null)
            {
                issue = "Scene View 또는 Temperature Legend 요소가 없습니다.";
                return false;
            }

            if (!controller.IsBound || image.image != previewTexture ||
                sourceStatus.style.display.value != DisplayStyle.None ||
                legendMax.text != "35" || legendMin.text != "20")
            {
                issue = "기존 Scene Texture 연결 또는 Legend Placeholder 상태가 올바르지 않습니다.";
                return false;
            }

            Button fitButton = root.Q<Button>("SceneFitButton");
            Button resetButton = root.Q<Button>("SceneResetViewButton");
            Button variableButton = root.Q<Button>("SceneVariableButton");
            Button realtimeButton = root.Q<Button>("SceneRealtimeButton");
            if (fitButton == null || resetButton == null || variableButton == null || realtimeButton == null)
            {
                issue = "Scene Toolbar 버튼 구성이 올바르지 않습니다.";
                return false;
            }

            // This validation injects a texture without a camera or contour visualizers.
            // Actions requiring those scene components remain unavailable; preview freeze is usable.
            if (fitButton.enabledSelf || resetButton.enabledSelf || variableButton.enabledSelf ||
                !realtimeButton.enabledSelf ||
                !variableButton.ClassListContains("scene-toolbar-button--selected") ||
                !realtimeButton.ClassListContains("scene-toolbar-button--selected"))
            {
                issue = "Scene Toolbar 활성화 상태가 연결된 기능과 일치하지 않습니다.";
                return false;
            }

            Debug.Log(
                $"{SceneViewLogTag} PASS: texture binding, temperature selection, preview controls, camera dependency state.");
            issue = string.Empty;
            return true;
        }
        finally
        {
            controller.Dispose();
        }
    }

    private static bool ValidateSimulationStatus(out string issue)
    {
        VisualElement root = uiDocument.rootVisualElement;
        var controller = new MainDashboardSimulationStatusController();
        if (!controller.Initialize(root, out issue))
            return false;

        try
        {
            MainDashboardSimulationState[] states =
            {
                MainDashboardSimulationState.Idle,
                MainDashboardSimulationState.Running,
                MainDashboardSimulationState.Paused,
                MainDashboardSimulationState.Completed,
                MainDashboardSimulationState.Error
            };
            string[] labels = { "Idle", "Running", "Paused", "Completed", "Error" };
            string[] suffixes = { "idle", "running", "paused", "completed", "error" };
            VisualElement panel = root.Q<VisualElement>("SimulationStatusPanel");
            Label stateValue = root.Q<Label>("SimulationStatusValue");
            for (int i = 0; i < states.Length; i++)
            {
                controller.Render(new MainDashboardMockSimulationStatusViewModel(states[i]));
                if (stateValue.text != labels[i] ||
                    !panel.ClassListContains($"simulation-status--{suffixes[i]}"))
                {
                    issue = $"Simulation Mock 상태 표현이 올바르지 않습니다: {labels[i]}";
                    return false;
                }
            }

            if (root.Q<Label>("SimulationTimeValue").text != "320.0 s" ||
                root.Q<Label>("SimulationTimeStepValue").text != "0.0200 s" ||
                root.Q<Label>("SimulationGridValue").text != "256 × 128 × 96" ||
                root.Q<Label>("SimulationFpsValue").text != "60" ||
                root.Q<Label>("SimulationMemoryValue").text != "Est. 21.4 / 24.0 GB")
            {
                issue = "Simulation Status Mock Metrics 표시가 올바르지 않습니다.";
                return false;
            }

            Debug.Log(
                $"{SimulationStatusLogTag} PASS: status panel, five mock states, metrics binding.");
            issue = string.Empty;
            return true;
        }
        finally
        {
            controller.Dispose();
        }
    }

    private static bool ValidateTrendCharts(out string issue)
    {
        MainDashboardTrendController trends = runtimeDocument.Trends;
        if (trends == null)
        {
            issue = "Trend Controller가 생성되지 않았습니다.";
            return false;
        }

        for (int i = 0; i <= 12; i++)
        {
            float time = i * 10.0f;
            float normalized = i / 12.0f;
            trends.AddTemperatureSample(new MainDashboardTemperatureTrendSample(
                time,
                Mathf.Lerp(30.0f, 24.8f, normalized),
                Mathf.Lerp(30.0f, 22.0f, normalized),
                Mathf.Lerp(31.5f, 27.5f, normalized),
                24.0f));
            trends.AddPerformanceSample(new MainDashboardPerformanceTrendSample(
                time,
                float.NaN,
                58.0f + Mathf.Sin(normalized * Mathf.PI * 2.0f) * 4.0f));
        }

        VisualElement root = uiDocument.rootVisualElement;
        VisualElement temperatureHost = root.Q<VisualElement>("TemperatureTrendChartHost");
        VisualElement performanceHost = root.Q<VisualElement>("GpuPerformanceTrendChartHost");
        Label roomLegend = root.Q<Label>("RoomAverageTrendLegend");
        Label gpuLegend = root.Q<Label>("GpuUsageTrendLegend");
        if (temperatureHost.childCount == 0 || performanceHost.childCount == 0 ||
            !roomLegend.text.Contains("24.8") || !gpuLegend.text.Contains("Unavailable"))
        {
            issue = "Temperature/GPU Performance Trend 구성 또는 Mock 표시가 올바르지 않습니다.";
            return false;
        }

        trends.ShowGpuPerformanceTrend();
        if (root.Q<VisualElement>("GpuPerformanceTrendPanel").style.display.value != DisplayStyle.Flex ||
            !root.Q<Button>("GpuPerformanceTrendTab").ClassListContains("trend-tab--selected"))
        {
            issue = "GPU Performance Trend Tab 전환이 올바르지 않습니다.";
            return false;
        }

        trends.ShowTemperatureTrend();
        Debug.Log(
            $"{TrendLogTag} PASS: UI Toolkit chart, temperature target binding, FPS series, GPU unavailable fallback, tabs.");
        issue = string.Empty;
        return true;
    }

    private static bool ValidateDataIntegration(out string issue)
    {
        if (runtimeDocument.ViewModel == null)
        {
            issue = "Dashboard ViewModel이 Runtime UIDocument에 연결되지 않았습니다.";
            return false;
        }

        var dataModel = new MockDashboardSimulationDataModel(24.0f);
        var viewModel = new MainDashboardViewModel(dataModel);
        int metricsEventCount = 0;
        int temperatureEventCount = 0;
        MainDashboardTemperatureTrendSample temperatureSample = default;
        viewModel.MetricsUpdated += () => metricsEventCount++;
        viewModel.TemperatureTrendSampled += sample =>
        {
            temperatureEventCount++;
            temperatureSample = sample;
        };

        var metrics = new SimulationResultMetrics
        {
            simulationTimeSeconds = 12.0f,
            hasValidRoomAverage = true,
            avgRoomTemperatureDegC = 24.8f,
            hasValidInletAverage = true,
            inletAverageTemperatureDegC = 30.1f,
            hasValidOutletAverage = true,
            outletAverageTemperatureDegC = 23.9f,
            hasValidVelocityDiagnostic = true,
            maxSpeedPhys = 3.8f,
            hasValidDensityDiagnostic = true,
            massResidualNormalized = 0.0002f,
            massConservationStatus = "OK"
        };
        dataModel.Publish(metrics);
        viewModel.Kpi.Refresh();

        bool valid = metricsEventCount == 1 &&
                     temperatureEventCount == 1 &&
                     Mathf.Abs(temperatureSample.TimeSeconds - 12.0f) < 0.001f &&
                     Mathf.Abs(temperatureSample.TargetDegC - 24.0f) < 0.001f &&
                     viewModel.Kpi.RoomAverage.HasValue &&
                     Mathf.Abs((float)viewModel.Kpi.RoomAverage.Value - 24.8f) < 0.001f &&
                     viewModel.Kpi.MassError.HasValue &&
                     Mathf.Abs((float)viewModel.Kpi.MassError.Value - 0.02f) < 0.001f;

        viewModel.Dispose();
        dataModel.Publish(metrics);
        valid &= metricsEventCount == 1 && temperatureEventCount == 1;
        if (!valid)
        {
            issue = "SimulationDataModel → Dashboard ViewModel → UI 데이터 전달 또는 이벤트 해제가 올바르지 않습니다.";
            return false;
        }

        Debug.Log(
            $"{DataIntegrationLogTag} PASS: metrics event, KPI mapping, temperature trend mapping, dispose unsubscribe.");
        issue = string.Empty;
        return true;
    }

    private static void BeginResolution(int index)
    {
        ReleaseValidationTarget();
        resolutionIndex = index;
        Vector2Int resolution = TestResolutions[index];
        validationTarget = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32)
        {
            name = $"MainDashboardValidation_{resolution.x}x{resolution.y}"
        };
        validationTarget.Create();
        uiDocument.panelSettings.targetTexture = validationTarget;
        settleFrames = SettleFrameCount;
    }

    private static void Fail(string message)
    {
        exitCode = 1;
        Debug.LogError($"{LogTag} FAIL {message}");
        CompletePlayModeValidation();
    }

    private static void CompletePlayModeValidation()
    {
        EditorApplication.update -= PollValidation;
        if (uiDocument != null && uiDocument.panelSettings != null)
            uiDocument.panelSettings.targetTexture = null;
        ReleaseValidationTarget();
        EditorApplication.ExitPlaymode();
    }

    private static void FinishInEditMode()
    {
        bool exitWhenFinished = SessionState.GetBool(BatchKey, false);
        SessionState.EraseBool(RunningKey);
        SessionState.EraseBool(BatchKey);
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.update -= PollValidation;
        runtimeDocument = null;
        uiDocument = null;
        resolutionIndex = -1;
        visualCaptureStage = 0;
        navigationValidated = false;

        if (exitWhenFinished)
            EditorApplication.Exit(exitCode);
    }

    private static void ReleaseValidationTarget()
    {
        if (validationTarget == null)
            return;

        validationTarget.Release();
        Object.DestroyImmediate(validationTarget);
        validationTarget = null;
    }

    private static void CaptureVisualValidationImage(string relativePath)
    {
        if (validationTarget == null)
            return;

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string capturePath = Path.Combine(projectRoot, relativePath);
        string captureDirectory = Path.GetDirectoryName(capturePath);
        if (!string.IsNullOrEmpty(captureDirectory))
            Directory.CreateDirectory(captureDirectory);

        RenderTexture previousTarget = RenderTexture.active;
        Texture2D capture = null;
        try
        {
            RenderTexture.active = validationTarget;
            capture = new Texture2D(
                validationTarget.width,
                validationTarget.height,
                TextureFormat.RGBA32,
                false);
            capture.ReadPixels(
                new Rect(0.0f, 0.0f, validationTarget.width, validationTarget.height),
                0,
                0,
                false);
            capture.Apply(false, false);
            File.WriteAllBytes(capturePath, capture.EncodeToPNG());
            Debug.Log($"{TrendLogTag} Visual capture saved: {capturePath}");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (capture != null)
                Object.DestroyImmediate(capture);
        }
    }

    private sealed class MockDashboardSimulationDataModel : IMainDashboardSimulationDataModel
    {
        public MockDashboardSimulationDataModel(float targetTemperatureDegC)
        {
            TargetTemperatureDegC = targetTemperatureDegC;
        }

        public event System.Action<SimulationResultMetrics> MetricsUpdated;

        public SimulationResultMetrics LatestMetrics { get; private set; }
        public SimulationController SimulationController => null;
        public float TargetTemperatureDegC { get; }

        public void Publish(SimulationResultMetrics metrics)
        {
            LatestMetrics = metrics;
            MetricsUpdated?.Invoke(metrics);
        }

        public void Tick()
        {
        }

        public void Dispose()
        {
            MetricsUpdated = null;
            LatestMetrics = null;
        }
    }
}
