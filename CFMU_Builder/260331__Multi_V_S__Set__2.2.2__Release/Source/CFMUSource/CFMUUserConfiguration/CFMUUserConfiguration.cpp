#include "CFMU/CFMU.h"
#include "CFMU_Builder/CFMUFunctions.h"

extern "C" {
	#include "CFMUAdaptor.h"
}

#define MODEL_NAME			"Multi_V_S__Set_CFMU"
#define MODEL_VERSION		"2.2.2"
#define DEFAULT_PERIOD		1 // 1sec

void CFMU_Configuration(void) {
	// Set Model Name
	SetModelName(MODEL_NAME);

	// Set Model Version
	SetModelVersion(MODEL_VERSION);

	// Set Default Period (sec)
	SetPeriodDefault(DEFAULT_PERIOD);
}

void CFMU_StructureDefine(void) {
	// Child CFMU
	CreateChildCFMU("Multi_V_S");
	CreateChildCFMU("IDU_01", "");
	CreateChildCFMU("IDU_02", "");
	CreateChildCFMU("IDU_03", "");
	CreateChildCFMU("IDU_04", "");
	CreateChildCFMU("IDU_05", "");

	// Establish connection : ODU <-> IDU
	ConnectMemoryTxRxDevice("ODU<->IDU", "Multi_V_S.IDU_Tx", "Multi_V_S.IDU_Rx");
	ConnectMemoryTxRxDevice("ODU<->IDU", "IDU_01.MultiV_Tx", "IDU_01.MultiV_Rx");
	ConnectMemoryTxRxDevice("ODU<->IDU", "IDU_02.MultiV_Tx", "IDU_02.MultiV_Rx");
	ConnectMemoryTxRxDevice("ODU<->IDU", "IDU_03.MultiV_Tx", "IDU_03.MultiV_Rx");
	ConnectMemoryTxRxDevice("ODU<->IDU", "IDU_04.MultiV_Tx", "IDU_04.MultiV_Rx");
	ConnectMemoryTxRxDevice("ODU<->IDU", "IDU_05.MultiV_Tx", "IDU_05.MultiV_Rx");

	// Establish connection : ODU <-> LGMV
	//CreateSerialCommDevice("Multi_V_S.LGMV_COM", "COM3", false, 2400);
	//ConnectSerialCommDevice("Multi_V_S.LGMV_COM", "Multi_V_S.LGMV_Tx", "Multi_V_S.LGMV_Rx");
}
