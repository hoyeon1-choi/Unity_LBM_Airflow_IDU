using System.Linq;
using UnityEngine;

public class SimulationSceneCache : MonoBehaviour
{
    public DeviceObstacles[] HeatSources { get; private set; } = System.Array.Empty<DeviceObstacles>();
    public Racks[] Racks { get; private set; } = System.Array.Empty<Racks>();
    public ACSource[] ACSources { get; private set; } = System.Array.Empty<ACSource>();
    public LBMZouHeBox[] ZouHeBoxes { get; private set; } = System.Array.Empty<LBMZouHeBox>();

    public bool IsDirty { get; private set; } = true;

    public void MarkDirty()
    {
        IsDirty = true;
    }

    public void ForceRefresh()
    {
        Refresh();
    }

    public void Refresh()
    {
        HeatSources = FindObjectsByType<DeviceObstacles>(FindObjectsSortMode.InstanceID);
        Racks = FindObjectsByType<Racks>(FindObjectsSortMode.InstanceID);
        ACSources = FindObjectsByType<ACSource>(FindObjectsSortMode.InstanceID);
        ZouHeBoxes = FindObjectsByType<LBMZouHeBox>(FindObjectsSortMode.InstanceID)
            .Where(box => box != null && box.Power)
            .ToArray();

        IsDirty = false;
    }

    public void LogIfEmpty()
    {
        if (HeatSources == null || HeatSources.Length == 0)
            Debug.LogWarning("There are no heat sources in the Scene.");

        if (Racks == null || Racks.Length == 0)
            Debug.LogWarning("There are no racks in the Scene.");

        if (ACSources == null || ACSources.Length == 0)
            Debug.LogWarning("There are no AC sources in the Scene.");

        if (ZouHeBoxes == null || ZouHeBoxes.Length == 0)
            Debug.LogWarning("There are no Zou-He boxes in the Scene.");
    }
}