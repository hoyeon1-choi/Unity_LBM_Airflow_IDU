using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CoSimulationEditorCommandBridge
{
    private const string RunMultiV50sTriggerRelativePath = "Temp/CoSimulationTests/run_multiv_product_50s.trigger";
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
        if (isProcessing || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        string triggerPath = ToProjectPath(RunMultiV50sTriggerRelativePath);
        if (!File.Exists(triggerPath))
            return;

        isProcessing = true;
        try
        {
            string token = File.ReadAllText(triggerPath).Trim();
            File.Delete(triggerPath);
            WriteStatus($"Started MultiV product 50s test. token={token}, time={DateTime.Now:O}");
            Debug.Log($"[CoSimulation] Command bridge starting MultiV product 50s test. token={token}");
            CoSimulationSceneConfigurator.RunMultiVProductDraftTest(
                targetSimulationTimeSeconds: 50.0f,
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
        if (File.Exists(ToProjectPath(RunMultiV50sTriggerRelativePath)))
            ProcessPendingCommand();
    }

    private static void WriteStatus(string status)
    {
        string path = ToProjectPath(CommandStatusRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, status);
    }

    private static string ToProjectPath(string relativePath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}