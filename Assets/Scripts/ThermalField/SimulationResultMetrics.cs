using System;
using UnityEngine;

[Serializable]
public class SimulationResultMetrics
{
    [Header("Case / Time")]
    public ulong stepCount;
    public float simulationTimeSeconds;
    public float dtPhys;
    public string preset;
    public string caseName;
    public string readbackMode;

    [Header("Counts")]
    public int fluidCellCount;
    public int rackCellCount;
    public int inletSampleCount;
    public int outletSampleCount;

    [Header("Thermal Result")]
    public float avgRoomTemperatureDegC;
    public float minRoomTemperatureDegC;
    public float maxRoomTemperatureDegC;
    public float roomTemperatureStdDevDegC;
    public float inletAverageTemperatureDegC;
    public float outletAverageTemperatureDegC;
    public float outletTemperatureStdDevDegC;
    public float outletInnerPlaneAverageTemperatureDegC;
    public int outletInnerPlaneSampleCount;
    public bool hasValidOutletInnerPlaneAverage;

    [Header("Flow Result")]
    public float inletAverageSpeedPhys;
    public float outletAverageSpeedPhys;
    public float inletAverageNormalSpeedPhys;
    public float outletAverageNormalSpeedPhys;
    public float inletFlowRatePhysSigned;
    public float outletFlowRatePhysSigned;
    public float inletFlowRatePhysAbs;
    public float outletFlowRatePhysAbs;
    public float inletFlowRateCMM;
    public float outletFlowRateCMM;
    public float netFlowRatePhysSigned;
    public float relativeFlowImbalance;
    public float maxSpeedPhys;
    public float maxMach;
    public bool hasValidVelocityDiagnostic;
    public bool hasValidFlowDiagnostic;

    [Header("Conservation / Stability")]
    public float avgDensity;
    public float densityStdDev;
    public float massResidualNormalized;
    public string massConservationStatus;
    public bool hasValidDensityDiagnostic;
    public float tauF;
    public float tauT;
    public float tauFRaw;
    public float tauTRaw;
    public float tauFluidMin;
    public float tauThermalMin;
    public float tauFluidMax;
    public float tauThermalMax;
    public bool tauFWasClamped;
    public bool tauTWasClamped;
    public float nuPhysTarget;
    public float alphaPhysTarget;
    public float nuPhysEffective;
    public float alphaPhysEffective;
    public float nuPhysEffectiveRatio;
    public float alphaPhysEffectiveRatio;
    public float maxMachLimit;
    public float reynoldsNumber;
    public float prandtlNumber;
    public string stabilityStatus;
    public string readinessStatus;

    [Header("Debug Counters")]
    public uint thermalInletClampCount;
    public uint thermalOutletClampCount;
    public uint fluidInletClampCount;
    public uint fluidOutletClampCount;

    [Header("Internal / Legacy")]
    public float avgRoomTemperatureRaw;
    public float inletAverageTemperatureRaw;
    public float outletAverageTemperatureRaw;
    public float inletAverageSpeedLat;
    public float outletAverageSpeedLat;
    public float minRoomTemperatureRaw;
    public float maxRoomTemperatureRaw;
    public float roomTemperatureStdDevRaw;
    public float outletTemperatureStdDevRaw;
    public float maxSpeedLat;
    public float outletInnerPlaneAverageTemperatureRaw;
    [Obsolete("Use maxMach instead. Will be removed after validation.")]
    public float machMax;
    [Obsolete("Use maxSpeedPhys or maxMach for user-facing results. Will be removed after validation.")]
    public float averageKineticEnergyLat;

    [Header("Status")]
    public bool hasValidRoomAverage;
    public bool hasValidInletAverage;
    public bool hasValidOutletAverage;
    public string statusMessage;

    public void Clear()
    {
        stepCount = 0;
        simulationTimeSeconds = 0f;
        dtPhys = 0f;
        preset = "";
        caseName = "";
        readbackMode = "";

        fluidCellCount = 0;
        rackCellCount = 0;
        inletSampleCount = 0;
        outletSampleCount = 0;

        avgRoomTemperatureDegC = 0f;
        minRoomTemperatureDegC = 0f;
        maxRoomTemperatureDegC = 0f;
        roomTemperatureStdDevDegC = 0f;
        inletAverageTemperatureDegC = 0f;
        outletAverageTemperatureDegC = 0f;
        outletTemperatureStdDevDegC = 0f;
        outletInnerPlaneAverageTemperatureDegC = 0f;
        outletInnerPlaneSampleCount = 0;
        hasValidOutletInnerPlaneAverage = false;

        inletAverageSpeedPhys = 0f;
        outletAverageSpeedPhys = 0f;
        inletAverageNormalSpeedPhys = 0f;
        outletAverageNormalSpeedPhys = 0f;
        inletFlowRatePhysSigned = 0f;
        outletFlowRatePhysSigned = 0f;
        inletFlowRatePhysAbs = 0f;
        outletFlowRatePhysAbs = 0f;
        inletFlowRateCMM = 0f;
        outletFlowRateCMM = 0f;
        netFlowRatePhysSigned = 0f;
        relativeFlowImbalance = 0f;
        maxSpeedPhys = 0f;
        maxMach = 0f;
        hasValidVelocityDiagnostic = false;
        hasValidFlowDiagnostic = false;

        avgDensity = 0f;
        densityStdDev = 0f;
        massResidualNormalized = 0f;
        massConservationStatus = "No data.";
        hasValidDensityDiagnostic = false;
        tauF = 0f;
        tauT = 0f;
        tauFRaw = 0f;
        tauTRaw = 0f;
        tauFluidMin = 0f;
        tauThermalMin = 0f;
        tauFluidMax = 0f;
        tauThermalMax = 0f;
        tauFWasClamped = false;
        tauTWasClamped = false;
        nuPhysTarget = 0f;
        alphaPhysTarget = 0f;
        nuPhysEffective = 0f;
        alphaPhysEffective = 0f;
        nuPhysEffectiveRatio = 0f;
        alphaPhysEffectiveRatio = 0f;
        maxMachLimit = 0f;
        reynoldsNumber = 0f;
        prandtlNumber = 0f;
        stabilityStatus = "";
        readinessStatus = "";

        thermalInletClampCount = 0u;
        thermalOutletClampCount = 0u;
        fluidInletClampCount = 0u;
        fluidOutletClampCount = 0u;

        avgRoomTemperatureRaw = 0f;
        inletAverageTemperatureRaw = 0f;
        outletAverageTemperatureRaw = 0f;
        inletAverageSpeedLat = 0f;
        outletAverageSpeedLat = 0f;
        minRoomTemperatureRaw = 0f;
        maxRoomTemperatureRaw = 0f;
        roomTemperatureStdDevRaw = 0f;
        outletTemperatureStdDevRaw = 0f;
        maxSpeedLat = 0f;
        outletInnerPlaneAverageTemperatureRaw = 0f;
#pragma warning disable 0618
        machMax = 0f;
        averageKineticEnergyLat = 0f;
#pragma warning restore 0618

        hasValidRoomAverage = false;
        hasValidInletAverage = false;
        hasValidOutletAverage = false;
        statusMessage = "No sampled data.";
    }

    public string ToSummaryText()
    {
        return
            $"=== Result Metrics ===\n" +
            $"[Case / Time]\n" +
            $"Step                  : {stepCount}\n" +
            $"Simulation Time       : {simulationTimeSeconds:F3} s\n" +
            $"dtPhys                : {dtPhys:E6} s\n" +
            $"Preset                : {SafeText(preset)}\n" +
            $"Case                  : {SafeText(caseName)}\n" +
            $"Readback Mode         : {SafeText(readbackMode)}\n" +
            $"[Thermal Result]\n" +
            $"Room Avg Temp         : {ValueOrDash(hasValidRoomAverage, avgRoomTemperatureDegC, "F3")} degC\n" +
            $"Room Min / Max Temp   : {ValueOrDash(hasValidRoomAverage, minRoomTemperatureDegC, "F3")} / {ValueOrDash(hasValidRoomAverage, maxRoomTemperatureDegC, "F3")} degC\n" +
            $"Room Temp StdDev      : {ValueOrDash(hasValidRoomAverage, roomTemperatureStdDevDegC, "F4")} degC\n" +
            $"Inlet Avg Temp        : {ValueOrDash(hasValidInletAverage, inletAverageTemperatureDegC, "F3")} degC\n" +
            $"Outlet Avg Temp       : {ValueOrDash(hasValidOutletAverage, outletAverageTemperatureDegC, "F3")} degC\n" +
            $"[Flow Result]\n" +
            $"Inlet Avg Speed       : {ValueOrDash(hasValidVelocityDiagnostic && hasValidInletAverage, inletAverageSpeedPhys, "F4")} m/s\n" +
            $"Outlet Avg Speed      : {ValueOrDash(hasValidVelocityDiagnostic && hasValidOutletAverage, outletAverageSpeedPhys, "F4")} m/s\n" +
            $"Inlet Flow            : {ValueOrDash(hasValidFlowDiagnostic, inletFlowRatePhysAbs, "F6")} m3/s ({ValueOrDash(hasValidFlowDiagnostic, inletFlowRateCMM, "F3")} CMM)\n" +
            $"Outlet Flow           : {ValueOrDash(hasValidFlowDiagnostic, outletFlowRatePhysAbs, "F6")} m3/s ({ValueOrDash(hasValidFlowDiagnostic, outletFlowRateCMM, "F3")} CMM)\n" +
            $"Relative Imbalance    : {ValueOrDash(hasValidFlowDiagnostic, relativeFlowImbalance, "F6")}\n" +
            $"Max Speed             : {ValueOrDash(hasValidVelocityDiagnostic, maxSpeedPhys, "F4")} m/s\n" +
            $"[Stability / Conservation]\n" +
            $"Max Mach              : {ValueOrDash(hasValidVelocityDiagnostic, maxMach, "F6")}\n" +
            $"Avg Density           : {ValueOrDash(hasValidDensityDiagnostic, avgDensity, "F6")}\n" +
            $"Density StdDev        : {ValueOrDash(hasValidDensityDiagnostic, densityStdDev, "F6")}\n" +
            $"Mass Residual         : {ValueOrDash(hasValidDensityDiagnostic, massResidualNormalized, "E4")}\n" +
            $"Mass Status           : {SafeText(massConservationStatus)}\n" +
            $"[Solver Diagnostics]\n" +
            $"tau_f raw -> clamped  : {tauFRaw:F6} -> {tauF:F6} (min={tauFluidMin:F4}, clamped={tauFWasClamped})\n" +
            $"tau_T raw -> clamped  : {tauTRaw:F6} -> {tauT:F6} (min={tauThermalMin:F4}, clamped={tauTWasClamped})\n" +
            $"nu target/effective   : {nuPhysTarget:E4} / {nuPhysEffective:E4} (x{nuPhysEffectiveRatio:F2})\n" +
            $"alpha target/effect   : {alphaPhysTarget:E4} / {alphaPhysEffective:E4} (x{alphaPhysEffectiveRatio:F2})\n" +
            $"Max Mach Limit        : {maxMachLimit:F4}\n" +
            $"Re / Pr               : {reynoldsNumber:E4} / {prandtlNumber:F4}\n" +
            $"Stability Status      : {SafeText(stabilityStatus)}\n" +
            $"Readiness Status      : {SafeText(readinessStatus)}\n" +
            $"[Debug Counters]\n" +
            $"Thermal Clamp In/Out  : {thermalInletClampCount} / {thermalOutletClampCount}\n" +
            $"Fluid Clamp In/Out    : {fluidInletClampCount} / {fluidOutletClampCount}\n" +
            $"Status                : {SafeText(statusMessage)}";
    }

    private static string ValueOrDash(bool valid, float value, string format)
    {
        return valid ? value.ToString(format) : "-";
    }

    private static string SafeText(string value)
    {
        return string.IsNullOrEmpty(value) ? "-" : value;
    }
}
