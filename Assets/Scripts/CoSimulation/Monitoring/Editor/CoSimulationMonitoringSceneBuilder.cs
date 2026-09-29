#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CoSimulationMonitoringSceneBuilder
{
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap/ApplicationBootstrap.unity";
    private const string MonitoringScenePath = "Assets/Scenes/Monitoring/CoSimulationMonitoring.unity";
    private const string SimulationScenePath = "Assets/Scenes/LBMScenes/LBM_1wayCST.unity";
    private const string DashboardScriptPath =
        "Assets/Scripts/CoSimulation/Monitoring/CoSimulationMonitoringDashboard.cs";

    [InitializeOnLoadMethod]
    private static void ScheduleLegacyMonitoringSceneUpgrade()
    {
        EditorApplication.delayCall += UpgradeLegacyMonitoringScene;
    }

    public static void CreateOrRefreshApplicationScenes()
    {
        EnsureSceneFolder("Bootstrap");
        EnsureSceneFolder("Monitoring");

        CreateBootstrapScene();
        CreateMonitoringScene();
        AddScenesToBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"[App Bootstrap][Bootstrap] Refreshed {BootstrapScenePath} and {MonitoringScenePath}; " +
            "Build order is Bootstrap -> LBM -> Monitoring.");
    }

    public static void CreateOrRefreshMonitoringScene()
    {
        EnsureSceneFolder("Monitoring");
        CreateMonitoringScene();
        AddScenesToBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CoSim Monitor] Refreshed editable Monitoring hierarchy: {MonitoringScenePath}");
    }

    private static void CreateBootstrapScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        scene.name = "ApplicationBootstrap";
        GameObject bootstrap = new GameObject("ApplicationBootstrap");
        bootstrap.AddComponent<CoSimulationApplicationBootstrap>();
        SceneManager.MoveGameObjectToScene(bootstrap, scene);

        if (!EditorSceneManager.SaveScene(scene, BootstrapScenePath))
            throw new IOException($"Failed to save bootstrap scene: {BootstrapScenePath}");

        EditorSceneManager.CloseScene(scene, true);
    }

    private static void CreateMonitoringScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        scene.name = "CoSimulationMonitoring";
        EnsureAuthoredMonitoringHierarchy(scene);

        if (!EditorSceneManager.SaveScene(scene, MonitoringScenePath))
            throw new IOException($"Failed to save monitoring scene: {MonitoringScenePath}");

        EditorSceneManager.CloseScene(scene, true);
    }

    private static void UpgradeLegacyMonitoringScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || !File.Exists(MonitoringScenePath))
            return;

        string sceneText = File.ReadAllText(MonitoringScenePath);
        string dashboardGuid = AssetDatabase.AssetPathToGUID(DashboardScriptPath);
        bool hasDashboard = !string.IsNullOrEmpty(dashboardGuid) && sceneText.Contains("guid: " + dashboardGuid);
        if (hasDashboard && CountOccurrences(sceneText, "m_Name: PlotArea") >= 5)
            return;

        Scene scene = SceneManager.GetSceneByPath(MonitoringScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (!wasLoaded)
            scene = EditorSceneManager.OpenScene(MonitoringScenePath, OpenSceneMode.Additive);

        try
        {
            EnsureAuthoredMonitoringHierarchy(scene);
            if (!EditorSceneManager.SaveScene(scene, MonitoringScenePath))
                throw new IOException($"Failed to upgrade monitoring scene: {MonitoringScenePath}");
            Debug.Log(
                "[CoSim Monitor] Monitoring Scene을 Inspector 편집 가능한 Canvas/Panel/Graph 계층으로 업그레이드했습니다.");
        }
        finally
        {
            if (!wasLoaded && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void EnsureAuthoredMonitoringHierarchy(Scene scene)
    {
        CoSimulationMonitoringEntryPoint entryPoint = null;
        CoSimulationMonitoringDashboard dashboard = null;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (entryPoint == null)
                entryPoint = roots[i].GetComponentInChildren<CoSimulationMonitoringEntryPoint>(true);
            if (dashboard == null)
                dashboard = roots[i].GetComponentInChildren<CoSimulationMonitoringDashboard>(true);
        }

        if (entryPoint == null)
        {
            GameObject entryPointObject = new GameObject("CoSimulationMonitoringEntryPoint");
            SceneManager.MoveGameObjectToScene(entryPointObject, scene);
            entryPoint = entryPointObject.AddComponent<CoSimulationMonitoringEntryPoint>();
        }

        if (dashboard == null)
        {
            GameObject dashboardObject = new GameObject("CoSimulationMonitoringDashboard", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(dashboardObject, scene);
            dashboard = dashboardObject.AddComponent<CoSimulationMonitoringDashboard>();
        }

        dashboard.BuildAuthoredInterface(TMP_Settings.defaultFontAsset);
        CoSimulationSignalGraphPanel signalPanel = dashboard.GetComponentInChildren<CoSimulationSignalGraphPanel>(true);
        int signalSeriesCount = signalPanel != null ? Mathf.Max(1, signalPanel.SignalCount) : 1;
        CoSimulationMonitorChartGraphic[] charts =
            dashboard.GetComponentsInChildren<CoSimulationMonitorChartGraphic>(true);
        for (int i = 0; i < charts.Length; i++)
        {
            CoSimulationMonitorChartGraphic chart = charts[i];
            int seriesCount = chart.AuthoredSeriesCount;
            if (signalPanel != null && chart.transform.IsChildOf(signalPanel.transform))
                seriesCount = signalSeriesCount;
            else if (chart.transform.parent != null && chart.transform.parent.name == "TemperatureChartPanel")
                seriesCount = 5;
            else if (chart.transform.parent != null && chart.transform.parent.name == "PressureChartPanel")
                seriesCount = 2;
            chart.ConfigureSeries(Mathf.Max(1, seriesCount));
            EditorUtility.SetDirty(chart);
        }
        entryPoint.ConfigureAuthoredDashboard(dashboard);
        EditorUtility.SetDirty(entryPoint);
        EditorUtility.SetDirty(dashboard);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    [MenuItem("Tools/Co-Simulation/Validate Monitoring Scene")]
    public static void ValidateMonitoringScene()
    {
        ValidateBootstrapScene();
        Scene scene = EditorSceneManager.OpenScene(MonitoringScenePath, OpenSceneMode.Additive);
        try
        {
            CoSimulationMonitoringEntryPoint entryPoint = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length && entryPoint == null; i++)
                entryPoint = roots[i].GetComponentInChildren<CoSimulationMonitoringEntryPoint>(true);

            if (entryPoint == null)
                throw new MissingComponentException("Monitoring scene has no CoSimulationMonitoringEntryPoint.");

            CoSimulationMonitoringDashboard authoredDashboard = null;
            for (int i = 0; i < roots.Length && authoredDashboard == null; i++)
                authoredDashboard = roots[i].GetComponentInChildren<CoSimulationMonitoringDashboard>(true);
            if (authoredDashboard == null || authoredDashboard.transform.Find("Background") == null)
                throw new MissingComponentException("Monitoring scene has no authored dashboard hierarchy.");
            CoSimulationSignalGraphPanel authoredSignalPanel =
                authoredDashboard.GetComponentInChildren<CoSimulationSignalGraphPanel>(true);
            if (authoredSignalPanel == null ||
                authoredSignalPanel.GetComponentsInChildren<UnityEngine.UI.Toggle>(true).Length < 20)
                throw new MissingComponentException("Authored Monitoring scene has no complete signal selector hierarchy.");

            GameObject smokeObject = new GameObject("MonitoringDashboard_EditModeSmokeTest");
            SceneManager.MoveGameObjectToScene(smokeObject, scene);
            try
            {
                CoSimulationMonitoringDashboard dashboard = smokeObject.AddComponent<CoSimulationMonitoringDashboard>();
                if (smokeObject.GetComponent<Canvas>() == null)
                    dashboard.SendMessage("Awake", SendMessageOptions.RequireReceiver);
                Canvas canvas = smokeObject.GetComponent<Canvas>();
                TMP_Text[] labels = smokeObject.GetComponentsInChildren<TMP_Text>(true);
                if (canvas == null || labels.Length < 40)
                    throw new MissingComponentException("Monitoring dashboard UI hierarchy was not created completely.");
                if (smokeObject.GetComponentsInChildren<UnityEngine.UI.Text>(true).Length != 0)
                    throw new MissingComponentException("Monitoring dashboard still contains legacy bitmap UI.Text labels.");
                if (canvas.targetDisplay != 1)
                    throw new MissingComponentException("Monitoring dashboard is not targeting Display 2 by default.");
                Camera displayCamera = smokeObject.GetComponentInChildren<Camera>(true);
                if (displayCamera == null || displayCamera.targetDisplay != 1 || displayCamera.cullingMask != 0)
                    throw new MissingComponentException("Monitoring display backdrop camera was not configured correctly.");
                if (labels[0].font == null)
                    throw new MissingComponentException("Monitoring dashboard did not create or assign a TMP font asset.");
                CoSimulationSignalGraphPanel signalGraphPanel =
                    smokeObject.GetComponentInChildren<CoSimulationSignalGraphPanel>(true);
                if (signalGraphPanel == null)
                    throw new MissingComponentException("Monitoring dashboard has no user-selectable signal graph page.");
                if (signalGraphPanel.GetComponentsInChildren<UnityEngine.UI.Toggle>(true).Length < 20)
                    throw new MissingComponentException("Signal graph page did not create the actuator/sensor selector rows.");
                CoSimulationMonitorChartGraphic[] chartGraphics =
                    smokeObject.GetComponentsInChildren<CoSimulationMonitorChartGraphic>(true);
                if (chartGraphics.Length < 5)
                    throw new MissingComponentException("Monitoring dashboard did not create all overview and user graph charts.");
                for (int i = 0; i < chartGraphics.Length; i++)
                {
                    if (!chartGraphics[i].HasAuthoredHierarchy)
                        throw new MissingComponentException(
                            $"Monitoring chart '{chartGraphics[i].name}' has no editable Background/Grid/Series hierarchy.");
                }
                if (smokeObject.GetComponentsInChildren<CoSimulationMonitorSeriesGraphic>(true).Length < 109)
                    throw new MissingComponentException("Monitoring charts did not create all editable Series objects.");
            }
            finally
            {
                Object.DestroyImmediate(smokeObject);
            }

            Debug.Log("[CoSim Monitor][Bootstrap] Validation passed: application bootstrap/monitoring/dashboard/font/display-camera hierarchy OK.");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void ValidateBootstrapScene()
    {
        Scene scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Additive);
        try
        {
            CoSimulationApplicationBootstrap bootstrap = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length && bootstrap == null; i++)
                bootstrap = roots[i].GetComponentInChildren<CoSimulationApplicationBootstrap>(true);

            if (bootstrap == null)
                throw new MissingComponentException("Bootstrap scene has no CoSimulationApplicationBootstrap.");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void EnsureSceneFolder(string folderName)
    {
        string path = $"Assets/Scenes/{folderName}";
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder("Assets/Scenes", folderName);
    }

    private static void AddScenesToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>();
        AddUnique(scenes, BootstrapScenePath, true);
        AddUnique(scenes, SimulationScenePath, true);
        AddUnique(scenes, MonitoringScenePath, true);

        EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
        for (int i = 0; i < existing.Length; i++)
        {
            EditorBuildSettingsScene item = existing[i];
            if (item == null || string.IsNullOrWhiteSpace(item.path))
                continue;
            AddUnique(scenes, item.path, item.enabled);
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void AddUnique(List<EditorBuildSettingsScene> scenes, string path, bool enabled)
    {
        for (int i = 0; i < scenes.Count; i++)
        {
            if (scenes[i].path == path)
                return;
        }
        scenes.Add(new EditorBuildSettingsScene(path, enabled));
    }
}

[CustomEditor(typeof(CoSimulationMonitoringDashboard))]
public sealed class CoSimulationMonitoringDashboardEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Panel, Text, Button, and Chart objects under the Canvas are editable in the Hierarchy. " +
            "A full rebuild resets manual UI edits to their defaults.",
            MessageType.Info);
        if (GUILayout.Button("Rebuild Monitoring Hierarchy") &&
            EditorUtility.DisplayDialog(
                "Rebuild Monitoring Hierarchy",
                "Delete the current Dashboard child UI and rebuild the default hierarchy?",
                "Rebuild",
                "Cancel"))
        {
            ((CoSimulationMonitoringDashboard)target).RebuildMonitoringHierarchy();
        }
    }
}

[CustomEditor(typeof(CoSimulationSignalGraphPanel))]
public sealed class CoSimulationSignalGraphPanelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "After editing the Signal Catalog or default IDs, rebuild to update selector rows and graphs.",
            MessageType.Info);
        if (GUILayout.Button("Rebuild Signal Graph Hierarchy") &&
            EditorUtility.DisplayDialog(
                "Rebuild Signal Graph Hierarchy",
                "Delete the current Signal Graph child UI and rebuild it from the Inspector catalog?",
                "Rebuild",
                "Cancel"))
        {
            ((CoSimulationSignalGraphPanel)target).RebuildSignalGraphHierarchy();
        }
    }
}

[CustomEditor(typeof(CoSimulationMonitorChartGraphic))]
public sealed class CoSimulationMonitorChartGraphicEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Background and Grid use standard UI Images; each data line is a Series object. " +
            "Rebuild after changing default styles or grid counts.",
            MessageType.Info);
        if (GUILayout.Button("Rebuild Chart Hierarchy") &&
            EditorUtility.DisplayDialog(
                "Rebuild Chart Hierarchy",
                "Delete this Chart's Background/PlotArea/Grid/Series children and rebuild them from defaults?",
                "Rebuild",
                "Cancel"))
        {
            ((CoSimulationMonitorChartGraphic)target).RebuildChartHierarchy();
        }
    }
}
#endif
