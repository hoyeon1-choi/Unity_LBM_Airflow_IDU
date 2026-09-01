using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CoSimulationSceneConfigurator
{
    private const string PreferredScenePath = "Assets/Scenes/LBMScenes/LBM_1wayCST.unity";
    private const string LegacyScenePath = "Assets/Prefabs/Scenes/LBMScenes/LBM_1wayCST.unity";
    private const string HarnessName = "__CoSimulationHarness";
    private const string LegacyHarnessName = "__CoSimulationSmokeHarness";
    private const string HistoricHarnessName = "CoSimulationHarness";

    [MenuItem("Tools/Co-Simulation/Apply Production Harness To Open Scene")]
    public static void ApplyProductionHarnessToOpenScene()
    {
        if (!EnsureSceneLoaded())
            return;

        CoSimulationProfile profile = GetSelectedProfileOrDefault();
        ConfigurationResult result = ConfigureOpenScene(
            profile,
            targetSimulationTimeSeconds: 0.0f,
            overrideTargetSimulationTime: false,
            quitEditorWhenComplete: false);

        if (!result.IsValid)
            return;

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(
            $"[CoSimulation] Production harness configured. profile={profile.ProfileName}, " +
            $"harness={result.Harness.name}, controller={result.Controller.name}, fmus={result.FmuModels.Count}");
    }

    [MenuItem("Tools/Co-Simulation/Run Short Integration Test (50s)")]
    public static void RunShortIntegrationTest50s()
    {
        RunShortIntegrationTest(50.0f, false);
    }

    [MenuItem("Tools/Co-Simulation/Run MultiV Product Draft Test (50s)")]
    public static void RunMultiVProductDraftTest50s()
    {
        RunMultiVProductDraftTest(50.0f, false);
    }

    [MenuItem("Tools/Co-Simulation/Probe MultiV Product Native Initialization")]
    public static void ProbeMultiVProductNativeInitializationMenu()
    {
        ProbeMultiVProductNativeInitialization(false);
    }

    public static void ProbeMultiVProductNativeInitialization(bool quitEditorWhenComplete)
    {
        if (!EnsureSceneLoaded())
        {
            if (quitEditorWhenComplete)
                EditorApplication.Exit(1);

            return;
        }

        CoSimulationProfile profile = CoSimulationProfile.CreateDefaultMultiVProductProfile();
        ConfigurationResult result = ConfigureOpenScene(
            profile,
            targetSimulationTimeSeconds: 50.0f,
            overrideTargetSimulationTime: true,
            quitEditorWhenComplete: false);

        if (!result.IsValid)
        {
            if (quitEditorWhenComplete)
                EditorApplication.Exit(1);

            return;
        }

        Debug.Log($"[CoSimulation] MultiV native initialization probe configured. profile={profile.ProfileName}, fmus={result.FmuModels.Count}");

        bool allNative = true;
        StringBuilder summary = new StringBuilder(256);
        double stepSize = Math.Max(profile.CoSimStepSizeSeconds, 1.0e-6);

        for (int i = 0; i < result.FmuModels.Count; i++)
        {
            FmuCoSimulationModel model = result.FmuModels[i];
            if (model == null)
                continue;

            try
            {
                Debug.Log($"[CoSimulation] Native probe initializing {model.ModelId} from {model.name}...");
                model.Initialize(0.0, 50.0, stepSize);
                Debug.Log($"[CoSimulation] Native probe initialized {model.ModelId}. runtime={model.RuntimeMode}");
                if (summary.Length > 0)
                    summary.Append("; ");

                summary.Append(model.ModelId).Append(':').Append(model.RuntimeMode);
                if (model.NativeFallbackActive || !string.Equals(model.RuntimeMode, "Native", StringComparison.Ordinal))
                    allNative = false;
            }
            catch (Exception ex)
            {
                if (summary.Length > 0)
                    summary.Append("; ");

                summary.Append(model.ModelId).Append(":Failed(").Append(ex.Message).Append(')');
                allNative = false;
            }
        }

        string message = $"[CoSimulation] MultiV native initialization probe completed={(allNative ? "OK" : "Check")}, profile={profile.ProfileName}, runtime={summary}";
        if (allNative)
            Debug.Log(message);
        else
            Debug.LogWarning(message);

        if (quitEditorWhenComplete)
            EditorApplication.Exit(allNative ? 0 : 1);
    }
    [MenuItem("Tools/Co-Simulation/Create Simple Controller-Plant Profile Asset")]
    public static void CreateSimpleControllerPlantProfileAsset()
    {
        const string folder = "Assets/CoSimulationProfiles";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "CoSimulationProfiles");

        CoSimulationProfile profile = CoSimulationProfile.CreateDefaultSimpleProfile();
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Simple_ControllerPlant_Profile.asset");
        AssetDatabase.CreateAsset(profile, path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = profile;
        Debug.Log($"[CoSimulation] Created profile asset: {path}");
    }

    [MenuItem("Tools/Co-Simulation/Create MultiV Product Draft Profile Asset")]
    public static void CreateMultiVProductDraftProfileAsset()
    {
        const string folder = "Assets/CoSimulationProfiles";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "CoSimulationProfiles");

        CoSimulationProfile profile = CoSimulationProfile.CreateDefaultMultiVProductProfile();
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/MultiV_Product_Draft_Profile.asset");
        AssetDatabase.CreateAsset(profile, path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = profile;
        Debug.Log($"[CoSimulation] Created MultiV draft profile asset: {path}");
    }
    public static void RunShortIntegrationTest(float targetSimulationTimeSeconds, bool quitEditorWhenComplete)
    {
        RunShortIntegrationTest(
            GetSelectedProfileOrDefault,
            targetSimulationTimeSeconds,
            quitEditorWhenComplete);
    }

    public static void RunMultiVProductDraftTest(float targetSimulationTimeSeconds, bool quitEditorWhenComplete)
    {
        RunShortIntegrationTest(
            CoSimulationProfile.CreateDefaultMultiVProductProfile,
            targetSimulationTimeSeconds,
            quitEditorWhenComplete);
    }

    public static void RunShortIntegrationTest(
        CoSimulationProfile profile,
        float targetSimulationTimeSeconds,
        bool quitEditorWhenComplete)
    {
        RunShortIntegrationTest(
            () => profile != null ? profile : GetSelectedProfileOrDefault(),
            targetSimulationTimeSeconds,
            quitEditorWhenComplete);
    }

    private static void RunShortIntegrationTest(
        Func<CoSimulationProfile> profileFactory,
        float targetSimulationTimeSeconds,
        bool quitEditorWhenComplete)
    {
        if (!EnsureSceneLoaded())
        {
            if (quitEditorWhenComplete)
                EditorApplication.Exit(1);

            return;
        }

        CoSimulationProfile profile = profileFactory != null
            ? profileFactory()
            : GetSelectedProfileOrDefault();

        if (profile == null)
            profile = CoSimulationProfile.CreateDefaultSimpleProfile();

        ConfigurationResult result = ConfigureOpenScene(
            profile,
            targetSimulationTimeSeconds,
            overrideTargetSimulationTime: true,
            quitEditorWhenComplete: quitEditorWhenComplete);

        if (!result.IsValid)
        {
            if (quitEditorWhenComplete)
                EditorApplication.Exit(1);

            return;
        }

        SetPrivate(result.Controller, "useTargetSimulationTime", true);
        SetPrivate(result.Controller, "targetSimulationTimeSeconds", Mathf.Max(0.0f, targetSimulationTimeSeconds));
        result.Controller.SetSimulationRunning(true);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log(
            $"[CoSimulation] Short integration test configured. profile={profile.ProfileName}, target={targetSimulationTimeSeconds:F3}s, " +
            $"quitEditorWhenComplete={quitEditorWhenComplete}");

        EditorApplication.isPlaying = true;
    }

    private static ConfigurationResult ConfigureOpenScene(
        CoSimulationProfile profile,
        float targetSimulationTimeSeconds,
        bool overrideTargetSimulationTime,
        bool quitEditorWhenComplete)
    {
        if (profile == null)
            profile = CoSimulationProfile.CreateDefaultSimpleProfile();

        SimulationController controller = FindSceneComponent<SimulationController>();
        if (controller == null)
            controller = CreateControllerFallback();

        if (controller == null)
        {
            LogSceneDiagnostics();
            Debug.LogError("[CoSimulation] SimulationController not found.");
            return ConfigurationResult.Invalid;
        }

        GameObject harness = GetOrCreateHarness();
        DisableOtherCoSimulationRunners(harness);

        RemoveLegacySmokeDriver(harness);

        AirflowLbmSignalAdapter adapter = GetOrAdd<AirflowLbmSignalAdapter>(harness);
        CoSimulationCsvLogger csvLogger = GetOrAdd<CoSimulationCsvLogger>(harness);
        CoSimulationOrchestrator orchestrator = GetOrAdd<CoSimulationOrchestrator>(harness);
        CoSimulationRunMonitor monitor = GetOrAdd<CoSimulationRunMonitor>(harness);
        orchestrator.enabled = true;
        monitor.enabled = true;

        List<FmuCoSimulationModel> configuredFmus = ConfigureFmuModels(harness.transform, profile);

        SimulationResultSampler sampler = controller.GetComponent<SimulationResultSampler>();
        if (sampler == null)
            sampler = Undo.AddComponent<SimulationResultSampler>(controller.gameObject);

        SimulationMetricsFileLogger metricsLogger = controller.GetComponent<SimulationMetricsFileLogger>();
        if (metricsLogger == null)
            metricsLogger = FindSceneComponent<SimulationMetricsFileLogger>();

        if (metricsLogger == null)
            metricsLogger = Undo.AddComponent<SimulationMetricsFileLogger>(controller.gameObject);

        SetPrivate(sampler, "simulationController", controller);
        SetPrivate(metricsLogger, "simulationController", controller);
        SetPrivate(metricsLogger, "resultSampler", sampler);

        LBMZouHeBox[] inletTargets = FindInlets();

        adapter.ConfigureFromProfile(profile, controller, sampler, inletTargets);

        csvLogger.ConfigureLogging(metricsLogger, profile.CsvFilePrefix, true);

        orchestrator.ApplyProfile(profile);
        SetPrivate(orchestrator, "airflowAdapter", adapter);
        SetPrivate(orchestrator, "csvLogger", csvLogger);
        SetPrivate(orchestrator, "fmuModels", configuredFmus);

        monitor.ConfigureProductionRun(
            orchestrator,
            adapter,
            controller,
            minimumHealthyCoSimSteps: 1,
            startSimulationOnPlay: true,
            runInitialCoSimStepOnStart: true,
            quitEditorWhenComplete: quitEditorWhenComplete,
            exitPlayModeWhenComplete: overrideTargetSimulationTime && !quitEditorWhenComplete);

        if (!overrideTargetSimulationTime)
        {
            SetPrivate(controller, "useTargetSimulationTime", controller.UseTargetSimulationTime);
            SetPrivate(controller, "targetSimulationTimeSeconds", controller.TargetSimulationTimeSeconds);
        }
        else
        {
            SetPrivate(controller, "useTargetSimulationTime", true);
            SetPrivate(controller, "targetSimulationTimeSeconds", Mathf.Max(0.0f, targetSimulationTimeSeconds));
        }

        EditorUtility.SetDirty(harness);
        EditorUtility.SetDirty(adapter);
        EditorUtility.SetDirty(csvLogger);
        EditorUtility.SetDirty(orchestrator);
        EditorUtility.SetDirty(monitor);
        for (int i = 0; i < configuredFmus.Count; i++)
            EditorUtility.SetDirty(configuredFmus[i]);
        EditorUtility.SetDirty(sampler);
        EditorUtility.SetDirty(metricsLogger);
        EditorUtility.SetDirty(controller);

        return new ConfigurationResult(
            harness,
            controller,
            adapter,
            csvLogger,
            orchestrator,
            monitor,
            configuredFmus);
    }

    private static List<FmuCoSimulationModel> ConfigureFmuModels(Transform harnessRoot, CoSimulationProfile profile)
    {
        List<FmuCoSimulationModel> configured = new List<FmuCoSimulationModel>();
        RemoveUnusedFmuChildren(harnessRoot, profile);
        IReadOnlyList<CoSimulationFmuModelConfig> fmuConfigs = profile.FmuModels;

        for (int i = 0; i < fmuConfigs.Count; i++)
        {
            CoSimulationFmuModelConfig config = fmuConfigs[i];
            if (config == null || string.IsNullOrWhiteSpace(config.modelId))
                continue;

            string childName = string.IsNullOrWhiteSpace(config.childObjectName)
                ? $"{config.modelId}_Model"
                : config.childObjectName.Trim();

            GameObject fmuGo = GetOrCreateChild(harnessRoot, childName);
            FmuCoSimulationModel model = GetOrAdd<FmuCoSimulationModel>(fmuGo);
            model.ConfigureModel(config);

            if (config.loadMissingRealParametersFromFmu)
            {
                model.PopulateRealParameterOverridesFromModelDescription(false);
                model.PopulateStringParameterOverridesFromModelDescription(false);
            }

            configured.Add(model);
        }

        return configured;
    }

    private static CoSimulationProfile GetSelectedProfileOrDefault()
    {
        CoSimulationProfile selected = Selection.activeObject as CoSimulationProfile;
        return selected != null ? selected : CoSimulationProfile.CreateDefaultSimpleProfile();
    }

    private static bool EnsureSceneLoaded()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid() && scene.isLoaded && scene.GetRootGameObjects().Length > 0)
            return true;

        string scenePath = File.Exists(PreferredScenePath)
            ? PreferredScenePath
            : (File.Exists(LegacyScenePath) ? LegacyScenePath : string.Empty);

        if (string.IsNullOrEmpty(scenePath))
        {
            Debug.LogError(
                $"[CoSimulation] No open scene and default scene was not found. " +
                $"Checked {PreferredScenePath} and {LegacyScenePath}.");
            return false;
        }

        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        return true;
    }

    private static GameObject GetOrCreateHarness()
    {
        GameObject harness = GameObject.Find(HarnessName);
        if (harness != null)
            return harness;

        harness = GameObject.Find(LegacyHarnessName);
        if (harness != null)
        {
            Undo.RecordObject(harness, "Rename Co-Simulation Harness");
            harness.name = HarnessName;
            return harness;
        }

        harness = GameObject.Find(HistoricHarnessName);
        if (harness != null)
        {
            Undo.RecordObject(harness, "Rename Co-Simulation Harness");
            harness.name = HarnessName;
            return harness;
        }

        harness = new GameObject(HarnessName);
        Undo.RegisterCreatedObjectUndo(harness, "Create Co-Simulation Harness");
        return harness;
    }

    private static void RemoveLegacySmokeDriver(GameObject harness)
    {
#pragma warning disable 0618
        CoSimulationSmokeDriver legacy = harness.GetComponent<CoSimulationSmokeDriver>();
#pragma warning restore 0618
        if (legacy == null)
            return;

        Undo.DestroyObjectImmediate(legacy);
    }


    private static void RemoveUnusedFmuChildren(Transform harnessRoot, CoSimulationProfile profile)
    {
        if (harnessRoot == null || profile == null)
            return;

        HashSet<string> expectedChildNames = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<CoSimulationFmuModelConfig> configs = profile.FmuModels;
        for (int i = 0; i < configs.Count; i++)
        {
            CoSimulationFmuModelConfig config = configs[i];
            if (config == null || string.IsNullOrWhiteSpace(config.modelId))
                continue;

            string childName = string.IsNullOrWhiteSpace(config.childObjectName)
                ? $"{config.modelId}_Model"
                : config.childObjectName.Trim();
            expectedChildNames.Add(childName);
        }

        for (int i = harnessRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = harnessRoot.GetChild(i);
            if (child == null || expectedChildNames.Contains(child.name))
                continue;

            if (child.GetComponent<FmuCoSimulationModel>() == null)
                continue;

            Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    private static void DisableOtherCoSimulationRunners(GameObject activeHarness)
    {
        if (activeHarness == null)
            return;

        CoSimulationOrchestrator[] orchestrators = FindSceneComponents<CoSimulationOrchestrator>();
        for (int i = 0; i < orchestrators.Length; i++)
        {
            CoSimulationOrchestrator candidate = orchestrators[i];
            if (candidate == null || BelongsToHarness(candidate.transform, activeHarness.transform))
                continue;

            Undo.RecordObject(candidate, "Disable inactive co-simulation orchestrator");
            candidate.enabled = false;
            EditorUtility.SetDirty(candidate);
        }

        CoSimulationRunMonitor[] monitors = FindSceneComponents<CoSimulationRunMonitor>();
        for (int i = 0; i < monitors.Length; i++)
        {
            CoSimulationRunMonitor candidate = monitors[i];
            if (candidate == null || BelongsToHarness(candidate.transform, activeHarness.transform))
                continue;

            Undo.RecordObject(candidate, "Disable inactive co-simulation monitor");
            candidate.enabled = false;
            EditorUtility.SetDirty(candidate);
        }
    }


    private static bool BelongsToHarness(Transform candidate, Transform harnessRoot)
    {
        if (candidate == null || harnessRoot == null)
            return false;

        return candidate == harnessRoot || candidate.IsChildOf(harnessRoot);
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        if (component != null)
            return component;

        component = Undo.AddComponent<T>(go);
        return component;
    }

    private static GameObject GetOrCreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            return existing.gameObject;

        GameObject child = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(child, $"Create {name}");
        child.transform.SetParent(parent, false);
        return child;
    }

    private static LBMZouHeBox[] FindInlets()
    {
        LBMZouHeBox[] boxes = FindSceneComponents<LBMZouHeBox>();
        List<LBMZouHeBox> inlets = new List<LBMZouHeBox>();

        for (int i = 0; i < boxes.Length; i++)
        {
            LBMZouHeBox box = boxes[i];
            if (box != null && box.Power && box.PatchKind == LBMZouHeBox.Kind.Inlet)
                inlets.Add(box);
        }

        return inlets.ToArray();
    }

    private static SimulationController CreateControllerFallback()
    {
        GameObject controllerGo = FindSceneGameObjectByName("SimulationController");
        if (controllerGo == null)
            return null;

        SimulationController controller = controllerGo.GetComponent<SimulationController>();
        if (controller == null)
        {
            controller = Undo.AddComponent<SimulationController>(controllerGo);
            Debug.LogWarning(
                "[CoSimulation] Added temporary SimulationController component because the scene instance " +
                "was not bound as a typed component.");
        }

        GameObject domain = FindSceneGameObjectByName("CavityBounds");
        ComputeShader computeShader =
            AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/D3Q7LBMThermalKernel.compute");

        if (domain != null)
            SetPrivate(controller, "domain", domain);

        if (computeShader != null)
            SetPrivate(controller, "lbmComputeShader", computeShader);

        SetPrivate(controller, "dxPhys", 0.04f);
        SetPrivate(controller, "U_ref", 2.0f);
        SetPrivate(controller, "runSimulation", true);

        return controller;
    }

    private static T FindSceneComponent<T>() where T : Component
    {
        T[] components = FindSceneComponents<T>();
        return components.Length > 0 ? components[0] : null;
    }

    private static T[] FindSceneComponents<T>() where T : Component
    {
        List<T> sceneComponents = new List<T>();
        Scene scene = EditorSceneManager.GetActiveScene();

        if (scene.IsValid())
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T[] children = roots[i].GetComponentsInChildren<T>(true);
                for (int j = 0; j < children.Length; j++)
                    AddSceneComponent(sceneComponents, children[j]);
            }
        }

        if (sceneComponents.Count > 0)
            return sceneComponents.ToArray();

        T[] all = Resources.FindObjectsOfTypeAll<T>();
        for (int i = 0; i < all.Length; i++)
            AddSceneComponent(sceneComponents, all[i]);

        return sceneComponents.ToArray();
    }

    private static void AddSceneComponent<T>(List<T> components, T component) where T : Component
    {
        if (component == null)
            return;

        if (EditorUtility.IsPersistent(component))
            return;

        if (!component.gameObject.scene.IsValid())
            return;

        if (!components.Contains(component))
            components.Add(component);
    }

    private static void LogSceneDiagnostics()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        int rootCount = 0;
        bool hasNamedController = false;

        if (scene.IsValid())
        {
            GameObject[] roots = scene.GetRootGameObjects();
            rootCount = roots.Length;
            for (int i = 0; i < roots.Length; i++)
            {
                if (FindGameObjectByName(roots[i].transform, "SimulationController") != null)
                {
                    hasNamedController = true;
                    break;
                }
            }
        }

        Debug.LogError(
            $"[CoSimulation] Scene diagnostics: scene={scene.path}, isLoaded={scene.isLoaded}, " +
            $"rootCount={rootCount}, hasNamedSimulationController={hasNamedController}, " +
            $"resourceControllerCount={Resources.FindObjectsOfTypeAll<SimulationController>().Length}");
    }

    private static GameObject FindSceneGameObjectByName(string objectName)
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid())
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject found = FindGameObjectByName(roots[i].transform, objectName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static GameObject FindGameObjectByName(Transform root, string objectName)
    {
        if (root == null)
            return null;

        if (root.name == objectName)
            return root.gameObject;

        for (int i = 0; i < root.childCount; i++)
        {
            GameObject found = FindGameObjectByName(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static void SetPrivate(object target, string fieldName, object value)
    {
        FieldInfo field = FindField(target.GetType(), fieldName);
        if (field == null)
            throw new MissingFieldException(target.GetType().Name, fieldName);

        field.SetValue(target, value);

        if (target is UnityEngine.Object unityObject)
            EditorUtility.SetDirty(unityObject);
    }

    private static FieldInfo FindField(Type type, string fieldName)
    {
        while (type != null)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
                return field;

            type = type.BaseType;
        }

        return null;
    }

    private readonly struct ConfigurationResult
    {
        public static readonly ConfigurationResult Invalid = new ConfigurationResult();

        public readonly GameObject Harness;
        public readonly SimulationController Controller;
        public readonly AirflowLbmSignalAdapter Adapter;
        public readonly CoSimulationCsvLogger CsvLogger;
        public readonly CoSimulationOrchestrator Orchestrator;
        public readonly CoSimulationRunMonitor Monitor;
        public readonly List<FmuCoSimulationModel> FmuModels;

        public bool IsValid => Harness != null && Controller != null && Orchestrator != null && Monitor != null;

        public ConfigurationResult(
            GameObject harness,
            SimulationController controller,
            AirflowLbmSignalAdapter adapter,
            CoSimulationCsvLogger csvLogger,
            CoSimulationOrchestrator orchestrator,
            CoSimulationRunMonitor monitor,
            List<FmuCoSimulationModel> fmuModels)
        {
            Harness = harness;
            Controller = controller;
            Adapter = adapter;
            CsvLogger = csvLogger;
            Orchestrator = orchestrator;
            Monitor = monitor;
            FmuModels = fmuModels ?? new List<FmuCoSimulationModel>();
        }
    }
}
