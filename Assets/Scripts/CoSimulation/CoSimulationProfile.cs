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
        NormalizeMultiVIndoorUnitControlConnections(map);
        return map;
    }

    private void NormalizeMultiVIndoorUnitControlConnections(CoSimConnectionMap map)
    {
        // Older authored scenes may contain one shared source for every room.
        // Normalize only the runtime clone so serialized legacy profiles gain
        // independent R2-R5 power, mode, temperature, and fan commands.
        if (map == null || ProfileName.IndexOf("MultiV", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        for (int room = 2; room <= 5; room++)
        {
            string controllerTarget = $"IDU_{room:00}.FOnOff";
            string productTarget = $"idu_{room:00}_onoff";
            string roomPowerSignal = $"idu_{room:00}_on";
            string controllerPrefix = $"IDU_{room:00}.";
            string roomControlPrefix = $"idu_{room:00}_";
            for (int i = 0; i < map.Connections.Count; i++)
            {
                CoSimConnection connection = map.Connections[i];
                if (connection == null ||
                    !string.Equals(connection.sourceModelId, "profile", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(connection.sourceVariableName, "idu_on", StringComparison.Ordinal) &&
                    (string.Equals(connection.targetVariableName, controllerTarget, StringComparison.Ordinal) ||
                     string.Equals(connection.targetVariableName, productTarget, StringComparison.Ordinal)))
                {
                    connection.sourceVariableName = roomPowerSignal;
                    continue;
                }

                if (!connection.targetVariableName.StartsWith(
                        controllerPrefix,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(connection.sourceVariableName, "set_mode", StringComparison.Ordinal) &&
                    string.Equals(connection.targetVariableName, $"{controllerPrefix}SetMode", StringComparison.Ordinal))
                    connection.sourceVariableName = $"{roomControlPrefix}set_mode";
                else if (string.Equals(connection.sourceVariableName, "set_temp", StringComparison.Ordinal) &&
                         string.Equals(connection.targetVariableName, $"{controllerPrefix}SetTemp", StringComparison.Ordinal))
                    connection.sourceVariableName = $"{roomControlPrefix}set_temp";
                else if (string.Equals(connection.sourceVariableName, "set_fan", StringComparison.Ordinal) &&
                         string.Equals(connection.targetVariableName, $"{controllerPrefix}SetFan", StringComparison.Ordinal))
                    connection.sourceVariableName = $"{roomControlPrefix}set_fan";
            }
        }
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
        logEveryCoSimStep = false;
        airflowModelId = "airflow";
        sensorSignalName = "T_sensor";
        dischargeSignalName = "T_discharge";
        sensorSource = AirflowLbmSignalAdapter.SensorTemperatureSource.OutletAverageTemperatureDegC;
        fallbackTemperatureDegC = 20.0f;
        syncControllerAfterSet = false;

        CoSimulationFmuModelConfig controller = new CoSimulationFmuModelConfig(
            "MultiV_Controller_Model",
            "Multi_V_S__Set_CFMU",
            "controller/Multi_V_S__Set_CFMU.fmu");
        controller.defaultStepSize = 1.0;
        controller.useExternalRuntime = true;
        controller.launchBundledServer = true;
        controller.fallbackToMockOnNativeFailure = false;
        controller.externalCommandTimeoutMs = 30000;
        controller.loadMissingRealParametersFromFmu = false;
        controller.realParameterOverrides = new List<CoSimulationRealParameterPreset>
        {
            new CoSimulationRealParameterPreset("Period", 1.0),
            new CoSimulationRealParameterPreset("Multi_V_S.TotalIDUNum", 5.0),
            new CoSimulationRealParameterPreset("IDU_01.IDU_Address", 1.0),
            new CoSimulationRealParameterPreset("IDU_02.IDU_Address", 2.0),
            new CoSimulationRealParameterPreset("IDU_03.IDU_Address", 3.0),
            new CoSimulationRealParameterPreset("IDU_04.IDU_Address", 4.0),
            new CoSimulationRealParameterPreset("IDU_05.IDU_Address", 5.0)
        };
        controller.integerParameterOverrides = new List<CoSimulationIntegerParameterPreset>
        {
            new CoSimulationIntegerParameterPreset("IDU_01.Type", 1),
            new CoSimulationIntegerParameterPreset("IDU_02.Type", 1),
            new CoSimulationIntegerParameterPreset("IDU_03.Type", 1),
            new CoSimulationIntegerParameterPreset("IDU_04.Type", 1),
            new CoSimulationIntegerParameterPreset("IDU_05.Type", 1)
        };
        controller.stringParameterOverrides = new List<CoSimulationStringParameterPreset>
        {
            new CoSimulationStringParameterPreset("IDU_01.Option_HEX_path", "{FMU_ROOT}/Korea_MultiV_CST_Main_EEPROM_24C16_RNW0721C2S_SAA43756039_001_4DDC_0x03F4B670.hex"),
            new CoSimulationStringParameterPreset("IDU_02.Option_HEX_path", "{FMU_ROOT}/Korea_MultiV_CST_Main_EEPROM_24C16_RNW0721C2S_SAA43756039_001_4DDC_0x03F4B670.hex"),
            new CoSimulationStringParameterPreset("IDU_03.Option_HEX_path", "{FMU_ROOT}/Korea_MultiV_CST_Main_EEPROM_24C16_RNW0721C2S_SAA43756039_001_4DDC_0x03F4B670.hex"),
            new CoSimulationStringParameterPreset("IDU_04.Option_HEX_path", "{FMU_ROOT}/Korea_MultiV_CST_Main_EEPROM_24C16_RNW0721C2S_SAA43756039_001_4DDC_0x03F4B670.hex"),
            new CoSimulationStringParameterPreset("IDU_05.Option_HEX_path", "{FMU_ROOT}/Korea_MultiV_CST_Main_EEPROM_24C16_RNW0721C2S_SAA43756039_001_4DDC_0x03F4B670.hex"),
            new CoSimulationStringParameterPreset("Multi_V_S.Option_HEX_path", "{FMU_ROOT}/S_SAA37571716_RPUW100S9S_141016_0456.hex")
        };

        CoSimulationFmuModelConfig product = new CoSimulationFmuModelConfig(
            "MultiV_Product_Model",
            "MULTIV_FMU_WARPPER",
            "product/MULTIV_FMU_WARPPER.fmu")
        {
            defaultStepSize = 0.02,
            useAdaptiveSubsteps = true,
            adaptiveMinStepSize = 0.02,
            adaptiveMaxStepSize = 1.0,
            adaptiveInitialStepSize = 0.02,
            adaptiveFastCommandThresholdMs = 1000,
            adaptiveSlowCommandThresholdMs = 5000,
            adaptiveSuccessesBeforeIncrease = 5,
            adaptiveIncreaseFactor = 1.25,
            adaptiveDecreaseFactor = 0.5,
            useExternalRuntime = true,
            fallbackToMockOnNativeFailure = false,
            externalCommandTimeoutMs = 30000,
            loadMissingRealParametersFromFmu = false
        };
        product.initialRealInputValues = CreateMultiVProductInitialInputs();

        fmuModels = new List<CoSimulationFmuModelConfig>
        {
            controller,
            product,
            new CoSimulationFmuModelConfig("Simple_Chamber_R2_Model", "Simple_Chamber_R2", "plant/Simple_Chamber_R2.fmu") { defaultStepSize = 1.0, useExternalRuntime = true, fallbackToMockOnNativeFailure = false, externalCommandTimeoutMs = 30000, loadMissingRealParametersFromFmu = false },
            new CoSimulationFmuModelConfig("Simple_Chamber_R3_Model", "Simple_Chamber_R3", "plant/Simple_Chamber_R3.fmu") { defaultStepSize = 1.0, useExternalRuntime = true, fallbackToMockOnNativeFailure = false, externalCommandTimeoutMs = 30000, loadMissingRealParametersFromFmu = false },
            new CoSimulationFmuModelConfig("Simple_Chamber_R4_Model", "Simple_Chamber_R4", "plant/Simple_Chamber_R4.fmu") { defaultStepSize = 1.0, useExternalRuntime = true, fallbackToMockOnNativeFailure = false, externalCommandTimeoutMs = 30000, loadMissingRealParametersFromFmu = false },
            new CoSimulationFmuModelConfig("Simple_Chamber_R5_Model", "Simple_Chamber_R5", "plant/Simple_Chamber_R5.fmu") { defaultStepSize = 1.0, useExternalRuntime = true, fallbackToMockOnNativeFailure = false, externalCommandTimeoutMs = 30000, loadMissingRealParametersFromFmu = false }
        };

        constantSignals = new List<CoSimConstantSignal>
        {
            NewRealConstant("profile", "idu_on", 0.0),
            NewRealConstant("profile", "idu_02_on", 0.0),
            NewRealConstant("profile", "idu_03_on", 0.0),
            NewRealConstant("profile", "idu_04_on", 0.0),
            NewRealConstant("profile", "idu_05_on", 0.0),
            NewRealConstant("profile", "set_mode", 0.0),
            NewRealConstant("profile", "set_temp", 28.0),
            NewRealConstant("profile", "set_fan", 4.0),
            NewRealConstant("profile", "idu_02_set_mode", 0.0),
            NewRealConstant("profile", "idu_02_set_temp", 28.0),
            NewRealConstant("profile", "idu_02_set_fan", 4.0),
            NewRealConstant("profile", "idu_03_set_mode", 0.0),
            NewRealConstant("profile", "idu_03_set_temp", 28.0),
            NewRealConstant("profile", "idu_03_set_fan", 4.0),
            NewRealConstant("profile", "idu_04_set_mode", 0.0),
            NewRealConstant("profile", "idu_04_set_temp", 28.0),
            NewRealConstant("profile", "idu_04_set_fan", 4.0),
            NewRealConstant("profile", "idu_05_set_mode", 0.0),
            NewRealConstant("profile", "idu_05_set_temp", 28.0),
            NewRealConstant("profile", "idu_05_set_fan", 4.0),
            NewRealConstant("profile", "room_humidity_percent", 40.0),
            NewRealConstant("profile", "outdoor_temp_c", 35.0),
            NewRealConstant("profile", "zero", 0.0)
        };

        connections = new List<CoSimConnection>();
        for (int i = 1; i <= 5; i++)
            AddMultiVIndoorUnitConnections(i);

        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Pressure_HI", "Multi_V_S__Set_CFMU", "Multi_V_S.Sensor__Pressure_HI", "Product high pressure sensor to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Pressure_LO", "Multi_V_S__Set_CFMU", "Multi_V_S.Sensor__Pressure_LO", "Product low pressure sensor to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_SC_Out", "Multi_V_S__Set_CFMU", "Multi_V_S.Sensor__Temp_SC_Out", "Product subcooling outlet temperature to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_SC_In", "Multi_V_S__Set_CFMU", "Multi_V_S.Sensor__Temp_SC_In", "Product subcooling inlet temperature to controller."));
        connections.Add(NewConnection("profile", "outdoor_temp_c", "Multi_V_S__Set_CFMU", "Multi_V_S.Sensor__Temp_OutAir", "Outdoor air temperature default."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Liquid", "Multi_V_S__Set_CFMU", "Multi_V_S.Sensor__Temp_Liquid", "Product liquid temperature to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_HEXPipe", "Multi_V_S__Set_CFMU", "Multi_V_S.Sensor__Temp_HEXPipe", "Product HEX pipe temperature to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Discharge", "Multi_V_S__Set_CFMU", "Multi_V_S.Sensor__Temp_Discharge", "Product discharge temperature to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", "ODU_Sensor_Temp_Suction", "Multi_V_S__Set_CFMU", "Multi_V_S.Sensor__Temp_Suction", "Product suction temperature to controller."));
        connections.Add(NewConnection("Multi_V_S__Set_CFMU", "Multi_V_S.Comp__TarFreq", "MULTIV_FMU_WARPPER", "Comp_CurFreq", "Controller compressor target frequency to product compressor input."));
        connections.Add(NewConnection("Multi_V_S__Set_CFMU", "Multi_V_S.Fan1__TarRPM", "MULTIV_FMU_WARPPER", "Fan_CurRPM", "Controller fan target RPM to product fan input."));
        connections.Add(NewConnection("Multi_V_S__Set_CFMU", "Multi_V_S.MAIN_EEV__TarPulse", "MULTIV_FMU_WARPPER", "MAIN_EEV_CurPulse", "Controller main EEV target pulse to product."));
        controllerSetpointSignal = new CoSimSignalReference("profile", "set_temp");
        controllerOutputSignal = new CoSimSignalReference("Multi_V_S__Set_CFMU", "Multi_V_S.Comp__TarFreq");
        plantInputSignal = new CoSimSignalReference("MULTIV_FMU_WARPPER", "Comp_CurFreq");
        dischargeOutputSignal = new CoSimSignalReference("MULTIV_FMU_WARPPER", "IDU_01_Air_Temp_Discharge");

        debugSignals = new List<CoSimDebugSignal>
        {
            new CoSimDebugSignal("LBM_T_sensor", "airflow", "T_sensor"),
            new CoSimDebugSignal("SetTemp", "profile", "set_temp"),
            new CoSimDebugSignal("CompTarFreq", "Multi_V_S__Set_CFMU", "Multi_V_S.Comp__TarFreq"),
            new CoSimDebugSignal("FanTarRPM", "Multi_V_S__Set_CFMU", "Multi_V_S.Fan1__TarRPM"),
            new CoSimDebugSignal("IDU01_T_dis", "MULTIV_FMU_WARPPER", "IDU_01_Air_Temp_Discharge"),
            new CoSimDebugSignal("IDU01_T_suc", "airflow", "T_sensor"),
            new CoSimDebugSignal("IDU01_RH_suc", "airflow", "RH_suction"),
            new CoSimDebugSignal("IDU01_mfr_suc", "airflow", "mfr_suction")
        };
    }

    private static List<CoSimulationRealParameterPreset> CreateMultiVProductInitialInputs()
    {
        List<CoSimulationRealParameterPreset> values = new List<CoSimulationRealParameterPreset>
        {
            new CoSimulationRealParameterPreset("Comp_CurFreq", 0.0),
            new CoSimulationRealParameterPreset("Fan_CurRPM", 0.0),
            new CoSimulationRealParameterPreset("reversing_valve_mode_flag", 0.0),
            new CoSimulationRealParameterPreset("MAIN_EEV_CurPulse", 10.0)
        };

        for (int i = 1; i <= 5; i++)
        {
            string idu = $"idu_{i:00}";
            values.Add(new CoSimulationRealParameterPreset($"{idu}_onoff", 0.0));
            values.Add(new CoSimulationRealParameterPreset($"{idu}_fan_mode", 4.0));
            values.Add(new CoSimulationRealParameterPreset($"{idu}_pulse", 0.0));
            values.Add(new CoSimulationRealParameterPreset($"{idu}_temp_air", 20.0));
            values.Add(new CoSimulationRealParameterPreset($"{idu}_RH_air", 40.0));
        }

        return values;
    }

    private void AddMultiVIndoorUnitConnections(int index)
    {
        string idu = $"IDU_{index:00}";
        string iduLower = $"idu_{index:00}";
        string chamber = index == 1 ? "airflow" : $"Simple_Chamber_R{index}";
        string suctionTemperature = index == 1 ? "T_sensor" : "T_air_suc";
        string suctionHumidityName = index == 1 ? "RH_suction" : "RH_air_suc";
        string pipeInOutput = index == 2 ? "IDU_02_Sensor_Temp_Pipe_In2" : $"IDU_{index:00}_Sensor_Temp_Pipe_In";
        string powerSignal = index == 1 ? "idu_on" : $"idu_{index:00}_on";
        string controlPrefix = index == 1 ? string.Empty : $"idu_{index:00}_";

        connections.Add(NewConnection("profile", powerSignal, "Multi_V_S__Set_CFMU", $"{idu}.FOnOff", "Indoor unit on command."));
        connections.Add(NewConnection("profile", $"{controlPrefix}set_mode", "Multi_V_S__Set_CFMU", $"{idu}.SetMode", "Indoor unit mode command."));
        connections.Add(NewConnection("profile", $"{controlPrefix}set_temp", "Multi_V_S__Set_CFMU", $"{idu}.SetTemp", "Indoor unit set temperature."));
        connections.Add(NewConnection("profile", $"{controlPrefix}set_fan", "Multi_V_S__Set_CFMU", $"{idu}.SetFan", "Indoor unit fan command."));
        connections.Add(NewConnection(chamber, suctionTemperature, "Multi_V_S__Set_CFMU", $"{idu}.Room_Temp", "Indoor suction temperature to controller room sensor."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", pipeInOutput, "Multi_V_S__Set_CFMU", $"{idu}.Pipe_In_Temp", "Product pipe-in temperature to controller."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", $"IDU_{index:00}_Sensor_Temp_Pipe_Out", "Multi_V_S__Set_CFMU", $"{idu}.Pipe_Out_Temp", "Product pipe-out temperature to controller."));
        connections.Add(NewConnection(index == 1 ? "airflow" : "profile", index == 1 ? "RH_suction" : "room_humidity_percent", "Multi_V_S__Set_CFMU", $"{idu}.Humidity", "Indoor suction humidity to controller."));

        connections.Add(NewConnection("profile", powerSignal, "MULTIV_FMU_WARPPER", $"{iduLower}_onoff", "Indoor unit on command to product."));
        connections.Add(NewConnection("Multi_V_S__Set_CFMU", $"{idu}.CurSetFan", "MULTIV_FMU_WARPPER", $"{iduLower}_fan_mode", "Controller fan mode to product."));
        connections.Add(NewConnection("Multi_V_S__Set_CFMU", $"{idu}.EEV_TarPulse", "MULTIV_FMU_WARPPER", $"{iduLower}_pulse", "Controller EEV target pulse to product."));
        connections.Add(NewConnection(chamber, suctionTemperature, "MULTIV_FMU_WARPPER", $"{iduLower}_temp_air", "Indoor suction temperature to product inlet air."));
        CoSimConnection suctionHumidity = NewConnection(
            chamber,
            suctionHumidityName,
            "MULTIV_FMU_WARPPER",
            $"{iduLower}_RH_air",
            "Chamber suction RH percent to product indoor inlet air (clamped to physical range)."
        );
        suctionHumidity.useClampMin = true;
        suctionHumidity.clampMin = 0.0;
        suctionHumidity.useClampMax = true;
        suctionHumidity.clampMax = 100.0;
        connections.Add(suctionHumidity);

        connections.Add(NewConnection("MULTIV_FMU_WARPPER", $"IDU_{index:00}_Air_Temp_Discharge", chamber, index == 1 ? "T_discharge" : "T_air_dis", "Product discharge temperature to indoor airflow model."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", $"IDU_{index:00}_Air_RH_Discharge", chamber, index == 1 ? "RH_discharge" : "RH_air_dis", "Product discharge RH to indoor airflow model."));
        connections.Add(NewConnection("MULTIV_FMU_WARPPER", $"IDU_{index:00}_Air_mfr_Discharge", chamber, index == 1 ? "mfr_discharge" : "mfr_air_dis", "Product discharge mass flow to indoor airflow model."));
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
    public bool launchBundledServer = false;
    public bool fallbackToMockOnNativeFailure = true;
    public int externalCommandTimeoutMs = 30000;
    public bool logging = true;
    [Tooltip("Enables verbose logging inside the FMU implementation. Keep disabled for performance runs.")]
    public bool nativeFmuLogging = false;
    [Tooltip("Writes one Unity log entry before and after every external FMU substep. Keep disabled for normal runs.")]
    public bool verboseExternalStepLogging = false;
    [Tooltip("Transfers all Real inputs/outputs for one FMU in one host request.")]
    public bool batchExternalRealIo = true;
    [Tooltip("Does not resend an external FMU input when its value has not changed.")]
    public bool skipUnchangedExternalInputs = true;
    [Min(0f)] public double unchangedInputTolerance = 1.0e-9;
    [Tooltip("Positive value overrides modelDescription DefaultExperiment tolerance. Zero uses the FMU default.")]
    [Min(0f)] public double experimentToleranceOverride = 0.0;
    public double defaultStepSize = 2.0;
    public bool useAdaptiveSubsteps = false;
    public double adaptiveMinStepSize = 0.02;
    public double adaptiveMaxStepSize = 1.0;
    public double adaptiveInitialStepSize = 0.02;
    public int adaptiveFastCommandThresholdMs = 1000;
    public int adaptiveSlowCommandThresholdMs = 5000;
    public int adaptiveSuccessesBeforeIncrease = 5;
    public double adaptiveIncreaseFactor = 1.25;
    public double adaptiveDecreaseFactor = 0.5;
    public bool loadMissingRealParametersFromFmu = true;
    public List<CoSimulationRealParameterPreset> realParameterOverrides =
        new List<CoSimulationRealParameterPreset>();
    public List<CoSimulationIntegerParameterPreset> integerParameterOverrides =
        new List<CoSimulationIntegerParameterPreset>();
    public List<CoSimulationStringParameterPreset> stringParameterOverrides =
        new List<CoSimulationStringParameterPreset>();
    [Tooltip("Real input values applied while the FMU is in initialization mode. They also provide the first-step fallback for delayed controller outputs.")]
    public List<CoSimulationRealParameterPreset> initialRealInputValues =
        new List<CoSimulationRealParameterPreset>();

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
public class CoSimulationIntegerParameterPreset
{
    public bool enabled = true;
    public string variableName = string.Empty;
    public int value = 0;

    public CoSimulationIntegerParameterPreset()
    {
    }

    public CoSimulationIntegerParameterPreset(string variableName, int value)
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
