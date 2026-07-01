using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Co-Simulation/Connection Map", fileName = "CoSimConnectionMap")]
public class CoSimConnectionMap : ScriptableObject
{
    [SerializeField] private List<CoSimConnection> connections = new List<CoSimConnection>();

    public IReadOnlyList<CoSimConnection> Connections => connections;

    public int Count => connections != null ? connections.Count : 0;

    public IEnumerable<CoSimConnection> EnabledConnections
    {
        get
        {
            if (connections == null)
                yield break;

            for (int i = 0; i < connections.Count; i++)
            {
                CoSimConnection connection = connections[i];
                if (connection != null && connection.enabled)
                    yield return connection;
            }
        }
    }

    [ContextMenu("Create Default Simple Airflow FMU Map")]
    public void CreateDefaultSimpleAirflowFmuMap()
    {
        if (connections == null)
            connections = new List<CoSimConnection>();

        connections.Clear();
        Add("airflow", "T_sensor", "Simple_CFMU", "T_sensor",
            "LBM outlet average temperature to controller sensor input.");
        Add("Simple_CFMU", "Hz", "Simple_Plant", "hz_Plant",
            "Controller frequency output to plant frequency input.");
        Add("airflow", "T_sensor", "Simple_Plant", "T_sensor_Plant",
            "LBM outlet average temperature to plant sensor input.");
        Add("Simple_Plant", "T_dis_Plant", "airflow", "T_discharge",
            "Plant discharge temperature to LBM inlet temperature target.");
    }

    public static CoSimConnectionMap CreateDefaultRuntimeMap()
    {
        CoSimConnectionMap map = CreateInstance<CoSimConnectionMap>();
        map.CreateDefaultSimpleAirflowFmuMap();
        map.name = "Runtime_Default_Simple_Airflow_FMU_Map";
        return map;
    }

    private void Add(string sourceModelId, string sourceVariableName,
                     string targetModelId, string targetVariableName,
                     string description)
    {
        connections.Add(new CoSimConnection
        {
            enabled = true,
            sourceModelId = sourceModelId,
            sourceVariableName = sourceVariableName,
            targetModelId = targetModelId,
            targetVariableName = targetVariableName,
            scale = 1.0,
            offset = 0.0,
            useClampMin = false,
            useClampMax = false,
            description = description
        });
    }
}
