using UnityEngine;
using Unity.Mathematics;

#if UNITY_EDITOR
using UnityEditor; // Handles.Label
#endif

[ExecuteAlways]
[RequireComponent(typeof(BoxCollider))]
public class LBMZouHeBox : MonoBehaviour
{
    public enum Kind { Inlet, Outlet }
    public enum Axis { X, Y, Z }
    public enum Sign { Negative = -1, Positive = +1 }
    public enum BoundaryInputMode
    {
        Velocity,
        VolumeFlowRate,
        PressureDensity,
        AutoMassBalancedOutlet
    }

    public enum FlowRateInputUnit
    {
        CubicMetersPerSecond,
        CMM
    }

    [Header("Boundary Type")]
    [SerializeField] private bool power = true;
    public bool Power => power;

    public Kind kind = Kind.Inlet;
    [SerializeField] private BoundaryInputMode boundaryInputMode = BoundaryInputMode.Velocity;

    [Header("Geometry / Patch Summary")]
    [SerializeField, ReadOnly] private float patchAreaPhysCached = 0.0f;
    [SerializeField, ReadOnly] private float targetFlowRateM3psCached = 0.0f;
    [SerializeField, ReadOnly] private float targetFlowRateCMMCached = 0.0f;
    [TextArea(4, 12)]
    [SerializeField, ReadOnly] private string boundarySummary = "";

    [Header("Inlet Settings")]
    [Tooltip("Physical inlet velocity vector [m/s] (World space). " +
             "This directly controls inlet direction without rotating the box.")]
    [SerializeField] private Vector3 windSpeedPhys = new Vector3(0f, 0f, -0.8f);

    [Header("Inlet Volume Flow")]
    [SerializeField] private FlowRateInputUnit volumeFlowRateUnit = FlowRateInputUnit.CMM;
    [SerializeField] private float volumeFlowRateM3ps = 0.1f;
    [SerializeField] private float volumeFlowRateCMM = 6.0f;

    [Header("Inlet Volume Flow Direction")]
    [Tooltip("Flow angle from inward normal toward the first BC tangential axis [deg]. Positive/negative follows the world axis direction shown in the boundary summary.")]
    [Range(-80.0f, 80.0f)]
    [SerializeField] private float volumeFlowTangentialAngleAdeg = 0.0f;
    [Tooltip("Flow angle from inward normal toward the second BC tangential axis [deg]. Positive/negative follows the world axis direction shown in the boundary summary.")]
    [Range(-80.0f, 80.0f)]
    [SerializeField] private float volumeFlowTangentialAngleBdeg = 0.0f;
    [Tooltip("Computed normalized world-space flow direction used by Volume Flow Rate mode.")]
    [SerializeField, ReadOnly] private Vector3 volumeFlowDirectionWorld = Vector3.zero;

    [Header("Thermal Settings")]
    [Tooltip("Inlet physical temperature target [degC]. This is converted to LBM temperature before sending to solver.")]
    [SerializeField] private float inletTemperatureDegC = 20.0f;

    [Header("Computed Temperature (read-only)")]
    [SerializeField, ReadOnly] private float inletTemperatureLBM = 0.5f;

    [Header("Outlet Settings")]
    [Tooltip("Outlet density target (pressure proxy).")]
    [SerializeField] private float rhoOut = 1.0f;

    [Header("Adaptive Outlet Feedback (read-only)")]
    [SerializeField, ReadOnly] private float adaptiveRhoOutOffset = 0.0f;

    [Header("Advanced LBM Boundary")]
    [SerializeField] private bool enableMassFluxCorrection = true;

    [Tooltip("Target outlet normal speed [m/s] computed by controller.")]
    [SerializeField, ReadOnly] private float targetOutletNormalSpeedPhys = 0.0f;

    [Tooltip("Blend between interior normal speed and target normal speed.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float outletNormalVelocityBlend = 0.35f;

    [Tooltip("Weak pressure anchor. 0 = interior rho only, 1 = rho target only.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float outletRhoAnchor = 0.10f;

    [Tooltip("Maximum allowed outlet normal speed [m/s].")]
    [SerializeField] private float maxOutletNormalSpeedPhys = 2.0f;

    [Header("Outlet Debug Override")]
    [SerializeField] private bool forceFullTargetNormalBlendForDebug = false;
    [SerializeField] private bool forceZeroRhoAnchorForDebug = false;

    [Header("How far from the surface to place the ZH plane (in cells)")]
    [Min(0)] public int fluidOffset = 0;

    [Header("Computed (read-only)")]
    [SerializeField] private Axis snappedAxis = Axis.Z;
    [SerializeField] private Sign snappedSign = Sign.Positive;
    [SerializeField] private int planeIndex = 0;
    [SerializeField] private uint3 minIdx;
    [SerializeField] private uint3 maxIdx;

    [Header("Normals (debug)")]
    [SerializeField] private Vector3 faceNormalWorld;
    [SerializeField] private Vector3 planeNormalWorld;

    public uint3 MinIdx => minIdx;
    public uint3 MaxIdx => maxIdx;
    public int PlaneIndex => planeIndex;
    public Kind PatchKind => kind;
    public BoundaryInputMode InputMode => GetEffectiveBoundaryInputMode();

    public float3 InletVelocityLat { get; private set; } = new float3(0f, 0f, 0f);
    public float InletTemperatureDegC => inletTemperatureDegC;
    public float InletTemperature => inletTemperatureLBM;

    public float BaseRhoOut => rhoOut;
    public float AdaptiveRhoOutOffset => adaptiveRhoOutOffset;
    public float RhoOut => Mathf.Clamp(rhoOut + adaptiveRhoOutOffset, 0.90f, 1.10f);

    public bool EnableMassFluxCorrection =>
        kind == Kind.Outlet &&
        GetEffectiveBoundaryInputMode() == BoundaryInputMode.AutoMassBalancedOutlet &&
        enableMassFluxCorrection;
    public float TargetOutletNormalSpeedPhys => targetOutletNormalSpeedPhys;
    public float PatchAreaPhysCached => patchAreaPhysCached;
    public float TargetFlowRateM3psCached => targetFlowRateM3psCached;
    public float TargetFlowRateCMMCached => targetFlowRateCMMCached;
    public string BoundarySummary => boundarySummary;
    public float OutletNormalVelocityBlend =>
        forceFullTargetNormalBlendForDebug ? 1.0f : outletNormalVelocityBlend;
    public float OutletRhoAnchor =>
        forceZeroRhoAnchorForDebug ? 0.0f : outletRhoAnchor;

    public float TargetOutletNormalSpeedLat
    {
        get
        {
            var ctrl = SimulationController.Instance;
            if (ctrl == null) return 0f;

            float scale = ctrl.DtPhys / Mathf.Max(ctrl.CellSize, 1e-8f);
            float clampedPhys = Mathf.Clamp(targetOutletNormalSpeedPhys, 0f, Mathf.Max(maxOutletNormalSpeedPhys, 0f));
            return clampedPhys * scale;
        }
    }

    public void SetAdaptiveRhoOutOffset(float offset)
    {
        adaptiveRhoOutOffset = offset;
    }

    public void ResetAdaptiveRhoOutOffset()
    {
        adaptiveRhoOutOffset = 0.0f;
    }

    public void SetTargetOutletNormalSpeedPhys(float value)
    {
        targetOutletNormalSpeedPhys = Mathf.Clamp(value, 0f, Mathf.Max(maxOutletNormalSpeedPhys, 0f));
        UpdateOutletTargetFlowCache();
    }

    public void ResetTargetOutletNormalSpeedPhys()
    {
        targetOutletNormalSpeedPhys = 0.0f;
        UpdateOutletTargetFlowCache();
    }

    public float3 WindSpeedPhys => new float3(windSpeedPhys.x, windSpeedPhys.y, windSpeedPhys.z);
    public Vector3 WindSpeedPhysVector3 => windSpeedPhys;
    public float WindSpeedMagnitudePhys => math.length(WindSpeedPhys);
    public Vector3 FaceNormalWorld => faceNormalWorld;
    public Vector3 PlaneNormalWorld => planeNormalWorld;

    [Header("Gizmos")]
    public bool drawPatchBounds = true;
    public bool drawCellsIndividually = false;
    public bool showIndexLabel = true;

    public Color inletPatchColor = new Color(0.10f, 0.75f, 1.00f, 0.20f);
    public Color outletPatchColor = new Color(1.00f, 0.55f, 0.10f, 0.20f);
    public Color cellWireColor = new Color(0.00f, 0.90f, 0.90f, 1.00f);
    public Color normalColor = Color.cyan;

    private BoxCollider _box;

    public Axis NormalAxis => snappedAxis;
    public Sign NormalSign => snappedSign;

    private BoundaryInputMode GetEffectiveBoundaryInputMode()
    {
        if (kind == Kind.Inlet)
        {
            if (boundaryInputMode == BoundaryInputMode.VolumeFlowRate)
                return BoundaryInputMode.VolumeFlowRate;

            return BoundaryInputMode.Velocity;
        }

        if (boundaryInputMode == BoundaryInputMode.PressureDensity)
            return BoundaryInputMode.PressureDensity;

        return enableMassFluxCorrection
            ? BoundaryInputMode.AutoMassBalancedOutlet
            : BoundaryInputMode.PressureDensity;
    }

    private void NormalizeBoundaryInputModeForKind()
    {
        if (kind == Kind.Inlet)
        {
            if (boundaryInputMode == BoundaryInputMode.PressureDensity ||
                boundaryInputMode == BoundaryInputMode.AutoMassBalancedOutlet)
            {
                boundaryInputMode = BoundaryInputMode.Velocity;
            }

            return;
        }

        if (boundaryInputMode == BoundaryInputMode.Velocity ||
            boundaryInputMode == BoundaryInputMode.VolumeFlowRate)
        {
            boundaryInputMode = enableMassFluxCorrection
                ? BoundaryInputMode.AutoMassBalancedOutlet
                : BoundaryInputMode.PressureDensity;
        }
    }

    private void SyncFlowRateFields()
    {
        if (volumeFlowRateUnit == FlowRateInputUnit.CMM)
            volumeFlowRateM3ps = Mathf.Max(0.0f, volumeFlowRateCMM / 60.0f);
        else
            volumeFlowRateCMM = Mathf.Max(0.0f, volumeFlowRateM3ps * 60.0f);
    }

    private void ApplyInletInputMode(float dx, float dt)
    {
        SyncFlowRateFields();
        volumeFlowTangentialAngleAdeg = Mathf.Clamp(volumeFlowTangentialAngleAdeg, -80.0f, 80.0f);
        volumeFlowTangentialAngleBdeg = Mathf.Clamp(volumeFlowTangentialAngleBdeg, -80.0f, 80.0f);

        if (GetEffectiveBoundaryInputMode() == BoundaryInputMode.VolumeFlowRate)
        {
            float area = Mathf.Max(PatchAreaPhys(dx), 1e-8f);
            float normalVelocity = volumeFlowRateM3ps / area;
            Vector3 velocityScale = BuildVolumeFlowVelocityScaleWorld(out Vector3 normalizedDirection);
            windSpeedPhys = velocityScale * normalVelocity;
            volumeFlowDirectionWorld = normalizedDirection;
        }
        else
        {
            volumeFlowDirectionWorld = windSpeedPhys.sqrMagnitude > 1e-12f
                ? windSpeedPhys.normalized
                : Vector3.zero;
        }

        float scale = (dx > 0f) ? (dt / dx) : 0f;
        float3 uPhys = new float3(windSpeedPhys.x, windSpeedPhys.y, windSpeedPhys.z);
        float3 uLat = uPhys * scale;

        float uLatMax = 0.30f / math.sqrt(3.0f);
        float mag = math.length(uLat);
        if (mag > uLatMax && mag > 1e-8f)
            uLat *= (uLatMax / mag);

        InletVelocityLat = uLat;
        targetFlowRateM3psCached = Mathf.Abs(GetFlowRatePhys(dx));
        targetFlowRateCMMCached = targetFlowRateM3psCached * 60.0f;
    }

    private void UpdateOutletTargetFlowCache()
    {
        targetFlowRateM3psCached = targetOutletNormalSpeedPhys * patchAreaPhysCached;
        targetFlowRateCMMCached = targetFlowRateM3psCached * 60.0f;
    }

    void OnEnable()
    {
        Refresh();
        NotifySceneCacheDirty();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        NormalizeBoundaryInputModeForKind();
        SyncFlowRateFields();
        Refresh();
        NotifySceneCacheDirty();
    }
#endif

    void OnDisable()
    {
        NotifySceneCacheDirty();
    }

    private void NotifySceneCacheDirty()
    {
        var cache = FindFirstObjectByType<SimulationSceneCache>(FindObjectsInactive.Exclude);
        if (cache != null)
        {
            cache.MarkDirty();
        }
    }

    public void Refresh()
    {
        _box = GetComponent<BoxCollider>();
        if (_box == null)
        {
            Debug.LogError("LBMZouHeBox needs a BoxCollider.");
            return;
        }

        var ctrl = SimulationController.Instance;
        if (ctrl == null || ctrl.DomainRoot == null)
        {
            return;
        }

        Vector3 fwd = transform.forward;
        faceNormalWorld = (kind == Kind.Inlet) ? fwd.normalized : (-fwd).normalized;

        snappedAxis = PickDominantAxis(faceNormalWorld);
        snappedSign = PickSign(faceNormalWorld, snappedAxis);
        planeNormalWorld = AxisToWorld(snappedAxis) * (snappedSign == Sign.Positive ? 1f : -1f);

        float dxCell = ctrl.CellSize;
        uint Nx = ctrl.Nx;
        uint Ny = ctrl.Ny;
        uint Nz = ctrl.Nz;
        Vector3 domMinW = ctrl.DomainRoot.position - ctrl.DomainRoot.localScale * 0.5f;

        uint3 bMin, bMax;
        BoundsToGrid(_box.bounds, domMinW, dxCell, Nx, Ny, Nz, out bMin, out bMax);

        BuildPatchFromBox(
            bMin, bMax,
            snappedAxis, snappedSign,
            Nx, Ny, Nz,
            fluidOffset,
            out minIdx, out maxIdx, out planeIndex);

        patchAreaPhysCached = PatchAreaPhys(dxCell);

        if (kind == Kind.Inlet)
        {
            ApplyInletInputMode(dxCell, ctrl.DtPhys);

            float tLbm = ctrl.TemperatureDegCToLBM(inletTemperatureDegC);
            inletTemperatureLBM = Mathf.Clamp01(tLbm);
        }
        else
        {
            InletVelocityLat = new float3(0f, 0f, 0f);
            inletTemperatureLBM = 0f;
            UpdateOutletTargetFlowCache();
        }

        boundarySummary = BuildBoundarySummary(dxCell);
    }

    public uint PatchCellCount
    {
        get
        {
            uint sx = maxIdx.x - minIdx.x + 1;
            uint sy = maxIdx.y - minIdx.y + 1;
            uint sz = maxIdx.z - minIdx.z + 1;

            return snappedAxis switch
            {
                Axis.X => sy * sz,
                Axis.Y => sx * sz,
                _ => sx * sy
            };
        }
    }

    public float PatchAreaPhys(float dx)
    {
        return PatchCellCount * dx * dx;
    }

    public float GetNormalSpeedPhys()
    {
        Vector3 n = planeNormalWorld.normalized;
        return Vector3.Dot(windSpeedPhys, n);
    }

    public float GetFlowRatePhys(float dx)
    {
        if (kind != Kind.Inlet)
            return 0f;

        return PatchAreaPhys(dx) * GetNormalSpeedPhys();
    }

    public string GetSummaryText(float dx)
    {
        Refresh();
        return boundarySummary;
    }

    private string BuildBoundarySummary(float dx)
    {
        string axisText = $"{(snappedSign == Sign.Positive ? "+" : "-")}{snappedAxis}";
        string indexRangeText = $"x:[{minIdx.x},{maxIdx.x}] y:[{minIdx.y},{maxIdx.y}] z:[{minIdx.z},{maxIdx.z}]";
        float area = PatchAreaPhys(dx);
        BoundaryInputMode effectiveMode = GetEffectiveBoundaryInputMode();

        if (kind == Kind.Inlet)
        {
            float normalSpeed = GetNormalSpeedPhys();
            float inwardNormalSpeed = -normalSpeed;
            float flowRate = GetFlowRatePhys(dx);
            float flowRateAbs = Mathf.Abs(flowRate);
            GetTangentialAxes(out Vector3 tangentA, out Vector3 tangentB);

            return
                $"[Inlet] {name}\n" +
                $"  Input mode         : {effectiveMode}\n" +
                $"  Plane / Normal     : {axisText}, planeIndex={planeIndex}\n" +
                $"  Patch area         : {area:F6} m^2 ({PatchCellCount} cells)\n" +
                $"  Velocity (phys)    : ({windSpeedPhys.x:F4}, {windSpeedPhys.y:F4}, {windSpeedPhys.z:F4}) m/s\n" +
                $"  Velocity magnitude : {WindSpeedMagnitudePhys:F4} m/s\n" +
                $"  Normal speed       : {normalSpeed:F4} m/s (plane signed)\n" +
                $"  Inward normal speed: {inwardNormalSpeed:F4} m/s\n" +
                $"  Flow rate          : {flowRateAbs:F6} m^3/s ({flowRateAbs * 60.0f:F3} CMM)\n" +
                $"  Signed flow        : {flowRate:F6} m^3/s\n" +
                $"  Velocity (lattice) : ({InletVelocityLat.x:F6}, {InletVelocityLat.y:F6}, {InletVelocityLat.z:F6})\n" +
                $"  Flow direction     : ({volumeFlowDirectionWorld.x:F4}, {volumeFlowDirectionWorld.y:F4}, {volumeFlowDirectionWorld.z:F4})\n" +
                $"  Tangential axes    : A={FormatAxis(tangentA)}, B={FormatAxis(tangentB)}\n" +
                $"  Tangential angles  : A={volumeFlowTangentialAngleAdeg:F2} deg, B={volumeFlowTangentialAngleBdeg:F2} deg\n" +
                $"  Inlet temp (degC)  : {inletTemperatureDegC:F2}\n" +
                $"  Inlet temp (LBM)   : {inletTemperatureLBM:F6}\n" +
                $"  Warning            : {(area <= 1e-12f ? "Patch area is zero." : "None")}\n" +
                $"  Cell bounds        : {indexRangeText}";
        }

        return
            $"[Outlet] {name}\n" +
            $"  Input mode         : {effectiveMode}\n" +
            $"  Plane / Normal     : {axisText}, planeIndex={planeIndex}\n" +
            $"  Patch area         : {area:F6} m^2 ({PatchCellCount} cells)\n" +
            $"  Outlet rho target  : {RhoOut:F6}\n" +
            $"  Base rho target    : {BaseRhoOut:F6}\n" +
            $"  Adaptive offset    : {adaptiveRhoOutOffset:+0.000000;-0.000000;0.000000}\n" +
            $"  Flux correction    : {EnableMassFluxCorrection}\n" +
            $"  Target normal phys : {targetOutletNormalSpeedPhys:F4} m/s\n" +
            $"  Target normal lat  : {TargetOutletNormalSpeedLat:F6}\n" +
            $"  Target flow         : {targetFlowRateM3psCached:F6} m^3/s ({targetFlowRateCMMCached:F3} CMM)\n" +
            $"  Normal blend       : {outletNormalVelocityBlend:F3}\n" +
            $"  Rho anchor         : {outletRhoAnchor:F3}\n" +
            $"  Warning            : {(area <= 1e-12f ? "Patch area is zero." : "None")}\n" +
            $"  Force Full Blend    : {forceFullTargetNormalBlendForDebug}\n" +
            $"  Force Zero RhoAnchor: {forceZeroRhoAnchorForDebug}\n" +
            $"  Cell bounds        : {indexRangeText}";
    }

    private static void BuildPatchFromBox(
        uint3 bMin, uint3 bMax,
        Axis axis, Sign sign,
        uint Nx, uint Ny, uint Nz,
        int offsetCells,
        out uint3 outMin, out uint3 outMax, out int outPlane)
    {
        int face = (sign == Sign.Positive)
            ? SelectAxis(bMax, axis)
            : SelectAxis(bMin, axis);

        int dir = (sign == Sign.Positive) ? +1 : -1;

        int idx = face - dir * (1 + offsetCells);
        outPlane = ClampIndex(idx, axis, Nx, Ny, Nz);

        uint2 tMin = SelectAxisPairMin(bMin, axis);
        uint2 tMax = SelectAxisPairMax(bMax, axis);
        int2 clamped = ClampPair(tMin, tMax, axis, Nx, Ny, Nz);

        ComposeRectOnPlane(outPlane, clamped, axis, out outMin, out outMax);
    }

    private static Axis PickDominantAxis(Vector3 v)
    {
        v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        if (v.x >= v.y && v.x >= v.z) return Axis.X;
        if (v.y >= v.x && v.y >= v.z) return Axis.Y;
        return Axis.Z;
    }

    private Vector3 BuildVolumeFlowVelocityScaleWorld(out Vector3 normalizedDirection)
    {
        Vector3 inwardNormal = GetInwardNormalWorld();
        if (inwardNormal.sqrMagnitude < 1e-12f)
            inwardNormal = Vector3.back;

        GetTangentialAxes(out Vector3 tangentA, out Vector3 tangentB);

        float angleA = Mathf.Clamp(volumeFlowTangentialAngleAdeg, -80.0f, 80.0f);
        float angleB = Mathf.Clamp(volumeFlowTangentialAngleBdeg, -80.0f, 80.0f);

        Vector3 direction =
            inwardNormal +
            tangentA * Mathf.Tan(angleA * Mathf.Deg2Rad) +
            tangentB * Mathf.Tan(angleB * Mathf.Deg2Rad);

        if (direction.sqrMagnitude < 1e-12f)
            direction = inwardNormal;

        float normalDot = Vector3.Dot(direction, inwardNormal);
        if (normalDot <= 1e-6f)
        {
            direction = inwardNormal;
            normalDot = 1.0f;
        }

        normalizedDirection = direction.normalized;

        // Preserve the requested volume flow rate by keeping the inward-normal
        // velocity component equal to volumeFlowRate / patchArea.
        return direction / Mathf.Max(normalDot, 1e-6f);
    }

    private Vector3 GetInwardNormalWorld()
    {
        Vector3 outwardNormal = planeNormalWorld;
        if (outwardNormal.sqrMagnitude < 1e-12f)
            outwardNormal = faceNormalWorld;
        if (outwardNormal.sqrMagnitude < 1e-12f)
            outwardNormal = transform.forward;
        if (outwardNormal.sqrMagnitude < 1e-12f)
            outwardNormal = Vector3.forward;

        return -outwardNormal.normalized;
    }

    private void GetTangentialAxes(out Vector3 tangentA, out Vector3 tangentB)
    {
        switch (snappedAxis)
        {
            case Axis.X:
                tangentA = Vector3.up;
                tangentB = Vector3.forward;
                break;
            case Axis.Y:
                tangentA = Vector3.right;
                tangentB = Vector3.forward;
                break;
            default:
                tangentA = Vector3.right;
                tangentB = Vector3.up;
                break;
        }
    }

    private static string FormatAxis(Vector3 axis)
    {
        if (axis == Vector3.right) return "+X";
        if (axis == Vector3.left) return "-X";
        if (axis == Vector3.up) return "+Y";
        if (axis == Vector3.down) return "-Y";
        if (axis == Vector3.forward) return "+Z";
        if (axis == Vector3.back) return "-Z";

        return $"({axis.x:F1},{axis.y:F1},{axis.z:F1})";
    }

    private static Sign PickSign(Vector3 v, Axis a)
    {
        float s = (a == Axis.X) ? v.x : (a == Axis.Y ? v.y : v.z);
        return (s >= 0f) ? Sign.Positive : Sign.Negative;
    }

    private static Vector3 AxisToWorld(Axis a)
    {
        return a switch
        {
            Axis.X => Vector3.right,
            Axis.Y => Vector3.up,
            _ => Vector3.forward
        };
    }

    private static void BoundsToGrid(
        Bounds b, Vector3 domMinW, float dx,
        uint Nx, uint Ny, uint Nz,
        out uint3 gMin, out uint3 gMax)
    {
        Vector3 minRel = b.min - domMinW;
        Vector3 maxRel = b.max - domMinW;

        int ix0 = Mathf.FloorToInt(minRel.x / dx);
        int iy0 = Mathf.FloorToInt(minRel.y / dx);
        int iz0 = Mathf.FloorToInt(minRel.z / dx);
        int ix1 = Mathf.FloorToInt(maxRel.x / dx);
        int iy1 = Mathf.FloorToInt(maxRel.y / dx);
        int iz1 = Mathf.FloorToInt(maxRel.z / dx);

        gMin = new uint3(
            (uint)Mathf.Clamp(ix0, 0, (int)Nx - 1),
            (uint)Mathf.Clamp(iy0, 0, (int)Ny - 1),
            (uint)Mathf.Clamp(iz0, 0, (int)Nz - 1));

        gMax = new uint3(
            (uint)Mathf.Clamp(ix1, 0, (int)Nx - 1),
            (uint)Mathf.Clamp(iy1, 0, (int)Ny - 1),
            (uint)Mathf.Clamp(iz1, 0, (int)Nz - 1));
    }

    private static int ClampIndex(int idx, Axis axis, uint Nx, uint Ny, uint Nz)
    {
        int hi = (int)((axis == Axis.X) ? Nx : (axis == Axis.Y) ? Ny : Nz) - 2;
        return Mathf.Clamp(idx, 1, hi);
    }

    private static int SelectAxis(uint3 v, Axis a)
    {
        return (int)(a == Axis.X ? v.x : (a == Axis.Y ? v.y : v.z));
    }

    private static uint2 SelectAxisPairMin(uint3 v, Axis normal)
    {
        return normal switch
        {
            Axis.X => new uint2(v.y, v.z),
            Axis.Y => new uint2(v.x, v.z),
            _ => new uint2(v.x, v.y)
        };
    }

    private static uint2 SelectAxisPairMax(uint3 v, Axis normal)
    {
        return normal switch
        {
            Axis.X => new uint2(v.y, v.z),
            Axis.Y => new uint2(v.x, v.z),
            _ => new uint2(v.x, v.y)
        };
    }

    private static int2 ClampPair(uint2 pMin, uint2 pMax, Axis normal, uint Nx, uint Ny, uint Nz)
    {
        int t0Min, t0Max, t1Min, t1Max;

        if (normal == Axis.X)
        {
            t0Min = Mathf.Clamp((int)pMin.x, 1, (int)Ny - 2);
            t0Max = Mathf.Clamp((int)pMax.x, 1, (int)Ny - 2);
            t1Min = Mathf.Clamp((int)pMin.y, 1, (int)Nz - 2);
            t1Max = Mathf.Clamp((int)pMax.y, 1, (int)Nz - 2);
        }
        else if (normal == Axis.Y)
        {
            t0Min = Mathf.Clamp((int)pMin.x, 1, (int)Nx - 2);
            t0Max = Mathf.Clamp((int)pMax.x, 1, (int)Nx - 2);
            t1Min = Mathf.Clamp((int)pMin.y, 1, (int)Nz - 2);
            t1Max = Mathf.Clamp((int)pMax.y, 1, (int)Nz - 2);
        }
        else
        {
            t0Min = Mathf.Clamp((int)pMin.x, 1, (int)Nx - 2);
            t0Max = Mathf.Clamp((int)pMax.x, 1, (int)Nx - 2);
            t1Min = Mathf.Clamp((int)pMin.y, 1, (int)Ny - 2);
            t1Max = Mathf.Clamp((int)pMax.y, 1, (int)Ny - 2);
        }

        if (t0Min > t0Max) t0Min = t0Max;
        if (t1Min > t1Max) t1Min = t1Max;

        return new int2(
            (t0Min << 16) | (t0Max & 0xFFFF),
            (t1Min << 16) | (t1Max & 0xFFFF));
    }

    private static void ComposeRectOnPlane(int planeIdx, int2 packedRanges, Axis normal,
        out uint3 minOut, out uint3 maxOut)
    {
        int aMin = (packedRanges.x >> 16) & 0xFFFF;
        int aMax = packedRanges.x & 0xFFFF;
        int bMin = (packedRanges.y >> 16) & 0xFFFF;
        int bMax = packedRanges.y & 0xFFFF;

        switch (normal)
        {
            case Axis.X:
                minOut = new uint3((uint)planeIdx, (uint)aMin, (uint)bMin);
                maxOut = new uint3((uint)planeIdx, (uint)aMax, (uint)bMax);
                break;

            case Axis.Y:
                minOut = new uint3((uint)aMin, (uint)planeIdx, (uint)bMin);
                maxOut = new uint3((uint)aMax, (uint)planeIdx, (uint)bMax);
                break;

            default:
                minOut = new uint3((uint)aMin, (uint)bMin, (uint)planeIdx);
                maxOut = new uint3((uint)aMax, (uint)bMax, (uint)planeIdx);
                break;
        }
    }

    void OnDrawGizmosSelected()
    {
        Refresh();

        var ctrl = SimulationController.Instance;
        if (ctrl == null || ctrl.DomainRoot == null) return;

        float dx = ctrl.CellSize;
        Vector3 domMinW = ctrl.DomainRoot.position - ctrl.DomainRoot.localScale * 0.5f;

        Bounds patch = ComputePatchWorldBounds(domMinW, dx);

        if (drawPatchBounds)
        {
            Gizmos.color = (kind == Kind.Inlet) ? inletPatchColor : outletPatchColor;
            Gizmos.DrawCube(patch.center, patch.size);
            Gizmos.DrawWireCube(patch.center, patch.size);
        }

        Gizmos.color = normalColor;
        Gizmos.DrawRay(patch.center, faceNormalWorld * 5f * dx);

        Gizmos.color = Color.magenta;
        Gizmos.DrawRay(patch.center, planeNormalWorld * 5f * dx);

        if (kind == Kind.Inlet)
        {
            Vector3 v = windSpeedPhys;
            if (v.sqrMagnitude > 1e-12f)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawRay(patch.center, v.normalized * 6f * dx);
            }
        }

        if (drawCellsIndividually)
        {
            Gizmos.color = cellWireColor;
            Vector3 size = new Vector3(dx, dx, dx);

            if (snappedAxis == Axis.X)
            {
                int x = planeIndex;
                for (int y = (int)minIdx.y; y <= (int)maxIdx.y; ++y)
                for (int z = (int)minIdx.z; z <= (int)maxIdx.z; ++z)
                {
                    Vector3 c = domMinW + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * dx;
                    Gizmos.DrawWireCube(c, size);
                }
            }
            else if (snappedAxis == Axis.Y)
            {
                int y = planeIndex;
                for (int x = (int)minIdx.x; x <= (int)maxIdx.x; ++x)
                for (int z = (int)minIdx.z; z <= (int)maxIdx.z; ++z)
                {
                    Vector3 c = domMinW + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * dx;
                    Gizmos.DrawWireCube(c, size);
                }
            }
            else
            {
                int z = planeIndex;
                for (int x = (int)minIdx.x; x <= (int)maxIdx.x; ++x)
                for (int y = (int)minIdx.y; y <= (int)maxIdx.y; ++y)
                {
                    Vector3 c = domMinW + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * dx;
                    Gizmos.DrawWireCube(c, size);
                }
            }
        }

#if UNITY_EDITOR
        if (showIndexLabel)
        {
            string txt =
                $"{kind} (face={(kind == Kind.Inlet ? "+Z" : "-Z")} local)\n" +
                $"snap: {(snappedSign == Sign.Positive ? '+' : '-')}{snappedAxis}  plane={planeIndex}\n" +
                $"x:[{minIdx.x},{maxIdx.x}]  y:[{minIdx.y},{maxIdx.y}]  z:[{minIdx.z},{maxIdx.z}]\n" +
                $"windPhys={windSpeedPhys}  u_lat={InletVelocityLat}\n" +
                $"T_in={inletTemperatureDegC:F2}C ({inletTemperatureLBM:F4} lbm)  rho_out={rhoOut}\n" +
                $"adaptiveRhoOffset={adaptiveRhoOutOffset:+0.000000;-0.000000;0.000000}\n" +
                $"targetOutletNormalPhys={targetOutletNormalSpeedPhys:F4} m/s\n" +
                $"targetOutletNormalLat={TargetOutletNormalSpeedLat:F6}\n" +
                $"normalBlend={outletNormalVelocityBlend:F3}, rhoAnchor={outletRhoAnchor:F3}";
            Handles.color = Color.white;
            Handles.Label(patch.center + Vector3.up * (0.6f * dx), txt);
        }
#endif
    }

    private Bounds ComputePatchWorldBounds(Vector3 domMinW, float dx)
    {
        Vector3 wMin = domMinW + new Vector3(minIdx.x, minIdx.y, minIdx.z) * dx;
        Vector3 wMax = domMinW + new Vector3(maxIdx.x + 1.0f, maxIdx.y + 1.0f, maxIdx.z + 1.0f) * dx;

        Bounds b = new Bounds();
        b.SetMinMax(wMin, wMax);
        return b;
    }
}
