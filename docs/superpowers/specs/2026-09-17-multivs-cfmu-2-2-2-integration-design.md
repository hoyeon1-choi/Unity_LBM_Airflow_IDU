# MultiVS CFMU 2.2.2 Integration Design

## Goal

Replace the installed MultiVS controller FMU with the supplied 2.2.2 Co-Simulation FMU and update the existing LBM-FMU connection contract without changing the established sequential asynchronous execution model.

## Verified interface differences

- Installed FMU: SHA-256 `B2ED1375603D3C988EBF4387600DA34085CC15101780BD95E6217F3D4E60881F`, 101 variables.
- Supplied FMU: SHA-256 `C42DC0AE834B8C4F089234DEEA34CC4D470C71E4A409570DA9B689014C724B4E`, model version 2.2.2, 114 variables.
- New controller command outputs include `Multi_V_S.Comp__TarFreq` and `Multi_V_S.Fan1__TarRPM`.
- The main EEV command is case-sensitive and is now `Multi_V_S.MAIN_EEV__TarPulse`.
- Current-value outputs `Multi_V_S.Comp__CurFreq` and `Multi_V_S.Fan1__CurRPM` remain available for monitoring.
- New valve, pressure-target, error, and indoor-unit compressor outputs are not required by the current product FMU contract.

## Connection contract

The product FMU will receive controller commands as follows:

| Controller output | Product input |
| --- | --- |
| `Multi_V_S.Comp__TarFreq` | `Comp_CurFreq` |
| `Multi_V_S.Fan1__TarRPM` | `Fan_CurRPM` |
| `Multi_V_S.MAIN_EEV__TarPulse` | `MAIN_EEV_CurPulse` |

`Multi_V_S.4Way_Valve__OnOff` and `Multi_V_S.HotGas_Valve__OnOff` will not be activated in this change. The existing cooling-mode initialization of `reversing_valve_mode_flag=0` remains unchanged.

## Files and behavior

- Replace `Assets/StreamingAssets/FMU/controller/Multi_V_S__Set_CFMU.fmu` with the supplied Co-Simulation archive.
- Update `CoSimulationProfile.cs` so newly created runtime/default profiles use the target command outputs.
- Update `MultiV_Product_Draft_Profile.asset` so the reusable profile asset uses the same contract.
- Update only the matching connection and debug signal strings inside `LBM_1wayCST.unity`; preserve all unrelated user scene changes.
- Update controller/product diagnostic scripts that query or transfer the old output names.
- Keep model IDs, initialization values, one-step-delay policy, sequential asynchronous stepping, LBM pause behavior, timeouts, and failure-state handling unchanged.

## Diagnostics

- Primary controller output/debug values will use target frequency and target fan RPM.
- Current frequency and RPM remain available in the FMU and may be inspected independently, but will not drive the product FMU.
- Static interface validation must confirm every active controller reference exists with the expected Real input/output causality in the supplied `modelDescription.xml`.

## Verification

1. Demonstrate that the pre-change profile fails validation against the supplied FMU because of the old main EEV case and old drive-output selection.
2. Verify the installed FMU hash matches the supplied archive.
3. Validate controller and product connection endpoints, types, and causalities from both FMU model descriptions.
4. Build `Assembly-CSharp.csproj` and `Assembly-CSharp-Editor.csproj` without restore.
5. Run the focused controller feedback and Controller-to-Product diagnostic scripts when the local native host/runtime is available.
6. Inspect the final diff to ensure unrelated scene edits and Mass-Flux/LBM behavior were not changed.

## Known risks

- The supplied FMU has a new GUID and new bundled native binaries, so runtime behavior must be validated rather than inferred from metadata alone.
- The large scene file already contains user changes. Edits must be limited to exact signal-name replacements.
- Inverter feedback inputs still have no verified matching outputs in the current product FMU, so no synthetic mapping will be added.
