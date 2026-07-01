# Unity LBM Airflow IDU

Unity 6000.1.4f1 project for LBM airflow and thermal simulation in an IDU-style room model.

## Modes

### LBM-only

The baseline solver runs the Unity LBM airflow and thermal simulation without FMU coupling. It keeps the existing MRT collision model, mass-flux corrected outlet behavior, result sampling, and metrics logging flow.

The LBM-only baseline is tagged as:

```text
v0.1.0-lbm-only
```

### FMU-LBM co-simulation

The co-simulation mode connects the LBM simulation to two simple FMUs:

- `Simple_CFMU`: controller FMU. Reads the LBM outlet/sensor temperature and outputs fan/cooling frequency `Hz`.
- `Simple_Plant`: plant FMU. Reads `Hz` and sensor temperature, then outputs discharge temperature `T_dis_Plant`.
- `AirflowLbmSignalAdapter`: maps LBM metrics to FMU inputs and applies the plant discharge temperature back to inlet boundary targets.

Default signal map:

```text
airflow.T_sensor -> Simple_CFMU.T_sensor
Simple_CFMU.Hz -> Simple_Plant.hz_Plant
airflow.T_sensor -> Simple_Plant.T_sensor_Plant
Simple_Plant.T_dis_Plant -> airflow.T_discharge
```

The FMU co-simulation release is tagged as:

```text
v0.2.0-fmu-cosim
```

## Required FMU and native plugin files

Native runtime plugin:

```text
Assets/Plugins/x86_64/FmuNativePlugin.dll
```

Controller FMU:

```text
Assets/StreamingAssets/FMU/controller/Simple_CFMU.fmu
Assets/StreamingAssets/FMU/controller/binaries/win32/Simple_CFMU.dll
Assets/StreamingAssets/FMU/controller/binaries/win64/Simple_CFMU.dll
```

Plant FMU:

```text
Assets/StreamingAssets/FMU/plant/Simple_Plant.fmu
Assets/StreamingAssets/FMU/plant/binaries/win32/Simple_Plant.dll
Assets/StreamingAssets/FMU/plant/binaries/win64/Simple_Plant.dll
```

The `.fmu` and `.dll` files are intentionally tracked in git for this project.

## NativePlugin runtime

`FmuCoSimulationModel` uses the native runtime first:

```text
FmuNativePlugin.dll -> NativeFmi2Runtime
```

If native initialization fails and `fallbackToMockOnNativeFailure` is enabled, it falls back to `MockFmi2Runtime`. Check `runtimeMode` in the Inspector or the co-sim CSV to confirm the active runtime:

```text
Simple_CFMU:Native; Simple_Plant:Native
```

## Logging

The co-simulation CSV logger writes to the same base folder as `SimulationMetricsFileLogger`.

Typical locations depend on the logger configuration:

```text
Application.persistentDataPath/LBMResults
<project-root>/LBMResults_test
custom folder from SimulationMetricsFileLogger
```

Files use the `co_simulation_yyyyMMdd_HHmmss.csv` naming pattern by default.

## Running production co-simulation

1. Open the Unity scene.
2. Select `Tools > Co-Simulation > Apply Production Harness To Open Scene`.
3. Set the total simulation time in `SimulationController > Simulation Stop Condition`.
4. Press Play.
5. Check `CoSimulationRunMonitor`, `CoSimulationOrchestrator`, and the co-sim CSV for `T_sensor`, `T_set`, `Hz`, `plantHz`, `T_dis`, `appliedInlet`, and `runtimeMode`.

The production harness uses the `SimulationController` stop condition. It does not own the total simulation time.

## Running the short integration test

Use this only as a quick connection check:

```text
Tools > Co-Simulation > Run Short Integration Test (50s)
```

This temporarily sets the `SimulationController` target time to 50 seconds and starts Play mode.

Legacy batch entry point:

```text
CoSimulationSmokeTestLauncher.Run50SecondTest()
```

The legacy name is kept for compatibility, but new workflows should use `CoSimulationSceneConfigurator` or the Unity menu.
