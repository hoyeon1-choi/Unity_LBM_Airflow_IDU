using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class SimulationSetupLayoutValidation
{
    private const string LogTag = "[UX02][F01][T05][Case=LayoutValidation]";
    private const string CaseSelectionLogTag = "[UX02][F02][Case=LayoutValidation]";
    private const string ModelSelectionLogTag = "[UX02][F03][Case=LayoutValidation]";
    private const string MeshLogTag = "[UX02][F04][Case=LayoutValidation]";
    private const string PhysicsLogTag = "[UX02][F05][Case=LayoutValidation]";
    private const string BoundaryLogTag = "[UX02][F06][Case=LayoutValidation]";
    private const string RunControlLogTag = "[UX02][F07][Case=LayoutValidation]";
    private const string ProgressLogTag = "[UX02][F08][Case=LayoutValidation]";
    private const string SystemMonitorLogTag = "[UX02][F09][Case=LayoutValidation]";
    private const string SolverIntegrationLogTag = "[UX02][F10][Case=IntegrationValidation]";
    private const string UxmlAssetPath = "Assets/UI/UX02/SimulationSetup.uxml";
    private const string UssAssetPath = "Assets/UI/UX02/SimulationSetup.uss";
    private const string PanelSettingsAssetPath =
        "Assets/UI/UX01/Resources/MainDashboardPanelSettings.asset";
    private const string RunningKey = "UX02.F01.T05.ValidationRunning";
    private const string BatchKey = "UX02.F01.T05.ValidationBatch";
    private const double TimeoutSeconds = 45.0;
    private const int SettleFrameCount = 4;

    private static readonly Vector2Int[] TestResolutions =
    {
        new Vector2Int(960, 540),
        new Vector2Int(1280, 720),
        new Vector2Int(1920, 1080)
    };

    private static SimulationSetupRuntimeDocument runtimeDocument;
    private static UIDocument uiDocument;
    private static RenderTexture validationTarget;
    private static double deadline;
    private static int resolutionIndex = -1;
    private static int settleFrames;
    private static int exitCode;
    private static bool navigationValidated;
    private static bool caseSelectionValidated;
    private static bool modelSelectionValidated;
    private static bool meshValidated;
    private static bool physicsValidated;
    private static bool boundaryValidated;
    private static bool runControlValidated;
    private static InMemorySimulationSetupRunTarget validationRunTarget;
    private static bool progressValidated;
    private static InMemorySimulationSetupProgressSource validationProgressSource;
    private static bool systemMonitorValidated;
    private static InMemorySimulationSetupSystemMonitorSource validationSystemMonitorSource;

    [InitializeOnLoadMethod]
    private static void ResumeAfterDomainReload()
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;

        SubscribeToPlayMode();
        if (EditorApplication.isPlaying)
            EditorApplication.delayCall += BeginPlayModeValidation;
    }

    [MenuItem("Tools/UX-02/Validate F01 Simulation Layout")]
    public static void RunFromMenu()
    {
        StartValidation(false);
    }

    [MenuItem("Tools/UX-02/Validate F10 Solver Integration Contracts")]
    public static void RunF10SolverIntegrationValidation()
    {
        if (ValidateF10SolverIntegration(out string issue))
            Debug.Log($"{SolverIntegrationLogTag} PASS command configuration and state snapshot contracts.");
        else
            Debug.LogError($"{SolverIntegrationLogTag} {issue}");
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

        if (!ValidateAssets(out string assetIssue))
        {
            Debug.LogError($"{LogTag} {assetIssue}");
            if (exitWhenFinished)
                EditorApplication.Exit(3);
            return;
        }

        if (!ValidateJsonCaseStore(out string storeIssue))
        {
            Debug.LogError($"{CaseSelectionLogTag} {storeIssue}");
            if (exitWhenFinished)
                EditorApplication.Exit(4);
            return;
        }

        if (!ValidateF10SolverIntegration(out string integrationIssue))
        {
            Debug.LogError($"{SolverIntegrationLogTag} {integrationIssue}");
            if (exitWhenFinished)
                EditorApplication.Exit(5);
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
        if (AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlAssetPath) == null)
        {
            issue = $"UXML asset is missing: {UxmlAssetPath}";
            return false;
        }

        if (AssetDatabase.LoadAssetAtPath<StyleSheet>(UssAssetPath) == null)
        {
            issue = $"USS asset is missing: {UssAssetPath}";
            return false;
        }

        if (AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsAssetPath) == null)
        {
            issue = $"PanelSettings asset is missing: {PanelSettingsAssetPath}";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private static bool ValidateJsonCaseStore(out string issue)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "UX02CaseStoreValidation_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "cases.json");
        try
        {
            var store = new JsonSimulationSetupCaseStore(path);
            string timestamp = DateTime.UtcNow.ToString("O");
            var first = new SimulationSetupUserCase
            {
                id = "user:validation-1",
                name = "Validation_Case_1",
                description = "JSON validation",
                basedOnPreset = CaseStudyPreset.A0_Baseline.ToString(),
                dxPhys = 0.04f,
                tauFluidMin = 0.505f,
                tauThermalMin = 0.525f,
                turbulence = "Off",
                createdUtc = timestamp,
                modifiedUtc = timestamp
            };
            var cases = new List<SimulationSetupUserCase> { first };
            if (!store.Save(cases, out issue))
                return false;

            SimulationSetupUserCase second = first.Clone();
            second.id = "user:validation-2";
            second.name = "Validation_Case_2";
            cases.Add(second);
            if (!store.Save(cases, out issue))
                return false;

            List<SimulationSetupUserCase> loaded = store.Load(out issue);
            if (!string.IsNullOrEmpty(issue) || loaded.Count != 2 ||
                loaded[0].name != first.name || loaded[1].name != second.name ||
                Mathf.Abs(loaded[0].tauFluidMin - 0.505f) > 0.0001f)
            {
                issue = "JSON case save/reload/replace round-trip is inconsistent.";
                return false;
            }

            Debug.Log($"{CaseSelectionLogTag} PASS JSON save, replace, and reload round-trip.");
            issue = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            issue = $"JSON case validation failed: {exception.Message}";
            return false;
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private static bool ValidateF10SolverIntegration(out string issue)
    {
        GameObject host = null;
        try
        {
            host = new GameObject("UX02_F10_IntegrationValidation");
            host.SetActive(false);
            SimulationController solver = host.AddComponent<SimulationController>();

            bool applied = solver.TryApplySetupConfiguration(
                "F10_IntegrationValidation",
                CaseStudyPreset.A0_Baseline,
                SimulationSetupMeshController.RequiredCellSizeMeters,
                0.56f,
                0.56f,
                SimulationController.TurbulenceModel.Smagorinsky,
                0.03f,
                0.7f,
                0f,
                30f,
                20f,
                0.71f,
                0.05f,
                -9.81f,
                out issue);
            if (!applied)
                return false;

            var stateAdapter = new SimulationControllerStateAdapter(() => solver);
            SimulationSetupSolverStateSnapshot snapshot = stateAdapter.Read();
            // This isolated controller intentionally has no domain or compute shader, so
            // its health must be surfaced as Error while the configured values remain readable.
            if (!snapshot.IsAvailable || snapshot.RunState != SimulationSetupRunState.Error ||
                snapshot.ActiveCaseName != "F10_IntegrationValidation" ||
                Mathf.Abs(solver.CellSize - SimulationSetupMeshController.RequiredCellSizeMeters) > 0.000001f ||
                Mathf.Abs(solver.TauFluidMin - 0.56f) > 0.000001f ||
                Mathf.Abs(solver.TauThermalMin - 0.56f) > 0.000001f)
            {
                issue =
                    "The solver command/state adapter round-trip did not preserve staged values. " +
                    $"available={snapshot.IsAvailable}, state={snapshot.RunState}, " +
                    $"case='{snapshot.ActiveCaseName}', dx={solver.CellSize:F6}, " +
                    $"tauFMin={solver.TauFluidMin:F6}, tauTMin={solver.TauThermalMin:F6}, " +
                    $"stateError='{snapshot.ErrorMessage}'.";
                return false;
            }

            if (solver.TryApplySetupConfiguration(
                    "F10_InvalidDx",
                    CaseStudyPreset.A0_Baseline,
                    0.05f,
                    0.56f,
                    0.56f,
                    SimulationController.TurbulenceModel.Smagorinsky,
                    0.03f,
                    0.7f,
                    0f,
                    30f,
                    20f,
                    0.71f,
                    0.05f,
                    -9.81f,
                    out string invalidIssue) || string.IsNullOrWhiteSpace(invalidIssue))
            {
                issue = "The required dxPhys=0.04 m guard did not reject an invalid setup.";
                return false;
            }

            Debug.Log($"{SolverIntegrationLogTag} PASS dxPhys guard and solver state mapping.");
            issue = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            issue = $"F10 integration validation threw an exception: {exception.Message}";
            return false;
        }
        finally
        {
            if (host != null)
                UnityEngine.Object.DestroyImmediate(host);
        }
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

        BeginResolution(0, AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsAssetPath));
        GameObject host = new GameObject("SimulationSetupLayoutValidationHost");
        host.SetActive(false);
        runtimeDocument = host.AddComponent<SimulationSetupRuntimeDocument>();
        runtimeDocument.ConfigureCaseStore(new InMemorySimulationSetupCaseStore());
        runtimeDocument.ConfigureModelSource(BuildValidationModelSource());
        runtimeDocument.ConfigureBoundarySource(BuildValidationBoundarySource());
        validationRunTarget = new InMemorySimulationSetupRunTarget();
        runtimeDocument.ConfigureRunTarget(validationRunTarget);
        validationProgressSource = new InMemorySimulationSetupProgressSource(
            new SimulationSetupProgressSnapshot(true, 12f, true, 30f, 0.004f, 3000UL, false));
        runtimeDocument.ConfigureProgressSource(validationProgressSource);
        validationSystemMonitorSource = new InMemorySimulationSetupSystemMonitorSource(
            new SimulationSetupSystemMonitorSnapshot(
                true, "Validation GPU", "Direct3D12", float.NaN,
                180.2f, 8192f, 512.5f, 32768f, 60f));
        runtimeDocument.ConfigureSystemMonitorSource(validationSystemMonitorSource);
        host.SetActive(true);
        deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
        navigationValidated = false;
        caseSelectionValidated = false;
        modelSelectionValidated = false;
        meshValidated = false;
        physicsValidated = false;
        boundaryValidated = false;
        runControlValidated = false;
        progressValidated = false;
        systemMonitorValidated = false;
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

        if (!navigationValidated)
        {
            if (!ValidateStepNavigation(out string navigationIssue))
            {
                Fail(navigationIssue);
                return;
            }

            navigationValidated = true;
            return;
        }

        if (!caseSelectionValidated)
        {
            if (!ValidateCaseSelection(out string caseSelectionIssue))
            {
                Fail(caseSelectionIssue);
                return;
            }

            caseSelectionValidated = true;
            settleFrames = SettleFrameCount;
            return;
        }

        if (!modelSelectionValidated)
        {
            if (!ValidateModelSelection(out string modelSelectionIssue))
            {
                Fail(modelSelectionIssue);
                return;
            }

            modelSelectionValidated = true;
            settleFrames = SettleFrameCount;
            return;
        }

        if (!meshValidated)
        {
            if (!ValidateMeshConfiguration(out string meshIssue))
            {
                Fail(meshIssue);
                return;
            }

            meshValidated = true;
            settleFrames = SettleFrameCount;
            return;
        }

        if (!physicsValidated)
        {
            if (!ValidatePhysicsConfiguration(out string physicsIssue))
            {
                Fail(physicsIssue);
                return;
            }

            physicsValidated = true;
            settleFrames = SettleFrameCount;
            return;
        }

        if (!boundaryValidated)
        {
            if (!ValidateBoundaryConfiguration(out string boundaryIssue))
            {
                Fail(boundaryIssue);
                return;
            }

            boundaryValidated = true;
            settleFrames = SettleFrameCount;
            return;
        }

        if (!runControlValidated)
        {
            if (!ValidateRunControl(out string runControlIssue))
            {
                Fail(runControlIssue);
                return;
            }

            runControlValidated = true;
            settleFrames = SettleFrameCount;
            return;
        }

        if (!progressValidated)
        {
            if (!ValidateProgress(out string progressIssue))
            {
                Fail(progressIssue);
                return;
            }

            progressValidated = true;
            settleFrames = SettleFrameCount;
            return;
        }

        if (!systemMonitorValidated)
        {
            if (!ValidateSystemMonitor(out string systemMonitorIssue))
            {
                Fail(systemMonitorIssue);
                return;
            }

            systemMonitorValidated = true;
            settleFrames = SettleFrameCount;
            return;
        }

        if (settleFrames-- > 0)
            return;

        if (!runtimeDocument.TryValidateCurrentLayout(out string layoutIssue))
        {
            Vector2Int failedResolution = TestResolutions[resolutionIndex];
            Fail($"{failedResolution.x}x{failedResolution.y}: {layoutIssue}");
            return;
        }

        if (!runtimeDocument.TryValidateRunLayout(out string runLayoutIssue))
        {
            Vector2Int failedResolution = TestResolutions[resolutionIndex];
            Fail($"{failedResolution.x}x{failedResolution.y}: {runLayoutIssue}");
            return;
        }

        VisualElement root = uiDocument.rootVisualElement.Q<VisualElement>("SimulationSetup");
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

        Debug.Log($"{LogTag} PASS all {TestResolutions.Length} resolutions and six workflow steps.");
        CompletePlayModeValidation();
    }

    private static bool ValidateStepNavigation(out string issue)
    {
        SimulationSetupStepNavigationController navigation = runtimeDocument.StepNavigation;
        if (navigation == null)
        {
            issue = "Step navigation controller was not created.";
            return false;
        }

        int eventCount = 0;
        navigation.CurrentStepChanged += _ => eventCount++;
        foreach (SimulationSetupStepNavigationController.SimulationStep step in
                 System.Enum.GetValues(typeof(SimulationSetupStepNavigationController.SimulationStep)))
        {
            if (!navigation.SelectStep(step) || navigation.CurrentStep != step)
            {
                issue = $"Unable to select workflow step: {step}.";
                return false;
            }

            Button selectedButton = uiDocument.rootVisualElement.Q<Button>(step + "StepItem");
            VisualElement selectedPanel =
                uiDocument.rootVisualElement.Q<VisualElement>(step + "Panel");
            if (selectedButton == null || selectedPanel == null ||
                !selectedButton.ClassListContains("simulation-step-item--selected") ||
                !selectedPanel.ClassListContains("simulation-step-panel--selected") ||
                selectedPanel.style.display.value != DisplayStyle.Flex)
            {
                issue = $"Selected workflow state is inconsistent: {step}.";
                return false;
            }
        }

        if (eventCount != 6 ||
            !navigation.SelectStep(SimulationSetupStepNavigationController.SimulationStep.Case))
        {
            issue = "Workflow step change events are inconsistent.";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private static bool ValidateCaseSelection(out string issue)
    {
        SimulationSetupCaseController cases = runtimeDocument.CaseSelection;
        if (cases == null || cases.CaseCount != 11 || !cases.SupportsPersistentEditing)
        {
            issue = "Case catalog count or JSON storage state is inconsistent.";
            return false;
        }

        VisualElement root = uiDocument.rootVisualElement;
        Button newButton = root.Q<Button>("NewCaseButton");
        Button duplicateButton = root.Q<Button>("DuplicateCaseButton");
        Button deleteButton = root.Q<Button>("DeleteCaseButton");
        Label countLabel = root.Q<Label>("CaseCountValue");
        if (newButton == null || duplicateButton == null || deleteButton == null ||
            countLabel == null || !newButton.enabledSelf || !duplicateButton.enabledSelf ||
            deleteButton.enabledSelf || countLabel.text != "11 cases" ||
            string.IsNullOrEmpty(newButton.tooltip))
        {
            issue = "Case action availability or case count is inconsistent.";
            return false;
        }

        int selectionEventCount = 0;
        cases.SelectedCaseChanged += _ => selectionEventCount++;
        CaseStudyPreset selectedPreset = CaseStudyPreset.B6_FluidTau_0510_Thermal_0530_Smag010;
        if (!cases.SelectCase(selectedPreset) || cases.SelectedCase != selectedPreset)
        {
            issue = "Unable to select the B6 built-in case.";
            return false;
        }

        Button selectedRow = root.Q<Button>("CaseListItem_builtin_" + selectedPreset);
        Label selectedName = root.Q<Label>("SelectedCaseNameValue");
        Label selectedDx = root.Q<Label>("SelectedCaseDxValue");
        Label selectedFluidTau = root.Q<Label>("SelectedCaseFluidTauValue");
        Label selectedThermalTau = root.Q<Label>("SelectedCaseThermalTauValue");
        Label selectedTurbulence = root.Q<Label>("SelectedCaseTurbulenceValue");
        Label headerCase = root.Q<Label>("SimulationSetupCaseValue");
        FloatField indoorTemperature = root.Q<FloatField>("CaseIndoorTemperatureField");
        FloatField indoorHumidity = root.Q<FloatField>("CaseIndoorHumidityField");
        FloatField outdoorTemperature = root.Q<FloatField>("CaseOutdoorTemperatureField");
        FloatField outdoorHumidity = root.Q<FloatField>("CaseOutdoorHumidityField");
        FloatField setTemperature = root.Q<FloatField>("CaseSetTemperatureField");
        FloatField targetTime = root.Q<FloatField>("CaseTargetSimulationTimeField");
        if (selectedRow == null || selectedName == null || selectedDx == null ||
            selectedFluidTau == null || selectedThermalTau == null ||
            selectedTurbulence == null || headerCase == null || indoorTemperature == null ||
            indoorHumidity == null || outdoorTemperature == null || outdoorHumidity == null ||
            setTemperature == null || targetTime == null ||
            !selectedRow.ClassListContains("case-list-item--selected") ||
            selectedName.text != selectedPreset.ToString() || selectedDx.text != "0.040 m" ||
            selectedFluidTau.text != "0.510" || selectedThermalTau.text != "0.530" ||
            selectedTurbulence.text != "Smagorinsky (0.10)" ||
            !headerCase.text.Contains(selectedPreset.ToString()) ||
            Mathf.Abs(indoorTemperature.value - 30f) > 0.000001f ||
            Mathf.Abs(indoorHumidity.value - 50f) > 0.000001f ||
            Mathf.Abs(outdoorTemperature.value - 35f) > 0.000001f ||
            Mathf.Abs(outdoorHumidity.value - 70f) > 0.000001f ||
            Mathf.Abs(setTemperature.value - 28f) > 0.000001f ||
            Mathf.Abs(targetTime.value - 30f) > 0.000001f)
        {
            issue = "Selected Case metadata mapping is inconsistent.";
            return false;
        }

        targetTime.value = 45f;
        if (!cases.InitialConditionsValid ||
            Mathf.Abs(cases.SelectedInitialConditions.TargetSimulationTimeSeconds - 45f) > 0.000001f)
        {
            issue = "Case initial-condition staging is inconsistent.";
            return false;
        }

        if (selectionEventCount != 1)
        {
            issue = "Case selection event count is inconsistent.";
            return false;
        }

        cases.CreateNewCase();
        string newCaseId = cases.SelectedCaseId;
        if (cases.CaseCount != 12 || cases.SelectedDefinition.IsBuiltIn ||
            !cases.CanDeleteSelectedCase || !deleteButton.enabledSelf ||
            root.Q<Label>("SelectedCaseStorageValue").text != "JSON user case")
        {
            issue = "New JSON case state is inconsistent.";
            return false;
        }

        cases.DuplicateSelectedCase();
        if (cases.CaseCount != 13 || cases.SelectedDefinition.IsBuiltIn ||
            cases.SelectedCaseId == newCaseId)
        {
            issue = "Duplicated JSON case state is inconsistent.";
            return false;
        }

        cases.DeleteSelectedCase();
        if (cases.CaseCount != 12 || cases.SelectedCase != CaseStudyPreset.A0_Baseline ||
            deleteButton.enabledSelf || !cases.SelectCase(newCaseId))
        {
            issue = "JSON duplicate deletion or selection fallback is inconsistent.";
            return false;
        }

        cases.DeleteSelectedCase();
        if (cases.CaseCount != 11 || cases.SelectedCase != CaseStudyPreset.A0_Baseline ||
            deleteButton.enabledSelf || countLabel.text != "11 cases")
        {
            issue = "JSON case cleanup or baseline restoration is inconsistent.";
            return false;
        }

        Debug.Log(
            $"{CaseSelectionLogTag} PASS 11 built-ins, selection, metadata, and JSON CRUD actions.");
        issue = string.Empty;
        return true;
    }

    private static ISimulationSetupModelSource BuildValidationModelSource()
    {
        var definitions = new List<SimulationSetupModelDefinition>
        {
            new SimulationSetupModelDefinition(
                "loaded:primary",
                "CavityBounds",
                "LBM_1wayCST",
                "Assets/Scenes/LBMScenes/LBM_1wayCST.unity",
                "CavityBounds",
                new Vector3(0f, 1f, 0f),
                new Vector3(5f, 2f, 8f),
                125, 50, 200,
                12, 8, 6, 1000, 500,
                2, 1, 3,
                true),
            new SimulationSetupModelDefinition(
                "loaded:secondary",
                "ValidationDomain",
                "ValidationScene",
                "Assets/Scenes/ValidationScene.unity",
                "ValidationDomain",
                new Vector3(1f, 2f, 3f),
                new Vector3(4f, 2f, 6f),
                100, 50, 150,
                8, 5, 4, 800, 400,
                1, 1, 2,
                false)
        };
        return new InMemorySimulationSetupModelSource(definitions);
    }

    private static ISimulationSetupBoundarySource BuildValidationBoundarySource()
    {
        var definitions = new List<SimulationSetupBoundaryDefinition>
        {
            new SimulationSetupBoundaryDefinition(
                "inlet:supply", "Supply Inlet", SimulationSetupBoundaryCategory.Inlet,
                nameof(LBMZouHeBox), true, "[0,10,20] - [0,15,25]", "VolumeFlowRate",
                new Vector3(0f, 0f, 2f), 0.12f, 18f, "+X", 0.24f,
                false, 0f, 0f, 0f, 0.12f, string.Empty,
                ThermalBoundaryType.Adiabatic, false),
            new SimulationSetupBoundaryDefinition(
                "outlet:return", "Return Outlet", SimulationSetupBoundaryCategory.Outlet,
                nameof(LBMZouHeBox), true, "[99,10,20] - [99,15,25]", "AutoMassBalancedOutlet",
                Vector3.zero, 0f, 0f, "-X", 0.24f,
                true, 1f, 0.35f, 0.10f, 0.12f, string.Empty,
                ThermalBoundaryType.Adiabatic, false),
            new SimulationSetupBoundaryDefinition(
                "wall:domain", "ValidationDomain outer shell", SimulationSetupBoundaryCategory.Wall,
                "Compute shader domain shell", true, "x/y/z min & max grid planes", string.Empty,
                Vector3.zero, 0f, 0f, string.Empty, 0f,
                false, 0f, 0f, 0f, 0f, "Resting half-way bounce-back",
                ThermalBoundaryType.Adiabatic, true),
            new SimulationSetupBoundaryDefinition(
                "wall:device", "Server Cabinet", SimulationSetupBoundaryCategory.Wall,
                nameof(DeviceObstacles), true, "[25,0,30] - [35,20,45]", string.Empty,
                Vector3.zero, 0f, 35f, string.Empty, 0f,
                false, 0f, 0f, 0f, 0f, "Resting half-way bounce-back",
                ThermalBoundaryType.Isothermal, false)
        };
        return new InMemorySimulationSetupBoundarySource(definitions);
    }

    private static bool ValidateModelSelection(out string issue)
    {
        SimulationSetupModelController models = runtimeDocument.ModelSelection;
        VisualElement root = uiDocument.rootVisualElement;
        Button refreshButton = root.Q<Button>("RefreshModelListButton");
        Label countLabel = root.Q<Label>("ModelCountValue");
        if (models == null || models.ModelCount != 2 || !models.HasSelection ||
            refreshButton == null || countLabel == null || countLabel.text != "2 models" ||
            string.IsNullOrEmpty(refreshButton.tooltip))
        {
            issue = "Loaded model source or Model Selection controls are inconsistent.";
            return false;
        }

        Label nameLabel = root.Q<Label>("SelectedModelNameValue");
        Label sceneLabel = root.Q<Label>("SelectedModelSceneValue");
        Label sizeLabel = root.Q<Label>("SelectedModelSizeValue");
        Label gridLabel = root.Q<Label>("SelectedModelGridValue");
        Label geometryLabel = root.Q<Label>("SelectedModelGeometryValue");
        Label topologyLabel = root.Q<Label>("SelectedModelTopologyValue");
        Label componentsLabel = root.Q<Label>("SelectedModelComponentsValue");
        Button selectedRow = root.Q<Button>("ModelListItem_loaded_primary");
        if (nameLabel == null || sceneLabel == null || sizeLabel == null || gridLabel == null ||
            geometryLabel == null || topologyLabel == null || componentsLabel == null ||
            selectedRow == null || !selectedRow.ClassListContains("model-list-item--selected") ||
            nameLabel.text != "CavityBounds" || sceneLabel.text != "LBM_1wayCST" ||
            sizeLabel.text != "5 x 2 x 8 m" || gridLabel.text != "125 x 50 x 200" ||
            geometryLabel.text != "12 renderers / 8 colliders / 6 meshes" ||
            topologyLabel.text != "1,000 vertices / 500 triangles" ||
            componentsLabel.text != "2 patches / 1 AC / 3 obstacles")
        {
            issue = "Current model or geometry metadata mapping is inconsistent.";
            return false;
        }

        int eventCount = 0;
        models.SelectedModelChanged += _ => eventCount++;
        if (!models.SelectModel("loaded:secondary") || eventCount != 1 ||
            models.SelectedModelId != "loaded:secondary" ||
            nameLabel.text != "ValidationDomain" || sizeLabel.text != "4 x 2 x 6 m")
        {
            issue = "Loaded model selection behavior is inconsistent.";
            return false;
        }

        models.MarkDirty();
        models.RefreshIfNeeded();
        if (models.SelectedModelId != "loaded:secondary" || eventCount != 1)
        {
            issue = "Model refresh did not preserve the selected loaded model.";
            return false;
        }

        if (!runtimeDocument.StepNavigation.SelectStep(
                SimulationSetupStepNavigationController.SimulationStep.Model))
        {
            issue = "Unable to activate the Model step for layout validation.";
            return false;
        }

        Debug.Log(
            $"{ModelSelectionLogTag} PASS loaded-model discovery, selection, metadata, and refresh persistence.");
        issue = string.Empty;
        return true;
    }

    private static bool ValidateMeshConfiguration(out string issue)
    {
        if (!LbmGridMemoryEstimator.TryEstimate(
                new Vector3(5f, 2f, 8f),
                SimulationSetupMeshController.RequiredCellSizeMeters,
                out LbmGridMemoryEstimate baseline,
                out issue) ||
            baseline.Nx != 125 || baseline.Ny != 50 || baseline.Nz != 200 ||
            baseline.CellCount != 1250000L || baseline.TotalBytes != 315000000L ||
            baseline.LargestDistributionBufferBytes != 30000000L ||
            baseline.TotalBytes / baseline.CellCount != 252L)
        {
            issue = string.IsNullOrEmpty(issue)
                ? "LBM grid or 252-byte-per-cell memory formula is inconsistent."
                : issue;
            return false;
        }

        if (!runtimeDocument.StepNavigation.SelectStep(
                SimulationSetupStepNavigationController.SimulationStep.Mesh))
        {
            issue = "Unable to activate the Mesh step.";
            return false;
        }

        SimulationSetupMeshController mesh = runtimeDocument.MeshConfiguration;
        VisualElement root = uiDocument.rootVisualElement;
        FloatField cellSizeField = root.Q<FloatField>("MeshCellSizeField");
        if (mesh == null || !mesh.HasEstimate || cellSizeField == null ||
            cellSizeField.enabledSelf ||
            Mathf.Abs(cellSizeField.value - 0.04f) > 0.000001f)
        {
            issue = "Fixed 0.040 m Cell Size UI or mesh estimate state is inconsistent.";
            return false;
        }

        LbmGridMemoryEstimate estimate = mesh.CurrentEstimate;
        Label modelLabel = root.Q<Label>("MeshModelNameValue");
        Label domainLabel = root.Q<Label>("MeshDomainSizeValue");
        Label nxLabel = root.Q<Label>("MeshNxValue");
        Label nyLabel = root.Q<Label>("MeshNyValue");
        Label nzLabel = root.Q<Label>("MeshNzValue");
        Label cellsLabel = root.Q<Label>("MeshTotalCellsValue");
        Label distributionLabel = root.Q<Label>("MeshDistributionMemoryValue");
        Label stateLabel = root.Q<Label>("MeshStateMemoryValue");
        Label textureLabel = root.Q<Label>("MeshTextureMemoryValue");
        Label totalLabel = root.Q<Label>("MeshTotalMemoryValue");
        Label largestBufferLabel = root.Q<Label>("MeshLargestBufferMemoryValue");
        Label memoryStateLabel = root.Q<Label>("MeshMemoryStatusValue");
        if (estimate.Nx != 100 || estimate.Ny != 50 || estimate.Nz != 150 ||
            estimate.CellCount != 750000L || estimate.TotalBytes != 189000000L ||
            estimate.LargestDistributionBufferBytes != 18000000L ||
            modelLabel == null || domainLabel == null || nxLabel == null || nyLabel == null ||
            nzLabel == null || cellsLabel == null || distributionLabel == null ||
            stateLabel == null || textureLabel == null || totalLabel == null || largestBufferLabel == null ||
            memoryStateLabel == null || modelLabel.text != "ValidationDomain" ||
            domainLabel.text != "4 x 2 x 6 m" || nxLabel.text != "100" ||
            nyLabel.text != "50" || nzLabel.text != "150" ||
            cellsLabel.text != "750,000" || distributionLabel.text != "148.8 MiB" ||
            stateLabel.text != "17.2 MiB" || textureLabel.text != "14.3 MiB" ||
            totalLabel.text != "180.2 MiB" || largestBufferLabel.text != "17.2 MiB" ||
            memoryStateLabel.text != "Within limit")
        {
            issue = "Grid Size or GPU Memory Estimate UI mapping is inconsistent.";
            return false;
        }

        Debug.Log(
            $"{MeshLogTag} PASS fixed cell size, grid dimensions, actual-buffer memory formula, and UI mapping.");
        issue = string.Empty;
        return true;
    }

    private static bool ValidatePhysicsConfiguration(out string issue)
    {
        if (!runtimeDocument.StepNavigation.SelectStep(
                SimulationSetupStepNavigationController.SimulationStep.Physics))
        {
            issue = "Unable to activate the Physics step.";
            return false;
        }

        SimulationSetupPhysicsController physics = runtimeDocument.PhysicsConfiguration;
        VisualElement root = uiDocument.rootVisualElement;
        DropdownField collision = root.Q<DropdownField>("PhysicsCollisionModelField");
        DropdownField turbulence = root.Q<DropdownField>("PhysicsTurbulenceModelField");
        FloatField turbulenceConstant = root.Q<FloatField>("PhysicsTurbulenceConstantField");
        FloatField turbulentPrandtl = root.Q<FloatField>("PhysicsTurbulentPrandtlField");
        Toggle thermalEnabled = root.Q<Toggle>("PhysicsThermalEnabledToggle");
        Toggle buoyancyEnabled = root.Q<Toggle>("PhysicsBuoyancyEnabledToggle");
        DropdownField buoyancyModel = root.Q<DropdownField>("PhysicsBuoyancyModelField");
        Label fluidLattice = root.Q<Label>("PhysicsFluidLatticeValue");
        Label thermalLattice = root.Q<Label>("PhysicsThermalLatticeValue");
        Label tauThermal = root.Q<Label>("PhysicsTauThermalMinValue");
        Label gravityPhysical = root.Q<Label>("PhysicsGravityPhysicalValue");
        if (physics == null || !physics.HasConfiguration || !physics.IsValid ||
            collision == null || turbulence == null || turbulenceConstant == null ||
            turbulentPrandtl == null || thermalEnabled == null || buoyancyEnabled == null ||
            buoyancyModel == null || fluidLattice == null || thermalLattice == null ||
            tauThermal == null || gravityPhysical == null || collision.enabledSelf ||
            collision.value != "MRT" || collision.choices.Count != 1 ||
            collision.choices.Contains("BGK") || turbulence.choices.Count != 3 ||
            !turbulence.choices.Contains("Off") || !turbulence.choices.Contains("Smagorinsky") ||
            !turbulence.choices.Contains("WALE") || turbulence.choices.Contains("BGK") ||
            turbulence.value != "Smagorinsky" ||
            Mathf.Abs(turbulenceConstant.value - 0.03f) > 0.000001f ||
            Mathf.Abs(turbulentPrandtl.value - 0.7f) > 0.000001f ||
            thermalEnabled.enabledSelf || !thermalEnabled.value || buoyancyModel.enabledSelf ||
            buoyancyModel.value != "Boussinesq" || fluidLattice.text != "D3Q19 MRT" ||
            thermalLattice.text != "D3Q7 MRT" || tauThermal.text != "0.560" ||
            gravityPhysical.text != "(0, -9.81, 0) m/s²")
        {
            issue = "Supported LBM, turbulence, thermal, buoyancy, or gravity mapping is inconsistent.";
            return false;
        }

        int eventCount = 0;
        physics.ConfigurationChanged += _ => eventCount++;
        turbulence.value = "Off";
        if (physics.CurrentConfiguration.TurbulenceModel != "Off" ||
            turbulenceConstant.enabledSelf || turbulentPrandtl.enabledSelf)
        {
            issue = "Turbulence Off state did not disable SGS-only controls.";
            return false;
        }

        turbulence.value = "WALE";
        turbulenceConstant.value = 0.325f;
        buoyancyEnabled.value = false;
        if (!turbulenceConstant.enabledSelf || !turbulentPrandtl.enabledSelf ||
            physics.CurrentConfiguration.TurbulenceModel != "WALE" ||
            Mathf.Abs(physics.CurrentConfiguration.TurbulenceConstant - 0.325f) > 0.000001f ||
            physics.CurrentConfiguration.BuoyancyEnabled ||
            root.Q<FloatField>("PhysicsThermalExpansionBetaField").enabledSelf)
        {
            issue = "Supported turbulence or buoyancy editing state is inconsistent.";
            return false;
        }

        buoyancyEnabled.value = true;
        if (!physics.CurrentConfiguration.BuoyancyEnabled || eventCount < 4 || !physics.IsValid)
        {
            issue = "Physics configuration events or restored buoyancy state are inconsistent.";
            return false;
        }

        if (!runtimeDocument.CaseSelection.SelectCase(
                CaseStudyPreset.B5_FluidTau_0510_Thermal_0530_Smag006))
        {
            issue = "Unable to select B5 for Physics case mapping validation.";
            return false;
        }

        physics.RefreshIfNeeded();
        SimulationSetupPhysicsConfiguration b5 = physics.CurrentConfiguration;
        if (b5.TurbulenceModel != "Smagorinsky" ||
            Mathf.Abs(b5.TurbulenceConstant - 0.06f) > 0.000001f ||
            Mathf.Abs(b5.TauThermalMin - 0.53f) > 0.000001f)
        {
            issue = "Case-driven turbulence or thermal tau mapping is inconsistent.";
            return false;
        }

        Debug.Log(
            $"{PhysicsLogTag} PASS MRT-only collision, supported turbulence, thermal, buoyancy, and gravity UI.");
        issue = string.Empty;
        return true;
    }

    private static bool ValidateBoundaryConfiguration(out string issue)
    {
        if (!runtimeDocument.StepNavigation.SelectStep(
                SimulationSetupStepNavigationController.SimulationStep.Boundary))
        {
            issue = "Unable to activate the Boundary step.";
            return false;
        }

        SimulationSetupBoundaryController boundary = runtimeDocument.BoundaryConfiguration;
        VisualElement root = uiDocument.rootVisualElement;
        Label inletCount = root.Q<Label>("BoundaryInletCountValue");
        Label outletCount = root.Q<Label>("BoundaryOutletCountValue");
        Label wallCount = root.Q<Label>("BoundaryWallCountValue");
        DropdownField patchType = root.Q<DropdownField>("BoundaryPatchTypeField");
        DropdownField inletMode = root.Q<DropdownField>("BoundaryInletModeField");
        Vector3Field inletVelocity = root.Q<Vector3Field>("BoundaryInletVelocityField");
        FloatField inletFlow = root.Q<FloatField>("BoundaryInletFlowRateField");
        FloatField inletTemperature = root.Q<FloatField>("BoundaryInletTemperatureField");
        FloatField inletDischargeAngle = root.Q<FloatField>("BoundaryInletDischargeAngleField");
        if (boundary == null || boundary.BoundaryCount != 4 || boundary.InletCount != 1 ||
            boundary.OutletCount != 1 || boundary.WallCount != 2 || !boundary.IsValid ||
            inletCount == null || outletCount == null || wallCount == null ||
            patchType == null || inletMode == null || inletVelocity == null ||
            inletFlow == null || inletTemperature == null || inletDischargeAngle == null ||
            inletCount.text != "1" || outletCount.text != "1" || wallCount.text != "2" ||
            boundary.CurrentCategory != SimulationSetupBoundaryCategory.Inlet ||
            boundary.CurrentConfiguration.Name != "Supply Inlet" ||
            patchType.value != SimulationSetupBoundaryCategory.Inlet.ToString() ||
            inletMode.value != "VolumeFlowRate" || inletMode.choices.Count != 2 ||
            inletVelocity.enabledSelf || !inletFlow.enabledSelf ||
            Mathf.Abs(inletFlow.value - 0.12f) > 0.000001f ||
            Mathf.Abs(inletTemperature.value - 18f) > 0.000001f ||
            Mathf.Abs(inletDischargeAngle.value - 45f) > 0.000001f)
        {
            issue = "Inlet component binding, counts, or conditional controls are inconsistent.";
            return false;
        }

        int eventCount = 0;
        boundary.ConfigurationChanged += _ => eventCount++;
        inletMode.value = "Velocity";
        inletVelocity.value = new Vector3(1.25f, 0.1f, -0.2f);
        inletTemperature.value = 19.5f;
        SimulationSetupBoundaryConfiguration editedInlet = boundary.CurrentConfiguration;
        if (editedInlet.InputMode != "Velocity" || !inletVelocity.enabledSelf ||
            inletFlow.enabledSelf || editedInlet.VelocityPhys != inletVelocity.value ||
            Mathf.Abs(editedInlet.TemperatureDegC - 19.5f) > 0.000001f || eventCount != 3)
        {
            issue = "Staged Inlet parameter binding is inconsistent.";
            return false;
        }

        if (!boundary.SelectCategory(SimulationSetupBoundaryCategory.Outlet))
        {
            issue = "Unable to select the Outlet category.";
            return false;
        }
        Toggle massFlux = root.Q<Toggle>("BoundaryMassFluxToggle");
        Label outletMode = root.Q<Label>("BoundaryOutletModeValue");
        FloatField density = root.Q<FloatField>("BoundaryOutletDensityField");
        FloatField blend = root.Q<FloatField>("BoundaryOutletBlendField");
        FloatField anchor = root.Q<FloatField>("BoundaryOutletAnchorField");
        if (boundary.CurrentConfiguration.Name != "Return Outlet" || massFlux == null ||
            outletMode == null || density == null || blend == null || anchor == null ||
            massFlux.enabledSelf || !massFlux.value || outletMode.text != "AutoMassBalancedOutlet" ||
            Mathf.Abs(density.value - 1f) > 0.000001f ||
            Mathf.Abs(blend.value - 0.35f) > 0.000001f ||
            Mathf.Abs(anchor.value - 0.10f) > 0.000001f)
        {
            issue = "Mass-Flux Corrected Outlet mapping is inconsistent.";
            return false;
        }

        if (!boundary.SelectBoundary("wall:domain"))
        {
            issue = "Unable to select the implicit domain wall.";
            return false;
        }
        Toggle enabled = root.Q<Toggle>("BoundaryEnabledToggle");
        DropdownField thermal = root.Q<DropdownField>("BoundaryWallThermalField");
        FloatField wallTemperature = root.Q<FloatField>("BoundaryWallTemperatureField");
        if (enabled == null || thermal == null || wallTemperature == null || enabled.enabledSelf ||
            thermal.enabledSelf || wallTemperature.enabledSelf ||
            boundary.CurrentConfiguration.ThermalBoundary != ThermalBoundaryType.Adiabatic)
        {
            issue = "Implicit domain wall lock state is inconsistent.";
            return false;
        }

        if (!boundary.SelectBoundary("wall:device") || !thermal.enabledSelf ||
            !wallTemperature.enabledSelf || thermal.value != "Isothermal" ||
            Mathf.Abs(wallTemperature.value - 35f) > 0.000001f)
        {
            issue = "DeviceObstacles wall binding is inconsistent.";
            return false;
        }
        thermal.value = "Adiabatic";
        if (wallTemperature.enabledSelf ||
            boundary.CurrentConfiguration.ThermalBoundary != ThermalBoundaryType.Adiabatic ||
            eventCount != 4 || !boundary.IsValid)
        {
            issue = "Wall thermal staging or validation is inconsistent.";
            return false;
        }

        Debug.Log(
            $"{BoundaryLogTag} PASS actual Inlet/Outlet/Wall mapping, staged binding, and validation.");
        issue = string.Empty;
        return true;
    }

    private static bool ValidateRunControl(out string issue)
    {
        if (!runtimeDocument.StepNavigation.SelectStep(
                SimulationSetupStepNavigationController.SimulationStep.Run))
        {
            issue = "Unable to activate the Run step.";
            return false;
        }

        SimulationSetupRunControlController runControl = runtimeDocument.RunControl;
        VisualElement root = uiDocument.rootVisualElement;
        Button start = root.Q<Button>("SimulationStartButton");
        Button pause = root.Q<Button>("SimulationPauseButton");
        Button resume = root.Q<Button>("SimulationResumeButton");
        Button stop = root.Q<Button>("SimulationStopButton");
        Label state = root.Q<Label>("SimulationRunStateValue");
        Label readiness = root.Q<Label>("RunReviewReadinessValue");
        if (runControl == null || validationRunTarget == null || start == null || pause == null ||
            resume == null || stop == null || state == null || readiness == null ||
            runControl.State != SimulationSetupRunState.Idle || state.text != "Idle" ||
            !start.enabledSelf || pause.enabledSelf || resume.enabledSelf || stop.enabledSelf ||
            readiness.text != "Ready")
        {
            issue = "Idle Run Control state or setup review mapping is inconsistent.";
            return false;
        }

        if (!runControl.TryStartNewRun() || runControl.State != SimulationSetupRunState.Running ||
            start.enabledSelf || !pause.enabledSelf || resume.enabledSelf || !stop.enabledSelf)
        {
            issue = "Running button state is inconsistent.";
            return false;
        }

        if (!runControl.TryPause() || runControl.State != SimulationSetupRunState.Paused ||
            start.enabledSelf || pause.enabledSelf || !resume.enabledSelf || !stop.enabledSelf)
        {
            issue = "Paused button state is inconsistent.";
            return false;
        }

        validationRunTarget.IsResumeBlockedExternally = true;
        runControl.Refresh();
        if (resume.enabledSelf)
        {
            issue = "Resume must be disabled while an external FMU pause is active.";
            return false;
        }
        validationRunTarget.IsResumeBlockedExternally = false;
        runControl.Refresh();

        if (!runControl.TryResume() || runControl.State != SimulationSetupRunState.Running ||
            !pause.enabledSelf || !stop.enabledSelf)
        {
            issue = "Resume transition is inconsistent.";
            return false;
        }

        if (!runControl.TryStopPreservingResults() ||
            runControl.State != SimulationSetupRunState.Idle || !start.enabledSelf ||
            pause.enabledSelf || resume.enabledSelf || stop.enabledSelf)
        {
            issue = "Stop-with-preserved-results transition is inconsistent.";
            return false;
        }

        validationRunTarget.SetState(SimulationSetupRunState.Completed, "Target reached.");
        runControl.Refresh();
        if (runControl.State != SimulationSetupRunState.Completed || !start.enabledSelf ||
            pause.enabledSelf || resume.enabledSelf || stop.enabledSelf)
        {
            issue = "Completed button state is inconsistent.";
            return false;
        }

        validationRunTarget.SetState(SimulationSetupRunState.Error, "Validation error.");
        runControl.Refresh();
        if (runControl.State != SimulationSetupRunState.Error || !start.enabledSelf ||
            pause.enabledSelf || resume.enabledSelf || !stop.enabledSelf)
        {
            issue = "Error button state is inconsistent.";
            return false;
        }

        Debug.Log(
            $"{RunControlLogTag} PASS Start, Pause, Resume, Stop, and five state button policies.");
        issue = string.Empty;
        return true;
    }

    private static bool ValidateProgress(out string issue)
    {
        SimulationSetupProgressController progress = runtimeDocument.ProgressController;
        VisualElement root = uiDocument.rootVisualElement;
        VisualElement fill = root.Q<VisualElement>("SimulationProgressFill");
        Label percent = root.Q<Label>("SimulationProgressPercentValue");
        Label simulationTime = root.Q<Label>("SimulationTimeValue");
        Label targetTime = root.Q<Label>("SimulationTargetTimeValue");
        Label timeStep = root.Q<Label>("SimulationTimeStepValue");
        Label stepCount = root.Q<Label>("SimulationStepCountValue");
        Label remaining = root.Q<Label>("SimulationEstimatedRemainingValue");
        if (progress == null || validationProgressSource == null || fill == null ||
            percent == null || simulationTime == null || targetTime == null ||
            timeStep == null || stepCount == null || remaining == null)
        {
            issue = "Simulation Progress controller or UI fields are missing.";
            return false;
        }

        progress.Refresh();
        if (Mathf.Abs(progress.CurrentProgress01 - 0.4f) > 0.000001f ||
            fill.style.width.value.unit != LengthUnit.Percent ||
            Mathf.Abs(fill.style.width.value.value - 40f) > 0.000001f ||
            percent.text != "40.0%" || simulationTime.text != "12 s" ||
            targetTime.text != "30 s" || timeStep.text != "0.004 s/step" ||
            stepCount.text != "3,000" ||
            remaining.text != SimulationSetupProgressController.UndefinedEtaText)
        {
            issue = "Simulation time, target progress, timestep, or undefined ETA mapping is inconsistent.";
            return false;
        }

        validationProgressSource.Snapshot = new SimulationSetupProgressSnapshot(
            true, 30f, true, 30f, 0.004f, 7500UL, true);
        progress.Refresh();
        if (Mathf.Abs(progress.CurrentProgress01 - 1f) > 0.000001f ||
            percent.text != "100.0%" || Mathf.Abs(fill.style.width.value.value - 100f) > 0.000001f)
        {
            issue = "Completed progress state is inconsistent.";
            return false;
        }

        validationProgressSource.Snapshot = new SimulationSetupProgressSnapshot(
            true, 5f, false, 0f, 0.00025f, 20000UL, false);
        progress.Refresh();
        if (progress.CurrentProgress01 != 0f || percent.text != "Manual" ||
            targetTime.text != "Manual / no target" || timeStep.text != "2.5E-4 s/step" ||
            remaining.text != SimulationSetupProgressController.UndefinedEtaText)
        {
            issue = "Manual-run progress or small timestep formatting is inconsistent.";
            return false;
        }

        Debug.Log(
            $"{ProgressLogTag} PASS simulation time, target progress, timestep, step count, and undefined ETA policy.");
        issue = string.Empty;
        return true;
    }

    private static bool ValidateSystemMonitor(out string issue)
    {
        SimulationSetupSystemMonitorController monitor = runtimeDocument.SystemMonitor;
        VisualElement root = uiDocument.rootVisualElement;
        Label state = root.Q<Label>("SystemMonitorStateValue");
        Label gpuName = root.Q<Label>("SystemMonitorGpuNameValue");
        Label graphicsApi = root.Q<Label>("SystemMonitorGraphicsApiValue");
        Label gpuUsage = root.Q<Label>("SystemMonitorGpuUsageValue");
        Label gpuMemory = root.Q<Label>("SystemMonitorGpuMemoryValue");
        Label systemMemory = root.Q<Label>("SystemMonitorSystemMemoryValue");
        Label fps = root.Q<Label>("SystemMonitorFpsValue");
        Label status = root.Q<Label>("SystemMonitorStatus");
        if (monitor == null || validationSystemMonitorSource == null || state == null ||
            gpuName == null || graphicsApi == null || gpuUsage == null ||
            gpuMemory == null || systemMemory == null || fps == null || status == null)
        {
            issue = "GPU/System Monitor controller or UI fields are missing.";
            return false;
        }

        monitor.Refresh();
        if (state.text != "Live" || gpuName.text != "Validation GPU" ||
            graphicsApi.text != "Direct3D12" ||
            gpuUsage.text != SimulationSetupSystemMonitorController.UnavailableText ||
            !gpuUsage.ClassListContains("system-monitor__value--unavailable") ||
            gpuMemory.text != "Est. 180.2 / 8192 MiB" ||
            systemMemory.text != "Unity 512.5 / 32768 MiB" || fps.text != "60.0" ||
            !status.text.Contains("no Native Plugin is installed"))
        {
            issue = "Built-in system information or unavailable GPU utilization policy is inconsistent.";
            return false;
        }

        validationSystemMonitorSource.Snapshot = new SimulationSetupSystemMonitorSnapshot(
            true, "Validation GPU 2", "Vulkan", 72.5f,
            0f, 4096f, 640f, 32768f, 58.4f);
        monitor.Refresh();
        if (gpuName.text != "Validation GPU 2" || graphicsApi.text != "Vulkan" ||
            gpuUsage.text != "72.5%" ||
            gpuUsage.ClassListContains("system-monitor__value--unavailable") ||
            gpuMemory.text != "Total 4096 MiB" ||
            systemMemory.text != "Unity 640.0 / 32768 MiB" || fps.text != "58.4" ||
            !status.text.Contains("configured monitor provider"))
        {
            issue = "Optional GPU utilization provider or system metric refresh mapping is inconsistent.";
            return false;
        }

        Debug.Log(
            $"{SystemMonitorLogTag} PASS GPU identity/API, honest unavailable usage fallback, " +
            "solver GPU memory estimate, system memory, FPS, and provider refresh mapping.");
        issue = string.Empty;
        return true;
    }

    private static void BeginResolution(int index, PanelSettings panelSettings)
    {
        ReleaseValidationTarget();
        resolutionIndex = index;
        Vector2Int resolution = TestResolutions[index];
        validationTarget = new RenderTexture(
            resolution.x,
            resolution.y,
            0,
            RenderTextureFormat.ARGB32)
        {
            name = $"SimulationSetupValidation_{resolution.x}x{resolution.y}"
        };
        validationTarget.Create();
        panelSettings.targetTexture = validationTarget;
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
        navigationValidated = false;
        caseSelectionValidated = false;
        modelSelectionValidated = false;
        meshValidated = false;
        physicsValidated = false;
        boundaryValidated = false;
        runControlValidated = false;
        validationRunTarget = null;
        progressValidated = false;
        validationProgressSource = null;
        systemMonitorValidated = false;
        validationSystemMonitorSource = null;

        if (exitWhenFinished)
            EditorApplication.Exit(exitCode);
    }

    private static void ReleaseValidationTarget()
    {
        if (validationTarget == null)
            return;

        validationTarget.Release();
        UnityEngine.Object.DestroyImmediate(validationTarget);
        validationTarget = null;
    }
}
