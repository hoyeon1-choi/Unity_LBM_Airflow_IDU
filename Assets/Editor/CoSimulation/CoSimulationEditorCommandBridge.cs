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

    private static bool isProcessing;

    static CoSimulationEditorCommandBridge()
    {
        EditorApplication.delayCall += ProcessPendingCommand;
        EditorApplication.update += PollPendingCommand;
    }

    [MenuItem("Tools/Co-Simulation/Command Bridge/Run Pending Command")]
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
