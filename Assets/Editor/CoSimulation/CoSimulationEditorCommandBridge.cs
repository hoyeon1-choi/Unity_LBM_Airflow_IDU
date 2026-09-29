using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CoSimulationEditorCommandBridge
{
    private const string RunMultiV50sTriggerRelativePath = "Temp/CoSimulationTests/run_multiv_product_50s.trigger";
    private const string StopPlayModeTriggerRelativePath = "Temp/CoSimulationTests/stop_play_mode.trigger";
    private const float DefaultMultiVTargetSimulationTimeSeconds = 50.0f;
    private const string CommandStatusRelativePath = "Temp/CoSimulationTests/editor_command_status.txt";
    private const string AutoConfirmSessionKey = "CoSimulation.EditorCommand.AutoConfirm";

    private static bool isProcessing;

    static CoSimulationEditorCommandBridge()
    {
        EditorApplication.delayCall += ProcessPendingCommand;
        EditorApplication.update += PollPendingCommand;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    public static void ProcessPendingCommand()
    {

        if (ProcessStopPlayModeCommand())
            return;
        if (isProcessing || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        string triggerPath = ToProjectPath(RunMultiV50sTriggerRelativePath);
        if (!File.Exists(triggerPath))
            return;

        isProcessing = true;
        try
        {
            string commandText = File.ReadAllText(triggerPath).Trim();
            string token = string.IsNullOrWhiteSpace(commandText) ? "-" : commandText;
            float targetSimulationTimeSeconds = ParseTargetSimulationTimeSeconds(
                commandText,
                DefaultMultiVTargetSimulationTimeSeconds);

            File.Delete(triggerPath);
            SessionState.SetBool(AutoConfirmSessionKey, true);
            WriteStatus($"Started MultiV product test. target={targetSimulationTimeSeconds:F3}s, token={token}, time={DateTime.Now:O}");
            Debug.Log($"[CoSimulation] Command bridge starting MultiV product test. target={targetSimulationTimeSeconds:F3}s, token={token}");
            CoSimulationSceneConfigurator.RunMultiVProductDraftTest(
                targetSimulationTimeSeconds: targetSimulationTimeSeconds,
                quitEditorWhenComplete: false);
        }
        catch (Exception ex)
        {
            WriteStatus($"Failed to start MultiV product 50s test: {ex}");
            Debug.LogException(ex);
        }
        finally
        {
            isProcessing = false;
        }
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(AutoConfirmSessionKey, false))
            return;

        SessionState.EraseBool(AutoConfirmSessionKey);
        EditorApplication.delayCall += ApplyAutomatedStartupConditions;
    }

    private static void ApplyAutomatedStartupConditions()
    {
        CoSimulationOrchestrator orchestrator = UnityEngine.Object.FindFirstObjectByType<CoSimulationOrchestrator>();
        SimulationController controller = UnityEngine.Object.FindFirstObjectByType<SimulationController>();
        if (orchestrator == null || controller == null)
        {
            WriteStatus("Automated startup confirmation failed: orchestrator or SimulationController was not found.");
            return;
        }

        orchestrator.ApplyStartupConditions(30.0f, 50.0f, 35.0f, 70.0f, 28.0f, true, 0, 4, 3, 45.0f);
        CoSimulationStartupGate.Confirm();
        controller.SetSimulationRunning(true);
        WriteStatus($"Automated startup conditions confirmed. time={DateTime.Now:O}");
        Debug.Log("[CoSimulation] Command bridge confirmed automated R1-on startup conditions.");
    }

    private static void PollPendingCommand()
    {
        if (ProcessStopPlayModeCommand())
            return;
        if (File.Exists(ToProjectPath(RunMultiV50sTriggerRelativePath)))
            ProcessPendingCommand();
    }

    private static void WriteStatus(string status)
    {
        string path = ToProjectPath(CommandStatusRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, status);
    }

    private static bool ProcessStopPlayModeCommand()
    {
        string triggerPath = ToProjectPath(StopPlayModeTriggerRelativePath);
        if (!File.Exists(triggerPath))
            return false;

        string token = File.ReadAllText(triggerPath).Trim();
        File.Delete(triggerPath);
        WriteStatus($"Stop Play mode requested. token={token}, time={DateTime.Now:O}");
        Debug.Log($"[CoSimulation] Command bridge stopping Play mode. token={token}");

        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;

        return true;
    }

    private static float ParseTargetSimulationTimeSeconds(string commandText, float fallback)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return fallback;

        string[] tokens = commandText.Split(new[] { ' ', '\t', '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            int equalsIndex = token.IndexOf('=');
            if (equalsIndex >= 0)
                token = token.Substring(equalsIndex + 1);

            if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                return Mathf.Max(0.0f, parsed);
        }

        return fallback;
    }

    private static string ToProjectPath(string relativePath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
