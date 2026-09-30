using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-32000)]
[AddComponentMenu("Co-Simulation/Application Bootstrap")]
public sealed class CoSimulationApplicationBootstrap : MonoBehaviour
{
    private const string LogTag = "[App Bootstrap][Bootstrap]";

    [Header("Application Scenes")]
    [SerializeField] private string simulationScenePath = "Assets/Scenes/LBMScenes/LBM_1wayCST.unity";
    [SerializeField] private string monitoringScenePath = "Assets/Scenes/Monitoring/CoSimulationMonitoring.unity";
    [SerializeField] private bool loadMonitoringScene = true;
    [SerializeField] private bool makeSimulationSceneActive = true;

    [Header("Read-Only Status")]
    [SerializeField, ReadOnly] private string startupState = "Waiting";
    [SerializeField, ReadOnly] private string lastError = string.Empty;

    private static CoSimulationApplicationBootstrap instance;

    public string StartupState => startupState;
    public string LastError => lastError;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private IEnumerator Start()
    {
        // Present the Simulation Set-up overlay before any additive scene work starts.
        // Starting LoadSceneAsync during the very first frame can keep that frame from
        // reaching OnGUI while Unity integrates scene objects on the main thread.
        startupState = "Presenting Simulation Set-up";
        yield return null;

        double lbmLoadStartedAt = Time.realtimeSinceStartupAsDouble;
        startupState = "Loading LBM scene";
        yield return LoadSceneIfNeeded(simulationScenePath, "LBM");
        if (!string.IsNullOrEmpty(lastError))
            yield break;
        Debug.Log($"{LogTag} LBM scene ready in {Time.realtimeSinceStartupAsDouble - lbmLoadStartedAt:F3}s.");

        Scene simulationScene = SceneManager.GetSceneByPath(simulationScenePath);
        if (makeSimulationSceneActive && simulationScene.IsValid() && simulationScene.isLoaded)
            SceneManager.SetActiveScene(simulationScene);

        // Allow LBM scene Awake/OnEnable/Start reference resolution to finish before UI binding.
        yield return null;

        if (loadMonitoringScene)
        {
            // The monitoring dashboard is not needed to enter initial conditions. Its
            // authored TMP hierarchy and runtime Korean font used to add about 30 seconds
            // to the critical path before the set-up window became interactive.
            startupState = "Waiting for Simulation Set-up";
            while (CoSimulationStartupGate.IsWaitingForConfirmation)
                yield return null;

            // Let the confirmed set-up repaint once before monitoring initialization.
            yield return null;
            double monitoringLoadStartedAt = Time.realtimeSinceStartupAsDouble;
            startupState = "Loading monitoring scene";
            yield return LoadSceneIfNeeded(monitoringScenePath, "Monitoring");
            if (!string.IsNullOrEmpty(lastError))
                yield break;
            Debug.Log(
                $"{LogTag} Monitoring scene ready in " +
                $"{Time.realtimeSinceStartupAsDouble - monitoringLoadStartedAt:F3}s.");
        }

        if (makeSimulationSceneActive && simulationScene.IsValid() && simulationScene.isLoaded)
            SceneManager.SetActiveScene(simulationScene);

        startupState = "Ready";
        Debug.Log($"{LogTag} Ready. LBM='{simulationScenePath}', Monitoring='{monitoringScenePath}', monitoringEnabled={loadMonitoringScene}.");
    }

    private IEnumerator LoadSceneIfNeeded(string scenePath, string role)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            Fail($"{role} scene path is empty.");
            yield break;
        }

        Scene existing = SceneManager.GetSceneByPath(scenePath);
        if (existing.IsValid() && existing.isLoaded)
        {
            Debug.Log($"{LogTag} {role} scene already loaded: {scenePath}");
            yield break;
        }

        AsyncOperation operation;
        try
        {
            operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
        }
        catch (Exception exception)
        {
            Fail($"Failed to load {role} scene '{scenePath}'. Check Build Settings. {exception.Message}");
            yield break;
        }

        if (operation == null)
        {
            Fail($"Failed to start loading {role} scene '{scenePath}'. Check Build Settings.");
            yield break;
        }

        while (!operation.isDone)
            yield return null;

        Scene loaded = SceneManager.GetSceneByPath(scenePath);
        if (!loaded.IsValid() || !loaded.isLoaded)
        {
            Fail($"{role} scene load completed without a valid loaded scene: '{scenePath}'.");
            yield break;
        }

        Debug.Log($"{LogTag} Loaded {role} scene: {scenePath}");
    }

    private void Fail(string message)
    {
        startupState = "Failed";
        lastError = message;
        Debug.LogError($"{LogTag} {message}");
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
