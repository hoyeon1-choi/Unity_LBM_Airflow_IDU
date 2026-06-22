using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CaseStudyRuntimeRunner : MonoBehaviour
{
    [SerializeField] private CaseStudyPreset[] caseSequence =
    {
        CaseStudyPreset.A0_Baseline,
        CaseStudyPreset.A1_FluidTau_0530_Thermal_0560_Off,
        CaseStudyPreset.A2_FluidTau_0510_Thermal_0560_Off,
        CaseStudyPreset.A3_FluidTau_0510_Thermal_0530_Off,
        CaseStudyPreset.A4_FluidTau_0510_Thermal_0530_Smag003
    };

    [SerializeField] private float postCaseWaitSeconds = 2.0f;
    [SerializeField] private float maxRealSecondsPerCase = 1200.0f;
    [SerializeField] private bool quitEditorWhenComplete = true;

    private SimulationController simulationController;
    private SimulationMetricsFileLogger metricsFileLogger;
    private readonly StringBuilder runLog = new StringBuilder(2048);

    public static CaseStudyRuntimeRunner CreateDefaultRunner(bool quitWhenComplete)
    {
        var existing = FindFirstObjectByType<CaseStudyRuntimeRunner>();
        if (existing != null)
        {
            existing.quitEditorWhenComplete = quitWhenComplete;
            return existing;
        }

        var go = new GameObject("CaseStudyRuntimeRunner");
        if (Application.isPlaying)
            DontDestroyOnLoad(go);
        var runner = go.AddComponent<CaseStudyRuntimeRunner>();
        runner.quitEditorWhenComplete = quitWhenComplete;
        return runner;
    }

    private IEnumerator Start()
    {
        yield return RunCases();
    }

    private IEnumerator RunCases()
    {
        simulationController = FindFirstObjectByType<SimulationController>();
        metricsFileLogger = FindFirstObjectByType<SimulationMetricsFileLogger>();

        if (simulationController == null)
        {
            Debug.LogError("[CaseStudyRuntimeRunner] SimulationController not found.");
            Finish(1);
            yield break;
        }

        runLog.AppendLine("Case study runtime runner started.");
        runLog.AppendLine($"Started at: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        if (!simulationController.CaseStudyExecutionEnabled)
        {
            runLog.AppendLine("Case study execution is disabled by SimulationController setting.");
            Debug.LogWarning(
                "[CaseStudyRuntimeRunner] Case study execution is disabled. " +
                "Enable it in SimulationController > Case Study before running.");
            WriteRunLog();
            Finish(0);
            yield break;
        }

        simulationController.SetSimulationRunning(false);
        yield return null;

        foreach (CaseStudyPreset preset in caseSequence)
        {
            string caseName = preset.ToString();
            runLog.AppendLine($"START {caseName} at realTime={Time.realtimeSinceStartup:F3}s");
            Debug.Log($"[CaseStudyRuntimeRunner] Starting {caseName}");

            simulationController.RunCaseStudyPreset(preset);

            float startedAt = Time.realtimeSinceStartup;
            int stoppedFrames = 0;

            while (true)
            {
                yield return null;

                bool stoppedAtTarget =
                    !simulationController.IsSimulationRunning &&
                    simulationController.TargetTimeReached;

                if (stoppedAtTarget)
                {
                    stoppedFrames++;
                    if (stoppedFrames >= 10)
                        break;
                }
                else
                {
                    stoppedFrames = 0;
                }

                if (Time.realtimeSinceStartup - startedAt > maxRealSecondsPerCase)
                {
                    Debug.LogWarning($"[CaseStudyRuntimeRunner] Timeout while running {caseName}. Forcing stop.");
                    simulationController.SetSimulationRunning(false);
                    break;
                }
            }

            yield return new WaitForSecondsRealtime(postCaseWaitSeconds);

            if (metricsFileLogger != null)
            {
                metricsFileLogger.TryForceWriteTimeSeriesRow();
                metricsFileLogger.ForceWriteSummaryRow();
            }

            runLog.AppendLine(
                $"DONE {caseName}, simTime={simulationController.SimulatedTimeSeconds:F6}s, " +
                $"targetReached={simulationController.TargetTimeReached}");
            Debug.Log($"[CaseStudyRuntimeRunner] Completed {caseName}");

            yield return new WaitForSecondsRealtime(postCaseWaitSeconds);
        }

        runLog.AppendLine($"Finished at: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteRunLog();
        Finish(0);
    }

    private void WriteRunLog()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string reportDir = Path.Combine(projectRoot, "CaseStudyReports");
        Directory.CreateDirectory(reportDir);
        string path = Path.Combine(reportDir, "case_study_run_log.txt");
        File.WriteAllText(path, runLog.ToString(), Encoding.UTF8);
    }

    private void Finish(int exitCode)
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
        if (quitEditorWhenComplete)
            EditorApplication.Exit(exitCode);
#else
        Application.Quit(exitCode);
#endif
    }
}
