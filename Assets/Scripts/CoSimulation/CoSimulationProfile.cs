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

    [Header("Profile Constant Signals")]
    [SerializeField] private List<CoSimConstantSignal> constantSignals = new List<CoSimConstantSignal>();

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
    public IReadOnlyList<CoSimConstantSignal> ConstantSignals => constantSignals;
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

    [ContextMenu("Reset To MultiV Product Draft Defaults")]
    public void ResetToMultiVProductDefaults()
    {
        ApplyMultiVProductDefaults();
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

    public static CoSimulationProfile CreateDefaultMultiVProductProfile()
    {
        CoSimulationProfile profile = CreateInstance<CoSimulationProfile>();
        profile.name = "Runtime_MultiV_Product_Draft_Profile";
        profile.ApplyMultiVProductDefaults();
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
            new CoSimulationFmuModelConfig("Simple_CFMU_Model", "Simple_CFMU", "controller/Simple_CFMU.fmu"),
            new CoSimulationFmuModelConfig("Simple_Plant_Model", "Simple_Plant", "plant/Simple_Plant.fmu")
        };

        constantSignals = new List<CoSimConstantSignal>();

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

    private void ApplyMultiVProductDefaults()
    {
        profileName = "MultiV_Product_Draft";
        csvFilePrefix = "multi_v_product_co_simulation";
        coSimStepSizeSeconds = 1.0;
        useLbmSimulatedTime = true;
        runFmuBeforeLbmStep = false;
        logEveryCoSimStep = true;
        airflowModelId = "airflow";
        sensorSignalName = "T_sensor";
        dischargeSignalName = "T_discharge";
        sensorSource = AirflowLbmSignalAdapter.SensorTemperatureSource.OutletAverageTemperatureDegC;
        fallbackTemperatureDegC = 20.0f;
        syncControllerAfterSet = false;

        CoSimulationFmuModelConfig controller = new CoSimulationFmuModelConfig(
            "MultiV_Controller_Model",
            "Multi_V_S__Set_CFMU_CS",
            "controller/Multi_V_S__Set_CFMU_CS.fmu");
        controller.defaultStepSize = 1.0;
        controller.useExternalRuntime = true;
        controller.fallbackToMockOnNativeFailure = false;
        controller.externalCommandTimeoutMs = 30000;
        controller.loadMissingRealParametersFromFmu = false;
        controller.stringParameterOverrides = new List<CoSimulationStringParameterPreset>();

        fmuModels = new List<CoSimulationFmuModelConfig>
        {
            controller,
            new CoSimulationFmuModelConfig("MultiV_Product_Model", "MULTIV_FMU_WARPPER", "product/MULTIV_FMU_WARPPER.fmu") { defaultStepSize = 1.0, useExternalRuntime = true, fallbackToMockOnNativeFailure = false, externalCommandTimeoutMs = 30000, loadMissingRealParametersFromFmu = false },
            new CoSimulationFmuModelConfig("Simple_Chamber_R1_Model", "Simple_Chamber_R1", "plant/Simple_Chamber_R1.fmu") { defaultStepSize = 1.0, useExternalRuntime = true, fallbackToMockOnNativeFailure = false, externalCommandTimeoutMs = 30000, loadMissingRealParametersFromFmu = false },
            new CoSimulationFmuModelConfig("Simple_Chamber_R2_Model", "Simple_Chamber_R2", "plant/Simple_Chamber_R2.fmu") { defaultStepSize = 1.0, useExternalRuntime = true, fallbackToMockOnNativeFailure = false, externalCommandTimeoutMs = 30000, loadMissingRealParametersFromFmu = false },
            new CoSimulationFmuModelConfig("Simple_Chamber_R3_Model", "Simple_Chamber_R3", "plant/Simple_Chamber_R3.fmu") { defaultStepSize = 1.0, useExternalRuntime = true, fallbackToMockOnNativeFailure = false, externalCommandTimeoutMs = 30000, loadMissingRealParametersFromFmu = false },
            new CoSimulationFmuModelConfig("Simple_Chamber_R4_Model", "Simple_Chamber_R4", "plant/Simple_Chamber_R4.fmu") { defaultStepSize = 1.0, useExternalRuntime = true, fallbackToMockOnNativeFailure = false, externalCommandTimeoutMs = 30000, loadMissingRealParametersFromFmu = false },
            new CoSimulationFmuModelConfig("Simple_Chamber_R5_Model", "Simple_Chamber_R5", "plant/Simple_Chamber_R5.fmu") { defaultStepSize = 1.0, useExternalRuntime = true, fallbackToMockOnNativeFailure = false, externalCommandTimeoutMs = 30000, loadMissingRealParametersFromFmu = false }
        };

        constantSignals = new List<CoSimConstantSignal>
        {
            NewRealConstant("profile", "idu_on", 1.0),
            NewRealConstant("profile", "set_mode", 0.0),
            NewRealConstant("profile", "set_temp", 28.0),
            NewRealConstant("profile", "set_fan", 0.0),
            NewRealConstant("profile", "room_humidity_percent", 40.0),
            NewRealConstant("profile", "outdoor_temp_c", 35.0),
            NewRealConstant("profile", "zero", 0.0)
        };

        connections = new List<CoSimConnection>();
        for (int i = 1; i <= 5; i++)
            AddMultiVIndoorUnitConnections(i);

        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Pressure_HI", "Multi_V_S__Set_CFMU_CS", "Multi_V_S_Sensor__Pressure_HI", "Product high pressure sensor to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Pressure_LO", "Multi_V_S__Set_CFMU_CS", "Multi_V_S_Sensor__Pressure_LO", "Product low pressure sensor to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_SC_Out", "Multi_V_S__Set_CFMU_CS", "Multi_V_S_Sensor__Temp_SC_Out", "Product subcooling outlet temperature to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_SC_In", "Multi_V_S__Set_CFMU_CS", "Multi_V_S_Sensor__Temp_SC_In", "Product subcooling inlet temperature to controller."));
        connections.Add(NewConnection("profile", "outdoor_temp_c", "Multi_V_S__Set_CFMU_CS", "Multi_V_S_Sensor__Temp_OutAir", "Outdoor air temperature default."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Liquid", "Multi_V_S__Set_CFMU_CS", "Multi_V_S_Sensor__Temp_Liquid", "Product liquid temperature to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_HEXPipe", "Multi_V_S__Set_CFMU_CS", "Multi_V_S_Sensor__Temp_HEXPipe", "Product HEX pipe temperature to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Discharge", "Multi_V_S__Set_CFMU_CS", "Multi_V_S_Sensor__Temp_Discharge", "Product discharge temperature to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Suction", "Multi_V_S__Set_CFMU_CS", "Multi_V_S_Sensor__Temp_Suction", "Product suction temperature to controller."));
        connections.Add(NewConnection("Multi_V_S__Set_CFMU_CS", "Multi_V_S_Comp__TarFreq", "MULTIV_FMU_WARPPER", "Comp_CurFreq", "Controller compressor target frequency to product compressor input."));
        connections.Add(NewConnection("Multi_V_S__Set_CFMU_CS", "Multi_V_S_Fan1__TarRPM", "MULTIV_FMU_WARPPER", "Fan_CurRPM", "Controller fan target RPM to product fan input."));
        connections.Add(NewConnection("Multi_V_S__Set_CFMU_CS", "Multi_V_S_4Way_Valve__OnOff", "MULTIV_FMU_WARPPER", "reversing_valve_mode_flag", "Controller reversing valve output to product."));
        connections.Add(NewConnection("Multi_V_S__Set_CFMU_CS", "Multi_V_S_MAIN_EEV__CurPulse", "MULTIV_FMU_WARPPER", "MAIN_EEV_CurPulse", "Controller main EEV current pulse to product."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "IDU_01_Air_Temp_Discharge", "airflow", "T_discharge", "First IDU discharge temperature to LBM inlet boundary."));

        controllerSetpointSignal = new CoSimSignalReference("profile", "set_temp");
        controllerOutputSignal = new CoSimSignalReference("Multi_V_S__Set_CFMU_CS", "Multi_V_S_Comp__TarFreq");
        plantInputSignal = new CoSimSignalReference("MULTIV_FMU_WARPPER", "Comp_CurFreq");
        dischargeOutputSignal = new CoSimSignalReference("MULTIV_FMU_WARPPER", "IDU_01_Air_Temp_Discharge");

        debugSignals = new List<CoSimDebugSignal>
        {
            new CoSimDebugSignal("LBM_T_sensor", "airflow", "T_sensor"),
            new CoSimDebugSignal("SetTemp", "profile", "set_temp"),
            new CoSimDebugSignal("CompTarFreq", "Multi_V_S__Set_CFMU_CS", "Multi_V_S_Comp__TarFreq"),
            new CoSimDebugSignal("FanTarRPM", "Multi_V_S__Set_CFMU_CS", "Multi_V_S_Fan1__TarRPM"),
            new CoSimDebugSignal("IDU01_T_dis", "MULTIV_FMU_WARPPER", "IDU_01_Air_Temp_Discharge"),
            new CoSimDebugSignal("IDU01_T_suc", "Simple_Chamber_R1", "T_air_suc")
        };
    }

    private void AddMultiVIndoorUnitConnections(int index)
    {
        string idu = $"IDU_{index:00}";
        string iduLower = $"idu_{index:00}";
        string chamber = $"Simple_Chamber_R{index}";
        string pipeInOutput = index == 2 ? "IDU_02_Sensor_Temp_Pipe_In2" : $"IDU_{index:00}_Sensor_Temp_Pipe_In";

        connections.Add(NewConnection("profile", "idu_on", "Multi_V_S__Set_CFMU_CS", $"{idu}_FOnOff", "Indoor unit on command."));
        connections.Add(NewConnection("profile", "set_mode", "Multi_V_S__Set_CFMU_CS", $"{idu}_SetMode", "Indoor unit mode command."));
        connections.Add(NewConnection("profile", "set_temp", "Multi_V_S__Set_CFMU_CS", $"{idu}_SetTemp", "Indoor unit set temperature."));
        connections.Add(NewConnection("profile", "set_fan", "Multi_V_S__Set_CFMU_CS", $"{idu}_SetFan", "Indoor unit fan command."));
        connections.Add(NewConnection(chamber, "T_air_suc", "Multi_V_S__Set_CFMU_CS", $"{idu}_Room_Temp", "Chamber suction temperature to controller room sensor."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", pipeInOutput, "Multi_V_S__Set_CFMU_CS", $"{idu}_Pipe_In_Temp", "Product pipe-in temperature to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", $"IDU_{index:00}_Sensor_Temp_Pipe_Out", "Multi_V_S__Set_CFMU_CS", $"{idu}_Pipe_Out_Temp", "Product pipe-out temperature to controller."));
        connections.Add(NewConnection("profile", "room_humidity_percent", "Multi_V_S__Set_CFMU_CS", $"{idu}_Humidity", "Indoor humidity default."));

        connections.Add(NewConnection("profile", "idu_on", "MULTIV_FMU_WARPPER", $"{iduLower}_onoff", "Indoor unit on command to product."));
        connections.Add(NewConnection("Multi_V_S__Set_CFMU_CS", $"{idu}_CurSetFan", "MULTIV_FMU_WARPPER", $"{iduLower}_fan_mode", "Controller fan mode to product."));
        connections.Add(NewConnection("Multi_V_S__Set_CFMU_CS", $"{idu}_EEV_TarPulse", "MULTIV_FMU_WARPPER", $"{iduLower}_pulse", "Controller EEV target pulse to product."));
        connections.Add(NewConnection(chamber, "T_air_suc", "MULTIV_FMU_WARPPER", $"{iduLower}_temp_air", "Chamber suction temperature to product indoor inlet air."));
        connections.Add(NewConnection(chamber, "RH_air_suc", "MULTIV_FMU_WARPPER", $"{iduLower}_RH_air", "Chamber suction RH to product indoor inlet air."));

        connections.Add(NewConnection("MULTIV_FMU_WARPPER", $"IDU_{index:00}_Air_Temp_Discharge", chamber, "T_air_dis", "Product discharge temperature to chamber."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", $"IDU_{index:00}_Air_RH_Discharge", chamber, "RH_air_dis", "Product discharge RH to chamber."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", $"IDU_{index:00}_Air_mfr_Discharge", chamber, "mfr_air_dis", "Product discharge mass flow to chamber."));
    }

    private static CoSimConstantSignal NewRealConstant(string modelId, string variableName, double value)
    {
        return new CoSimConstantSignal
        {
            enabled = true,
            modelId = modelId,
            variableName = variableName,
            valueType = SignalValueType.Real,
            realValue = value
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
    public bool useExternalRuntime = false;
    public bool fallbackToMockOnNativeFailure = true;
    public int externalCommandTimeoutMs = 30000;
    public bool logging = true;
    public double defaultStepSize = 2.0;
    public bool loadMissingRealParametersFromFmu = true;
    public List<CoSimulationRealParameterPreset> realParameterOverrides =
        new List<CoSimulationRealParameterPreset>();
    public List<CoSimulationStringParameterPreset> stringParameterOverrides =
        new List<CoSimulationStringParameterPreset>();

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
public class CoSimulationRealParameterPreset
{
    public bool enabled = true;
    public string variableName = string.Empty;
    public double value = 0.0;

    public CoSimulationRealParameterPreset()
    {
    }

    public CoSimulationRealParameterPreset(string variableName, double value)
    {
        this.variableName = variableName;
        this.value = value;
    }
}

[Serializable]
public class CoSimulationStringParameterPreset
{
    public bool enabled = true;
    public string variableName = string.Empty;
    public string value = string.Empty;
    public bool rewriteModelDescriptionStart = true;

    public CoSimulationStringParameterPreset()
    {
    }

    public CoSimulationStringParameterPreset(string variableName, string value)
    {
        this.variableName = variableName;
        this.value = value;
    }
}

[Serializable]
public class CoSimConstantSignal
{
    public bool enabled = true;
    public string modelId = "profile";
    public string variableName = "constant";
    public SignalValueType valueType = SignalValueType.Real;
    public double realValue = 0.0;
    public int intValue = 0;
    public bool boolValue = false;
    public string stringValue = string.Empty;

    public CoSimSignalKey Key => new CoSimSignalKey(modelId, variableName);

    public CoSimSignalValue ToSignalValue(double simTimeSeconds)
    {
        switch (valueType)
        {
            case SignalValueType.Integer:
                return CoSimSignalValue.FromInteger(intValue, simTimeSeconds);
            case SignalValueType.Boolean:
                return CoSimSignalValue.FromBoolean(boolValue, simTimeSeconds);
            case SignalValueType.String:
                return CoSimSignalValue.FromString(stringValue, simTimeSeconds);
            default:
                return CoSimSignalValue.FromReal(realValue, simTimeSeconds);
        }
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
