당신은 Unity 6000.1.4f1 프로젝트에서 작업하는 코드 개발자입니다.
개발 환경은 Windows 11 64bit, Visual Studio 2022입니다.

목표:
Unity 기반 LBM 기류해석 솔버에서 실시간성 확보를 위해 dxPhys = 0.04 m를 유지한 상태로 Case Study를 실행할 수 있도록 코드를 수정/보완してください.
이번 Case Study의 목적은 고속 제트 코어 속도가 너무 빠르게 확산되어 감소하는 문제를 줄이면서, 안정성 및 질량보존성을 유지할 수 있는 설정을 찾는 것입니다.

배경:
현재 솔버는 MRT Collision, Mass-Flux Corrected Outlet, 선택적 Smagorinsky 난류모델을 사용합니다.
이전 결과에서 취출 제트의 고속 코어가 Fluent 대비 너무 빠르게 감쇠했습니다.
주요 원인은 tau_f가 tauMin = 0.56으로 클램프되면서 유효 물리 점성계수 nu_phys가 과도하게 커지는 것으로 추정됩니다.
dx = 0.04 m 조건에서 현재 nu_phys는 약 1.1E-2 m2/s 수준이며, 실제 공기 동점성계수보다 매우 큽니다.
따라서 이번 작업은 dx = 0.04 m를 고정하고, tauFluidMin / tauThermalMin / Smagorinsky 설정을 비교할 수 있도록 만드는 것이 핵심입니다.

중요 제약조건:
- dxPhys = 0.04 m를 유지하십시오.
- Domain, Inlet, Outlet 형상은 기본적으로 변경하지 마십시오.
- Mass-Flux Corrected Outlet은 유지하십시오.
- Collision Model은 MRT를 유지하십시오.
- 대규모 리팩토링은 하지 마십시오.
- 무거운 검증 루틴은 추가하지 마십시오.
- 기존 동작을 깨지 않도록 최소 수정으로 구현하십시오.
- Case Study는 Unity Inspector 또는 간단한 런타임 Preset/Case Runner에서 실행 가능해야 합니다.
- 모든 로그에는 Case 이름 또는 Experiment Tag가 포함되어야 합니다.

수행할 작업:

1. 유동 tau 하한과 열 tau 하한을 분리하십시오.

현재 코드는 tauMin 하나로 tau_f와 tau_T를 함께 클램프하는 구조로 보입니다.
이를 아래처럼 분리하십시오.

- tauFluidMin
- tauThermalMin
- tauFluidMax
- tauThermalMax

요구사항:
- tau_f_raw는 tauFluidMin / tauFluidMax로 클램프하십시오.
- tau_T_raw는 tauThermalMin / tauThermalMax로 클램프하십시오.
- 새 필드는 Unity Inspector에 노출하십시오.
- 기존 기본 동작은 유지하십시오.
  - 기본값은 tauFluidMin = 0.56
  - 기본값은 tauThermalMin = 0.56
- 기존 tauMin 필드가 남아 있다면 backward compatibility를 위해 기본값 또는 migration 용도로만 사용하십시오.
- 가능하면 기존 tauMin 값을 새 tauFluidMin / tauThermalMin의 기본값으로 안전하게 반영하십시오.
- Inspector Range는 아래를 권장합니다.
  - tauFluidMin: 0.5001 ~ 1.0
  - tauThermalMin: 0.5001 ~ 1.0
  - tauFluidMax: 1.0 ~ 4.0
  - tauThermalMax: 1.0 ~ 4.0
- tau가 0.5에 가까워질수록 불안정할 수 있으므로 경고는 출력하되, 임의로 0.53 이상으로 다시 강제 클램프하지는 마십시오.

2. Scaling 진단 정보를 추가하십시오.

Solver Summary, Result Metrics, CSV Logger 중 가능한 위치에 아래 정보를 추가하십시오.

필수 진단 항목:
- tau_f_raw
- tau_T_raw
- tau_f
- tau_T
- tauFluidMin
- tauThermalMin
- tau_f_was_clamped
- tau_T_was_clamped
- nuPhysTarget
- alphaPhysTarget
- nuPhysEffective
- alphaPhysEffective
- nuPhysEffective / nuPhysTarget
- alphaPhysEffective / alphaPhysTarget
- maxMach 설정값
- 실제 sampled max Mach가 이미 있다면 해당 값
- Re
- Pr

tau_f 또는 tau_T가 클램프된 경우 경고를 출력하십시오.

경고 메시지 예시:
[LBM Scaling] tau_f raw=0.500xxx was clamped to 0.560000. nuPhys target=1.5E-5, effective=1.108E-2, ratio=739x. Jet momentum diffusion may be excessive.

한글 메시지를 사용해도 됩니다. 예:
[LBM Scaling] tau_f raw=0.500xxx가 0.560000으로 클램프되었습니다. nuPhys target=1.5E-5, effective=1.108E-2, ratio=739x. 제트 운동량 확산이 과도할 수 있습니다.

3. Case Study Runner 또는 Preset 목록을 추가하십시오.

아래 Case들을 쉽게 적용하고 실행할 수 있는 가벼운 구조를 추가하십시오.
구현 방식은 기존 구조에 가장 덜 침습적인 방법을 선택하십시오.

허용되는 방식:
- MonoBehaviour 컴포넌트
- Editor Utility
- ScriptableObject Preset List
- SimulationController 내부의 간단한 ApplyCase 메서드
- Inspector 버튼 방식

과도한 자동화는 필요 없습니다.
모든 Case를 자동으로 연속 실행하는 것이 위험하다면 아래 기능만 제공해도 됩니다.

- 선택한 Case 적용
- 선택한 Case 실행
- 다음 Case 적용
- Baseline으로 복귀

4. 최소 필수 Case 목록을 구현하십시오.

A0:
- name: A0_Baseline
- dxPhys = 0.04
- tauFluidMin = 0.560
- tauThermalMin = 0.560
- turbulence = Smagorinsky
- smagorinskyConstant = 0.03
- purpose: 현재 기준 Case

A1:
- name: A1_FluidTau_0530_Thermal_0560_Off
- dxPhys = 0.04
- tauFluidMin = 0.530
- tauThermalMin = 0.560
- turbulence = Off
- purpose: 열확산은 유지하고 운동량 확산만 줄이는 효과 확인

A2:
- name: A2_FluidTau_0510_Thermal_0560_Off
- dxPhys = 0.04
- tauFluidMin = 0.510
- tauThermalMin = 0.560
- turbulence = Off
- purpose: 운동량 확산을 강하게 줄였을 때 속도 코어 회복 여부 확인

A3:
- name: A3_FluidTau_0510_Thermal_0530_Off
- dxPhys = 0.04
- tauFluidMin = 0.510
- tauThermalMin = 0.530
- turbulence = Off
- purpose: 운동량 확산과 열확산을 동시에 줄였을 때 결과 확인

A4:
- name: A4_FluidTau_0510_Thermal_0530_Smag003
- dxPhys = 0.04
- tauFluidMin = 0.510
- tauThermalMin = 0.530
- turbulence = Smagorinsky
- smagorinskyConstant = 0.03
- purpose: 기본 확산을 줄인 뒤 Smagorinsky 영향 재확인

5. 선택적 2차 Case 목록도 가능하면 추가하십시오.

B1:
- name: B1_FluidTau_0515_Thermal_0535_Off
- dxPhys = 0.04
- tauFluidMin = 0.515
- tauThermalMin = 0.535
- turbulence = Off

B2:
- name: B2_FluidTau_0510_Thermal_0530_Off
- dxPhys = 0.04
- tauFluidMin = 0.510
- tauThermalMin = 0.530
- turbulence = Off

B3:
- name: B3_FluidTau_0505_Thermal_0525_Off
- dxPhys = 0.04
- tauFluidMin = 0.505
- tauThermalMin = 0.525
- turbulence = Off

B4:
- name: B4_FluidTau_0510_Thermal_0530_Smag003
- dxPhys = 0.04
- tauFluidMin = 0.510
- tauThermalMin = 0.530
- turbulence = Smagorinsky
- smagorinskyConstant = 0.03

B5:
- name: B5_FluidTau_0510_Thermal_0530_Smag006
- dxPhys = 0.04
- tauFluidMin = 0.510
- tauThermalMin = 0.530
- turbulence = Smagorinsky
- smagorinskyConstant = 0.06

B6:
- name: B6_FluidTau_0510_Thermal_0530_Smag010
- dxPhys = 0.04
- tauFluidMin = 0.510
- tauThermalMin = 0.530
- turbulence = Smagorinsky
- smagorinskyConstant = 0.10

6. Case 실행 동작

각 Case는 아래 순서로 실행되도록 구성하십시오.

- Case 설정 적용
- dxPhys = 0.04 적용 확인
- Scaling 재계산
- 필요 시 Simulation 재초기화
- 짧은 목표 시간까지 우선 실행
  - 권장: 물리시간 30초
- targetSimulationTime은 Inspector에서 변경 가능하게 유지
- 현재 Logger가 지원한다면 1초 또는 2초 간격으로 metrics 저장
- 결과 파일명 또는 experiment_tag에 Case 이름 포함
- 모든 Case를 자동 연속 실행할 필요는 없습니다.
- 안전을 위해 수동으로 A0 → A1 → A2 → A3 → A4 순서로 실행할 수 있어도 충분합니다.

7. 필수 비교 Metrics

기존 Metrics를 최대한 활용하되, 로그에는 최소한 아래 항목이 포함되도록 하십시오.

기본 정보:
- case name
- experiment tag
- step count
- physical simulation time
- dxPhys
- dtPhys

Scaling:
- tau_f_raw
- tau_T_raw
- tau_f
- tau_T
- tauFluidMin
- tauThermalMin
- tau_f_was_clamped
- tau_T_was_clamped
- nu_phys
- alpha_phys
- nuPhysTarget
- alphaPhysTarget
- nu ratio
- alpha ratio
- Re
- Pr

유동:
- Max Speed
- Max Mach
- Inlet Avg Speed
- Outlet Avg Speed
- Inlet Flow
- Outlet Flow
- Relative Flow Imbalance

안정성/보존성:
- Avg Density
- Density StdDev
- Mass Residual
- Mass Status
- Thermal Clamp In/Out
- Fluid Clamp In/Out

온도:
- Room Avg Temp
- Room Min / Max Temp
- Room Temp StdDev
- Inlet Avg Temp
- Outlet Avg Temp

8. 선택 사항: Jet Core 도달거리 Metrics 추가

가능하면 저비용으로 아래 지표를 추가하십시오.

- velocityReach_1p0_m
- velocityReach_1p5_m
- velocityReach_2p0_m
- jetCoreMaxDistance_m

정의:
Inlet patch center에서 Inlet flow direction 방향으로 투영했을 때, 각 속도 기준 이상을 만족하는 셀의 최대 투영거리입니다.

기준 속도:
- 1.0 m/s
- 1.5 m/s
- 2.0 m/s

구현 가이드:
- 이미 계산된 inlet flow direction을 사용하십시오.
- fluid cell만 대상으로 하십시오.
- cell world position 또는 physical position을 inlet patch center 기준으로 계산하십시오.
- 상대 위치 벡터를 inlet flow direction에 dot product하여 투영거리로 사용하십시오.
- 투영거리가 0보다 큰 셀만 사용하십시오.
- 각 threshold 이상인 셀 중 최대 투영거리를 기록하십시오.
- 정확도보다 저비용/안정성을 우선하십시오.
- 가능하면 FullMetrics readback 시에만 계산하십시오.
- GPU readback 비용이 크거나 구현 리스크가 크면 TODO만 남기고 필수 구현에서는 제외하십시오.

9. Stability Guardrails

tau가 0.5에 가까우면 불안정해질 수 있으므로 아래 경고를 추가하십시오.

- tauFluidMin < 0.51이면 경고
- tauThermalMin < 0.51이면 경고
- Max Mach > 0.30이면 경고
- densityStdDev가 급격히 증가하면 경고
- mass residual이 악화되면 경고

주의:
- tauFluidMin을 사용자가 0.505로 설정했는데 내부에서 몰래 0.53으로 되돌리지 마십시오.
- 단, solver가 발산하거나 NaN이 발생하는 경우에는 기존 안정성 처리 로직을 유지하십시오.
- Max Mach가 높을 때 단순히 maxMach를 낮추라는 메시지만 출력하지 말고, tau clamp와 유효 점성 증가 가능성도 함께 표시하십시오.

10. CODEX 작업 결과로 보고할 내용

수정 완료 후 아래 내용을 정리해서 보고하십시오.

- 수정한 파일 목록
- 각 파일별 변경 요약
- 새로 추가한 Inspector 항목
- A0~A4 실행 방법
- 각 Case의 의미
- 새 진단값의 의미
- 알려진 리스크
- 컴파일 에러 또는 경고 발생 여부
- 최소 수동 검증 절차

최소 수동 검증 절차 예시:
1. Unity를 연다.
2. SimulationController가 붙은 GameObject를 선택한다.
3. Case Study Runner 또는 Preset에서 A0를 적용한다.
4. 물리시간 30초까지 실행한다.
5. CSV 또는 Console Summary에서 tau_f, tau_T, nu ratio, alpha ratio, Max Mach, Mass Residual을 확인한다.
6. A1, A2, A3, A4를 같은 방식으로 반복한다.
7. 1.0 / 1.5 / 2.0 m/s 도달거리 또는 동일 View의 속도 contour를 비교한다.
8. Mass Status가 OK인지 확인한다.
9. densityStdDev와 Max Mach가 급격히 악화되지 않는지 확인한다.

중요:
- 과도한 리팩토링 금지.
- 테스트 자동화에 시간을 많이 쓰지 말 것.
- 우선 A0~A4를 쉽게 적용하고 로그로 비교 가능하게 만드는 것이 핵심입니다.
- 실시간성 때문에 dx = 0.04 m는 반드시 유지하십시오.
- 기존 Mass-Flux Corrected Outlet 동작은 깨지 않도록 주의하십시오.