# AGENTS.md

## Project Context

This project is a Unity-based LBM CFD solver for virtual chamber / HVAC airflow and thermal simulation.

Development environment:

- Unity: 6000.1.4f1
- OS: Windows 11 64-bit
- IDE: Visual Studio 2022
- Main purpose: improve the existing Unity LBM solver into a user-friendly, PowerFLOW-like simulation setup and result interpretation workflow.

The solver currently includes:

- D3Q19 fluid LBM
- D3Q7 thermal LBM
- Zou-He inlet / outlet boundary handling
- Mass-Flux Corrected Outlet
- thermal solver wrapper
- result sampler
- metrics logger
- temperature graph UI
- Unity object / BoxCollider based boundary setup

The goal of this refactoring is not to turn the solver into OpenLB-style research code. The goal is to make the solver easier for product developers to use, in a workflow closer to SIMULIA PowerFLOW.

---

## High-Level Direction

Benchmark the user experience against SIMULIA PowerFLOW, while preserving the internal Unity LBM solver as much as possible.

PowerFLOW-style direction:

- CAD / geometry based domain setup
- automatic lattice discretization
- resolution / refinement based setup
- physical input based UI
- solver presets
- automatic stability diagnostics
- transient statistics centered results
- user-friendly boundary objects
- simplified result summaries
- easy run readiness check

Internal implementation can still follow LBM / OpenLB-like concepts:

- lattice unit conversion
- lattice velocity
- relaxation time
- Mach number
- Reynolds number
- Prandtl number
- material / boundary assignment
- D3Q19 / D3Q7 collision and streaming
- mass conservation monitoring

Use this split:

- Internal numerical structure: OpenLB-like
- User experience: PowerFLOW-like
- Validation / reference comparison: Fluent-like

---

## General Coding Rules

1. Keep the existing solver physics stable unless explicitly required.
2. Do not rewrite the ComputeShader unless absolutely necessary.
3. Preserve the existing Mass-Flux Corrected Outlet behavior.
4. Preserve room / inlet / outlet temperature sampling.
5. Preserve inlet / outlet flow calculation.
6. Preserve density and mass conservation diagnostics.
7. Prefer adding a user-friendly wrapper layer instead of deleting working solver logic.
8. Existing serialized fields should be kept where possible to avoid breaking Unity scene data.
9. If a serialized field must be renamed, use `FormerlySerializedAs`.
10. Use Unity Inspector attributes such as `Header`, `Tooltip`, `Space`, `SerializeField`, and `ContextMenu` before creating custom editors.
11. Avoid large unrelated refactors.
12. Keep code compatible with Unity 6000.1.4f1.
13. Keep C# code compatible with Visual Studio 2022.
14. Prefer simple, explicit, maintainable code over clever abstractions.

---

## Important Existing Files

Primary files:

- `SimulationController.cs`
- `ThermalSolver.cs`
- `LBMZouHeBox.cs`
- `D3Q7LBMThermalKernel.compute`
- `SimulationResultSampler.cs`
- `SimulationResultMetrics.cs`
- `SimulationMetricsFileLogger.cs`
- `TemperatureGraphPresenter.cs`
- `TemperatureGraphGraphic.cs`

When modifying these files, check project-wide references before deleting public fields, serialized fields, enums, or methods.

---

# 1. PowerFLOW-Style User Setup

## Goal

Make the solver usable through physical simulation settings rather than low-level LBM parameters.

General users should set:

- domain size
- cell size / resolution preset
- inlet / outlet position and size
- inlet velocity or flow rate
- inlet temperature
- outlet type
- fluid viscosity
- fluid density
- Prandtl number
- reference temperature
- temperature min / max
- solver preset

Advanced users can still access:

- `dxPhys`
- `dtPhys`
- `maxMach`
- `tau_f`
- `tau_T`
- `nuLat`
- `alphaLat`
- `csLat`
- turbulence parameters
- outlet density parameters
- debug metrics

---

## Solver Preset

Add or maintain a user-facing solver preset enum:

```csharp
public enum SolverEasePreset
{
    FastPreview,
    Balanced,
    HighFidelity,
    Custom
}
```

### FastPreview

Purpose:

- quick design comparison
- larger cell size
- lower output frequency
- reduced memory usage

Expected behavior:

- increase `dxPhys`
- use stable `maxMach`
- reduce expensive logging / graph update frequency
- prioritize speed over detail

### Balanced

Purpose:

- default recommended setup
- balance between speed, stability, and resolution

Expected behavior:

- keep values close to the current project default
- provide stable `tau_f`, `tau_T`, `Mach`, `Re`, `Pr`

### HighFidelity

Purpose:

- more detailed result
- better spatial resolution
- stability first

Expected behavior:

- reduce `dxPhys`
- use lower `maxMach`
- increase result output detail moderately
- warn if memory is too large

### Custom

Purpose:

- expert mode
- preserve manually edited parameters

Expected behavior:

- do not automatically overwrite existing values
- expose advanced LBM parameters

---

## Preset Implementation Rules

Add an `ApplySolverPreset()` method in `SimulationController`.

Recommended behavior:

- apply preset only when explicitly requested or when preset changes
- provide `ContextMenu("Apply Solver Preset")`
- do not repeatedly overwrite Custom values in `OnValidate()`
- update summary strings after applying preset

Minimum diagnostics after preset application:

- selected preset
- `dxPhys`
- `dtPhys`
- grid size
- cell count
- estimated memory
- Mach
- Re
- Pr
- `tau_f`
- `tau_T`
- stability status

---

# 2. OpenLB-Style Unit Conversion, Hidden Behind UX

## Goal

Keep LBM unit conversion visible as diagnostics, but do not force normal users to edit it directly.

The solver should compute:

- `dxPhys`
- `dtPhys`
- lattice velocity
- speed scale
- physical to lattice conversion
- `nuLat`
- `alphaLat`
- `tau_f`
- `tau_T`
- Mach number
- Reynolds number
- Prandtl number
- optional Rayleigh number if buoyancy input is available

User-facing summary should show:

- physical setup
- lattice setup
- stability status
- warning messages

Do not expose unnecessary internal formulas as primary Inspector controls.

---

## Stability Diagnostic Rules

Add or maintain a stability summary that checks:

- `dxPhys > 0`
- `dtPhys > 0`
- `Mach` in recommended range
- `tau_f` in stable range
- `tau_T` in stable range
- `Pr > 0`
- grid cell count not too large
- estimated memory not too large

Recommended status enum:

```csharp
public enum SimulationHealthStatus
{
    OK,
    Warning,
    Invalid
}
```

Recommended messages:

- `OK: Balanced preset recommended.`
- `Warning: Mach number is high. Reduce characteristic velocity or maxMach.`
- `Warning: Grid is large. Increase dxPhys or use FastPreview.`
- `Invalid: dxPhys must be greater than zero.`
- `Invalid: tau_f is outside the stable range.`
- `Invalid: tau_T is outside the stable range.`

---

# 3. PowerFLOW-Style Boundary Object Setup

## Goal

Make boundaries easy to set using Unity objects and physical quantities.

The existing `LBMZouHeBox` should act as a user-friendly boundary object.

It should support:

- boundary type
- geometry / patch summary
- inlet settings
- outlet settings
- thermal settings
- advanced LBM boundary settings
- debug summary

---

## Boundary Type

Existing concepts should be preserved:

- Inlet
- Outlet

If needed later, additional user-facing boundary types may be added:

- Wall
- Opening
- HeatSource
- Rack
- PorousZone

Do not add unsupported behavior unless the underlying solver supports it.

---

## Boundary Input Mode

Add or maintain:

```csharp
public enum BoundaryInputMode
{
    Velocity,
    VolumeFlowRate,
    PressureDensity,
    AutoMassBalancedOutlet
}
```

### Inlet Modes

#### Velocity

Use existing `windSpeedPhys` behavior.

#### VolumeFlowRate

User enters flow rate. The solver computes normal velocity from patch area.

Supported units:

- m3/s
- CMM

Conversion:

```text
m3/s = CMM / 60
CMM = m3/s * 60
```

Velocity computation:

```text
normalVelocity = volumeFlowRateM3ps / patchAreaPhys
windSpeedPhys = normalDirection * normalVelocity
```

Then existing lattice conversion logic can be reused.

### Outlet Modes

#### PressureDensity

Use existing `rhoOut` behavior.

#### AutoMassBalancedOutlet

Use existing Mass-Flux Corrected Outlet behavior.

Default outlet UX should prefer AutoMassBalancedOutlet for HVAC room flow problems.

---

## Boundary Summary

Each boundary should provide a readable summary:

- boundary type
- input mode
- patch cell count
- patch physical area
- normal direction
- target velocity in m/s
- target flow rate in m3/s
- target flow rate in CMM
- inlet temperature in degC
- outlet rho target
- mass correction enabled
- warnings if patch area is zero

---

## Boundary Rules

1. Do not break existing `Refresh()` behavior.
2. Do not break `PatchCellCount`.
3. Do not break `PatchAreaPhys()`.
4. Do not break `TargetOutletNormalSpeedLat`.
5. Do not change `ThermalSolver.ZHInletPatchData` or `ZHOutletPatchData` unless necessary.
6. Do not modify ComputeShader for boundary UX changes.
7. Existing velocity inlet mode must keep working exactly as before.
8. Existing Mass-Flux Corrected Outlet must keep working.

---

# 4. Case Summary

## Goal

Before running the simulation, users should understand the whole case at a glance.

Add or maintain summary strings in `SimulationController`:

- `caseSummary`
- `boundarySummary`
- `solverSummary`
- `stabilitySummary`
- `recommendationSummary`
- `readinessSummary`

These may be Inspector multiline strings. A new UI panel is not required.

---

## Case Summary Contents

### Domain

- physical size
- `dxPhys`
- `nx`, `ny`, `nz`
- total cell count
- estimated memory

### Boundaries

- inlet count
- outlet count
- each inlet area, flow, velocity, temperature
- each outlet area, flow target, rho target, mass correction mode

### Solver

- preset
- `dtPhys`
- `maxMach`
- `tau_f`
- `tau_T`
- `nuPhys`
- `alphaPhys`
- `Re`
- `Pr`

### Stability

- OK / Warning / Invalid
- important warning messages

### Recommendation

Examples:

- `Ready to run.`
- `Use FastPreview to reduce memory.`
- `Reduce maxMach for better LBM stability.`
- `Increase dxPhys because the grid is too large.`
- `Check inlet and outlet patch sizes.`

---

# 5. Run Readiness Check

## Goal

Before simulation starts, catch critical configuration issues.

Add a readiness check method in `SimulationController`:

```csharp
public SimulationHealthStatus CheckRunReadiness()
```

or similar.

---

## Check Items

### Domain

- domain object exists
- `dxPhys > 0`
- `nx > 1`, `ny > 1`, `nz > 1`
- total cell count within allowed range

### Boundary

- at least one inlet exists
- at least one outlet exists
- inlet patch cell count is not zero
- outlet patch cell count is not zero

### Solver

- `dtPhys > 0`
- `tau_f` stable
- `tau_T` stable
- Mach recommended

### Thermal

- temperature min < temperature max
- inlet temperature within reasonable range

### Output

- sampler/logger missing should be Warning only, not Invalid

---

## Readiness Rules

- Only critical errors should block simulation.
- Warnings should not block simulation.
- Invalid setup may stop `StartSimulation` or solver rebuild.
- Use `Debug.LogWarning` for warnings.
- Use `Debug.LogError` for invalid setup.

---

# 6. Inspector UX Organization

## Goal

Make the Inspector readable for non-LBM experts.

Do not create a custom editor unless necessary.

Use:

- `[Header]`
- `[Tooltip]`
- `[Space]`
- `[SerializeField]`
- `[TextArea]`
- `[ContextMenu]`

---

## SimulationController Inspector Sections

Recommended order:

1. User Setup
2. Domain / Resolution
3. Solver Preset
4. Physical Properties
5. Temperature Setup
6. Boundary Auto Sync
7. Diagnostics / Summary
8. Advanced LBM Parameters
9. Debug / Logging

---

## LBMZouHeBox Inspector Sections

Recommended order:

1. Boundary Type
2. Geometry / Patch Summary
3. Inlet Settings
4. Outlet Settings
5. Thermal Settings
6. Advanced LBM Boundary
7. Debug Summary

---

## Tooltip Examples

For `tau_f`:

```text
LBM fluid relaxation time. Normal users should not edit this directly.
```

For `maxMach`:

```text
Lattice Mach number limit for LBM stability. Lower is usually more stable but slower.
```

For `rhoOut`:

```text
LBM density target corresponding to a pressure outlet condition.
```

For `dxPhys`:

```text
Physical lattice cell size in meters. Smaller values increase resolution and memory cost.
```

---

# 7. ResultSampler / Metrics / Logger / Temperature Graph Refactoring

## Goal

The existing ResultSampler, Metrics Logger, and Temperature Graph may be deleted, integrated, or reconstructed if doing so improves user convenience.

The goal is not to preserve every old debug metric. The goal is to help users understand simulation status and results quickly.

Affected files:

- `SimulationResultSampler.cs`
- `SimulationResultMetrics.cs`
- `SimulationMetricsFileLogger.cs`
- `TemperatureGraphPresenter.cs`
- `TemperatureGraphGraphic.cs`
- any related UI or debug output code

---

## Core Results That Must Remain

The following must remain available:

### Thermal

- room average temperature
- room minimum temperature
- room maximum temperature
- room temperature standard deviation or uniformity indicator
- inlet average temperature
- outlet average temperature

### Flow

- inlet average speed
- outlet average speed
- inlet flow rate in m3/s
- outlet flow rate in m3/s
- inlet flow rate in CMM
- outlet flow rate in CMM
- relative flow imbalance
- max speed
- max Mach if available

### Conservation / Stability

- average density
- density standard deviation
- normalized mass residual
- mass conservation status
- `tau_f`
- `tau_T`
- Reynolds number
- Prandtl number
- stability status
- readiness status

### Debug Counters

Keep only if useful:

- thermal clamp count
- fluid clamp count
- sampler status

---

## ResultSampler Rules

The ResultSampler may be simplified.

Allowed:

- separate user-facing result sampling from internal debug sampling
- remove metrics that are no longer displayed, logged, or used
- move advanced values to debug-only fields
- reduce unnecessary post-processing loops
- simplify output structure

Not allowed:

- breaking AsyncGPUReadback flow without replacement
- removing room temperature sampling
- removing inlet/outlet temperature sampling
- removing inlet/outlet flow calculation
- removing density / mass residual calculation
- breaking Mass-Flux Corrected Outlet diagnostics

---

## SimulationResultMetrics Rules

Reorganize metrics into user-centered groups:

1. Case / Time
2. Thermal Result
3. Flow Result
4. Stability / Conservation
5. Solver Diagnostics
6. Debug Counters

Delete or obsolete fields if:

- no longer calculated
- always default value
- not used by Summary, CSV, Graph, Solver, or UI
- duplicated by a clearer new metric
- originally added for temporary debugging only

If a public field might be referenced externally, mark it obsolete first:

```csharp
[Obsolete("Replaced by <newFieldName>. Will be removed after validation.")]
```

---

## ToSummaryText Output Order

`SimulationResultMetrics.ToSummaryText()` should print results in this order:

1. Case / Time
   - step
   - simulation time
   - `dtPhys`
   - preset if available

2. Thermal Result
   - room average temperature
   - room min / max temperature
   - room temperature standard deviation
   - inlet average temperature
   - outlet average temperature

3. Flow Result
   - inlet average speed
   - outlet average speed
   - inlet flow m3/s
   - outlet flow m3/s
   - inlet flow CMM
   - outlet flow CMM
   - relative flow imbalance

4. Stability / Conservation
   - max Mach
   - average density
   - density standard deviation
   - normalized mass residual
   - mass conservation status

5. Solver Diagnostics
   - `tau_f`
   - `tau_T`
   - Reynolds number
   - Prandtl number
   - stability status
   - readiness status

6. Debug Counters
   - thermal clamp count
   - fluid clamp count
   - status

Missing values should print as `-`, not throw exceptions.

---

## Metrics Logger Rules

The CSV logger may be restructured.

The CSV should focus on values useful for post-processing, not every internal debug variable.

Recommended CSV columns:

```text
timestamp_local
experiment_tag
step_count
sim_time_sec
dt_phys
preset
room_avg_temp_degC
room_min_temp_degC
room_max_temp_degC
room_temp_stddev_degC
inlet_avg_temp_degC
outlet_avg_temp_degC
inlet_flow_m3ps
outlet_flow_m3ps
inlet_flow_cmm
outlet_flow_cmm
relative_flow_imbalance
max_speed_phys
max_mach
avg_density
density_stddev
mass_residual_normalized
stability_status
readiness_status
status
```

Rules:

1. Header column count must match row column count.
2. CSV saving must keep working.
3. Old columns may be removed if they are debug-only or duplicated.
4. If backward compatibility is explicitly required, provide a legacy logging mode.
5. Otherwise, prefer a clean new CSV schema.

---

## Temperature Graph Rules

The graph may be simplified or rewritten.

Default graph should show:

- Room Avg Temp
- Inlet Temp
- Outlet Temp

Optional:

- Room Min / Max band
- Room StdDev indicator

Rules:

1. The x-axis must be `Simulation Time (s)`.
2. Do not label simulated time as `Actual Time`.
3. Avoid showing too many curves by default.
4. Flow and mass conservation may be shown in summary text instead of the temperature graph.
5. The graph should update without null exceptions.
6. If existing graph code is too complex, replace it with a simpler maintainable implementation.

---

# 8. Metrics Cleanup Policy

## Classification

Before deleting metrics, classify each field:

- KEEP: calculated and used
- MERGE: duplicated by another clearer metric
- OBSOLETE: may be externally referenced, keep temporarily with `[Obsolete]`
- DELETE: unused and no longer meaningful

---

## Delete Candidates

Consider deleting or replacing:

- temperature-only mode legacy fields
- temporary FullMetrics debug fields
- duplicate mass residual fields
- duplicate density standard deviation fields
- duplicate kinetic energy fields
- duplicate inlet/outlet imbalance fields
- duplicate clamp counters
- duplicate graph-only temperature fields
- duplicated outlet diagnostic fields
- old tau / Mach / Re / Pr fields superseded by stability diagnostics

---

## Do Not Delete If Needed For

- Mass-Flux Corrected Outlet validation
- inlet/outlet flow balance
- LBM stability check
- temperature uniformity analysis
- density / mass conservation analysis
- future Fluent / PowerFLOW benchmark comparison

---

# 9. User-Centered Final Result Structure

The final user-facing result should be simple.

## Thermal

- average room temperature
- min / max room temperature
- temperature standard deviation or uniformity
- inlet / outlet average temperature

## Flow

- inlet flow
- outlet flow
- flow imbalance
- max velocity

## Stability

- Mach
- average density
- mass residual
- `tau_f`
- `tau_T`
- stability status

## Run Status

- Ready / Warning / Invalid
- warning messages
- sampler status

---

# 10. Minimal Verification Policy

The user requested minimal validation due to token constraints.

Do not create complex regression tests unless explicitly asked.

Minimum required checks:

1. Unity C# compile errors are resolved.
2. ComputeShader kernel names and dispatch flow are not broken.
3. Existing velocity inlet still works.
4. Existing Mass-Flux Corrected Outlet still works.
5. `SimulationResultSampler` can sample without null exceptions.
6. `SimulationResultMetrics.ToSummaryText()` prints without exceptions.
7. CSV header count equals row count.
8. Temperature graph updates with simulation time.
9. Case Summary is not null or empty after setup.
10. Readiness Summary reports Ready / Warning / Invalid.

Avoid lengthy validation reports. Provide only concise verification results.

---

# 11. Preferred Implementation Order

Use this order to reduce risk:

1. Add `SolverEasePreset` and `ApplySolverPreset()`.
2. Add grid / memory / stability diagnostics.
3. Add Case Summary.
4. Add Run Readiness Check.
5. Add Boundary Input Mode.
6. Add volume flow rate input for inlet.
7. Organize Inspector sections.
8. Reorganize `SimulationResultMetrics.ToSummaryText()`.
9. Reconstruct CSV logger if useful.
10. Simplify or reconstruct Temperature Graph if useful.
11. Run minimal integration cleanup.

If only a small change is possible, prioritize:

1. Preset
2. Diagnostics
3. Case Summary
4. Readiness Check

---

# 12. Codex Task Prompt: Full PowerFLOW UX Refactoring

Use this prompt when asking Codex to apply the full direction.

```text
현재 Unity LBM Solver를 SIMULIA PowerFLOW 벤치마킹 방향으로 사용자 편의성을 개선해줘.

목표:
- OpenLB 같은 연구용 코드 스타일이 아니라, PowerFLOW처럼 사용자가 물리 조건과 오브젝트 기반으로 쉽게 케이스를 셋업하게 만든다.
- 내부 LBM 수치해석 로직은 최대한 유지한다.
- 복잡한 LBM 파라미터는 preset, 자동 계산, summary, diagnostics 뒤로 숨긴다.
- 사용자는 domain, resolution, inlet/outlet, 풍량/풍속, 온도, solver preset, readiness 상태를 쉽게 이해해야 한다.

반드시 반영할 것:
1. SolverEasePreset 추가 또는 정리
   - FastPreview
   - Balanced
   - HighFidelity
   - Custom

2. SimulationController 개선
   - ApplySolverPreset()
   - grid / memory / stability diagnostics
   - caseSummary
   - boundarySummary
   - solverSummary
   - stabilitySummary
   - recommendationSummary
   - readinessSummary
   - CheckRunReadiness()

3. LBMZouHeBox 개선
   - BoundaryInputMode 추가
   - Velocity 입력 유지
   - VolumeFlowRate 입력 추가
   - CMM 및 m3/s 지원
   - patch area 기준 normal velocity 자동 계산
   - Outlet AutoMassBalancedOutlet 모드 추가 또는 정리
   - boundary summary 추가

4. Inspector UX 정리
   - User Setup
   - Domain / Resolution
   - Solver Preset
   - Physical Properties
   - Boundary Settings
   - Diagnostics / Summary
   - Advanced LBM Parameters
   - Debug / Logging

5. ResultSampler / Metrics / Logger / Temperature Graph 재구성 허용
   - 사용자 편의성이 좋아진다면 삭제, 통합, 재작성 가능
   - 단, room/inlet/outlet temperature, inlet/outlet flow, density, mass residual, stability diagnostics는 유지
   - Metrics는 사용자 중심으로 정리
   - CSV는 핵심 지표 중심으로 단순화
   - Temperature Graph는 Simulation Time 기준으로 Room Avg / Inlet / Outlet 온도를 명확히 표시

중요 제약:
- ComputeShader는 꼭 필요한 경우가 아니면 수정하지 마라.
- Mass-Flux Corrected Outlet 기능은 유지한다.
- 기존 Inlet Velocity 모드는 유지한다.
- ThermalSolver의 핵심 계산 흐름은 유지한다.
- 과도한 검증 자동화는 만들지 마라.
- Unity 6000.1.4f1, Windows 11, Visual Studio 2022 기준으로 컴파일 가능해야 한다.

최소 검증:
- Unity C# 컴파일 오류 없음
- 기존 velocity inlet 동작 유지
- 기존 Mass-Flux Corrected Outlet 동작 유지
- ResultSampler null exception 없음
- ToSummaryText 정상 출력
- CSV header와 row column 수 일치
- Temperature Graph가 Simulation Time 기준으로 갱신
- Case Summary와 Readiness Summary 출력

작업 결과 보고:
- 수정한 파일 목록
- 추가한 enum/class/function 목록
- 삭제/통합한 Metrics 목록
- 새 Inspector UX 구조
- 최소 검증 결과
```

---

# 13. Codex Task Prompt: Minimal First Step

Use this prompt if the refactor should be done in a small first step.

```text
Unity LBM Solver의 사용자 편의성을 PowerFLOW 방향으로 개선하는 첫 단계만 적용해줘.

이번 단계에서 할 것:
1. SimulationController에 SolverEasePreset 추가
   - FastPreview
   - Balanced
   - HighFidelity
   - Custom

2. ApplySolverPreset() 추가
   - FastPreview: 빠른 반복 해석용
   - Balanced: 기본 추천 설정
   - HighFidelity: 고해상도/안정성 우선
   - Custom: 기존 수동 설정 유지

3. grid / memory / stability summary 추가
   - nx, ny, nz
   - total cell count
   - dxPhys
   - dtPhys
   - estimated memory
   - Mach
   - Re
   - Pr
   - tau_f
   - tau_T
   - stability status

4. caseSummary와 readinessSummary 추가

이번 단계에서 하지 말 것:
- ComputeShader 수정 금지
- ThermalSolver 수정 금지
- ResultSampler / Metrics / Logger / Graph는 아직 크게 수정하지 않음
- Boundary 구조 대규모 변경 금지

최소 검증:
- Unity C# 컴파일 오류 없음
- Preset 변경 시 summary 갱신
- Custom 선택 시 기존 값 유지
```

---

# 14. Codex Task Prompt: Result System Reconstruction

Use this prompt when focusing on ResultSampler, Metrics Logger, and Temperature Graph.

```text
ResultSampler, SimulationResultMetrics, Metrics Logger, Temperature Graph를 PowerFLOW식 사용자 중심 결과 체계로 재구성해줘.

목표:
- 사용자가 해석 상태와 결과를 쉽게 이해하게 만든다.
- 불필요한 내부 디버그 metrics는 삭제, 통합, 또는 Debug 전용으로 이동한다.
- 기존 결과 체계를 보존하는 것보다 사용자 편의성을 우선한다.

대상 파일:
- SimulationResultSampler.cs
- SimulationResultMetrics.cs
- SimulationMetricsFileLogger.cs
- TemperatureGraphPresenter.cs
- TemperatureGraphGraphic.cs
- 관련 UI / Debug 출력 코드

반드시 유지할 핵심 값:
- room avg/min/max/stddev temperature
- inlet avg temperature
- outlet avg temperature
- inlet flow m3/s and CMM
- outlet flow m3/s and CMM
- relative flow imbalance
- max speed
- max Mach if available
- average density
- density stddev
- normalized mass residual
- stability status
- readiness status
- sampler status

허용되는 작업:
- 미사용 Metrics 삭제
- 중복 Metrics 통합
- Obsolete 처리
- CSV 컬럼 재구성
- Temperature Graph 단순화 또는 재작성
- ToSummaryText 사용자 중심 재작성

CSV 추천 컬럼:
- timestamp_local
- experiment_tag
- step_count
- sim_time_sec
- dt_phys
- preset
- room_avg_temp_degC
- room_min_temp_degC
- room_max_temp_degC
- room_temp_stddev_degC
- inlet_avg_temp_degC
- outlet_avg_temp_degC
- inlet_flow_m3ps
- outlet_flow_m3ps
- inlet_flow_cmm
- outlet_flow_cmm
- relative_flow_imbalance
- max_speed_phys
- max_mach
- avg_density
- density_stddev
- mass_residual_normalized
- stability_status
- readiness_status
- status

Temperature Graph:
- x축은 반드시 Simulation Time (s)
- 기본 표시: Room Avg Temp, Inlet Temp, Outlet Temp
- Actual Time 표현 제거
- 그래프가 복잡하면 단순하고 안정적인 구조로 재작성

중요 제약:
- ThermalSolver와 ComputeShader의 물리 계산 로직은 바꾸지 마라.
- AsyncGPUReadback 흐름을 깨지 마라.
- Mass-Flux Corrected Outlet 검증에 필요한 flow/density/mass metrics는 유지한다.
- CSV header와 row column 수는 반드시 일치해야 한다.

최소 검증:
- Unity C# 컴파일 오류 없음
- ResultSampler null exception 없음
- ToSummaryText 정상 출력
- CSV header/row column 수 일치
- Temperature Graph가 Simulation Time 기준으로 갱신
```

---

# 15. Final Cleanup Prompt

Use this after applying several refactoring prompts.

```text
지금까지 적용한 PowerFLOW 벤치마킹 UX 개선 작업을 최소 범위에서 정리해줘.

목표:
- 컴파일 오류 제거
- 중복 enum/field 정리
- Summary string 갱신 흐름 정리
- Preset, BoundaryInputMode, ReadinessCheck, Metrics Summary가 서로 충돌하지 않게 정리

확인할 파일:
- SimulationController.cs
- LBMZouHeBox.cs
- SimulationResultSampler.cs
- SimulationResultMetrics.cs
- SimulationMetricsFileLogger.cs
- TemperatureGraphPresenter.cs
- TemperatureGraphGraphic.cs
- ThermalSolver.cs는 꼭 필요한 경우만 확인
- ComputeShader는 수정하지 않는 것을 원칙으로 함

최소 확인:
1. Unity C# 컴파일 오류 없음
2. 기존 Inlet Velocity 모드 유지
3. 기존 Mass-Flux Corrected Outlet 유지
4. Case Summary 출력
5. Readiness Summary 출력
6. Result Summary 출력
7. CSV header/row column 수 일치
8. Temperature Graph x축이 Simulation Time 기준

보고:
- 수정한 파일 목록
- 추가한 enum/class/function 목록
- 삭제/통합/Obsolete 처리한 Metrics 목록
- 남겨둔 Advanced/Internal 항목
- 최소 검증 결과
```
