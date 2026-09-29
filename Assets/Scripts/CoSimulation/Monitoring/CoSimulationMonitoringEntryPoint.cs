using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
[AddComponentMenu("Co-Simulation/Monitoring Entry Point")]
public sealed class CoSimulationMonitoringEntryPoint : MonoBehaviour
{
    [Header("Authored Dashboard")]
    [Tooltip("Dashboard stored in the Monitoring scene. If empty, the legacy runtime fallback finds or creates one.")]
    [SerializeField] private CoSimulationMonitoringDashboard dashboard;
    [SerializeField] private bool createDashboardAutomatically = true;

    [Header("Display Separation")]
    [SerializeField] private bool preferDedicatedMonitoringDisplay = true;
    [SerializeField, Range(0, 7)] private int monitoringDisplayIndex = 1;
    [SerializeField, Min(640)] private int monitoringRenderWidth = 1920;
    [SerializeField, Min(360)] private int monitoringRenderHeight = 1080;
    [SerializeField] private bool fallbackToPrimaryDisplay = true;
    [SerializeField] private bool enableF10VisibilityToggle = true;

    private IEnumerator Start()
    {
        // ApplicationBootstrap owns scene loading. Wait one frame so references created by the
        // already-loaded LBM scene have completed their Start phase before the dashboard binds.
        yield return null;

        CoSimulationMonitoringDashboard monitor = dashboard != null
            ? dashboard
            : FindFirstObjectByType<CoSimulationMonitoringDashboard>();
        if (createDashboardAutomatically && monitor == null)
        {
            GameObject dashboardObject = new GameObject("CoSimulationMonitoringDashboard");
            dashboardObject.transform.SetParent(transform, false);
            monitor = dashboardObject.AddComponent<CoSimulationMonitoringDashboard>();
        }

        if (monitor != null)
            monitor.ConfigureDisplay(
                monitoringDisplayIndex,
                preferDedicatedMonitoringDisplay,
                fallbackToPrimaryDisplay,
                enableF10VisibilityToggle,
                monitoringRenderWidth,
                monitoringRenderHeight);

        if (FindFirstObjectByType<CoSimulationOrchestrator>() == null)
        {
            Debug.LogWarning(
                "[CoSim Monitor][Bootstrap] CoSimulationOrchestrator was not found. " +
                "Start the application from Assets/Scenes/Bootstrap/ApplicationBootstrap.unity; " +
                "the Monitoring scene no longer loads the LBM scene directly.");
        }
    }

    public void ConfigureAuthoredDashboard(CoSimulationMonitoringDashboard authoredDashboard)
    {
        dashboard = authoredDashboard;
        createDashboardAutomatically = authoredDashboard == null;
    }
}
