# Unity LBM/FMU Dashboard 개발

Unity Version: 6000.1.4f1\
UI Framework: UI Toolkit\
Language: C#

첨부된 Reference Image를 해당 화면의 시각적 기준으로 사용한다.

중요:\
Reference Image는 목표 UX/레이아웃을 정의하기 위한 자료이며 픽셀 단위 복사가 목적은 아니다.\
기존 프로젝트 구조를 우선하며 UXML / USS / C#을 재사용 가능한 형태로 구현한다.

---

# 공통 실행 규칙

## 1. 한 번에 하나의 작업(T)만 수행

내가 다음과 같이 지시한다.

UX01 / F03 / T02

그러면 T02만 수행한다.

동일 F의 다른 T 작업이나 다음 F 작업을 임의로 진행하지 않는다.

---

## 2. 기존 프로젝트 조사 우선

코드를 생성하거나 수정하기 전에 관련 기존 구현을 확인한다.

확인 대상:

- Assets 폴더 구조
- UIDocument
- UXML
- USS
- PanelSettings
- 기존 UI Controller
- 기존 LBM Solver
- Simulation Manager
- ComputeShader 관련 코드
- FMU 관련 코드
- 데이터 모델
- 기존 Event / Interface

이미 존재하는 기능은 최대한 재사용한다.

---

## 3. 임의 의사결정 금지

다음 중 하나라도 발생하면 구현을 진행하지 말고 나에게 확인한다.

- 기존 Architecture 변경
- 기존 API 변경
- 기존 Script 삭제
- LBM Solver 구조 변경
- FMU Interface 변경
- ComputeShader 변경
- GPU Buffer 구조 변경
- GPU → CPU Readback 추가
- Package 설치
- 데이터 저장 형식 결정
- 새로운 데이터 모델의 핵심 구조 결정
- Sampling 주기 결정
- UI Update 주기 결정
- Thread / Async 처리 방식 결정
- 화면 해상도 기준 결정
- Reference Image에서 동작을 추측해야 하는 경우
- 여러 방식 중 향후 전체 Architecture에 영향을 미치는 선택

질문 형식:

[의사결정 필요]

현재 상황:

선택지 A:\
선택지 B:\
선택지 C:

각 선택지 장단점:

권장안:

결정이 필요한 이유:

사용자 답변을 받은 뒤 계속한다.

---

## 4. 작업 시작 시

먼저 다음만 간단히 출력한다.

[현재 작업]\
UX:\
Feature:\
Task:

[관련 기존 구조]\
...

[변경 예정]\
CREATE:\
MODIFY:\
DELETE:

DELETE는 명시적 승인이 없으면 하지 않는다.

---

# UX-01 MAIN DASHBOARD

Reference Image:\
UX01_Main_Dashboard_Reference.png

목적:\
현재 LBM/FMU Simulation의 전체 상태를 한 화면에서 파악한다.

화면 구성:

- Top Bar
- Navigation
- KPI Cards
- Main 3D Scene
- Result Legend
- Simulation Status
- Temperature Trend
- GPU / Simulation Performance
- Key Indicators

---

# F01 MAIN LAYOUT

## T01 기존 UI 구조 조사

다음을 조사한다.

- UIDocument 위치
- UXML 구조
- USS 구조
- PanelSettings
- 화면 관리 방식
- 기존 Navigation
- 기존 Dashboard 관련 Script

코드 변경하지 않는다.

결과만 보고한다.

---

## T02 Main Dashboard UXML Skeleton

다음 VisualElement 영역을 만든다.

MainDashboard

- TopBar
- NavigationPanel
- MainContent
- KPIContainer
- SceneContainer
- ResultLegendContainer
- SimulationStatusPanel
- BottomAnalyticsPanel

실제 데이터는 연결하지 않는다.

---

## T03 기본 USS Layout

Reference Image를 참고해 Dark Industrial Theme을 구성한다.

원칙:

- 고정 pixel 남용 금지
- flex 기반
- 공통 spacing 변수 활용
- 반복 색상/크기 class화
- 향후 해상도 변경 가능하도록 구성

색상/폰트 정책이 기존 프로젝트에 있다면 기존 것을 사용한다.

새 Design Token 체계가 필요하면 먼저 확인한다.

---

## T04 Layout 검증

확인:

- Panel overlap
- 잘림
- resize 대응
- hierarchy
- USS 중복
- Runtime UIDocument 표시

문제가 있으면 F01 범위 내에서 수정한다.

---

# F02 NAVIGATION

## T01 Navigation 구조 생성

메뉴:

Home\
Simulation\
Results\
FMU Monitor\
Compare\
Report\
Settings

현재는 UI만 구현한다.

---

## T02 Navigation Button 상태

상태:

Normal\
Hover\
Selected\
Disabled

현재 페이지 표시 기능을 구현한다.

---

## T03 Screen Switching 구조 조사

기존 화면 전환 Manager가 있는지 조사한다.

있으면 재사용한다.

없다면 구현 방법을 제안하고,\
Architecture 결정이 필요하면 사용자에게 확인한다.

---

## T04 Main Dashboard 연결

Home 버튼으로 UX-01을 표시한다.

다른 화면은 Placeholder 상태를 허용한다.

---

# F03 KPI CARDS

KPI:

Room Avg\
ΔT\
Max Velocity\
Mass Error\
GPU Usage

## T01 KPI Card 공통 Component

재사용 가능한 KPI Card 구조를 만든다.

구성:

Icon\
Label\
Value\
Unit\
Optional Status

---

## T02 Mock Data 표시

실제 Solver와 연결하지 않고 Mock Data를 표시한다.

---

## T03 KPI ViewModel Interface

UI가 Solver를 직접 참조하지 않도록 데이터 Interface를 설계한다.

기존 데이터 구조와 충돌하면 구현 전에 확인한다.

---

## T04 실제 데이터 연결

Room Avg\
ΔT\
Max Velocity\
Mass Error\
GPU Usage

각 데이터가 현재 프로젝트 어디에서 생성되는지 먼저 조사한다.

존재하지 않는 값은 계산식을 임의로 만들지 않는다.

---

# F04 3D SCENE

## T01 기존 Camera/Scene 구조 조사

다음을 조사한다.

- Main Camera
- Simulation Camera
- RenderTexture 사용 여부
- Camera Controller
- Room Model
- Result visualization

변경하지 않는다.

---

## T02 Dashboard Scene View 구성

기존 Camera를 사용할지 별도 Camera를 사용할지 판단이 필요하면 먼저 질문한다.

---

## T03 Result Legend Placeholder

Temperature Legend UI를 배치한다.

Solver 결과 연결은 하지 않는다.

---

## T04 Scene Toolbar

기능 Placeholder:

Fit\
Reset View\
Temperature\
Real-time

동작을 추측해야 한다면 구현하지 않는다.

---

# F05 SIMULATION STATUS

## T01 Status Panel UI

표시 항목:

Status\
Time\
Time Step\
Grid\
FPS\
GPU\
Memory

---

## T02 Mock State Binding

Idle / Running / Paused / Completed / Error 상태를 표현한다.

---

## T03 기존 Simulation 상태 연결

실제 Simulation Manager의 상태 구조를 조사한다.

기존 API를 우선한다.

---

# F06 TREND CHART

## T01 Chart 구현 방식 조사

기존 Chart Library 또는 자체 그래프 구현 여부를 확인한다.

외부 Package 설치는 금지한다.

---

## T02 Temperature Trend Placeholder

Room Avg\
Inlet\
Outlet\
Target

---

## T03 GPU Performance Trend

GPU Usage\
FPS

---

## T04 실제 데이터 연결

Sampling / buffer length / update interval이 정해져 있지 않으면 먼저 사용자에게 확인한다.

---

# F07 DATA INTEGRATION

## T01 SimulationDataModel 현황 조사

기존 데이터 전달 경로를 조사한다.

---

## T02 Dashboard ViewModel 설계

권장 방향:

Solver\
→ SimulationDataModel\
→ Dashboard ViewModel\
→ UI Toolkit

기존 Architecture와 충돌할 경우 먼저 확인한다.

---

## T03 Event Binding

Polling보다 기존 Event 구조가 있다면 우선 사용한다.

새 Update 전략 결정이 필요하면 사용자 확인.

---

## T04 UX-01 Integration Test

확인:

- KPI
- Status
- Trend
- Scene
- Navigation

UX-01 밖의 기능은 수정하지 않는다.

\==================================================

# UX-02 SIMULATION

Reference Image:\
UX02_Simulation_Reference.png

목적:\
LBM Simulation 조건 설정과 실행 상태를 하나의 Workflow로 제공한다.

주요 영역:

Case\
Model\
Mesh\
Physics\
Boundary\
Run\
Progress\
GPU/System Monitor\
Console

---

# F01 SIMULATION LAYOUT

T01 기존 Simulation UI 조사\
T02 UXML Skeleton\
T03 Tab/Step Navigation\
T04 USS Layout\
T05 Layout 검증

Wizard 단계:

1 Case\
2 Model\
3 Mesh\
4 Physics\
5 Boundary\
6 Run

---

# F02 CASE SELECTION

T01 기존 Case 개념 조사\
T02 Case List UI\
T03 Selected Case 표시\
T04 New/Duplicate/Delete UI\
T05 Case Metadata Panel

Case 저장 방식이 명확하지 않으면 저장 기능은 구현하지 않고 질문한다.

---

# F03 MODEL

T01 기존 Geometry/Model Loader 조사\
T02 현재 모델 정보 표시\
T03 Model Selection UI\
T04 Geometry Metadata 표시\
T05 기존 Model Loader 연결

새 파일 형식을 임의로 지원하지 않는다.

---

# F04 MESH

T01 LBM Grid 구조 조사\
T02 Grid Size UI\
T03 Cell Size UI\
T04 Grid 예상 크기 표시\
T05 Memory Estimate 표시

Memory 계산식이 기존 Solver 구조에 따라 달라지면 먼저 실제 Buffer 구조를 조사한다.

---

# F05 PHYSICS

T01 기존 Solver Parameter 조사\
T02 LBM Model Selector\
T03 Turbulence 설정\
T04 Thermal 설정\
T05 Buoyancy 설정\
T06 Gravity 표시

BGK/MRT 등 현재 Solver에 실제 지원되는 항목만 활성화한다.

---

# F06 BOUNDARY

T01 기존 Boundary 구조 조사\
T02 Inlet UI\
T03 Outlet UI\
T04 Wall UI\
T05 Boundary Parameter Binding\
T06 Boundary Validation

Solver Boundary 정의를 UI 편의를 위해 임의 변경하지 않는다.

---

# F07 RUN CONTROL

## T01 Start

기존 Simulation 시작 API를 찾는다.

API가 여러 개거나 불명확하면 호출하지 말고 사용자에게 확인한다.

## T02 Pause / Resume

기존 기능 존재 여부 확인 후 연결.

## T03 Stop

Simulation 종료와 Reset의 의미가 다를 경우 사용자 확인.

## T04 State-based Button

Idle\
Running\
Paused\
Completed\
Error

상태에 따라 버튼 활성도를 관리한다.

---

# F08 PROGRESS

T01 Simulation Time\
T02 Progress Bar\
T03 Time Step\
T04 Estimated Remaining Time

Remaining Time 계산 방법이 정의되어 있지 않으면 임의 구현하지 않는다.

---

# F09 GPU / SYSTEM MONITOR

T01 GPU 정보 취득 방식 조사\
T02 GPU Usage\
T03 GPU Memory\
T04 System Memory\
T05 FPS

추가 Native Plugin이 필요하면 설치하지 말고 먼저 확인한다.

---

# F10 SOLVER INTEGRATION

T01 Solver API Inventory\
T02 UI → Solver Command Adapter\
T03 Solver → UI State Adapter\
T04 Exception Handling\
T05 UX-02 Integration Test

UI가 Solver Component를 직접 광범위하게 참조하지 않도록 한다.
