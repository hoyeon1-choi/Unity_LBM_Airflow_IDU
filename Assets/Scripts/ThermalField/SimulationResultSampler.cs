using System.Collections;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public class SimulationResultSampler : MonoBehaviour
{
    public enum ReadbackMode
    {
        FullMetrics = 0,
        TemperatureOnly = 1
    }

    [Header("References")]
    [SerializeField] private SimulationController simulationController;

    [Header("Sampling")]
    [SerializeField] private bool enableSampling = true;
    [SerializeField] private float sampleIntervalSeconds = 2.0f;
    [SerializeField] private bool logMetricsToConsole = false;
    [SerializeField] private ReadbackMode readbackMode = ReadbackMode.FullMetrics;

    [Header("Read-Only Metrics")]
    [SerializeField, ReadOnly] private SimulationResultMetrics latestMetrics = new SimulationResultMetrics();
    [SerializeField, ReadOnly] private float nextSampleSimTimeSeconds = 0.0f;
    [SerializeField, ReadOnly] private float lastRequestedSampleSimTimeSeconds = -1.0f;

    [Header("Readback Debug")]
    [SerializeField, ReadOnly] private string activeReadbackMode = "";

    private Coroutine _samplingRoutine;
    private bool _requestInFlight = false;

    public SimulationResultMetrics LatestMetrics => latestMetrics;

    private void Awake()
    {
        if (simulationController == null)
            simulationController = FindFirstObjectByType<SimulationController>();

        latestMetrics.Clear();
        ResetSamplingSchedule();
    }

    private void OnEnable()
    {
        ResetSamplingSchedule();

        if (_samplingRoutine == null)
            _samplingRoutine = StartCoroutine(SamplingLoop());
    }

    private void OnDisable()
    {
        if (_samplingRoutine != null)
        {
            StopCoroutine(_samplingRoutine);
            _samplingRoutine = null;
        }
    }

    public void ResetSamplingSchedule()
    {
        lastRequestedSampleSimTimeSeconds = -1.0f;

        if (simulationController != null)
            nextSampleSimTimeSeconds = simulationController.SimulatedTimeSeconds + Mathf.Max(sampleIntervalSeconds, 1e-6f);
        else
            nextSampleSimTimeSeconds = Mathf.Max(sampleIntervalSeconds, 1e-6f);
    }

    private IEnumerator SamplingLoop()
    {
        while (true)
        {
            if (enableSampling)
                TryRequestSamplingBySimulatedTime();

            yield return null;
        }
    }

    public void ForceSampleNow()
    {
        TryRequestSampling(force: true);
    }

    private void TryRequestSamplingBySimulatedTime()
    {
        if (simulationController == null)
        {
            latestMetrics.statusMessage = "SimulationController is null.";
            return;
        }

        float simTime = simulationController.SimulatedTimeSeconds;
        if (simTime + 1e-6f < nextSampleSimTimeSeconds)
            return;

        if (TryRequestSampling())
        {
            lastRequestedSampleSimTimeSeconds = simTime;
            nextSampleSimTimeSeconds += Mathf.Max(sampleIntervalSeconds, 1e-6f);
        }
    }

    private bool TryRequestSampling(bool force = false)
    {
        activeReadbackMode = readbackMode.ToString();

        if (_requestInFlight)
            return false;

        if (simulationController == null || simulationController.LBMSolver == null)
        {
            latestMetrics.statusMessage = "SimulationController or solver is null.";
            return false;
        }

        if (!force && !simulationController.IsSolverReadyForReadback)
        {
            latestMetrics.statusMessage = "Solver is not ready for readback.";
            return false;
        }

        var solver = simulationController.LBMSolver;

        if (solver.TemperatureBuffer == null ||
            solver.FieldBuffer == null ||
            solver.DebugThermalClampCounterBuffer == null)
        {
            latestMetrics.statusMessage = "Solver buffers are not ready.";
            return false;
        }

        if (readbackMode == ReadbackMode.FullMetrics && solver.VelocityRhoBuffer == null)
        {
            latestMetrics.statusMessage = "VelocityRhoBuffer is not ready.";
            return false;
        }

        _requestInFlight = true;

        if (readbackMode == ReadbackMode.FullMetrics)
        {
            if (logMetricsToConsole)
                Debug.Log("[SimulationResultSampler] Requesting FullMetrics readback.");

            StartCoroutine(RequestFullMetricsCoroutine(
                solver.TemperatureBuffer,
                solver.VelocityRhoBuffer,
                solver.FieldBuffer,
                solver.DebugThermalClampCounterBuffer));
        }
        else
        {
            if (logMetricsToConsole)
                Debug.Log("[SimulationResultSampler] Requesting TemperatureOnly readback.");

            StartCoroutine(RequestTemperatureOnlyCoroutine(
                solver.TemperatureBuffer,
                solver.FieldBuffer,
                solver.DebugThermalClampCounterBuffer));
        }

        return true;
    }

    private IEnumerator RequestFullMetricsCoroutine(
        GraphicsBuffer temperatureBuffer,
        GraphicsBuffer velocityRhoBuffer,
        GraphicsBuffer fieldBuffer,
        GraphicsBuffer debugClampBuffer)
    {
        var tempRequest = AsyncGPUReadback.Request(temperatureBuffer);
        var velRequest = AsyncGPUReadback.Request(velocityRhoBuffer);
        var fieldRequest = AsyncGPUReadback.Request(fieldBuffer);
        var clampRequest = AsyncGPUReadback.Request(debugClampBuffer);

        yield return new WaitUntil(() =>
            tempRequest.done && velRequest.done && fieldRequest.done && clampRequest.done);

        _requestInFlight = false;

        if (tempRequest.hasError || velRequest.hasError || fieldRequest.hasError || clampRequest.hasError)
        {
            latestMetrics.statusMessage =
                $"AsyncGPUReadback failed: " +
                $"temp={tempRequest.hasError}, " +
                $"vel={velRequest.hasError}, " +
                $"field={fieldRequest.hasError}, " +
                $"clamp={clampRequest.hasError}";

            yield break;
        }

        NativeArray<float> tempData = tempRequest.GetData<float>();
        NativeArray<float4> velData = velRequest.GetData<float4>();
        NativeArray<uint> fieldData = fieldRequest.GetData<uint>();
        NativeArray<uint> clampData = clampRequest.GetData<uint>();

        ComputeMetrics(tempData, velData, fieldData, clampData);

        if (logMetricsToConsole)
        {
            Debug.Log(latestMetrics.ToSummaryText());

            if (clampData.Length >= 4)
            {
                Debug.Log(
                    $"[ClampCounter] thermal_in={clampData[0]}, thermal_out={clampData[1]}, " +
                    $"fluid_in={clampData[2]}, fluid_out={clampData[3]}");
            }
        }
    }

    private IEnumerator RequestTemperatureOnlyCoroutine(
        GraphicsBuffer temperatureBuffer,
        GraphicsBuffer fieldBuffer,
        GraphicsBuffer debugClampBuffer)
    {
        var tempRequest = AsyncGPUReadback.Request(temperatureBuffer);
        var fieldRequest = AsyncGPUReadback.Request(fieldBuffer);
        var clampRequest = AsyncGPUReadback.Request(debugClampBuffer);

        yield return new WaitUntil(() =>
            tempRequest.done && fieldRequest.done && clampRequest.done);

        _requestInFlight = false;

        if (tempRequest.hasError || fieldRequest.hasError || clampRequest.hasError)
        {
            latestMetrics.statusMessage =
                $"AsyncGPUReadback failed: " +
                $"temp={tempRequest.hasError}, " +
                $"field={fieldRequest.hasError}, " +
                $"clamp={clampRequest.hasError}";

            yield break;
        }

        NativeArray<float> tempData = tempRequest.GetData<float>();
        NativeArray<uint> fieldData = fieldRequest.GetData<uint>();
        NativeArray<uint> clampData = clampRequest.GetData<uint>();

        ComputeTemperatureOnlyMetrics(tempData, fieldData, clampData);

        if (logMetricsToConsole)
        {
            Debug.Log(latestMetrics.ToSummaryText());

            if (clampData.Length >= 4)
            {
                Debug.Log(
                    $"[ClampCounter] thermal_in={clampData[0]}, thermal_out={clampData[1]}, " +
                    $"fluid_in={clampData[2]}, fluid_out={clampData[3]}");
            }
        }
    }

    private static uint ClampToInterior(int value, uint maxExclusive)
    {
        if (maxExclusive <= 2)
            return 0;

        return (uint)Mathf.Clamp(value, 1, (int)maxExclusive - 2);
    }

    private static bool TryGetInnerPlaneCell(
        LBMZouHeBox box,
        uint x, uint y, uint z,
        uint nx, uint ny, uint nz,
        out uint ix, out uint iy, out uint iz)
    {
        ix = x;
        iy = y;
        iz = z;

        int sign = (box.NormalSign == LBMZouHeBox.Sign.Positive) ? +1 : -1;

        switch (box.NormalAxis)
        {
            case LBMZouHeBox.Axis.X:
                ix = ClampToInterior((int)x - sign, nx);
                return true;
            case LBMZouHeBox.Axis.Y:
                iy = ClampToInterior((int)y - sign, ny);
                return true;
            case LBMZouHeBox.Axis.Z:
                iz = ClampToInterior((int)z - sign, nz);
                return true;
            default:
                return false;
        }
    }

    private static float3 GetPatchNormal(LBMZouHeBox box)
    {
        float sign = (box.NormalSign == LBMZouHeBox.Sign.Positive) ? 1f : -1f;

        switch (box.NormalAxis)
        {
            case LBMZouHeBox.Axis.X:
                return new float3(sign, 0f, 0f);
            case LBMZouHeBox.Axis.Y:
                return new float3(0f, sign, 0f);
            default:
                return new float3(0f, 0f, sign);
        }
    }

    private void ComputeMetrics(
        NativeArray<float> tempData,
        NativeArray<float4> velData,
        NativeArray<uint> fieldData,
        NativeArray<uint> clampData)
    {
        latestMetrics.Clear();

        if (simulationController == null)
        {
            latestMetrics.statusMessage = "SimulationController is missing.";
            return;
        }

        uint nx = simulationController.Nx;
        uint ny = simulationController.Ny;
        uint nz = simulationController.Nz;

        if (nx == 0 || ny == 0 || nz == 0)
        {
            latestMetrics.statusMessage = "Invalid domain resolution.";
            return;
        }

        if (tempData.Length == 0 || velData.Length == 0 || fieldData.Length == 0)
        {
            latestMetrics.statusMessage = "Readback data is empty.";
            return;
        }

        const uint FLUID = 0u;
        const uint RACK = 3u;

        double roomTempSum = 0.0;
        double roomTempSqSum = 0.0;
        double rhoSum = 0.0;
        double rhoSqSum = 0.0;
        double kineticSum = 0.0;

        float minRoomTemp = float.PositiveInfinity;
        float maxRoomTemp = float.NegativeInfinity;
        float maxSpeedLat = 0f;

        int roomCount = 0;
        int rackCount = 0;

        for (int i = 0; i < fieldData.Length; i++)
        {
            uint flag = fieldData[i];
            if (flag != FLUID && flag != RACK)
                continue;

            float t = tempData[i];
            float4 vr = velData[i];
            float rho = vr.w;
            float speedLat = math.length(vr.xyz);

            roomTempSum += t;
            roomTempSqSum += (double)t * t;
            rhoSum += rho;
            rhoSqSum += (double)rho * rho;
            kineticSum += 0.5 * rho * speedLat * speedLat;

            minRoomTemp = math.min(minRoomTemp, t);
            maxRoomTemp = math.max(maxRoomTemp, t);
            maxSpeedLat = math.max(maxSpeedLat, speedLat);

            if (flag == RACK)
                rackCount++;

            roomCount++;
        }

        latestMetrics.fluidCellCount = roomCount;
        latestMetrics.rackCellCount = rackCount;
        latestMetrics.hasValidRoomAverage = roomCount > 0;

        if (roomCount > 0)
        {
            float avgTempRaw = (float)(roomTempSum / roomCount);
            float tempVarRaw = (float)math.max(
                0.0,
                (roomTempSqSum / roomCount) - ((roomTempSum / roomCount) * (roomTempSum / roomCount)));

            float avgRho = (float)(rhoSum / roomCount);
            float rhoVar = (float)math.max(
                0.0,
                (rhoSqSum / roomCount) - ((rhoSum / roomCount) * (rhoSum / roomCount)));

            latestMetrics.avgRoomTemperatureRaw = avgTempRaw;
            latestMetrics.minRoomTemperatureRaw = minRoomTemp;
            latestMetrics.maxRoomTemperatureRaw = maxRoomTemp;
            latestMetrics.roomTemperatureStdDevRaw = Mathf.Sqrt(tempVarRaw);

            latestMetrics.avgDensity = avgRho;
            latestMetrics.densityStdDev = Mathf.Sqrt(rhoVar);
            latestMetrics.massResidualNormalized = Mathf.Abs(avgRho - 1.0f);
            latestMetrics.averageKineticEnergyLat = (float)(kineticSum / roomCount);
            latestMetrics.maxSpeedLat = maxSpeedLat;

            latestMetrics.avgRoomTemperatureDegC =
                simulationController.TemperatureLBMToDegC(avgTempRaw);
            latestMetrics.minRoomTemperatureDegC =
                simulationController.TemperatureLBMToDegC(minRoomTemp);
            latestMetrics.maxRoomTemperatureDegC =
                simulationController.TemperatureLBMToDegC(maxRoomTemp);

            float tempScaleDegC =
                simulationController.TemperatureLBMToDegC(1f) -
                simulationController.TemperatureLBMToDegC(0f);

            latestMetrics.roomTemperatureStdDevDegC =
                latestMetrics.roomTemperatureStdDevRaw * tempScaleDegC;

            latestMetrics.maxSpeedPhys = LatticeSpeedToPhysical(maxSpeedLat);
        }

        LBMZouHeBox[] boxes = FindObjectsByType<LBMZouHeBox>(FindObjectsSortMode.InstanceID);

        double inletTempSum = 0.0;
        double inletSpeedSumLat = 0.0;
        int inletCount = 0;

        double outletTempSum = 0.0;
        double outletTempSqSum = 0.0;
        double outletSpeedSumLat = 0.0;
        int outletCount = 0;

        double outletInnerTempSum = 0.0;
        int outletInnerCount = 0;

        double inletNormalSpeedPhysSum = 0.0;
        double outletNormalSpeedPhysSum = 0.0;
        double inletFlowRateSignedSum = 0.0;
        double outletFlowRateSignedSum = 0.0;

        float dx = simulationController.CellSize;
        float cellArea = dx * dx;
        float latToPhys = (simulationController.DtPhys > 1e-8f)
            ? dx / simulationController.DtPhys
            : 0f;

        foreach (var box in boxes)
        {
            if (box == null || !box.Power)
                continue;

            uint3 min = box.MinIdx;
            uint3 max = box.MaxIdx;
            float3 normal = GetPatchNormal(box);

            for (uint z = min.z; z <= max.z; z++)
            {
                for (uint y = min.y; y <= max.y; y++)
                {
                    for (uint x = min.x; x <= max.x; x++)
                    {
                        int idx = (int)(x + y * nx + z * nx * ny);
                        if (idx < 0 || idx >= tempData.Length)
                            continue;

                        float tRaw = tempData[idx];
                        float3 uLat = velData[idx].xyz;
                        float speedLat = math.length(uLat);

                        float3 uPhys = uLat * latToPhys;
                        float normalSpeedPhys = math.dot(uPhys, normal);
                        float cellFlowSigned = normalSpeedPhys * cellArea;

                        if (box.PatchKind == LBMZouHeBox.Kind.Inlet)
                        {
                            inletTempSum += tRaw;
                            inletSpeedSumLat += speedLat;
                            inletNormalSpeedPhysSum += normalSpeedPhys;
                            inletFlowRateSignedSum += cellFlowSigned;
                            inletCount++;
                        }
                        else
                        {
                            outletTempSum += tRaw;
                            outletTempSqSum += (double)tRaw * tRaw;
                            outletSpeedSumLat += speedLat;
                            outletNormalSpeedPhysSum += normalSpeedPhys;
                            outletFlowRateSignedSum += cellFlowSigned;
                            outletCount++;

                            if (TryGetInnerPlaneCell(box, x, y, z, nx, ny, nz, out uint ix, out uint iy, out uint iz))
                            {
                                int innerIdx = (int)(ix + iy * nx + iz * nx * ny);
                                if (innerIdx >= 0 && innerIdx < tempData.Length)
                                {
                                    outletInnerTempSum += tempData[innerIdx];
                                    outletInnerCount++;
                                }
                            }
                        }
                    }
                }
            }
        }

        latestMetrics.inletSampleCount = inletCount;
        latestMetrics.outletSampleCount = outletCount;

        latestMetrics.hasValidInletAverage = inletCount > 0;
        if (inletCount > 0)
        {
            latestMetrics.inletAverageTemperatureRaw = (float)(inletTempSum / inletCount);
            latestMetrics.inletAverageTemperatureDegC =
                simulationController.TemperatureLBMToDegC(latestMetrics.inletAverageTemperatureRaw);

            latestMetrics.inletAverageSpeedLat = (float)(inletSpeedSumLat / inletCount);
            latestMetrics.inletAverageSpeedPhys =
                LatticeSpeedToPhysical(latestMetrics.inletAverageSpeedLat);

            latestMetrics.inletAverageNormalSpeedPhys = (float)(inletNormalSpeedPhysSum / inletCount);
            latestMetrics.inletFlowRatePhysSigned = (float)inletFlowRateSignedSum;
            latestMetrics.inletFlowRatePhysAbs = Mathf.Abs(latestMetrics.inletFlowRatePhysSigned);
        }

        latestMetrics.hasValidOutletAverage = outletCount > 0;
        if (outletCount > 0)
        {
            latestMetrics.outletAverageTemperatureRaw = (float)(outletTempSum / outletCount);
            latestMetrics.outletAverageTemperatureDegC =
                simulationController.TemperatureLBMToDegC(latestMetrics.outletAverageTemperatureRaw);

            latestMetrics.outletAverageSpeedLat = (float)(outletSpeedSumLat / outletCount);
            latestMetrics.outletAverageSpeedPhys =
                LatticeSpeedToPhysical(latestMetrics.outletAverageSpeedLat);

            latestMetrics.outletAverageNormalSpeedPhys = (float)(outletNormalSpeedPhysSum / outletCount);
            latestMetrics.outletFlowRatePhysSigned = (float)outletFlowRateSignedSum;
            latestMetrics.outletFlowRatePhysAbs = Mathf.Abs(latestMetrics.outletFlowRatePhysSigned);

            float outletVarRaw = (float)math.max(
                0.0,
                (outletTempSqSum / outletCount) - ((outletTempSum / outletCount) * (outletTempSum / outletCount)));

            latestMetrics.outletTemperatureStdDevRaw = Mathf.Sqrt(outletVarRaw);

            float tempScaleDegC =
                simulationController.TemperatureLBMToDegC(1f) -
                simulationController.TemperatureLBMToDegC(0f);

            latestMetrics.outletTemperatureStdDevDegC =
                latestMetrics.outletTemperatureStdDevRaw * tempScaleDegC;
        }

        latestMetrics.outletInnerPlaneSampleCount = outletInnerCount;
        latestMetrics.hasValidOutletInnerPlaneAverage = outletInnerCount > 0;

        if (outletInnerCount > 0)
        {
            latestMetrics.outletInnerPlaneAverageTemperatureRaw =
                (float)(outletInnerTempSum / outletInnerCount);

            latestMetrics.outletInnerPlaneAverageTemperatureDegC =
                simulationController.TemperatureLBMToDegC(
                    latestMetrics.outletInnerPlaneAverageTemperatureRaw);
        }

        latestMetrics.hasValidFlowDiagnostic = (inletCount > 0 || outletCount > 0);
        if (latestMetrics.hasValidFlowDiagnostic)
        {
            latestMetrics.netFlowRatePhysSigned =
                latestMetrics.inletFlowRatePhysSigned + latestMetrics.outletFlowRatePhysSigned;

            float denom = Mathf.Max(
                latestMetrics.inletFlowRatePhysAbs,
                latestMetrics.outletFlowRatePhysAbs,
                1e-8f);

            latestMetrics.relativeFlowImbalance =
                latestMetrics.netFlowRatePhysSigned / denom;
        }

        if (clampData.Length >= 4)
        {
            latestMetrics.thermalInletClampCount = clampData[0];
            latestMetrics.thermalOutletClampCount = clampData[1];
            latestMetrics.fluidInletClampCount = clampData[2];
            latestMetrics.fluidOutletClampCount = clampData[3];
        }

        latestMetrics.statusMessage =
            $"Sampled at t={simulationController.SimulatedTimeSeconds:F3}s (full)";
    }

    private void ComputeTemperatureOnlyMetrics(
        NativeArray<float> tempData,
        NativeArray<uint> fieldData,
        NativeArray<uint> clampData)
    {
        latestMetrics.Clear();

        if (simulationController == null)
        {
            latestMetrics.statusMessage = "SimulationController is missing.";
            return;
        }

        uint nx = simulationController.Nx;
        uint ny = simulationController.Ny;
        uint nz = simulationController.Nz;

        if (nx == 0 || ny == 0 || nz == 0)
        {
            latestMetrics.statusMessage = "Invalid domain resolution.";
            return;
        }

        if (tempData.Length == 0 || fieldData.Length == 0)
        {
            latestMetrics.statusMessage = "Readback data is empty.";
            return;
        }

        const uint FLUID = 0u;
        const uint RACK = 3u;

        double roomTempSum = 0.0;
        double roomTempSqSum = 0.0;

        float minRoomTemp = float.PositiveInfinity;
        float maxRoomTemp = float.NegativeInfinity;

        int roomCount = 0;
        int rackCount = 0;

        for (int i = 0; i < fieldData.Length; i++)
        {
            uint flag = fieldData[i];
            if (flag != FLUID && flag != RACK)
                continue;

            float t = tempData[i];

            roomTempSum += t;
            roomTempSqSum += (double)t * t;

            minRoomTemp = math.min(minRoomTemp, t);
            maxRoomTemp = math.max(maxRoomTemp, t);

            if (flag == RACK)
                rackCount++;

            roomCount++;
        }

        latestMetrics.fluidCellCount = roomCount;
        latestMetrics.rackCellCount = rackCount;
        latestMetrics.hasValidRoomAverage = roomCount > 0;

        if (roomCount > 0)
        {
            float avgTempRaw = (float)(roomTempSum / roomCount);
            float tempVarRaw = (float)math.max(
                0.0,
                (roomTempSqSum / roomCount) - ((roomTempSum / roomCount) * (roomTempSum / roomCount)));

            latestMetrics.avgRoomTemperatureRaw = avgTempRaw;
            latestMetrics.minRoomTemperatureRaw = minRoomTemp;
            latestMetrics.maxRoomTemperatureRaw = maxRoomTemp;
            latestMetrics.roomTemperatureStdDevRaw = Mathf.Sqrt(tempVarRaw);

            latestMetrics.avgRoomTemperatureDegC =
                simulationController.TemperatureLBMToDegC(avgTempRaw);
            latestMetrics.minRoomTemperatureDegC =
                simulationController.TemperatureLBMToDegC(minRoomTemp);
            latestMetrics.maxRoomTemperatureDegC =
                simulationController.TemperatureLBMToDegC(maxRoomTemp);

            float tempScaleDegC =
                simulationController.TemperatureLBMToDegC(1f) -
                simulationController.TemperatureLBMToDegC(0f);

            latestMetrics.roomTemperatureStdDevDegC =
                latestMetrics.roomTemperatureStdDevRaw * tempScaleDegC;
        }

        LBMZouHeBox[] boxes = FindObjectsByType<LBMZouHeBox>(FindObjectsSortMode.InstanceID);

        double inletTempSum = 0.0;
        int inletCount = 0;

        double outletTempSum = 0.0;
        double outletTempSqSum = 0.0;
        int outletCount = 0;

        double outletInnerTempSum = 0.0;
        int outletInnerCount = 0;

        foreach (var box in boxes)
        {
            if (box == null || !box.Power)
                continue;

            uint3 min = box.MinIdx;
            uint3 max = box.MaxIdx;

            for (uint z = min.z; z <= max.z; z++)
            {
                for (uint y = min.y; y <= max.y; y++)
                {
                    for (uint x = min.x; x <= max.x; x++)
                    {
                        int idx = (int)(x + y * nx + z * nx * ny);
                        if (idx < 0 || idx >= tempData.Length)
                            continue;

                        float tRaw = tempData[idx];

                        if (box.PatchKind == LBMZouHeBox.Kind.Inlet)
                        {
                            inletTempSum += tRaw;
                            inletCount++;
                        }
                        else
                        {
                            outletTempSum += tRaw;
                            outletTempSqSum += (double)tRaw * tRaw;
                            outletCount++;

                            if (TryGetInnerPlaneCell(box, x, y, z, nx, ny, nz, out uint ix, out uint iy, out uint iz))
                            {
                                int innerIdx = (int)(ix + iy * nx + iz * nx * ny);
                                if (innerIdx >= 0 && innerIdx < tempData.Length)
                                {
                                    outletInnerTempSum += tempData[innerIdx];
                                    outletInnerCount++;
                                }
                            }
                        }
                    }
                }
            }
        }

        latestMetrics.inletSampleCount = inletCount;
        latestMetrics.outletSampleCount = outletCount;

        latestMetrics.hasValidInletAverage = inletCount > 0;
        if (inletCount > 0)
        {
            latestMetrics.inletAverageTemperatureRaw = (float)(inletTempSum / inletCount);
            latestMetrics.inletAverageTemperatureDegC =
                simulationController.TemperatureLBMToDegC(latestMetrics.inletAverageTemperatureRaw);
        }

        latestMetrics.hasValidOutletAverage = outletCount > 0;
        if (outletCount > 0)
        {
            latestMetrics.outletAverageTemperatureRaw = (float)(outletTempSum / outletCount);
            latestMetrics.outletAverageTemperatureDegC =
                simulationController.TemperatureLBMToDegC(latestMetrics.outletAverageTemperatureRaw);

            float outletVarRaw = (float)math.max(
                0.0,
                (outletTempSqSum / outletCount) - ((outletTempSum / outletCount) * (outletTempSum / outletCount)));

            latestMetrics.outletTemperatureStdDevRaw = Mathf.Sqrt(outletVarRaw);

            float tempScaleDegC =
                simulationController.TemperatureLBMToDegC(1f) -
                simulationController.TemperatureLBMToDegC(0f);

            latestMetrics.outletTemperatureStdDevDegC =
                latestMetrics.outletTemperatureStdDevRaw * tempScaleDegC;
        }

        latestMetrics.outletInnerPlaneSampleCount = outletInnerCount;
        latestMetrics.hasValidOutletInnerPlaneAverage = outletInnerCount > 0;

        if (outletInnerCount > 0)
        {
            latestMetrics.outletInnerPlaneAverageTemperatureRaw =
                (float)(outletInnerTempSum / outletInnerCount);

            latestMetrics.outletInnerPlaneAverageTemperatureDegC =
                simulationController.TemperatureLBMToDegC(
                    latestMetrics.outletInnerPlaneAverageTemperatureRaw);
        }

        if (clampData.Length >= 4)
        {
            latestMetrics.thermalInletClampCount = clampData[0];
            latestMetrics.thermalOutletClampCount = clampData[1];
            latestMetrics.fluidInletClampCount = clampData[2];
            latestMetrics.fluidOutletClampCount = clampData[3];
        }

        latestMetrics.hasValidFlowDiagnostic = false;

        latestMetrics.statusMessage =
            $"Sampled at t={simulationController.SimulatedTimeSeconds:F3}s (temperature-only)";
    }

    private float LatticeSpeedToPhysical(float speedLat)
    {
        float dt = simulationController.DtPhys;
        float dx = simulationController.CellSize;

        if (dt <= 1e-8f)
            return 0f;

        return speedLat * dx / dt;
    }
}