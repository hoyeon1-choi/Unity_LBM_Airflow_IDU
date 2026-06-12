using System.Collections.Generic;
using UnityEngine;
using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using Unity.VisualScripting;

public class ThermalSolver
{
    [StructLayout(LayoutKind.Sequential)]
    public struct HeatSourceData
    {
        public uint thermalBoundaryType;
        public uint3 min;
        public uint3 max;
        public float T_init;
        public float padding;
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct ACSourceData
    {
        public uint3 min;
        public uint3 max;
        public float2 padding;
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct ZHInletPatchData
    {
        public uint3 min;
        public uint3 max;
        public int planeIndex;
        public int normalAxis;
        public int normalSign;
        public float3 uTarget;
        public float T_init;
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct ZHOutletPatchData
    {
        public uint3 min;
        public uint3 max;
        public int planeIndex;
        public int normalAxis;
        public int normalSign;
        public float rhoTarget;

        public float targetNormalSpeed;
        public float normalVelocityBlend;
        public float rhoAnchor;
        public float padding0;
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct RackData
    {
        public uint3 min;
        public uint3 max;
        public uint openAxis;
        public uint shell;
        public float3 invK_lat;
        public float betaF;
        public float phi;
        public float qdot;
        public float padding;
    }

    public enum TurbulenceModel
    {
        None = 0,
        Smagorinsky = 1,
        WALE = 2
    }

    private GameObject _domain;
    private uint _nx, _ny, _nz;
    private float _cellSize, _tau_f, _tau_T, _T_cold, _T_ref, _beta, _gravity_y;

    private ComputeShader _lbmComputeShader;
    private ComputeShaderWrapper<Kernels, Uniforms> _shaderWrapper;

    private const int DistGroupCount = 5;
    private static readonly int[] DistGroupSizes = { 6, 5, 5, 5, 5 };
    private GraphicsBuffer[] _prevDistBuffers = new GraphicsBuffer[DistGroupCount];
    private GraphicsBuffer[] _curDistBuffers = new GraphicsBuffer[DistGroupCount];
    private GraphicsBuffer _velocityRhoBuffer, _temperatureBuffer, _fieldBuffer;

    private DeviceObstacles[] _heatSources;
    private Racks[] _racks;
    private ACSource[] _acSources;
    private LBMZouHeBox[] _zhBoxes;

    private GraphicsBuffer _heatSourceDataBuffer;
    private GraphicsBuffer _rackDataBuffer;
    private GraphicsBuffer _acSourceDataBuffer;
    private GraphicsBuffer _zhInletBuffer;
    private GraphicsBuffer _zhOutletBuffer;
    private GraphicsBuffer _debugThermalClampCounterBuffer;

    private RenderTexture _velocityTexture, _thermalTexture;

    public GraphicsBuffer[] CurDistBuffers => _curDistBuffers;
    public GraphicsBuffer[] PrevDistBuffers => _prevDistBuffers;
    public GraphicsBuffer VelocityRhoBuffer => _velocityRhoBuffer;
    public GraphicsBuffer TemperatureBuffer => _temperatureBuffer;
    public GraphicsBuffer FieldBuffer => _fieldBuffer;

    public GraphicsBuffer HeatSourcesDataBuffer => _heatSourceDataBuffer;
    public GraphicsBuffer RackDataBuffer => _rackDataBuffer;
    public GraphicsBuffer ACSourcesDataBuffer => _acSourceDataBuffer;
    public GraphicsBuffer ZHInletBuffer => _zhInletBuffer;
    public GraphicsBuffer ZHOutletBuffer => _zhOutletBuffer;
    public GraphicsBuffer DebugThermalClampCounterBuffer => _debugThermalClampCounterBuffer;

    public RenderTexture VelocityTexture => _velocityTexture;
    public RenderTexture ThermalTexture => _thermalTexture;

    private const int Q_f = 19;
    private const int Q_t = 7;

    private bool _fieldDirty = true;
    private int _visualizeEvery = 4;
    private ulong _stepCounter = 0;

    private TurbulenceModel _turbulenceModel = TurbulenceModel.Smagorinsky;
    private float _turbulenceModelConstant = 0.03f;
    private float _turbulentPrandtl = 0.7f;
    private GraphicsBuffer _debugOutletBcStateBuffer;
    public GraphicsBuffer DebugOutletBcStateBuffer => _debugOutletBcStateBuffer;

    public ThermalSolver(GameObject domain, ComputeShader shader,
                         float cellSize, float tau_f, float tau_T,
                         float T_ref, float beta, float gravity_y,
                         uint nx, uint ny, uint nz,
                         DeviceObstacles[] heatSources, Racks[] racks,
                         ACSource[] acSources, LBMZouHeBox[] zhBoxes)
    {
        _domain = domain;
        _nx = nx;
        _ny = ny;
        _nz = nz;
        _cellSize = cellSize;
        _tau_f = tau_f;
        _tau_T = tau_T;
        _T_ref = T_ref;
        _beta = beta;
        _gravity_y = gravity_y;
        _lbmComputeShader = shader;
        _heatSources = heatSources;
        _racks = racks;
        _acSources = acSources;
        _zhBoxes = zhBoxes;

        _shaderWrapper = new ComputeShaderWrapper<Kernels, Uniforms>(_lbmComputeShader);

        InitializeHeatSourceBuffer();
        InitializeRackBuffer();
        InitializeACSourceBuffer();
        InitializeZouHeBoxBuffer();
        InitializeOtherBuffers();

        SetBuffers();
        SetShaderParameters();
        DispatchInitialize();

        _fieldDirty = true;
        _stepCounter = 0;
    }

    public void Step()
    {
        if (_fieldDirty)
        {
            _shaderWrapper.Dispatch(Kernels.field_update, _nx, _ny, _nz);
            _fieldDirty = false;
        }

        _shaderWrapper.Dispatch(Kernels.collision, _nx, _ny, _nz);
        _shaderWrapper.Dispatch(Kernels.streaming, _nx, _ny, _nz);
        _shaderWrapper.Dispatch(Kernels.zou_he_bc, _nx, _ny, _nz);
        _shaderWrapper.Dispatch(Kernels.calculate_macro, _nx, _ny, _nz);

        _stepCounter++;
        if (_visualizeEvery <= 1 || (_stepCounter % (ulong)_visualizeEvery) == 0)
        {
            _shaderWrapper.Dispatch(Kernels.visualize, _nx, _ny, _nz);
        }
    }

    public void MarkFieldDirty()
    {
        _fieldDirty = true;
    }

    public void MarkAllDynamicInputsDirty()
    {
        _fieldDirty = true;
    }

    public void SetVisualizeInterval(int everySteps)
    {
        _visualizeEvery = Mathf.Max(1, everySteps);
    }

    public void Dispose()
    {
        Debug.Log("Disposing GraphicBuffers..");

        _heatSources = null;
        _racks = null;
        _acSources = null;
        _zhBoxes = null;

        for (int g = 0; g < DistGroupCount; ++g)
        {
            _curDistBuffers[g]?.Dispose();
            _prevDistBuffers[g]?.Dispose();
            _curDistBuffers[g] = null;
            _prevDistBuffers[g] = null;
        }

        _velocityRhoBuffer?.Dispose();
        _temperatureBuffer?.Dispose();
        _fieldBuffer?.Dispose();
        _heatSourceDataBuffer?.Dispose();
        _rackDataBuffer?.Dispose();
        _acSourceDataBuffer?.Dispose();
        _zhInletBuffer?.Dispose();
        _zhOutletBuffer?.Dispose();
        _debugThermalClampCounterBuffer?.Dispose();
        _debugOutletBcStateBuffer?.Dispose();
        
        _velocityRhoBuffer = null;
        _temperatureBuffer = null;
        _fieldBuffer = null;
        _heatSourceDataBuffer = null;
        _rackDataBuffer = null;
        _acSourceDataBuffer = null;
        _zhInletBuffer = null;
        _zhOutletBuffer = null;
        _debugThermalClampCounterBuffer = null;
        _debugOutletBcStateBuffer = null;

        _velocityTexture?.Release();
        _thermalTexture?.Release();
    }

    public void ResetField()
    {
        _fieldDirty = true;
        _stepCounter = 0;
        DispatchInitialize();
    }

    private void InitializeHeatSourceBuffer()
    {
        if (_heatSources == null || _heatSources.Length == 0)
        {
            int stride = Marshal.SizeOf<HeatSourceData>();
            _heatSourceDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, stride);
            return;
        }

        Vector3 domainMinCorner = _domain.transform.position - (_domain.transform.localScale / 2.0f);
        List<HeatSourceData> hsDataList = new List<HeatSourceData>();

        foreach (var hs in _heatSources)
        {
            hs.CalculateBounds(_cellSize, domainMinCorner, _nx, _ny, _nz);

            hsDataList.Add(new HeatSourceData
            {
                thermalBoundaryType = (uint)hs.BoundaryType,
                min = hs.MinIdx,
                max = hs.MaxIdx,
                T_init = 0.0f,
            });
        }

        if (hsDataList.Count > 0)
        {
            int stride = Marshal.SizeOf<HeatSourceData>();
            _heatSourceDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, hsDataList.Count, stride);
            _heatSourceDataBuffer.SetData(hsDataList.ToArray());
        }
    }

    private void InitializeRackBuffer()
    {
        if (_racks == null || _racks.Length == 0)
        {
            int stride = Marshal.SizeOf<RackData>();
            _rackDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, stride);
            return;
        }

        Vector3 domainMinCorner = _domain.transform.position - (_domain.transform.localScale / 2.0f);
        List<RackData> rackDataList = new List<RackData>();

        foreach (var rack in _racks)
        {
            rack.CalculateBounds(_cellSize, domainMinCorner, _nx, _ny, _nz);
            uint openAxis = rack.GetOpenAxis();

            rackDataList.Add(new RackData
            {
                min = rack.MinIdx,
                max = rack.MaxIdx,
                openAxis = openAxis,
                shell = rack.Shell,
                invK_lat = rack.InvKLat,
                betaF = rack.BetaF,
                phi = rack.Phi,
                qdot = rack.Power ? rack.Qdot : 0.0f,
            });
        }

        if (rackDataList.Count > 0)
        {
            int stride = Marshal.SizeOf<RackData>();
            _rackDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rackDataList.Count, stride);
            _rackDataBuffer.SetData(rackDataList.ToArray());
        }
    }

    private void InitializeACSourceBuffer()
    {
        if (_acSources == null || _acSources.Length == 0)
        {
            int stride = Marshal.SizeOf<ACSourceData>();
            _acSourceDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, stride);
            return;
        }

        Vector3 domainMinCorner = _domain.transform.position - (_domain.transform.localScale / 2.0f);
        List<ACSourceData> acsDataList = new List<ACSourceData>();

        foreach (var acs in _acSources)
        {
            acs.CalculateBounds(_cellSize, domainMinCorner, _nx, _ny, _nz);

            acsDataList.Add(new ACSourceData
            {
                min = acs.MinIdx,
                max = acs.MaxIdx,
            });
        }

        if (acsDataList.Count > 0)
        {
            int stride = Marshal.SizeOf<ACSourceData>();
            _acSourceDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, acsDataList.Count, stride);
            _acSourceDataBuffer.SetData(acsDataList.ToArray());
        }
    }

    private void InitializeZouHeBoxBuffer()
    {
        if (_zhBoxes == null || _zhBoxes.Length == 0)
        {
            int stride_inlet = Marshal.SizeOf<ZHInletPatchData>();
            _zhInletBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, stride_inlet);

            int stride_outlet = Marshal.SizeOf<ZHOutletPatchData>();
            _zhOutletBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, stride_outlet);
            return;
        }

        var inletList = new List<ZHInletPatchData>();
        var outletList = new List<ZHOutletPatchData>();

        foreach (var box in _zhBoxes)
        {
            if (box == null || !box.Power)
                continue;

            box.Refresh();

            int a = (box.NormalAxis == LBMZouHeBox.Axis.X) ? 0 :
                    (box.NormalAxis == LBMZouHeBox.Axis.Y) ? 1 : 2;
            int s = (box.NormalSign == LBMZouHeBox.Sign.Positive) ? +1 : -1;

            if (box.PatchKind == LBMZouHeBox.Kind.Inlet)
            {
                inletList.Add(new ZHInletPatchData
                {
                    min = box.MinIdx,
                    max = box.MaxIdx,
                    planeIndex = box.PlaneIndex,
                    normalAxis = a,
                    normalSign = s,
                    uTarget = box.InletVelocityLat,
                    T_init = box.InletTemperature,
                });
            }
            else
            {
                outletList.Add(new ZHOutletPatchData
                {
                    min = box.MinIdx,
                    max = box.MaxIdx,
                    planeIndex = box.PlaneIndex,
                    normalAxis = a,
                    normalSign = s,
                    rhoTarget = box.RhoOut,
                    targetNormalSpeed = box.TargetOutletNormalSpeedLat,
                    normalVelocityBlend = box.OutletNormalVelocityBlend,
                    rhoAnchor = box.OutletRhoAnchor,
                    padding0 = 0.0f,
                });
            }
        }

        if (inletList.Count > 0)
        {
            int stride = Marshal.SizeOf<ZHInletPatchData>();
            _zhInletBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inletList.Count, stride);
            _zhInletBuffer.SetData(inletList.ToArray());
        }
        else
        {
            int stride = Marshal.SizeOf<ZHInletPatchData>();
            _zhInletBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, stride);
        }

        if (outletList.Count > 0)
        {
            int stride = Marshal.SizeOf<ZHOutletPatchData>();
            _zhOutletBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, outletList.Count, stride);
            _zhOutletBuffer.SetData(outletList.ToArray());
        }
        else
        {
            int stride = Marshal.SizeOf<ZHOutletPatchData>();
            _zhOutletBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, stride);
        }
    }

    private void InitializeOtherBuffers()
    {
        int cellCount = checked((int)(_nx * _ny * _nz));

        for (int g = 0; g < DistGroupCount; ++g)
        {
            int count = checked(cellCount * DistGroupSizes[g]);
            _prevDistBuffers[g] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(float));
            _curDistBuffers[g] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(float));
        }

        _velocityRhoBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, Marshal.SizeOf<float4>());
        _temperatureBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(float));
        _fieldBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(uint));

        _debugThermalClampCounterBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            4,
            sizeof(uint));

        _debugOutletBcStateBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            4,
            Marshal.SizeOf<Vector4>());

        _debugOutletBcStateBuffer.SetData(new Vector4[4]);

        _velocityTexture = CreateTexture((int)_nx, (int)_ny, (int)_nz, RenderTextureFormat.ARGBFloat);
        _thermalTexture = CreateTexture((int)_nx, (int)_ny, (int)_nz, RenderTextureFormat.RFloat);
    }

    private void BindInputDistBuffers(Kernels[] kernels, GraphicsBuffer[] source)
    {
        _shaderWrapper.SetBuffer(kernels, Uniforms.f_in_0, source[0]);
        _shaderWrapper.SetBuffer(kernels, Uniforms.f_in_1, source[1]);
        _shaderWrapper.SetBuffer(kernels, Uniforms.f_in_2, source[2]);
        _shaderWrapper.SetBuffer(kernels, Uniforms.f_in_3, source[3]);
        _shaderWrapper.SetBuffer(kernels, Uniforms.f_in_4, source[4]);
    }

    private void BindOutputDistBuffers(Kernels[] kernels, GraphicsBuffer[] target)
    {
        _shaderWrapper.SetBuffer(kernels, Uniforms.f_out_0, target[0]);
        _shaderWrapper.SetBuffer(kernels, Uniforms.f_out_1, target[1]);
        _shaderWrapper.SetBuffer(kernels, Uniforms.f_out_2, target[2]);
        _shaderWrapper.SetBuffer(kernels, Uniforms.f_out_3, target[3]);
        _shaderWrapper.SetBuffer(kernels, Uniforms.f_out_4, target[4]);
    }

    private void SetBuffers()
    {
        var initializeKernels = new[] { Kernels.initialize };
        var collisionKernels = new[] { Kernels.collision };
        var streamingKernels = new[] { Kernels.streaming };
        var zhKernels = new[] { Kernels.initialize, Kernels.field_update, Kernels.zou_he_bc };
        var macroKernels = new[] { Kernels.calculate_macro };
        var visualizeKernels = new[] { Kernels.visualize };

        BindOutputDistBuffers(initializeKernels, _prevDistBuffers);

        BindInputDistBuffers(collisionKernels, _prevDistBuffers);
        BindOutputDistBuffers(collisionKernels, _curDistBuffers);

        BindInputDistBuffers(streamingKernels, _curDistBuffers);
        BindOutputDistBuffers(streamingKernels, _prevDistBuffers);

        BindOutputDistBuffers(new[] { Kernels.zou_he_bc }, _prevDistBuffers);
        BindInputDistBuffers(macroKernels, _prevDistBuffers);

        var allRwScalarKernels = new[]
        {
            Kernels.initialize, Kernels.field_update, Kernels.collision,
            Kernels.streaming, Kernels.zou_he_bc,
            Kernels.calculate_macro, Kernels.visualize
        };

        _shaderWrapper.SetBuffer(allRwScalarKernels, Uniforms.velocity_rho, _velocityRhoBuffer);
        _shaderWrapper.SetBuffer(allRwScalarKernels, Uniforms.temperature, _temperatureBuffer);
        _shaderWrapper.SetBuffer(allRwScalarKernels, Uniforms.field, _fieldBuffer);

        _shaderWrapper.SetBuffer(new[] { Kernels.initialize, Kernels.field_update, Kernels.calculate_macro }, Uniforms.heat_sources, _heatSourceDataBuffer);
        _shaderWrapper.SetBuffer(new[] { Kernels.initialize, Kernels.field_update, Kernels.collision, Kernels.calculate_macro }, Uniforms.racks, _rackDataBuffer);
        _shaderWrapper.SetBuffer(new[] { Kernels.initialize, Kernels.field_update, Kernels.calculate_macro }, Uniforms.ac_sources, _acSourceDataBuffer);
        _shaderWrapper.SetBuffer(zhKernels, Uniforms.zh_inlets, _zhInletBuffer);
        _shaderWrapper.SetBuffer(zhKernels, Uniforms.zh_outlets, _zhOutletBuffer);

        _shaderWrapper.SetBuffer(
            new[] { Kernels.zou_he_bc },
            Uniforms.debug_thermal_clamp_counters,
            _debugThermalClampCounterBuffer);

        _shaderWrapper.SetBuffer(
            new[] { Kernels.zou_he_bc },
            Uniforms.debug_outlet_bc_state,
            _debugOutletBcStateBuffer);

        _shaderWrapper.SetTexture(visualizeKernels, Uniforms.velocity_texture, _velocityTexture);
        _shaderWrapper.SetTexture(visualizeKernels, Uniforms.thermal_texture, _thermalTexture);
    }

    private void SetShaderParameters()
    {
        _shaderWrapper.SetFloat(Uniforms.tau_f, _tau_f);
        _shaderWrapper.SetFloat(Uniforms.tau_T, _tau_T);
        _shaderWrapper.SetFloat(Uniforms.T_ref, _T_ref);
        _shaderWrapper.SetFloat(Uniforms.beta, _beta);
        _shaderWrapper.SetFloat(Uniforms.gravity_y, _gravity_y);
        _shaderWrapper.SetInt(Uniforms.nx, (int)_nx);
        _shaderWrapper.SetInt(Uniforms.ny, (int)_ny);
        _shaderWrapper.SetInt(Uniforms.nz, (int)_nz);
        _shaderWrapper.SetInt(Uniforms.heat_source_count, _heatSourceDataBuffer?.count ?? 0);
        _shaderWrapper.SetInt(Uniforms.rack_count, _rackDataBuffer?.count ?? 0);
        _shaderWrapper.SetInt(Uniforms.ac_source_count, _acSourceDataBuffer?.count ?? 0);
        _shaderWrapper.SetInt(Uniforms.zh_inlet_count, _zhInletBuffer?.count ?? 0);
        _shaderWrapper.SetInt(Uniforms.zh_outlet_count, _zhOutletBuffer?.count ?? 0);

        _shaderWrapper.SetInt(Uniforms.turbulence_model, (int)_turbulenceModel);
        _shaderWrapper.SetFloat(Uniforms.C_sgs, _turbulenceModelConstant);
        _shaderWrapper.SetFloat(Uniforms.Pr_t, _turbulentPrandtl);
        _shaderWrapper.SetFloat(Uniforms.s_relax_min, 1e-6f);
        _shaderWrapper.SetFloat(Uniforms.s_relax_max, 1.98f);
    }

    public void SyncSourcesAtRuntime(DeviceObstacles[] heatSources, Racks[] racks,
                                     ACSource[] acSources, LBMZouHeBox[] zhBoxes)
    {
        _heatSources = heatSources;
        _racks = racks;
        _acSources = acSources;
        _zhBoxes = zhBoxes;

        Vector3 domainMinCorner = _domain.transform.position - (_domain.transform.localScale / 2.0f);

        var hsList = new List<HeatSourceData>();
        if (_heatSources != null && _heatSources.Length > 0)
        {
            foreach (var hs in _heatSources)
            {
                hs.CalculateBounds(_cellSize, domainMinCorner, _nx, _ny, _nz);

                hsList.Add(new HeatSourceData
                {
                    thermalBoundaryType = (uint)hs.BoundaryType,
                    min = hs.MinIdx,
                    max = hs.MaxIdx,
                    T_init = hs.Temperature,
                });
            }
        }

        ResizeOrUpload(ref _heatSourceDataBuffer, hsList, Marshal.SizeOf<HeatSourceData>(),
                       new[] { Kernels.initialize, Kernels.field_update, Kernels.calculate_macro },
                       Uniforms.heat_sources, Uniforms.heat_source_count);

        var rackList = new List<RackData>();
        if (_racks != null && _racks.Length > 0)
        {
            foreach (var rack in _racks)
            {
                rack.CalculateBounds(_cellSize, domainMinCorner, _nx, _ny, _nz);
                uint openAxis = rack.GetOpenAxis();
                rackList.Add(new RackData
                {
                    min = rack.MinIdx,
                    max = rack.MaxIdx,
                    openAxis = openAxis,
                    shell = rack.Shell,
                    invK_lat = rack.InvKLat,
                    betaF = rack.BetaF,
                    phi = rack.Phi,
                    qdot = rack.Power ? rack.Qdot : 0.0f,
                });
            }
        }

        ResizeOrUpload(ref _rackDataBuffer, rackList, Marshal.SizeOf<RackData>(),
                       new[] { Kernels.initialize, Kernels.field_update, Kernels.collision, Kernels.calculate_macro },
                       Uniforms.racks, Uniforms.rack_count);

        var acList = new List<ACSourceData>();
        if (_acSources != null && _acSources.Length > 0)
        {
            foreach (var ac in _acSources)
            {
                ac.CalculateBounds(_cellSize, domainMinCorner, _nx, _ny, _nz);
                acList.Add(new ACSourceData
                {
                    min = ac.MinIdx,
                    max = ac.MaxIdx
                });
            }
        }

        ResizeOrUpload(ref _acSourceDataBuffer, acList, Marshal.SizeOf<ACSourceData>(),
                       new[] { Kernels.initialize, Kernels.field_update, Kernels.calculate_macro },
                       Uniforms.ac_sources, Uniforms.ac_source_count);

        var inletList = new List<ZHInletPatchData>();
        var outletList = new List<ZHOutletPatchData>();
        if (_zhBoxes != null && _zhBoxes.Length > 0)
        {
            foreach (var box in _zhBoxes)
            {
                box.Refresh();

                int a = (box.NormalAxis == LBMZouHeBox.Axis.X) ? 0 :
                        (box.NormalAxis == LBMZouHeBox.Axis.Y) ? 1 : 2;
                int s = (box.NormalSign == LBMZouHeBox.Sign.Positive) ? +1 : -1;

                if (box.PatchKind == LBMZouHeBox.Kind.Inlet)
                {
                    inletList.Add(new ZHInletPatchData
                    {
                        min = box.MinIdx,
                        max = box.MaxIdx,
                        planeIndex = box.PlaneIndex,
                        normalAxis = a,
                        normalSign = s,
                        uTarget = box.InletVelocityLat,
                        T_init = box.InletTemperature
                    });
                }
                else
                {
                    outletList.Add(new ZHOutletPatchData
                    {
                        min = box.MinIdx,
                        max = box.MaxIdx,
                        planeIndex = box.PlaneIndex,
                        normalAxis = a,
                        normalSign = s,
                        rhoTarget = box.RhoOut,
                        targetNormalSpeed = box.TargetOutletNormalSpeedLat,
                        normalVelocityBlend = box.OutletNormalVelocityBlend,
                        rhoAnchor = box.OutletRhoAnchor,
                        padding0 = 0.0f,
                    });
                }
            }
        }

        ResizeOrUpload(ref _zhInletBuffer, inletList, Marshal.SizeOf<ZHInletPatchData>(),
                       new[] { Kernels.initialize, Kernels.field_update, Kernels.zou_he_bc },
                       Uniforms.zh_inlets, Uniforms.zh_inlet_count);

        ResizeOrUpload(ref _zhOutletBuffer, outletList, Marshal.SizeOf<ZHOutletPatchData>(),
                       new[] { Kernels.initialize, Kernels.field_update, Kernels.zou_he_bc },
                       Uniforms.zh_outlets, Uniforms.zh_outlet_count);

        _fieldDirty = true;
    }

    private void ResizeOrUpload<T>(ref GraphicsBuffer buffer,
                                   List<T> data,
                                   int stride,
                                   Kernels[] kernelsToBind,
                                   Uniforms bufferUniform,
                                   Uniforms countUniform)
    {
        int actualCount = data?.Count ?? 0;
        int bufferCount = Math.Max(1, actualCount);

        if (buffer == null || buffer.count != bufferCount)
        {
            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bufferCount, stride);
            _shaderWrapper.SetBuffer(kernelsToBind, bufferUniform, buffer);
        }

        if (actualCount > 0)
            buffer.SetData(data.ToArray());

        _shaderWrapper.SetInt(countUniform, actualCount);
    }

    public void updateShaderParameters()
    {
        _shaderWrapper.SetFloat(Uniforms.T_ref, _T_ref);
        _shaderWrapper.SetFloat(Uniforms.beta, _beta);
    }

    public void SetCollisionAndForcing(float tauF, float tauT, float gravityLat)
    {
        _tau_f = tauF;
        _tau_T = tauT;
        _gravity_y = gravityLat;
        _shaderWrapper.SetFloat(Uniforms.tau_f, _tau_f);
        _shaderWrapper.SetFloat(Uniforms.tau_T, _tau_T);
        _shaderWrapper.SetFloat(Uniforms.gravity_y, _gravity_y);
    }

    public void SetTurbulenceModel(TurbulenceModel model, float modelConstant, float prT)
    {
        _turbulenceModel = model;
        _turbulenceModelConstant = modelConstant;
        _turbulentPrandtl = prT;

        _shaderWrapper.SetInt(Uniforms.turbulence_model, (int)_turbulenceModel);
        _shaderWrapper.SetFloat(Uniforms.C_sgs, _turbulenceModelConstant);
        _shaderWrapper.SetFloat(Uniforms.Pr_t, _turbulentPrandtl);
    }

    public void ResetDebugThermalClampCounters()
    {
        if (_debugThermalClampCounterBuffer == null)
            return;

        uint[] zero = new uint[4] { 0u, 0u, 0u, 0u };
        _debugThermalClampCounterBuffer.SetData(zero);
    }

    private void DispatchInitialize()
    {
        _shaderWrapper.Dispatch(Kernels.initialize, _nx, _ny, _nz);
    }

    private RenderTexture CreateTexture(int x, int y, int z, RenderTextureFormat format)
    {
        RenderTexture dataTex = new RenderTexture(x, y, 0, format);
        dataTex.volumeDepth = z;
        dataTex.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        dataTex.filterMode = FilterMode.Bilinear;
        dataTex.wrapMode = TextureWrapMode.Clamp;
        dataTex.enableRandomWrite = true;
        dataTex.Create();

        return dataTex;
    }

    private enum Kernels
    {
        initialize,
        field_update,
        collision,
        streaming,
        zou_he_bc,
        calculate_macro,
        visualize,
    }

    private enum Uniforms
    {
        f_in_0,
        f_in_1,
        f_in_2,
        f_in_3,
        f_in_4,
        f_out_0,
        f_out_1,
        f_out_2,
        f_out_3,
        f_out_4,
        field,
        velocity_rho,
        temperature,
        heat_sources,
        racks,
        ac_sources,
        zh_inlets,
        zh_outlets,
        debug_thermal_clamp_counters,
        debug_outlet_bc_state,
        velocity_texture,
        thermal_texture,

        nx,
        ny,
        nz,
        tau_f,
        tau_T,
        beta,
        gravity_y,
        T_cold,
        T_ref,
        heat_source_count,
        rack_count,
        ac_source_count,
        zh_inlet_count,
        zh_outlet_count,

        turbulence_model,
        C_sgs,
        Pr_t,
        s_relax_min,
        s_relax_max,
    }
}