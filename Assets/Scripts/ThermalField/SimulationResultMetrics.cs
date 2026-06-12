using System;
using UnityEngine;

[Serializable]
public class SimulationResultMetrics
{
    [Header("Counts")]
    public int fluidCellCount;
    public int rackCellCount;
    public int inletSampleCount;
    public int outletSampleCount;

    [Header("Raw Simulation Units")]
    public float avgRoomTemperatureRaw;
    public float inletAverageTemperatureRaw;
    public float outletAverageTemperatureRaw;
    public float inletAverageSpeedLat;
    public float outletAverageSpeedLat;

    [Header("Extended Raw Metrics")]
    public float minRoomTemperatureRaw;
    public float maxRoomTemperatureRaw;
    public float roomTemperatureStdDevRaw;
    public float outletTemperatureStdDevRaw;

    public float avgDensity;
    public float densityStdDev;
    public float maxSpeedLat;

    [Header("Outlet Diagnostic")]
    public float outletInnerPlaneAverageTemperatureRaw;
    public float outletInnerPlaneAverageTemperatureDegC;
    public int outletInnerPlaneSampleCount;
    public bool hasValidOutletInnerPlaneAverage;

    [Header("Flow Diagnostic")]
    public float inletAverageNormalSpeedPhys;
    public float outletAverageNormalSpeedPhys;
    public float inletFlowRatePhysSigned;
    public float outletFlowRatePhysSigned;
    public float inletFlowRatePhysAbs;
    public float outletFlowRatePhysAbs;
    public float netFlowRatePhysSigned;
    public float relativeFlowImbalance;
    public bool hasValidFlowDiagnostic;

    [Header("Clamp Counters")]
    public uint thermalInletClampCount;
    public uint thermalOutletClampCount;
    public uint fluidInletClampCount;
    public uint fluidOutletClampCount;

    [Header("Display Units")]
    public float avgRoomTemperatureDegC;
    public float inletAverageTemperatureDegC;
    public float outletAverageTemperatureDegC;
    public float inletAverageSpeedPhys;
    public float outletAverageSpeedPhys;

    public float minRoomTemperatureDegC;
    public float maxRoomTemperatureDegC;
    public float roomTemperatureStdDevDegC;
    public float outletTemperatureStdDevDegC;
    public float maxSpeedPhys;

    [Header("Derived Indicators")]
    public float massResidualNormalized;
    public float averageKineticEnergyLat;

    [Header("Status")]
    public bool hasValidRoomAverage;
    public bool hasValidInletAverage;
    public bool hasValidOutletAverage;
    public string statusMessage;

    public void Clear()
    {
        fluidCellCount = 0;
        rackCellCount = 0;
        inletSampleCount = 0;
        outletSampleCount = 0;

        avgRoomTemperatureRaw = 0f;
        inletAverageTemperatureRaw = 0f;
        outletAverageTemperatureRaw = 0f;
        inletAverageSpeedLat = 0f;
        outletAverageSpeedLat = 0f;

        minRoomTemperatureRaw = 0f;
        maxRoomTemperatureRaw = 0f;
        roomTemperatureStdDevRaw = 0f;
        outletTemperatureStdDevRaw = 0f;

        avgDensity = 0f;
        densityStdDev = 0f;
        maxSpeedLat = 0f;

        outletInnerPlaneAverageTemperatureRaw = 0f;
        outletInnerPlaneAverageTemperatureDegC = 0f;
        outletInnerPlaneSampleCount = 0;
        hasValidOutletInnerPlaneAverage = false;

        inletAverageNormalSpeedPhys = 0f;
        outletAverageNormalSpeedPhys = 0f;
        inletFlowRatePhysSigned = 0f;
        outletFlowRatePhysSigned = 0f;
        inletFlowRatePhysAbs = 0f;
        outletFlowRatePhysAbs = 0f;
        netFlowRatePhysSigned = 0f;
        relativeFlowImbalance = 0f;
        hasValidFlowDiagnostic = false;

        thermalInletClampCount = 0u;
        thermalOutletClampCount = 0u;
        fluidInletClampCount = 0u;
        fluidOutletClampCount = 0u;

        avgRoomTemperatureDegC = 0f;
        inletAverageTemperatureDegC = 0f;
        outletAverageTemperatureDegC = 0f;
        inletAverageSpeedPhys = 0f;
        outletAverageSpeedPhys = 0f;

        minRoomTemperatureDegC = 0f;
        maxRoomTemperatureDegC = 0f;
        roomTemperatureStdDevDegC = 0f;
        outletTemperatureStdDevDegC = 0f;
        maxSpeedPhys = 0f;

        massResidualNormalized = 0f;
        averageKineticEnergyLat = 0f;

        hasValidRoomAverage = false;
        hasValidInletAverage = false;
        hasValidOutletAverage = false;
        statusMessage = "No sampled data.";
    }

    public string ToSummaryText()
    {
        return
            $"=== Result Metrics ===\n" +
            $"Room Avg Temp         : {(hasValidRoomAverage ? avgRoomTemperatureDegC.ToString("F3") : "-")} °C\n" +
            $"Room Min/Max Temp     : {(hasValidRoomAverage ? minRoomTemperatureDegC.ToString("F3") : "-")} / {(hasValidRoomAverage ? maxRoomTemperatureDegC.ToString("F3") : "-")} °C\n" +
            $"Room Temp StdDev      : {(hasValidRoomAverage ? roomTemperatureStdDevDegC.ToString("F4") : "-")} °C\n" +
            $"Inlet Avg Temp        : {(hasValidInletAverage ? inletAverageTemperatureDegC.ToString("F3") : "-")} °C\n" +
            $"Outlet Avg Temp       : {(hasValidOutletAverage ? outletAverageTemperatureDegC.ToString("F3") : "-")} °C\n" +
            $"Outlet Inner Temp     : {(hasValidOutletInnerPlaneAverage ? outletInnerPlaneAverageTemperatureDegC.ToString("F3") : "-")} °C\n" +
            $"Outlet Temp StdDev    : {(hasValidOutletAverage ? outletTemperatureStdDevDegC.ToString("F4") : "-")} °C\n" +
            $"Inlet Avg Speed       : {(hasValidInletAverage ? inletAverageSpeedPhys.ToString("F4") : "-")} m/s\n" +
            $"Outlet Avg Speed      : {(hasValidOutletAverage ? outletAverageSpeedPhys.ToString("F4") : "-")} m/s\n" +
            $"Inlet Normal Speed    : {(hasValidFlowDiagnostic ? inletAverageNormalSpeedPhys.ToString("F4") : "-")} m/s\n" +
            $"Outlet Normal Speed   : {(hasValidFlowDiagnostic ? outletAverageNormalSpeedPhys.ToString("F4") : "-")} m/s\n" +
            $"Inlet Flow Signed     : {(hasValidFlowDiagnostic ? inletFlowRatePhysSigned.ToString("F6") : "-")} m^3/s\n" +
            $"Outlet Flow Signed    : {(hasValidFlowDiagnostic ? outletFlowRatePhysSigned.ToString("F6") : "-")} m^3/s\n" +
            $"Inlet Flow Abs        : {(hasValidFlowDiagnostic ? inletFlowRatePhysAbs.ToString("F6") : "-")} m^3/s\n" +
            $"Outlet Flow Abs       : {(hasValidFlowDiagnostic ? outletFlowRatePhysAbs.ToString("F6") : "-")} m^3/s\n" +
            $"Net Flow Signed       : {(hasValidFlowDiagnostic ? netFlowRatePhysSigned.ToString("F6") : "-")} m^3/s\n" +
            $"Relative Imbalance    : {(hasValidFlowDiagnostic ? relativeFlowImbalance.ToString("F6") : "-")}\n" +
            $"Thermal Clamp In/Out  : {thermalInletClampCount} / {thermalOutletClampCount}\n" +
            $"Fluid Clamp In/Out    : {fluidInletClampCount} / {fluidOutletClampCount}\n" +
            $"Max Speed             : {maxSpeedPhys:F4} m/s\n" +
            $"Avg Density           : {avgDensity:F6}\n" +
            $"Density StdDev        : {densityStdDev:F6}\n" +
            $"Mass Residual         : {massResidualNormalized:E4}\n" +
            $"Avg Kin Energy Lat    : {averageKineticEnergyLat:E4}\n" +
            $"Fluid Cells           : {fluidCellCount}\n" +
            $"Rack Cells            : {rackCellCount}\n" +
            $"Inlet Samples         : {inletSampleCount}\n" +
            $"Outlet Samples        : {outletSampleCount}\n" +
            $"Outlet Inner Samples  : {outletInnerPlaneSampleCount}\n" +
            $"Status                : {statusMessage}";
    }
}