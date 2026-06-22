using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CaseStudyBatchRunner
{
    private const string ScenePath = "Assets/Scenes/LBMScenes/LBM_1wayCST.unity";
    private const string TriggerPath = "CaseStudyReports/run_case_study.trigger";
    private static double nextTriggerPollTime;

    static CaseStudyBatchRunner()
    {
        EditorApplication.update += PollTriggerFile;
    }

    [MenuItem("ThermalField/Case Study/Run A0-A4 And Keep Editor")]
    public static void RunAllCasesFromMenu()
    {
        StartRunner(quitWhenComplete: false);
    }

    public static void RunAllCasesAndQuit()
    {
        StartRunner(quitWhenComplete: true);
    }

    private static void StartRunner(bool quitWhenComplete)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[CaseStudyBatchRunner] Editor is already entering or running Play Mode.");
            return;
        }

        Scene activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.path != ScenePath)
        {
            if (Application.isBatchMode)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else
            {
                Debug.LogError(
                    $"[CaseStudyBatchRunner] Active scene is '{activeScene.path}'. " +
                    $"Open '{ScenePath}' before running the case study.");
                return;
            }
        }

        var controller = Object.FindFirstObjectByType<SimulationController>();
        if (controller != null && !controller.CaseStudyExecutionEnabled)
        {
            Debug.LogWarning(
                "[CaseStudyBatchRunner] Case study execution is disabled in SimulationController. " +
                "Enable it in the Case Study section before running.");
            return;
        }

        CaseStudyRuntimeRunner.CreateDefaultRunner(quitWhenComplete);
        EditorApplication.delayCall += () =>
        {
            Debug.Log("[CaseStudyBatchRunner] Entering Play Mode for A0-A4 case study run.");
            EditorApplication.isPlaying = true;
        };
    }

    private static void PollTriggerFile()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        double now = EditorApplication.timeSinceStartup;
        if (now < nextTriggerPollTime)
            return;

        nextTriggerPollTime = now + 2.0;

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string trigger = Path.Combine(projectRoot, TriggerPath);
        if (!File.Exists(trigger))
            return;

        try
        {
            File.Delete(trigger);
        }
        catch (IOException)
        {
            return;
        }

        Debug.Log("[CaseStudyBatchRunner] Trigger file detected. Starting A0-A4 case study in the open Editor.");
        StartRunner(quitWhenComplete: false);
    }
}
