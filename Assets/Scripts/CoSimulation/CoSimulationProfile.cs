using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Co-Simulation/Product Profile", fileName = "CoSimulationProductProfile")]
public class CoSimulationProfile : ScriptableObject
{
    [Header("Profile")]
    [SerializeField] private string profileName = "Simple_ControllerPlant";
    [SerializeField] private string csvFilePrefix = "co_simulation";

    [Header("Schedule")]
    [SerializeField] private double coSimStepSizeSeconds = 2.0;
    [SerializeField] private bool useLbmSimulatedTime = true;
    [SerializeField] private bool runFmuBeforeLbmStep = false;
    [SerializeField] private bool logEveryCoSimStep = true;

    [Header("Airflow Signals")]
    [SerializeField] private string airflowModelId = "airflow";
    [SerializeField] private string sensorSignalName = "T_sensor";
    [SerializeField] private string dischargeSignalName = "T_discharge";
    [SerializeField] private AirflowLbmSignalAdapter.SensorTemperatureSource sensorSource =
        AirflowLbmSignalAdapter.SensorTemperatureSource.OutletAverageTemperatureDegC;
    [SerializeField] private float fallbackTemperatureDegC = 20.0f;
    [SerializeField] private bool syncControllerAfterSet = false;

    [Header("FMU Models")]
    [SerializeField] private List<CoSimulationFmuModelConfig> fmuModels =
        new List<CoSimulationFmuModelConfig>();

    [Header("Connections")]
    [SerializeField] private List<CoSimConnection> connections = new List<CoSimConnection>();

    [Header("Primary Debug Signals")]
    [SerializeField] private CoSimSignalReference controllerSetpointSignal =
        new CoSimSignalReference("Simple_CFMU", "T_set");
    [SerializeField] private CoSimSignalReference controllerOutputSignal =
        new CoSimSignalReference("Simple_CFMU", "Hz");
    [SerializeField] private CoSimSignalReference plantInputSignal =
        new CoSimSignalReference("Simple_Plant", "hz_Plant");
    [SerializeField] private CoSimSignalReference dischargeOutputSignal =
        new CoSimSignalReference("Simple_Plant", "T_dis_Plant");

    [Header("Additional Debug Signals")]
    [SerializeField] private List<CoSimDebugSignal> debugSignals = new List<CoSimDebugSignal>();

    public string ProfileName => string.IsNullOrWhiteSpace(profileName) ? name : profileName;
    public string CsvFilePrefix => string.IsNullOrWhiteSpace(csvFilePrefix) ? "co_simulation" : csvFilePrefix;
    public double CoSimStepSizeSeconds => coSimStepSizeSeconds;
    public bool UseLbmSimulatedTime => useLbmSimulatedTime;
    public bool RunFmuBeforeLbmStep => runFmuBeforeLbmStep;
    public bool LogEveryCoSimStep => logEveryCoSimStep;
    public string AirflowModelId => string.IsNullOrWhiteSpace(airflowModelId) ? "airflow" : airflowModelId;
    public string SensorSignalName => string.IsNullOrWhiteSpace(sensorSignalName) ? "T_sensor" : sensorSignalName;
    public string DischargeSignalName => string.IsNullOrWhiteSpace(dischargeSignalName) ? "T_discharge" : dischargeSignalName;
    public AirflowLbmSignalAdapter.SensorTemperatureSource SensorSource => sensorSource;
    public float FallbackTemperatureDegC => fallbackTemperatureDegC;
    public bool SyncControllerAfterSet => syncControllerAfterSet;
    public IReadOnlyList<CoSimulationFmuModelConfig> FmuModels => fmuModels;
    public IReadOnlyList<CoSimConnection> Connections => connections;
    public IReadOnlyList<CoSimDebugSignal> DebugSignals => debugSignals;
    public CoSimSignalReference ControllerSetpointSignal => controllerSetpointSignal;
    public CoSimSignalReference ControllerOutputSignal => controllerOutputSignal;
    public CoSimSignalReference PlantInputSignal => plantInputSignal;
    public CoSimSignalReference DischargeOutputSignal => dischargeOutputSignal;

    [ContextMenu("Reset To Simple Controller/Plant Defaults")]
    public void ResetToSimpleControllerPlantDefaults()
    {
        ApplySimpleDefaults();
    }

    public CoSimConnectionMap CreateRuntimeConnectionMap()
    {
        CoSimConnectionMap map = CreateInstance<CoSimConnectionMap>();
        map.name = $"Runtime_{ProfileName}_ConnectionMap";
        map.SetConnections(connections);
        return map;
    }

    public static CoSimulationProfile CreateDefaultSimpleProfile()
    {
        CoSimulationProfile profile = CreateInstance<CoSimulationProfile>();
        profile.name = "Runtime_Simple_ControllerPlant_Profile";
        profile.ApplySimpleDefaults();
        return profile;
    }

    private void OnValidate()
    {
        if (coSimStepSizeSeconds < 1e-6)
            coSimStepSizeSeconds = 1e-6;
    }

    private void ApplySimpleDefaults()
    {
        profileName = "Simple_ControllerPlant";
        csvFilePrefix = "co_simulation";
        coSimStepSizeSeconds = 2.0;
        useLbmSimulatedTime = true;
        runFmuBeforeLbmStep = false;
        logEveryCoSimStep = true;
        airflowModelId = "airflow";
        sensorSignalName = "T_sensor";
        dischargeSignalName = "T_discharge";
        sensorSource = AirflowLbmSignalAdapter.SensorTemperatureSource.OutletAverageTemperatureDegC;
        fallbackTemperatureDegC = 20.0f;
        syncControllerAfterSet = false;

        fmuModels = new List<CoSimulationFmuModelConfig>
        {
            new CoSimulationFmuModelConfig("Simple_CFMU_Model", "Simple_CFMU", "Simple_CFMU.fmu"),
            new CoSimulationFmuModelConfig("Simple_Plant_Model", "Simple_Plant", "Simple_Plant.fmu")
        };

        connections = new List<CoSimConnection>
        {
            NewConnection("airflow", "T_sensor", "Simple_CFMU", "T_sensor",
                "LBM outlet average temperature to controller sensor input."),
            NewConnection("Simple_CFMU", "Hz", "Simple_Plant", "hz_Plant",
                "Controller frequency output to plant frequency input."),
            NewConnection("airflow", "T_sensor", "Simple_Plant", "T_sensor_Plant",
                "LBM outlet average temperature to plant sensor input."),
            NewConnection("Simple_Plant", "T_dis_Plant", "airflow", "T_discharge",
                "Plant discharge temperature to LBM inlet temperature target.")
        };

        controllerSetpointSignal = new CoSimSignalReference("Simple_CFMU", "T_set");
        controllerOutputSignal = new CoSimSignalReference("Simple_CFMU", "Hz");
        plantInputSignal = new CoSimSignalReference("Simple_Plant", "hz_Plant");
        dischargeOutputSignal = new CoSimSignalReference("Simple_Plant", "T_dis_Plant");

        debugSignals = new List<CoSimDebugSignal>
        {
            new CoSimDebugSignal("T_sensor", "airflow", "T_sensor"),
            new CoSimDebugSignal("T_set", "Simple_CFMU", "T_set"),
            new CoSimDebugSignal("Hz", "Simple_CFMU", "Hz"),
            new CoSimDebugSignal("plantHz", "Simple_Plant", "hz_Plant"),
            new CoSimDebugSignal("T_dis", "Simple_Plant", "T_dis_Plant")
        };
    }

    private static CoSimConnection NewConnection(
        string sourceModelId,
        string sourceVariableName,
        string targetModelId,
        string targetVariableName,
        string description)
    {
        return new CoSimConnection
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
        };
    }
}

[Serializable]
public class CoSimulationFmuModelConfig
{
    public string childObjectName = "FMU_Model";
    public string modelId = "FMU";
    public string fmuFileName = "model.fmu";
    public bool useMockRuntime = false;
    public bool fallbackToMockOnNativeFailure = true;
    public bool logging = true;
    public double defaultStepSize = 2.0;
    public bool loadMissingRealParametersFromFmu = true;

    public CoSimulationFmuModelConfig()
    {
    }

    public CoSimulationFmuModelConfig(string childObjectName, string modelId, string fmuFileName)
    {
        this.childObjectName = childObjectName;
        this.modelId = modelId;
        this.fmuFileName = fmuFileName;
    }
}

[Serializable]
public struct CoSimSignalReference
{
    public string modelId;
    public string variableName;

    public CoSimSignalReference(string modelId, string variableName)
    {
        this.modelId = modelId ?? string.Empty;
        this.variableName = variableName ?? string.Empty;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(modelId) &&
        !string.IsNullOrWhiteSpace(variableName);
}

[Serializable]
public class CoSimDebugSignal
{
    public string label = "signal";
    public string modelId = string.Empty;
    public string variableName = string.Empty;

    public CoSimDebugSignal()
    {
    }

    public CoSimDebugSignal(string label, string modelId, string variableName)
    {
        this.label = label;
        this.modelId = modelId;
        this.variableName = variableName;
    }

    public CoSimSignalReference Reference => new CoSimSignalReference(modelId, variableName);
}
