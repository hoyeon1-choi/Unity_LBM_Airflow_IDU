# MultiVS CFMU 2.2.2 Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Install the supplied MultiVS controller CFMU 2.2.2 and make the existing Controller-to-Product integration use target frequency, target fan RPM, and the case-correct main EEV command.

**Architecture:** Retain the existing `modelId + variableName` profile, sequential asynchronous FMU execution, and one-step-delay connection behavior. Change only the controller archive and its explicit signal references in the profile source, reusable asset, active scene, and focused diagnostics; validate all endpoints from FMU metadata.

**Tech Stack:** Unity 6000.1.4f1, C#/.NET, Unity YAML assets, FMI 2.0 Co-Simulation FMU archives, PowerShell diagnostics, Windows 11 x64.

**Spec:** `docs/superpowers/specs/2026-09-17-multivs-cfmu-2-2-2-integration-design.md`

## Global Constraints

- Preserve the existing sequential asynchronous FMU initialization and step order.
- Preserve LBM pause/resume behavior during FMU work and existing failure-state handling.
- Do not alter LBM geometry, Mass-Flux Corrected Outlet, MRT, scaling, or case-study settings.
- Preserve all unrelated changes already present in `Assets/Scenes/LBMScenes/LBM_1wayCST.unity`.
- Do not add mappings for inverter feedback, four-way valve, or hot-gas valve without a verified product-FMU source or an explicit request.
- Treat FMU variable names as case-sensitive.

## File Structure

- `Assets/StreamingAssets/FMU/controller/Multi_V_S__Set_CFMU.fmu`: installed controller runtime artifact.
- `Assets/Scripts/CoSimulation/CoSimulationProfile.cs`: factory defaults for new runtime and asset profiles.
- `Assets/CoSimulationProfiles/MultiV_Product_Draft_Profile.asset`: reusable serialized MultiV profile.
- `Assets/Scenes/LBMScenes/LBM_1wayCST.unity`: active scene's embedded runtime profile and debug references.
- `FmuHost/Diagnostics/Test-ControllerProductPlantChain.ps1`: focused Controller-to-Product transfer test.
- `FmuHost/Diagnostics/Test-ControllerFeedbackInputs.ps1`: controller lifecycle and feedback-input diagnostic.

---

### Task 1: Establish the failing interface baseline

**Files:**
- Read: `CFMU_Builder/260331__Multi_V_S__Set__2.2.2__Release/CFMU_FMI2_Release/CoSimulation/Multi_V_S__Set_CFMU.fmu`
- Read: `Assets/CoSimulationProfiles/MultiV_Product_Draft_Profile.asset`
- Read: `Assets/Scenes/LBMScenes/LBM_1wayCST.unity`

**Interfaces:**
- Consumes: supplied CFMU `modelDescription.xml` and current serialized controller references.
- Produces: recorded RED evidence showing the old main EEV name is absent and the requested target outputs are not yet selected.

- [ ] **Step 1: Run the case-sensitive contract check before editing**

Load the supplied archive, build an ordinal variable-name lookup, and test these required outputs:

```text
Multi_V_S.Comp__TarFreq
Multi_V_S.Fan1__TarRPM
Multi_V_S.MAIN_EEV__TarPulse
```

Collect active controller source names from the reusable profile and scene in the same command.

- [ ] **Step 2: Verify the check fails for the existing contract**

Expected RED evidence:

```text
Multi_V_S.Main_EEV__TarPulse is missing from supplied FMU
Profile still selects Multi_V_S.Comp__CurFreq
Profile still selects Multi_V_S.Fan1__CurRPM
```

The command must exit non-zero until the serialized references are updated.

---

### Task 2: Install the supplied CFMU artifact

**Files:**
- Modify: `Assets/StreamingAssets/FMU/controller/Multi_V_S__Set_CFMU.fmu`

**Interfaces:**
- Consumes: supplied archive SHA-256 `C42DC0AE834B8C4F089234DEEA34CC4D470C71E4A409570DA9B689014C724B4E`.
- Produces: the controller FMU path already consumed by `FmuCoSimulationModel`, now containing model version 2.2.2 and GUID `{8754A561-0453-4AC1-ADF8-CB644C3AFCEB}`.

- [ ] **Step 1: Replace the installed binary**

Copy exactly the supplied Co-Simulation archive to `Assets/StreamingAssets/FMU/controller/Multi_V_S__Set_CFMU.fmu`. Do not modify the `.meta` file so the Unity asset GUID remains stable.

- [ ] **Step 2: Verify the installed artifact**

Run:

```powershell
Get-FileHash -Algorithm SHA256 `
  'CFMU_Builder/260331__Multi_V_S__Set__2.2.2__Release/CFMU_FMI2_Release/CoSimulation/Multi_V_S__Set_CFMU.fmu', `
  'Assets/StreamingAssets/FMU/controller/Multi_V_S__Set_CFMU.fmu'
```

Expected: both hashes equal `C42DC0AE834B8C4F089234DEEA34CC4D470C71E4A409570DA9B689014C724B4E`.

---

### Task 3: Update the Unity connection contract

**Files:**
- Modify: `Assets/Scripts/CoSimulation/CoSimulationProfile.cs:263-276`
- Modify: `Assets/CoSimulationProfiles/MultiV_Product_Draft_Profile.asset:1327-1454`
- Modify: `Assets/Scenes/LBMScenes/LBM_1wayCST.unity:9341-11705`

**Interfaces:**
- Consumes: controller Real outputs and existing product Real inputs.
- Produces: active mappings `Comp__TarFreq -> Comp_CurFreq`, `Fan1__TarRPM -> Fan_CurRPM`, and `MAIN_EEV__TarPulse -> MAIN_EEV_CurPulse`.

- [ ] **Step 1: Update profile factory defaults**

Apply these exact substitutions in `CoSimulationProfile.cs`:

```text
Multi_V_S.Comp__CurFreq       -> Multi_V_S.Comp__TarFreq
Multi_V_S.Fan1__CurRPM        -> Multi_V_S.Fan1__TarRPM
Multi_V_S.Main_EEV__TarPulse  -> Multi_V_S.MAIN_EEV__TarPulse
CompCurFreq                   -> CompTarFreq
FanCurRPM                     -> FanTarRPM
```

Use target frequency for `controllerOutputSignal`. Do not change product input names.

- [ ] **Step 2: Update the reusable profile asset**

Apply the same variable substitutions to active connections, `controllerOutputSignal`, and debug signals in `MultiV_Product_Draft_Profile.asset`. Leave the disabled four-way-valve connection disabled.

- [ ] **Step 3: Update the active scene in place**

Apply only the three variable-name substitutions and the two debug labels to `LBM_1wayCST.unity`. Change `debugHzVariableName` to `Multi_V_S.Comp__TarFreq`. Do not reserialize or regenerate the scene.

- [ ] **Step 4: Run the contract check again**

Expected GREEN evidence:

```text
Controller required outputs: 3/3 found, type=Real, causality=output
Product required inputs: 3/3 found, type=Real, causality=input
Old active controller drive references: 0
```

---

### Task 4: Update focused runtime diagnostics

**Files:**
- Modify: `FmuHost/Diagnostics/Test-ControllerProductPlantChain.ps1:267-269`
- Modify: `FmuHost/Diagnostics/Test-ControllerFeedbackInputs.ps1:104-106`
- Inspect: `FmuHost/Diagnostics/Test-ControllerFeedbackInputs.ps1:244`

**Interfaces:**
- Consumes: the same controller command outputs as the Unity profile.
- Produces: diagnostics that query the current installed CFMU contract rather than legacy names.

- [ ] **Step 1: Update Controller-to-Product transfer reads**

Use these exact reads:

```powershell
$compHz = Get-Real $controller "Multi_V_S.Comp__TarFreq"
$fanRpm = Get-Real $controller "Multi_V_S.Fan1__TarRPM"
$mainEev = Get-Real $controller "Multi_V_S.MAIN_EEV__TarPulse"
```

Keep the product setters `Comp_CurFreq`, `Fan_CurRPM`, and `MAIN_EEV_CurPulse` unchanged.

- [ ] **Step 2: Update controller feedback expected outputs**

Set the required output list to the same three case-sensitive names. Retain the existing explicit `Comp__TarFreq` probe and remove no feedback-input checks.

- [ ] **Step 3: Parse both scripts**

Run the PowerShell parser on both files:

```powershell
[System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
```

Expected: `$errors.Count -eq 0` for each script.

---

### Task 5: Verify build, metadata, and focused runtime behavior

**Files:**
- Verify: all modified files from Tasks 2-4.

**Interfaces:**
- Consumes: installed FMUs, updated profile contract, existing `FmuHost` executable and native runtimes.
- Produces: fresh build and diagnostic evidence, or an explicit environment blocker with captured output.

- [ ] **Step 1: Validate every active FMU endpoint**

Parse controller and product model descriptions. For every enabled connection involving `Multi_V_S__Set_CFMU` or `MULTIV_FMU_WARPPER`, require source outputs and target inputs to exist with `Real` type and correct causality. Exclude `profile` and `airflow` endpoints from FMU metadata lookup.

- [ ] **Step 2: Build Unity C# projects**

Run:

```powershell
dotnet build Assembly-CSharp.csproj --no-restore
dotnet build Assembly-CSharp-Editor.csproj --no-restore
```

Expected: exit code 0 for both commands. Report warnings separately from errors.

- [ ] **Step 3: Run focused controller feedback diagnostics**

Run `Test-ControllerFeedbackInputs.ps1` with its existing parameters and capture its result file or log. Do not invent timeout values or alter execution order to make the test pass.

- [ ] **Step 4: Run the Controller-to-Product chain diagnostic**

Run `Test-ControllerProductPlantChain.ps1` using its documented/default short settings. Confirm controller initialization, sequential step completion, target-output reads, and product-input writes.

- [ ] **Step 5: Review the final diff and repository state**

Run:

```powershell
git diff --check
git diff --stat
git status --short --branch
```

Confirm no LBM solver, geometry, outlet, collision, scaling, or asynchronous ordering code changed. Do not commit the pre-existing scene changes as a whole; report the implementation diff and leave integration commits to the user unless a safe isolated staged patch is possible.
