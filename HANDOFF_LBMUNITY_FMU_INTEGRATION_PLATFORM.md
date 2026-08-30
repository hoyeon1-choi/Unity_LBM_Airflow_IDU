# LBMUNITY_FMU_INTEGRATION_PLATFORM Handoff

Last updated: 2026-08-31 KST
Project path: `D:\2_FFD\0_Unity\LBMUnity_1wayCST_FMU_Interagtion_PlatForm`
Unity target: Unity 6000.1.4f1 / Windows 11 / Visual Studio 2022
Remote repo: `https://github.com/hoyeon1-choi/Unity_LBM_Airflow_IDU.git`

## 1. Current Git State

Active branch:

```powershell
git switch feature/product-fmu-platform
```

Recent history:

```text
cebf531 (HEAD -> feature/product-fmu-platform) Add MultiV product FMU platform checkpoint
b7dedc2 Add MultiV product FMU draft integration
c7f6510 Add product FMU co-simulation profile platform
bbcd020 (tag: v0.2.0-fmu-cosim, origin/feature/fmu-lbm-coupling, feature/fmu-lbm-coupling) Add Simple FMU-LBM co-simulation coupling
349bb25 (tag: v0.1.0-lbm-only, origin/main, main) Initial GitHub Push
```

Important branch meaning:

- `main`: original LBM-only baseline, tagged `v0.1.0-lbm-only`.
- `feature/fmu-lbm-coupling`: Simple FMU-LBM co-simulation completed, tagged `v0.2.0-fmu-cosim`.
- `feature/product-fmu-platform`: current product FMU integration branch.

Current checkpoint commit:

```text
cebf531 Add MultiV product FMU platform checkpoint
```

Uncommitted changes intentionally left out of the checkpoint:

```text
.vscode/settings.json modified
CaseStudyReports/* deleted in working tree
```

These were not included because they are not directly part of the product FMU platform work. Decide later whether to keep, revert, or commit separately.

Push has not been confirmed after `cebf531`. To back up this branch to GitHub:

```powershell
git push -u origin feature/product-fmu-platform
```

## 2. Work Sequence Summary

### Phase A: Existing LBM Project Stabilization

- Original Unity LBM airflow simulation project was put under Git.
- Stable LBM-only version was tagged:

```text
v0.1.0-lbm-only
```

### Phase B: Simple FMU-LBM Coupling

Implemented Simple Controller + Simple Plant FMU co-simulation:

- Controller FMU: `Simple_CFMU.fmu`
- Plant FMU: `Simple_Plant.fmu`
- LBM sensor temperature is passed to FMU.
- Plant discharge/output temperature is passed back to LBM inlet boundary.
- Runtime parameter overrides can be edited from each FMU GameObject Inspector.
- Co-simulation CSV logging was aligned with `SimulationMetricsFileLogger` output directory.
- Native FMI runtime uses `Assets/Plugins/x86_64/FmuNativePlugin.dll`.
- Simple smoke/end-to-end tests were verified earlier.
- Stable Simple FMU version was tagged:

```text
v0.2.0-fmu-cosim
```

### Phase C: Product FMU Platform Branch

Created and continued work on:

```text
feature/product-fmu-platform
```

Main goal: generalize Simple-specific hardcoding into a configurable product co-simulation profile.

Product platform assumptions:

- Controller FMU + Product FMU + Plant/Chamber FMUs.
- Controller and Product exchange control/sensor signals.
- Chamber R1 is intended to be connected with the LBM airflow result in the next stage.
- Initial target is 50-second end-to-end verification.

## 3. Important Paths

Project root:

```text
D:\2_FFD\0_Unity\LBMUnity_1wayCST_FMU_Interagtion_PlatForm
```

Primary scene:

```text
Assets/Scenes/LBMScenes/LBM_1wayCST.unity
```

Removed duplicate/legacy scene path:

```text
Assets/Prefabs/Scenes/LBMScenes/LBM_1wayCST.unity
```

FMU root:

```text
Assets/StreamingAssets/FMU
```

FMU folder structure:

```text
Assets/StreamingAssets/FMU/controller
Assets/StreamingAssets/FMU/product
Assets/StreamingAssets/FMU/plant
```

Native plugin:

```text
Assets/Plugins/x86_64/FmuNativePlugin.dll
```

Product profile asset:

```text
Assets/CoSimulationProfiles/MultiV_Product_Draft_Profile.asset
```

## 4. Current FMU Inventory

Controller folder:

```text
Assets/StreamingAssets/FMU/controller/Multi_V_S__Set_CFMU_CS.fmu
Assets/StreamingAssets/FMU/controller/Simple_CFMU.fmu
```

Product folder:

```text
Assets/StreamingAssets/FMU/product/MULTIV_FMU_WARPPER.fmu
```

Plant folder:

```text
Assets/StreamingAssets/FMU/plant/Simple_Chamber_R1.fmu
Assets/StreamingAssets/FMU/plant/Simple_Chamber_R2.fmu
Assets/StreamingAssets/FMU/plant/Simple_Chamber_R3.fmu
Assets/StreamingAssets/FMU/plant/Simple_Chamber_R4.fmu
Assets/StreamingAssets/FMU/plant/Simple_Chamber_R5.fmu
Assets/StreamingAssets/FMU/plant/Simple_Plant.fmu
```

EEPROM/hex files remain under:

```text
Assets/StreamingAssets/FMU
```

Note: the new Controller CS FMU does not expose String parameters such as `Option_HEX_path`, so the previous Controller EEPROM string overrides were cleared from the MultiV profile. The EEPROM files remain available for future FMUs if needed.

## 5. Key Code Areas

### `Assets/Scripts/CoSimulation/CoSimulationProfile.cs`

Defines Simple and MultiV product profiles.

Current MultiV Controller config:

```text
childObjectName: MultiV_Controller_Model
modelId: Multi_V_S__Set_CFMU_CS
fmuFileName: controller/Multi_V_S__Set_CFMU_CS.fmu
```

Important variable naming update:

- Old Controller FMU used dot-style variable names, for example `Multi_V_S.Comp__TarFreq`.
- New CS Controller FMU uses underscore-style variable names, for example `Multi_V_S_Comp__TarFreq`.
- Profile connection map was updated accordingly.

Examples:

```text
Multi_V_S_Comp__TarFreq
Multi_V_S_Fan1__TarRPM
Multi_V_S_4Way_Valve__OnOff
Multi_V_S_MAIN_EEV__CurPulse
IDU_01_SetTemp
IDU_01_Room_Temp
IDU_01_CurSetFan
IDU_01_EEV_TarPulse
```

### `Assets/Editor/CoSimulation/CoSimulationSceneConfigurator.cs`

Editor menu tools:

```text
Tools/Co-Simulation/Apply Production Harness To Open Scene
Tools/Co-Simulation/Run Short Integration Test (50s)
Tools/Co-Simulation/Run MultiV Product Draft Test (50s)
Tools/Co-Simulation/Probe MultiV Product Native Initialization
Tools/Co-Simulation/Create MultiV Product Draft Profile Asset
```

Important behavior:

- `Run MultiV Product Draft Test (50s)` forces the runtime MultiV profile.
- `Apply Production Harness To Open Scene` uses the currently selected `CoSimulationProfile`; if no profile is selected, it falls back to Simple default.
- Before product testing in Unity, select `Assets/CoSimulationProfiles/MultiV_Product_Draft_Profile.asset` and then run `Apply Production Harness To Open Scene`.

Expected MultiV Hierarchy under harness:

```text
__CoSimulationHarness
  MultiV_Controller_Model
  MultiV_Product_Model
  Simple_Chamber_R1_Model
  Simple_Chamber_R2_Model
  Simple_Chamber_R3_Model
  Simple_Chamber_R4_Model
  Simple_Chamber_R5_Model
```

Warning: the saved scene file may still show Simple harness objects if Unity scene changes were not saved after applying the profile. If the Hierarchy shows Simple FMUs, reselect the MultiV profile and apply the harness again.

### `Assets/Scripts/CoSimulation/FmiRuntime.cs`

Managed C# wrapper over `FmuNativePlugin.dll`.

Current native functions exposed:

```text
Fmu_Load
Fmu_Initialize
Fmu_SetReal
Fmu_GetReal
Fmu_RegisterInitialReal
Fmu_DoStep
Fmu_Reset
Fmu_Unload
Fmu_GetLastError
```

The plugin source is not in the repo. C# side does not launch `FMI2CoSimulationServer.exe`.

### `Assets/Scripts/CoSimulation/FmuCoSimulationModel.cs`

Handles FMU unzip, native/mock runtime selection, Real parameter overrides, and String parameter modelDescription rewrite.

String parameter support is implemented by rewriting `modelDescription.xml` start values before native load. It is not native `fmi2SetString` support.

### Logging

Co-simulation CSV log location is tied to `SimulationMetricsFileLogger` location. The co-sim logger should write beside the metrics CSV.

## 6. FMU Test Results So Far

### Old Controller FMU

Old file:

```text
Multi_V_S__Set_CFMU.fmu
```

Findings:

- `modelDescription.xml` did not declare `needsExecutionTool=true`.
- FMU included `FMI2CoSimulationServer.exe`, but metadata did not say it was mandatory.
- `Fmu_Load` succeeded.
- `Fmu_Initialize` hung and did not return within the timeout.

Conclusion: old Controller FMU was not usable through the current native plugin path.

### New Controller CS FMU

New file:

```text
Multi_V_S__Set_CFMU_CS.fmu
```

Probe result:

```text
LOAD OK
INIT OK
STEP OK
GET OK Multi_V_S_Comp__TarFreq=0
GET OK Multi_V_S_Fan1__TarRPM=0
GET OK IDU_01_CurSetFan=0
GET OK IDU_01_EEV_TarPulse=0
```

Conclusion: Controller CS FMU works with current `FmuNativePlugin.dll`.

### Chamber FMUs

Files:

```text
Simple_Chamber_R1.fmu
Simple_Chamber_R2.fmu
Simple_Chamber_R3.fmu
Simple_Chamber_R4.fmu
Simple_Chamber_R5.fmu
```

Probe result:

```text
R1 LOAD OK / INIT OK / STEP OK
R2 LOAD OK / INIT OK / STEP OK
R3 LOAD OK / INIT OK / STEP OK
R4 LOAD OK / INIT OK / STEP OK
R5 LOAD OK / INIT OK / STEP OK
```

Conclusion: Chamber/Plant FMUs are individually usable.

### Product FMU

File:

```text
MULTIV_FMU_WARPPER.fmu
```

Current result:

```text
LOAD OK
INIT FAIL: fmi2ExitInitializationMode failed
```

License issue was seen earlier, but after the FMU/license replacement the license checkout error disappeared. Current failure is not license checkout.

Provided 29 initial input values were all accepted through `Fmu_RegisterInitialReal`, but initialization still failed.

Also tested:

- `Fmu_SetReal` instead of `Fmu_RegisterInitialReal`: still fails.
- `hasStopTime=false`: still fails.
- `stopTime=3600` matching `DefaultExperiment`: still fails.

Conclusion: Product FMU needs provider/modeler follow-up. It likely requires additional initialization parameters or a re-export with valid internal initial conditions.

## 7. Product FMU Initial Inputs Tested

These were provided and tested. All 29 variables were set successfully before initialization:

```text
Comp_CurFreq = 0
Fan_CurRPM = 0
reversing_valve_mode_flag = 0
MAIN_EEV_CurPulse = 10

idu_01_onoff = 1
idu_01_fan_mode = 4
idu_01_pulse = 10
idu_01_temp_air = 30
idu_01_RH_air = 50

idu_02_onoff = 1
idu_02_fan_mode = 4
idu_02_pulse = 10
idu_02_temp_air = 30
idu_02_RH_air = 50

idu_03_onoff = 1
idu_03_fan_mode = 4
idu_03_pulse = 10
idu_03_temp_air = 30
idu_03_RH_air = 50

idu_04_onoff = 1
idu_04_fan_mode = 4
idu_04_pulse = 10
idu_04_temp_air = 30
idu_04_RH_air = 50

idu_05_onoff = 1
idu_05_fan_mode = 4
idu_05_pulse = 10
idu_05_temp_air = 30
idu_05_RH_air = 50
```

Result remained:

```text
fmi2ExitInitializationMode failed
```

## 8. Build Verification

Latest successful C# build:

```powershell
dotnet build Assembly-CSharp.csproj
```

Result:

```text
Build succeeded
0 warnings
0 errors
```

Note: `dotnet build Assembly-CSharp.csproj --no-restore` initially failed because `Temp/obj/Assembly-CSharp/project.assets.json` was missing. Running normal `dotnet build` restored it and succeeded.

## 9. Recommended Next Steps

### Immediate Backup

Push current product branch:

```powershell
git push -u origin feature/product-fmu-platform
```

Then verify remote:

```powershell
git status --short --branch
git log --oneline --decorate -5
```

### Product FMU Resolution

Ask the Product FMU provider/modeler:

```text
MULTIV_FMU_WARPPER.fmu instantiates successfully but fails at fmi2ExitInitializationMode.
The 29 declared input variables were set successfully before initialization.
Please provide either:
1. a standalone FMI-checker-passing FMU, or
2. the complete list of fixed/tunable parameters and inputs required before fmi2ExitInitializationMode.
```

Also ask whether RH input expects:

```text
0.5 fraction
or
50 percent
```

The provided values used `50`, while the original modelDescription start values showed `0.2`. This mismatch did not explain the failure by itself, but it is worth confirming.

### Unity Product Test After New Product FMU

1. Replace `Assets/StreamingAssets/FMU/product/MULTIV_FMU_WARPPER.fmu`.
2. Let Unity import/generate `.meta` if needed.
3. Select `Assets/CoSimulationProfiles/MultiV_Product_Draft_Profile.asset`.
4. Run `Tools > Co-Simulation > Apply Production Harness To Open Scene`.
5. Confirm Hierarchy has 7 MultiV FMU objects.
6. Run `Tools > Co-Simulation > Probe MultiV Product Native Initialization`.
7. If probe passes, run `Tools > Co-Simulation > Run MultiV Product Draft Test (50s)`.

### R1-LBM Integration

Pending design/implementation item:

- R1 should be connected to the LBM airflow result.
- Current MultiV profile still uses `Simple_Chamber_R1` as a chamber FMU signal source for `T_air_suc`.
- Next implementation should decide how LBM sensor/output temperature maps into R1/product/controller signals.

Likely target:

```text
LBM outlet/room/inlet average temperature -> R1 suction or IDU_01 air input
Product IDU_01 discharge temperature -> LBM inlet boundary
```

Use the existing `AirflowLbmSignalAdapter` and connection map patterns rather than hardcoding inside individual FMU models.

## 10. Suggested First Message In New Workspace

Use this as the opening prompt in a new ChatGPT/Codex workspace:

```text
We are continuing the Unity LBM FMU integration project at:
D:\2_FFD\0_Unity\LBMUnity_1wayCST_FMU_Interagtion_PlatForm

Please read HANDOFF_LBMUNITY_FMU_INTEGRATION_PLATFORM.md first.
Current branch should be feature/product-fmu-platform.
The latest checkpoint commit is cebf531 Add MultiV product FMU platform checkpoint.
Do not revert unrelated .vscode or CaseStudyReports working-tree changes.
The current blocker is Product FMU MULTIV_FMU_WARPPER.fmu failing at fmi2ExitInitializationMode even after 29 initial inputs are set.
Controller CS FMU and Simple_Chamber_R1~R5 FMUs individually pass native Load/Init/Step.
Next task is to test a corrected Product FMU or implement the next R1-LBM mapping once Product initializes.
```
