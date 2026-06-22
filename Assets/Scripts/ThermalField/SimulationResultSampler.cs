using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public class SimulationResultSampler : MonoBehaviour
{
    private const float CsLat = 0.5773502691896258f;
    private const float TimeEpsilon = 1e-6f;

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
    [Tooltip("Keep this enabled for user-facing result metrics. It upgrades old scenes saved as TemperatureOnly to FullMetrics so flow and density diagnostics are available.")]
    [SerializeField] private bool requireFlowDiagnostics = true;
    [Tooltip("FullMetrics reads velocity/rho for flow, Mach, density and mass residual. TemperatureOnly is faster but only valid for temperature graph/result values.")]
    [SerializeField] private ReadbackMode readbackMode = ReadbackMode.FullMetrics;

    [Header("Read-Only Metrics")]
    [SerializeField, ReadOnly] private SimulationResultMetrics latestMetrics = new SimulationResultMetrics();
    [SerializeField, ReadOnly] private float nextSampleSimTimeSeconds = 0.0f;
    [SerializeField, ReadOnly] private float lastRequestedSampleSimTimeSeconds = -1.0f;
    [SerializeField, ReadOnly] private float lastCompletedSampleSimTimeSeconds = -1.0f;
    [SerializeField, ReadOnly] private int skippedSamplesWhileReadbackBusy = 0;

    [Header("Readback Debug")]
    [SerializeField, ReadOnly] private string activeReadbackMode = "";
    [SerializeField, ReadOnly] private string readbackModeStatus = "";

    private bool _requestInFlight = false;
    private PendingFullMetricsReadback _pendingFullMetricsReadback;
    private PendingTemperatureReadback _pendingTemperatureReadback;

    public SimulationResultMetrics LatestMetrics => latestMetrics;

    private void Awake()
    {
        NormalizeReadbackMode();

        if (simulationController == null)
            simulationController = FindFirstObjectByType<SimulationController>();

        latestMetrics.Clear();
        ResetSamplingSchedule();
    }

    private void OnEnable()
    {
        NormalizeReadbackMode();
        ResetSamplingSchedule();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        NormalizeReadbackMode();
    }
#endif

    private void OnDisable()
    {
        DisposePendingReadbacks();
        _requestInFlight = false;
    }

    public void ResetSamplingSchedule()
    {
        lastRequestedSampleSimTimeSeconds = -1.0f;
        lastCompletedSampleSimTimeSeconds = -1.0f;
        skippedSamplesWhileReadbackBusy = 0;

        float interval = GetSafeSampleInterval();
        float simTime = simulationController != null ? simulationController.SimulatedTimeSeconds : 0.0f;
        nextSampleSimTimeSeconds = GetNextSampleTimeAfter(simTime, interval);
    }

    private void LateUpdate()
    {
        if (enableSampling)
            TryRequestSamplingBySimulatedTime();
    }

    public void ForceSampleNow()
    {
        TryRequestSampling(force: true);
    }

    [ContextMenu("Use Full Metrics Readback")]
    public void UseFullMetricsReadback()
    {
        requireFlowDiagnostics = true;
        readbackMode = ReadbackMode.FullMetrics;
        UpdateReadbackModeStatus();
        ResetSamplingSchedule();
    }

    [ContextMenu("Use Temperature Only Readback")]
    public void UseTemperatureOnlyReadback()
    {
        requireFlowDiagnostics = false;
        readbackMode = ReadbackMode.TemperatureOnly;
        UpdateReadbackModeStatus();
        ResetSamplingSchedule();
    }

    private void TryRequestSamplingBySimulatedTime()
    {
        if (simulationController == null)
        {
            latestMetrics.statusMessage = "SimulationController is null.";
            return;
        }

        float simTime = simulationController.SimulatedTimeSeconds;
        if (simTime + TimeEpsilon < nextSampleSimTimeSeconds)
            return;

        float interval = GetSafeSampleInterval();
        if (_requestInFlight)
        {
            skippedSamplesWhileReadbackBusy++;
            nextSampleSimTimeSeconds = GetNextSampleTimeAfter(simTime, interval);
            readbackModeStatus =
                $"Readback busy. Skipped sample target near t={simTime:F3}s.";
            return;
        }

        if (TryRequestSampling())
            nextSampleSimTimeSeconds = GetNextSampleTimeAfter(simTime, interval);
    }

    private bool TryRequestSampling(bool force = false)
    {
        NormalizeReadbackMode();
        activeReadbackMode = readbackMode.ToString();

        if (_requestInFlight)
        {
            readbackModeStatus = "Readback request is already in flight.";
            return false;
        }

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
            readbackModeStatus = "FullMetrics requested, but VelocityRhoBuffer is not ready.";
            return false;
        }

        ulong requestedStepCount = simulationController.StepCount;
        float requestedSimTimeSeconds = simulationController.SimulatedTimeSeconds;
        lastRequestedSampleSimTimeSeconds = requestedSimTimeSeconds;

        _requestInFlight = true;

        if (readbackMode == ReadbackMode.FullMetrics)
        {
            if (logMetricsToConsole)
                Debug.Log("[SimulationResultSampler] Requesting FullMetrics readback.");

            RequestFullMetricsReadback(
                solver.TemperatureBuffer,
                solver.VelocityRhoBuffer,
                solver.FieldBuffer,
                solver.DebugThermalClampCounterBuffer,
                requestedStepCount,
                requestedSimTimeSeconds);
        }
        else
        {
            if (logMetricsToConsole)
                Debug.Log("[SimulationResultSampler] Requesting TemperatureOnly readback.");

            RequestTemperatureOnlyReadback(
                solver.TemperatureBuffer,
                solver.FieldBuffer,
                solver.DebugThermalClampCounterBuffer,
                requestedStepCount,
                requestedSimTimeSeconds);
        }

        return true;
    }

    private void NormalizeReadbackMode()
    {
        if (requireFlowDiagnostics && readbackMode == ReadbackMode.TemperatureOnly)
            readbackMode = ReadbackMode.FullMetrics;

        UpdateReadbackModeStatus();
    }

    private void UpdateReadbackModeStatus()
    {
        activeReadbackMode = readbackMode.ToString();

        readbackModeStatus = readbackMode == ReadbackMode.FullMetrics
            ? "FullMetrics: temperature, velocity, density, flow, Mach and mass residual are sampled."
            : "TemperatureOnly: flow, velocity, density, Mach and mass residual are unavailable.";
    }

    private float GetSafeSampleInterval()
    {
        return Mathf.Max(sampleIntervalSeconds, TimeEpsilon);
    }

    private static float GetNextSampleTimeAfter(float simTime, float interval)
    {
        if (interval <= TimeEpsilon)
            return simTime + TimeEpsilon;

        if (simTime <= 0.0f)
            return interval;

        float sampleIndex = Mathf.Floor((simTime + TimeEpsilon) / interval) + 1.0f;
        return sampleIndex * interval;
    }

    private void RequestFullMetricsReadback(
        GraphicsBuffer temperatureBuffer,
        GraphicsBuffer velocityRhoBuffer,
        GraphicsBuffer fieldBuffer,
        GraphicsBuffer debugClampBuffer,
        ulong sampleStepCount,
        float sampleTimeSeconds)
    {
        DisposePendingReadbacks();

        var pending = new PendingFullMetricsReadback(sampleStepCount, sampleTimeSeconds);
        _pendingFullMetricsReadback = pending;

        AsyncGPUReadback.Request(temperatureBuffer, request => OnFullTemperatureReadback(pending, request));
        AsyncGPUReadback.Request(velocityRhoBuffer, request => OnFullVelocityReadback(pending, request));
        AsyncGPUReadback.Request(fieldBuffer, request => OnFullFieldReadback(pending, request));
        AsyncGPUReadback.Request(debugClampBuffer, request => OnFullClampReadback(pending, request));
    }

    private void RequestTemperatureOnlyReadback(
        GraphicsBuffer temperatureBuffer,
        GraphicsBuffer fieldBuffer,
        GraphicsBuffer debugClampBuffer,
        ulong sampleStepCount,
        float sampleTimeSeconds)
    {
        DisposePendingReadbacks();

        var pending = new PendingTemperatureReadback(sampleStepCount, sampleTimeSeconds);
        _pendingTemperatureReadback = pending;

        AsyncGPUReadback.Request(temperatureBuffer, request => OnTemperatureOnlyTemperatureReadback(pending, request));
        AsyncGPUReadback.Request(fieldBuffer, request => OnTemperatureOnlyFieldReadback(pending, request));
        AsyncGPUReadback.Request(debugClampBuffer, request => OnTemperatureOnlyClampReadback(pending, request));
    }

    private void OnFullTemperatureReadback(PendingFullMetricsReadback pending, AsyncGPUReadbackRequest request)
    {
        if (!IsActive(pending) || TryFailFullReadback(pending, request, "temperature"))
            return;

        if (!TryCopyReadback(request, ref pending.tempData, pending, "temperature"))
            return;

        CompleteFullReadbackPart(pending);
    }

    private void OnFullVelocityReadback(PendingFullMetricsReadback pending, AsyncGPUReadbackRequest request)
    {
        if (!IsActive(pending) || TryFailFullReadback(pending, request, "velocity_rho"))
            return;

        if (!TryCopyReadback(request, ref pending.velData, pending, "velocity_rho"))
            return;

        CompleteFullReadbackPart(pending);
    }

    private void OnFullFieldReadback(PendingFullMetricsReadback pending, AsyncGPUReadbackRequest request)
    {
        if (!IsActive(pending) || TryFailFullReadback(pending, request, "field"))
            return;

        if (!TryCopyReadback(request, ref pending.fieldData, pending, "field"))
            return;

        CompleteFullReadbackPart(pending);
    }

    private void OnFullClampReadback(PendingFullMetricsReadback pending, AsyncGPUReadbackRequest request)
    {
        if (!IsActive(pending) || TryFailFullReadback(pending, request, "clamp"))
            return;

        if (!TryCopyReadback(request, ref pending.clampData, pending, "clamp"))
            return;

        CompleteFullReadbackPart(pending);
    }

    private void OnTemperatureOnlyTemperatureReadback(PendingTemperatureReadback pending, AsyncGPUReadbackRequest request)
    {
        if (!IsActive(pending) || TryFailTemperatureReadback(pending, request, "temperature"))
            return;

        if (!TryCopyReadback(request, ref pending.tempData, pending, "temperature"))
            return;

        CompleteTemperatureReadbackPart(pending);
    }

    private void OnTemperatureOnlyFieldReadback(PendingTemperatureReadback pending, AsyncGPUReadbackRequest request)
    {
        if (!IsActive(pending) || TryFailTemperatureReadback(pending, request, "field"))
            return;

        if (!TryCopyReadback(request, ref pending.fieldData, pending, "field"))
            return;

        CompleteTemperatureReadbackPart(pending);
    }

    private void OnTemperatureOnlyClampReadback(PendingTemperatureReadback pending, AsyncGPUReadbackRequest request)
    {
        if (!IsActive(pending) || TryFailTemperatureReadback(pending, request, "clamp"))
            return;

        if (!TryCopyReadback(request, ref pending.clampData, pending, "clamp"))
            return;

        CompleteTemperatureReadbackPart(pending);
    }

    private bool TryCopyReadback<T>(
        AsyncGPUReadbackRequest request,
        ref NativeArray<T> destination,
        PendingReadbackBase pending,
        string bufferName)
        where T : struct
    {
        try
        {
            destination = new NativeArray<T>(request.GetData<T>(), Allocator.Persistent);
            return true;
        }
        catch (System.Exception ex)
        {
            FailPendingReadback(pending, $"AsyncGPUReadback copy failed for {bufferName}: {ex.Message}");
            return false;
        }
    }

    private bool TryFailFullReadback(
        PendingFullMetricsReadback pending,
        AsyncGPUReadbackRequest request,
        string bufferName)
    {
        if (!request.hasError)
            return false;

        FailPendingReadback(pending, $"AsyncGPUReadback failed: {bufferName}");
        return true;
    }

    private bool TryFailTemperatureReadback(
        PendingTemperatureReadback pending,
        AsyncGPUReadbackRequest request,
        string bufferName)
    {
        if (!request.hasError)
            return false;

        FailPendingReadback(pending, $"AsyncGPUReadback failed: {bufferName}");
        return true;
    }

    private void CompleteFullReadbackPart(PendingFullMetricsReadback pending)
    {
        pending.completedParts++;
        if (pending.completedParts < PendingFullMetricsReadback.ExpectedParts)
            return;

        try
        {
            ComputeMetrics(
                pending.tempData,
                pending.velData,
                pending.fieldData,
                pending.clampData,
                pending.sampleStepCount,
                pending.sampleTimeSeconds);

            lastCompletedSampleSimTimeSeconds = pending.sampleTimeSeconds;
            LogMetricsIfRequested(pending.clampData);
        }
        finally
        {
            FinishPendingReadback(pending);
        }
    }

    private void CompleteTemperatureReadbackPart(PendingTemperatureReadback pending)
    {
        pending.completedParts++;
        if (pending.completedParts < PendingTemperatureReadback.ExpectedParts)
            return;

        try
        {
            ComputeTemperatureOnlyMetrics(
                pending.tempData,
                pending.fieldData,
                pending.clampData,
                pending.sampleStepCount,
                pending.sampleTimeSeconds);

            lastCompletedSampleSimTimeSeconds = pending.sampleTimeSeconds;
            LogMetricsIfRequested(pending.clampData);
        }
        finally
        {
            FinishPendingReadback(pending);
        }
    }

    private void LogMetricsIfRequested(NativeArray<uint> clampData)
    {
        if (!logMetricsToConsole)
            return;

        Debug.Log(latestMetrics.ToSummaryText());

        if (clampData.Length >= 4)
        {
            Debug.Log(
                $"[ClampCounter] thermal_in={clampData[0]}, thermal_out={clampData[1]}, " +
                $"fluid_in={clampData[2]}, fluid_out={clampData[3]}");
        }
    }

    private bool IsActive(PendingFullMetricsReadback pending)
    {
        return pending != null && ReferenceEquals(_pendingFullMetricsReadback, pending) && !pending.failed;
    }

    private bool IsActive(PendingTemperatureReadback pending)
    {
        return pending != null && ReferenceEquals(_pendingTemperatureReadback, pending) && !pending.failed;
    }

    private void FailPendingReadback(PendingReadbackBase pending, string message)
    {
        if (pending == null || pending.failed)
            return;

        pending.failed = true;
        latestMetrics.statusMessage = message;
        readbackModeStatus = message;
        FinishPendingReadback(pending);
    }

    private void FinishPendingReadback(PendingReadbackBase pending)
    {
        pending?.Dispose();

        if (pending is PendingFullMetricsReadback full && ReferenceEquals(_pendingFullMetricsReadback, full))
            _pendingFullMetricsReadback = null;

        if (pending is PendingTemperatureReadback temperature && ReferenceEquals(_pendingTemperatureReadback, temperature))
            _pendingTemperatureReadback = null;

        _requestInFlight = false;
    }

    private void DisposePendingReadbacks()
    {
        _pendingFullMetricsReadback?.Dispose();
        _pendingFullMetricsReadback = null;

        _pendingTemperatureReadback?.Dispose();
        _pendingTemperatureReadback = null;
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
        NativeArray<uint> clampData,
        ulong sampleStepCount,
        float sampleTimeSeconds)
    {
        latestMetrics.Clear();

        if (simulationController == null)
        {
            latestMetrics.statusMessage = "SimulationController is missing.";
            return;
        }

        PopulateCaseContext(sampleStepCount, sampleTimeSeconds);

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
            latestMetrics.hasValidDensityDiagnostic = true;
#pragma warning disable 0618
            latestMetrics.averageKineticEnergyLat = (float)(kineticSum / roomCount);
#pragma warning restore 0618
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
            latestMetrics.maxMach = maxSpeedLat / CsLat;
            latestMetrics.hasValidVelocityDiagnostic = true;
#pragma warning disable 0618
            latestMetrics.machMax = latestMetrics.maxMach;
#pragma warning restore 0618
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
            latestMetrics.inletFlowRateCMM = latestMetrics.inletFlowRatePhysAbs * 60.0f;
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
            latestMetrics.outletFlowRateCMM = latestMetrics.outletFlowRatePhysAbs * 60.0f;

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

        latestMetrics.massConservationStatus = BuildMassConservationStatus(
            latestMetrics.hasValidFlowDiagnostic,
            latestMetrics.massResidualNormalized,
            latestMetrics.relativeFlowImbalance);

        if (clampData.Length >= 4)
        {
            latestMetrics.thermalInletClampCount = clampData[0];
            latestMetrics.thermalOutletClampCount = clampData[1];
            latestMetrics.fluidInletClampCount = clampData[2];
            latestMetrics.fluidOutletClampCount = clampData[3];
        }

        latestMetrics.statusMessage =
            $"Sampled at t={sampleTimeSeconds:F3}s (full)";
    }

    private void ComputeTemperatureOnlyMetrics(
        NativeArray<float> tempData,
        NativeArray<uint> fieldData,
        NativeArray<uint> clampData,
        ulong sampleStepCount,
        float sampleTimeSeconds)
    {
        latestMetrics.Clear();

        if (simulationController == null)
        {
            latestMetrics.statusMessage = "SimulationController is missing.";
            return;
        }

        PopulateCaseContext(sampleStepCount, sampleTimeSeconds);
        latestMetrics.massConservationStatus = "Density unavailable in temperature-only mode.";

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
            $"Sampled at t={sampleTimeSeconds:F3}s (temperature-only)";
    }

    private void PopulateCaseContext(ulong sampleStepCount, float sampleTimeSeconds)
    {
        latestMetrics.stepCount = sampleStepCount;
        latestMetrics.simulationTimeSeconds = sampleTimeSeconds;
        latestMetrics.dtPhys = simulationController.DtPhys;
        latestMetrics.preset = simulationController.SolverPresetName;
        latestMetrics.caseName = simulationController.ActiveCaseName;
        latestMetrics.readbackMode = readbackMode.ToString();
        latestMetrics.tauF = simulationController.TauF;
        latestMetrics.tauT = simulationController.TauT;
        latestMetrics.tauFRaw = simulationController.TauFRaw;
        latestMetrics.tauTRaw = simulationController.TauTRaw;
        latestMetrics.tauFluidMin = simulationController.TauFluidMin;
        latestMetrics.tauThermalMin = simulationController.TauThermalMin;
        latestMetrics.tauFluidMax = simulationController.TauFluidMax;
        latestMetrics.tauThermalMax = simulationController.TauThermalMax;
        latestMetrics.tauFWasClamped = simulationController.TauFWasClamped;
        latestMetrics.tauTWasClamped = simulationController.TauTWasClamped;
        latestMetrics.nuPhysTarget = simulationController.NuPhysTarget;
        latestMetrics.alphaPhysTarget = simulationController.AlphaPhysTarget;
        latestMetrics.nuPhysEffective = simulationController.NuPhysEffective;
        latestMetrics.alphaPhysEffective = simulationController.AlphaPhysEffective;
        latestMetrics.nuPhysEffectiveRatio = simulationController.NuPhysEffectiveRatio;
        latestMetrics.alphaPhysEffectiveRatio = simulationController.AlphaPhysEffectiveRatio;
        latestMetrics.maxMachLimit = simulationController.MaxMachLimit;
        latestMetrics.reynoldsNumber = simulationController.ReynoldsNumber;
        latestMetrics.prandtlNumber = simulationController.PrandtlNumber;
        latestMetrics.stabilityStatus = simulationController.StabilityStatus.ToString();
        latestMetrics.readinessStatus = simulationController.ReadinessStatus.ToString();
    }

    private abstract class PendingReadbackBase : System.IDisposable
    {
        public readonly ulong sampleStepCount;
        public readonly float sampleTimeSeconds;
        public int completedParts;
        public bool failed;

        protected PendingReadbackBase(ulong sampleStepCount, float sampleTimeSeconds)
        {
            this.sampleStepCount = sampleStepCount;
            this.sampleTimeSeconds = sampleTimeSeconds;
        }

        public abstract void Dispose();

        protected static void DisposeIfCreated<T>(ref NativeArray<T> data)
            where T : struct
        {
            if (data.IsCreated)
                data.Dispose();

            data = default;
        }
    }

    private sealed class PendingFullMetricsReadback : PendingReadbackBase
    {
        public const int ExpectedParts = 4;

        public NativeArray<float> tempData;
        public NativeArray<float4> velData;
        public NativeArray<uint> fieldData;
        public NativeArray<uint> clampData;

        public PendingFullMetricsReadback(ulong sampleStepCount, float sampleTimeSeconds)
            : base(sampleStepCount, sampleTimeSeconds)
        {
        }

        public override void Dispose()
        {
            DisposeIfCreated(ref tempData);
            DisposeIfCreated(ref velData);
            DisposeIfCreated(ref fieldData);
            DisposeIfCreated(ref clampData);
        }
    }

    private sealed class PendingTemperatureReadback : PendingReadbackBase
    {
        public const int ExpectedParts = 3;

        public NativeArray<float> tempData;
        public NativeArray<uint> fieldData;
        public NativeArray<uint> clampData;

        public PendingTemperatureReadback(ulong sampleStepCount, float sampleTimeSeconds)
            : base(sampleStepCount, sampleTimeSeconds)
        {
        }

        public override void Dispose()
        {
            DisposeIfCreated(ref tempData);
            DisposeIfCreated(ref fieldData);
            DisposeIfCreated(ref clampData);
        }
    }

    private static string BuildMassConservationStatus(
        bool hasFlowDiagnostic,
        float massResidualNormalized,
        float relativeFlowImbalance)
    {
        if (!hasFlowDiagnostic)
            return "Flow diagnostic unavailable.";

        if (massResidualNormalized > 1e-2f || Mathf.Abs(relativeFlowImbalance) > 0.05f)
            return "Warning: review density residual or inlet/outlet flow balance.";

        return "OK";
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
