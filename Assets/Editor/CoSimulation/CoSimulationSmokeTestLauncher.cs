using System;

[Obsolete("Use CoSimulationSceneConfigurator.RunShortIntegrationTest or the Tools/Co-Simulation menu.")]
public static class CoSimulationSmokeTestLauncher
{
    public static void Run50SecondTest()
    {
        CoSimulationSceneConfigurator.RunShortIntegrationTest(
            targetSimulationTimeSeconds: 50.0f,
            quitEditorWhenComplete: true);
    }
}
