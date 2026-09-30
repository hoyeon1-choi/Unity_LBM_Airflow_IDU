# UX-05 COMPARE

Reference Image:\
UX05_Compare_Reference.png

Unity: 6000.1.4f1\
UI: UI Toolkit

목적:

서로 다른 Simulation Case의

- 3D 결과
- Room Temperature
- Outlet Temperature
- Velocity
- Mass Error
- Power
- COP

등을 동일 조건에서 비교한다.

---

# 중요 원칙

Case 저장 구조는 임의로 결정하지 않는다.

특히 다음 선택은 사용자 승인 없이 하지 않는다.

- JSON
- Binary
- ScriptableObject
- HDF5
- SQLite
- 외부 Database
- GPU Field 전체 저장
- Downsampling
- Compression
- Snapshot 해상도
- Case 결과 자동 삭제

먼저 현재 Solver 데이터 크기와 Case 구조를 조사한다.

\==================================================

# F01 LAYOUT

## T01 기존 Case/Result 구조 조사

코드 변경 금지.

## T02 Compare UXML Skeleton

영역:

CaseListPanel\
CasePreviewPanel\
TrendComparisonPanel\
KPIComparisonPanel\
DifferenceViewPanel

## T03 USS

Reference Image의 Dark Engineering Dashboard 스타일을 따른다.

## T04 Responsive 검증

\==================================================

# F02 CASE MANAGEMENT

## T01 Case 개념 조사

현재 프로젝트에서 Case를 식별하는 방법을 확인한다.

## T02 Case List UI

표시:

Case Name\
Date\
Description\
Status\
Thumbnail

## T03 Case Selection

복수 선택 지원 구조.

## T04 New Case

UI만 우선 구현.

실제 저장 동작은 Storage 결정 이후 구현.

## T05 Duplicate

데이터 복사 범위가 명확하지 않으면 사용자 확인.

## T06 Delete

삭제는 Confirmation 없이 수행하지 않는다.

\==================================================

# F03 CASE METADATA

T01 Metadata 후보 조사\
T02 Metadata UI\
T03 Solver Setting Summary\
T04 Boundary Summary\
T05 Model/Grid Summary

\==================================================

# F04 SNAPSHOT

T01 기존 Camera Capture 기능 조사\
T02 Case Thumbnail UI\
T03 Result View Snapshot\
T04 Snapshot 생성 Interface

Resolution / Format / 저장 위치 미정이면 확인 요청.

\==================================================

# F05 TREND COMPARISON

## T01 Room Average Temperature

복수 Case curve 표시.

## T02 Outlet Temperature

## T03 Velocity

## T04 Case Legend

## T05 Cursor / Tooltip

## T06 Time Axis Alignment

Case마다 Simulation Time/Time Step이 다른 경우 비교 기준을 임의 결정하지 않는다.

사용자에게 다음 선택을 요청한다.

- 실제 시간 기준
- Step 기준
- 정규화 시간 기준

\==================================================

# F06 KPI COMPARISON

표:

Case\
Room Avg\
ΔT\
Max Velocity\
Mass Error\
Power\
COP

## T01 KPI 데이터 출처 확인

## T02 Table UI

## T03 Sorting

## T04 Selected Case Highlight

## T05 Missing Value 처리

없는 데이터를 0으로 표시하지 않는다.

N/A 또는 사용자 지정 정책을 사용한다.

\==================================================

# F07 DIFFERENCE VIEW

## T01 요구 Data 검증

Case A와 Case B가 동일:

- Geometry
- Grid
- Coordinate system

인지 확인한다.

## T02 Difference Mode UI

예:

Case B - Case A

## T03 Difference Legend

± 범위 지원.

## T04 3D Difference Visualization

기존 Visualization Pipeline을 최대한 재사용한다.

## T05 성능 검토

전체 3D field subtraction이 GPU/CPU 어디에서 수행될지는 임의 결정하지 않는다.

현재 구조를 분석한 후 사용자에게 선택지를 제시한다.

\==================================================

# F08 CASE DATA MODEL

## T01 현재 결과 데이터 크기 분석

확인:

Grid Size\
Cell Count\
Variable Count\
Bytes / Cell\
Total Field Size\
Time History Size

가능하면 실제 코드/Buffer 정의를 기반으로 계산한다.

## T02 저장 옵션 비교

다음 후보를 필요에 따라 비교한다.

Memory\
Binary\
JSON\
ScriptableObject\
HDF5\
SQLite

비교 기준:

Expected Size\
Save Speed\
Load Speed\
Random Access\
3D Field 적합성\
버전 관리\
구현 복잡도

추천은 가능하지만 구현하지 않는다.

사용자 결정을 기다린다.

## T03 CaseData Interface

저장 방식 확정 후 구현한다.

## T04 Metadata / Field 분리

대용량 Field Data와 작은 Metadata를 분리할 필요가 있는지 검토한다.

## T05 Lazy Loading

필요성이 확인된 경우에만 설계한다.

## T06 Cache Strategy

메모리 사용량에 영향을 주므로 사용자 확인 필요.

\==================================================

# F09 DIFFERENCE DATA PROVIDER

T01 Case A 데이터 읽기\
T02 Case B 데이터 읽기\
T03 Coordinate/Grid Compatibility 확인\
T04 Difference 계산 Interface\
T05 Visualization Provider 연결

\==================================================

# F10 UX-05 INTEGRATION

## T01 전체 화면 연결

Case List\
→ Preview\
→ Trend\
→ KPI\
→ Difference View

## T02 State Management

No Case\
1 Case\
2+ Cases\
Loading\
Error

## T03 Error Handling

잘못된 Case 데이터가 전체 Dashboard를 중단시키지 않도록 한다.

## T04 Performance 확인

확인:

- 불필요한 Field 복사
- GC Allocation
- 매 Frame 전체 데이터 갱신
- GPU Readback
- Texture 재생성

## T05 최종 검증

UX-05 내부 기능만 검증한다.

UX-01\~04 구조 변경이 필요한 경우 직접 변경하지 말고 영향도를 보고한다.

\==================================================

# 모든 T 작업 완료 시 보고 형식

[완료 작업]\
UX05 / Fxx / Txx

[변경 파일]

CREATE:\
...

MODIFY:\
...

DELETE:\
없음

[구현 내용]\
...

[검증]

- Compile:
- Runtime:
- UI:
- 기존 기능 영향:

[사용자 확인 필요]\
...

[다음 작업]\
UX05 / Fxx / Txx

다음 작업은 자동으로 수행하지 않는다.
