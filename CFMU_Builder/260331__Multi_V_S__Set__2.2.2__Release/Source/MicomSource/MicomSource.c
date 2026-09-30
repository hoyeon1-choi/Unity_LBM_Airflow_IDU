#include "MicomSource.h"
#include "CFMUAdaptor.h"

#ifdef __CFMU__ // 메인함수가 위치한 c 파일에 1회 정의
#define main CFMU_Main
#endif

/******************** 모니터링 테스트 ********************/
UINT8 monitoringBuffer[3] = { 0,0,0 };

void MonitoringUpCount(void)
{
	monitoringBuffer[0] += 1;
	monitoringBuffer[1] += 2;
	monitoringBuffer[2] += 3;
}

/******************** 타이머 인터럽트 테스트 ********************/
UINT32 irq1Count_uint32 = 0;
void IRQ1_1sec(void)
{
	irq1Count_uint32++;
	MonitoringUpCount();
}

UINT32 irq2Count_uint32 = 0;
void IRQ2_100ms(void)
{
	irq2Count_uint32++;
}

/******************** 통신 테스트 ********************/
UINT8 Master_TxBuffer[BUFFER_LENGTH];
UINT8 Master_OneByteTxBufferRegister = 0;
UINT8 Master_CFMU_TxFlushTrigger = 0;
UINT8 Master_TxIng = 0;
UINT8 Master_TxBufferCursor = 0;
UINT8 Slave_RxBuffer[BUFFER_LENGTH];
UINT8 Slave_OneByteRxBufferRegister = 0;
UINT8 Slave_RxIng = 0;
UINT8 Slave_RxBufferCursor = 0;

void Master_SendTxBuffer(void) {
	Master_TxBufferCursor = 0;
	Master_OneByteTxBufferRegister = Master_TxBuffer[Master_TxBufferCursor++];
	Master_CFMU_TxFlushTrigger = 1;
	Master_TxIng = 1;
}

void Master_Tx_Interrupt_Handler(void) {
	if (Master_TxIng == 0) return;
	if (Master_TxBufferCursor < BUFFER_LENGTH) {
		if (Master_CFMU_TxFlushTrigger == 0) {
			Master_OneByteTxBufferRegister = Master_TxBuffer[Master_TxBufferCursor++];
			Master_CFMU_TxFlushTrigger = 1;
		}
		else {
			Master_TxBufferCursor = 0;
			Master_TxIng = 0;
		}
	}
	else {
		Master_TxBufferCursor = 0;
		Master_TxIng = 0;
	}
}

void Slave_Rx_Interrupt_Handler(void) {
	Slave_RxIng = 1;
	if (Slave_RxBufferCursor < BUFFER_LENGTH) {
		Slave_RxBuffer[Slave_RxBufferCursor++] = Slave_OneByteRxBufferRegister;
	}
	if (Slave_RxBufferCursor >= BUFFER_LENGTH) {
		Slave_RxBufferCursor = 0;
		Slave_RxIng = 0;
	}
}

/******************** EEPROM 테스트 ********************/
UINT8 aucEEPROMdata[0x0400];

/******************** 변수 참조형 Input, Output 테스트 + Main loop 쓰레드 테스트 ********************/
UINT8 a_uint8 = 0;
UINT16 b_uint16 = 0;
INT32 sum_a_b_int32 = 0;
UINT32 mainCount_uint32 = 0;

void main(void)
{
	// Initialization functions here
	int i = 0;
	for (i = 0; i < BUFFER_LENGTH; ++i) Master_TxBuffer[i] = i + 1;
	for (i = 0; i < BUFFER_LENGTH; ++i) Slave_RxBuffer[i] = 0;
	UINT8 txStart = 0;

	while (1)
	{
		sum_a_b_int32 = (INT32)a_uint8 + (INT32)b_uint16;
		mainCount_uint32++;
		if (irq1Count_uint32 == 5 && txStart == 0) {
			Master_SendTxBuffer();
			txStart = 1;
		}
	}
}
