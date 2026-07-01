# Release Notes

## v0.2.0-fmu-cosim

Adds Simple FMU-LBM co-simulation coupling on top of the LBM-only solver.

### Added

- Native FMI runtime bridge through `Assets/Plugins/x86_64/FmuNativePlugin.dll`.
- FMU model wrapper with native-first execution and mock fallback.
- Controller and plant FMU assets under `Assets/StreamingAssets/FMU`.
- Default co-sim signal map for:
  - LBM outlet/sensor temperature to controller and plant.
  - Controller `Hz` output to plant `hz_Plant`.
  - Plant `T_dis_Plant` output back to LBM inlet temperature targets.
- `AirflowLbmSignalAdapter` for reading LBM metrics and applying FMU discharge temperature to inlet boxes.
- `CoSimulationOrchestrator` for scheduled FMU-LBM signal transfer.
- `CoSimulationCsvLogger` aligned to `SimulationMetricsFileLogger` output location.
- `CoSimulationRunMonitor` for production run status and completion summaries.
- Editor configuration menu:
  - `Tools > Co-Simulation > Apply Production Harness To Open Scene`
  - `Tools > Co-Simulation > Run Short Integration Test (50s)`

### Changed

- Smoke-test-only code was converted into production-oriented co-simulation setup and monitoring.
- The short 50 second test is now an explicit integration-test command, not the main runtime behavior.
- Total simulation time remains controlled by `SimulationController`.
- FMU parameter overrides can be edited per FMU object in the Inspector and applied at runtime for tunable parameters.
- `Simple_Plant` fallback behavior was updated for the latest FMU parameters:
  - `coolingGain`
  - `deltaT_min`
  - `deltaT_max`
  - `tau`
  - `T_dis_abs_min`
  - `T_dis_abs_max`

### Logging

- LBM metrics CSV and co-sim CSV share the same base output folder.
- Co-sim CSV includes controller setpoint and plant `Hz` input diagnostics.
- Runtime mode is logged so Native vs MockFallback execution is visible.

### Validation

Validated with local C# builds:

```text
dotnet build Assembly-CSharp.csproj --no-restore
dotnet build Assembly-CSharp-Editor.csproj --no-restore
```

Both builds completed with 0 warnings and 0 errors.

## v0.1.0-lbm-only

Baseline LBM-only Unity solver state before FMU coupling.

This version keeps the original solver workflow without controller/plant FMU integration.
