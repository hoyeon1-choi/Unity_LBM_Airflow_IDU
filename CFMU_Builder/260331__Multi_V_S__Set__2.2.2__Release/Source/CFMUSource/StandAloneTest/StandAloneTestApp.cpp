#include "CFMU/CFMU.h"
#include "CFMU_Builder/CFMUFunctions.h"
#include "CFMU_Builder/CFMUBuildHelper.h"
#include "Utils/BuildInfo.h"
#include "Utils/Timer.h"

#include <iostream>
#include <filesystem>

using std::cerr;
using std::cout;
using std::endl;

namespace fs = std::filesystem;

size_t num_connected_IDU = 5;

void InitializeCFMUSimulator(void);

void SetUserParameters(void) {
	/****************************** SetParameter Here ******************************/
	// Note: Wokring Directory is 'root/Test/StandAlone'
	
	//SetParameterByName("Multi_V_S.LGMV_COM_on/off", 1);
	SetParameterByName("Multi_V_S.TotalIDUNum", num_connected_IDU);

	SetParameterByName("IDU_01.Type", 1);
	SetStringParameterByName("IDU_01.Option_HEX_path", "../../EEPROM_All/CST/TA_28K/RNW0830A2U_SAA41266526_39EB_0x57B76281_MP_221110.hex");
	SetParameterByName("IDU_01.IDU_Address", 1);

	SetParameterByName("IDU_02.Type", 1);
	SetStringParameterByName("IDU_02.Option_HEX_path", "../../EEPROM_All/CST/TA_28K/RNW0830A2U_SAA41266526_39EB_0x57B76281_MP_221110.hex");
	SetParameterByName("IDU_02.IDU_Address", 2);

	SetParameterByName("IDU_03.Type", 1);
	SetStringParameterByName("IDU_03.Option_HEX_path", "../../EEPROM_All/CST/TA_28K/RNW0830A2U_SAA41266526_39EB_0x57B76281_MP_221110.hex");
	SetParameterByName("IDU_03.IDU_Address", 3);

	SetParameterByName("IDU_04.Type", 1);
	SetStringParameterByName("IDU_04.Option_HEX_path", "../../EEPROM_All/CST/TA_28K/RNW0830A2U_SAA41266526_39EB_0x57B76281_MP_221110.hex");
	SetParameterByName("IDU_04.IDU_Address", 4);

	SetParameterByName("IDU_05.Type", 1);
	SetStringParameterByName("IDU_05.Option_HEX_path", "../../EEPROM_All/CST/TA_28K/RNW0830A2U_SAA41266526_39EB_0x57B76281_MP_221110.hex");
	SetParameterByName("IDU_05.IDU_Address", 5);


	/*******************************************************************************/
}

int main(void) {
	// Initialize CFMU Instance
	InitializeCFMUSimulator();

	//StartCSVWriter("result.csv");
	//SetTimeAccelerationLimit(CFMU_Time_Acceleration_Limit::x1);  // for LGMV test

	// Timer Start
	Timer sw;
	sw.tic();
	cout << "[INFO] [StandAloneTestApp.cpp] Stand-Alone Test start..." << endl;

	/****************************** Run Test Start ******************************/
	PrintParameters();
	PrintVariables();

	// Set Input
	SetVariableByName("Multi_V_S.Sensor__Pressure_HI", 2000);
	SetVariableByName("Multi_V_S.Sensor__Pressure_LO", 1800);
	SetVariableByName("Multi_V_S.Sensor__Temp_SC_Out", 35);
	SetVariableByName("Multi_V_S.Sensor__Temp_SC_In", 35);
	SetVariableByName("Multi_V_S.Sensor__Temp_OutAir", 24);
	SetVariableByName("Multi_V_S.Sensor__Temp_Liquid", 32);
	SetVariableByName("Multi_V_S.Sensor__Temp_HEXPipe", 30);
	SetVariableByName("Multi_V_S.Sensor__Temp_Discharge", 70);
	SetVariableByName("Multi_V_S.Sensor__Temp_Suction", 18);
	SetVariableByName("Multi_V_S.Inv1__Input_Current", 20);
	SetVariableByName("Multi_V_S.Inv1__Input_Voltage", 330);
	SetVariableByName("Multi_V_S.Inv1__InputPower_Freq", 60);
	SetVariableByName("Multi_V_S.Inv1__Phase_Current", 18);
	SetVariableByName("Multi_V_S.Inv1__DCLink_Voltage", 310);
	SetVariableByName("Multi_V_S.Inv1__IPM_Temp", 40);

	// 3¨¬¨¢ ¢¥e¡¾a
	for (size_t i = 0; i < 3; ++i) {
		RunMicom(60);
		PrintVariables();
	}

	// ¨öC©ø¡í¡¾a Au¢¯©ª on (CFMU A|¨úiAO¡¤A)
	char nameBuf[256];
	for (int i = 1; i <= num_connected_IDU; ++i) {
		sprintf_s(nameBuf, "IDU_%02d.FOnOff", i);
		SetVariableByName(nameBuf, 1);
		sprintf_s(nameBuf, "IDU_%02d.SetMode", i);
		SetVariableByName(nameBuf, 0);
		sprintf_s(nameBuf, "IDU_%02d.SetTemp", i);
		SetVariableByName(nameBuf, 18);
		sprintf_s(nameBuf, "IDU_%02d.SetFan", i);
		SetVariableByName(nameBuf, 3);
		sprintf_s(nameBuf, "IDU_%02d.Room_Temp", i);
		SetVariableByName(nameBuf, 24);
		sprintf_s(nameBuf, "IDU_%02d.Pipe_In_Temp", i);
		SetVariableByName(nameBuf, 10);
		sprintf_s(nameBuf, "IDU_%02d.Pipe_Out_Temp", i);
		SetVariableByName(nameBuf, 15);
		sprintf_s(nameBuf, "IDU_%02d.Humidity", i);
		SetVariableByName(nameBuf, 60);
	}

	// 1¨öA¡Æ¡Ì ¢¯iAu
	for (size_t i = 0; i < 60; ++i) {
		RunMicom(60);
		PrintVariables();
	}

	/****************************** Run Test End ******************************/

	cout << "[INFO] [StandAloneTestApp.cpp] Elapsed Time: " << sw.toc() << " sec" << endl;
	cout << "[OK] [StandAloneTestApp.cpp] CFMU Structure Test finished" << endl;

	return 0;
}

fs::path rootPath = fs::path("..") / "..";
CFMU_FMI_Config_Info fmiConfigInfo;
CFMU_FMI_Config_String fmiConfigString;
CFMU_FMI_Simulation_State fmiSimState;

void InitializeCFMUSimulator(void) {
	GetBuildInfo(&fmiConfigInfo);
	GetFMIConfigStringFromConfigInfo(&fmiConfigInfo, &fmiConfigString);
	fs::path dummyCFMUBinPath = rootPath / "Build" / "CFMUChildDLL" / fmiConfigString.buildOSStr / fmiConfigString.buildPlatformStr;
	std::strcpy(fmiSimState.parentDLLFilePath, (fs::absolute(dummyCFMUBinPath) / "dummy.dll").string().c_str());
	fs::path cwd = rootPath / "Test" / "StandAlone";
	std::strcpy(fmiSimState.cwd, fs::absolute(cwd).string().c_str());

	CFMU_SetFMIConfigurationInfo(&fmiConfigInfo, &fmiSimState);
	CFMU_UserInitialization();
	CFMU_PreInitialization();
	CFMU_SetStartValues();
	SetUserParameters();
	CFMU_ClearAndInitialization();
	CFMU_SetFMIConfigurationInfo(&fmiConfigInfo, &fmiSimState);
	CFMU_UserInitialization();
	CFMU_PreInitialization();
	CFMU_SetStartValues();
	SetUserParameters();
	CFMU_PostInitialization();
	//CFMU_RunMicomForPeriodNTimes(1); // Run Micom for 1 step to compute a initial value.
}