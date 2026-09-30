# 공통 조건

Unity 6000.1.4f1 / UI Toolkit / C#

한 번에 내가 지정한 T 작업 하나만 수행한다.

첨부 Reference Image는 화면 구조와 UX의 기준으로 사용한다.

기존 Solver / FMU / ComputeShader Architecture 변경이 필요한 경우 임의 변경하지 않는다.

특히 아래 사항은 반드시 사용자 확인을 받는다.

- ComputeBuffer 변경
- RenderTexture 변경
- GPU → CPU Readback
- 데이터 복제
- Sampling rate
- Visualization Algorithm 선택
- 새로운 Package
- FMU Wrapper 변경

\==================================================

# UX-03 RESULTS

Reference Image:\
UX03_Results_Reference.png

목적:\
LBM 결과를 공간적으로 탐색하고 정량적으로 확인한다.

Result Variable:

Temperature\
Velocity\
Pressure\
Streamline\
Comfort\
Mass Flux

---

# F01 LAYOUT

T01 기존 Result UI 조사\
T02 Results UXML Skeleton\
T03 Scene 영역 구성\
T04 Toolbar 구성\
T05 Analytics 영역 구성\
T06 USS 적용\
T07 Layout 검증

---

# F02 RESULT VARIABLE

T01 실제 Solver Output Inventory

각 변수의 실제 존재 여부와 데이터 위치를 확인한다.

T02 Variable Selector UI

Temperature\
Velocity\
Pressure\
Streamline\
Comfort\
Mass Flux

지원되지 않는 변수는 구현하지 않는다.

T03 Selected Variable State

T04 Variable Metadata

Unit\
Min\
Max\
Data Range

T05 Visualization 호출 Interface

---

# F03 COLOR MAP

## T01 기존 Visualization 조사

확인:

ComputeShader\
Material\
Shader\
Texture3D\
RenderTexture\
ComputeBuffer\
CPU Array

## T02 Data Path 확인

GPU 결과를 CPU로 복사해야 하는 구조라면 구현 전에 반드시 사용자 확인.

## T03 Color Map UI

Preset selector와 Legend를 구현한다.

## T04 Min/Max Range

Auto / Manual Range UI.

실제 범위 계산 방식은 별도 작업으로 유지.

## T05 Visualization 연결

기존 결과 rendering path를 우선 재사용한다.

---

# F04 SLICE

T01 기존 Slice 기능 조사\
T02 X Slice UI\
T03 Y Slice UI\
T04 Z Slice UI\
T05 Slice Position Slider\
T06 Scene Overlay 연결

새 Shader 구현이 필요하면 먼저 영향도를 보고한다.

---

# F05 ISO-SURFACE

T01 기존 Iso-Surface 기능 조사\
T02 Enable Toggle\
T03 Threshold UI\
T04 Rendering Interface\
T05 성능 영향 확인

Marching Cubes 등 알고리즘을 임의 선택하지 않는다.

---

# F06 STREAMLINE

T01 기존 Streamline Renderer 조사\
T02 Enable Toggle\
T03 Seed 관련 UI\
T04 기존 Visualization 연결

새 알고리즘 구현은 별도 승인 없이 하지 않는다.

---

# F07 VALUE PROBE

T01 Scene Picking 구조 조사\
T02 Probe Point 선택\
T03 World → Grid Coordinate 변환 조사\
T04 Temperature 표시\
T05 Velocity 표시\
T06 Pressure 표시\
T07 Cell Index 표시

좌표계 정의가 불분명하면 반드시 사용자 확인.

---

# F08 MIN/MAX

T01 기존 Reduction 계산 조사\
T02 Min 표시\
T03 Max 표시\
T04 Average 표시\
T05 위치 정보 표시

GPU Reduction 추가 구현이 필요하면 먼저 제안만 한다.

---

# F09 RESULT TREND

T01 Probe Trend UI\
T02 Temperature Trend\
T03 Velocity Trend\
T04 Pressure Trend\
T05 Buffer 연결

Sampling 정책을 임의 결정하지 않는다.

---

# F10 RESULT DATA INTERFACE

T01 실제 Result Data Structure 문서화\
T02 GPU/CPU 소유권 확인\
T03 ResultDataProvider Interface\
T04 UI Adapter\
T05 Variable Switching 최적화\
T06 UX-03 Integration Test

핵심 원칙:

Visualization UI 때문에 LBM Solver의 Simulation Step 성능을 떨어뜨리지 않는다.

\==================================================

# UX-04 FMU / SYSTEM MONITOR

Reference Image:\
UX04_FMU_System_Monitor_Reference.png

목적:\
Heat Pump/IDU FMU와 Unity Simulation의 연결 상태와 주요 물리 변수를 실시간 확인한다.

---

# F01 LAYOUT

T01 기존 FMU UI 조사\
T02 Monitor UXML Skeleton\
T03 System Diagram 영역\
T04 FMU Status 영역\
T05 Variable 영역\
T06 Trend 영역\
T07 USS / Layout 검증

---

# F02 FMU CONNECTION

T01 기존 FMU Wrapper 조사

확인:

Load\
Initialize\
Set\
Get\
DoStep\
Terminate

T02 Connection Status UI

Disconnected\
Initializing\
Running\
Paused\
Error

T03 Simulation Time

T04 FMU Step

T05 Solver Sync 상태

---

# F03 SYSTEM DIAGRAM

T01 Reference Image 기준 Diagram Skeleton\
T02 IDU 표시\
T03 Outdoor Unit 표시\
T04 Compressor 표시\
T05 Refrigerant / Airflow Connection 표현\
T06 상태 변화 표시

실제 냉매 사이클 계산을 UI에서 수행하지 않는다.

---

# F04 FMU VARIABLES

## T01 FMU Variable Inventory

modelDescription.xml 또는 기존 Wrapper에서 실제 변수를 조사한다.

## T02 Input Variable Table

Name\
Value\
Unit\
Description

## T03 Output Variable Table

Name\
Value\
Unit\
Description

## T04 Variable Group

Temperature\
Flow\
Fan\
Compressor\
Power\
State

## T05 Data Binding

UI가 FMU Instance를 직접 제어하지 않도록 한다.

---

# F05 TEMPERATURE TREND

T01 T_in Trend\
T02 T_out Trend\
T03 Buffer 구조\
T04 Real-time Update\
T05 Unit 표시

Sampling 주기 미정이면 사용자 확인.

---

# F06 COMPRESSOR / FAN

T01 Compressor Frequency UI\
T02 Fan RPM UI\
T03 Trend Chart\
T04 Current Value 표시\
T05 Alarm/Range 표시

Alarm 기준이 없다면 임의 설정하지 않는다.

---

# F07 POWER / COP

T01 Power 변수 조사\
T02 Power 표시\
T03 COP 변수 존재 여부 조사\
T04 COP 표시\
T05 Trend 확장 가능 구조

COP가 FMU 출력값이 아니면 계산식을 임의 추가하지 않는다.

---

# F08 FMU CONTROL

T01 현재 Writable Variable 조사\
T02 Control UI Skeleton\
T03 Setpoint Input\
T04 Validation\
T05 FMU Set 호출 Adapter

FMU Input 변경이 실제 Solver Coupling에 영향을 미치므로 실제 Set 호출 전에 데이터 흐름을 확인한다.

---

# F09 FMU DATA ADAPTER

T01 현재 FMU API Map 작성

T02 Adapter 설계

권장 흐름:

FMU Wrapper\
→ FMUDataAdapter\
→ SimulationDataModel\
→ FMUMonitor ViewModel\
→ UI

T03 Read Data Binding\
T04 Write Command Binding\
T05 Error Handling\
T06 UX-04 Integration Test

기존 FMU Wrapper API를 수정해야 한다면 변경하지 말고 영향범위를 먼저 보고한다.
