using UnityEditor;

[InitializeOnLoad]
internal static class CoSimulationOneShotR1FiveMinuteTest
{
    private const string SessionKey = "CoSimulation.OneShotR1FiveMinuteTest.20260916";

    static CoSimulationOneShotR1FiveMinuteTest()
    {
        if (SessionState.GetBool(SessionKey, false))
            return;

        SessionState.SetBool(SessionKey, true);
        EditorApplication.delayCall += RunWhenReady;
    }

    private static void RunWhenReady()
    {
        if (EditorApplication.isCompiling ||
            EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += RunWhenReady;
            return;
        }

        CoSimulationSceneConfigurator.RunMultiVProductDraftTest(
            targetSimulationTimeSeconds: 300.0f,
            quitEditorWhenComplete: false);
    }
}
