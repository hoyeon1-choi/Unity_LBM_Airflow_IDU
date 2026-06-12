using System.Collections.Generic;
using UnityEngine;
using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine.Rendering;
using System.Text;
using System.IO;

public class LDCSolver
{
    // The data structure to send to GPU
    [StructLayout(LayoutKind.Sequential)] // Correspond memory layouts between C# and HLSL
    public struct HeatSourceData
    {
        public uint type;
        
        // Square
        public uint3 min;   // idx
        public uint3 max;   // idx

        // Cylinder
        public float3 center;   // idx
        public float radius;    // idx

        public float T_init;
        public float padding; // The padding to align a stride into 32Bytes(= 16 * 2)
    };

    private GameObject _domain;
    private uint _nx, _ny, _nz;
    private float _cellSize, _lid_speed, _tau_f, _tau_T, _T_cold, _T_ref, _beta, _gravity_y;
    private string _csvFileName = "";

    private ComputeShader _lbmComputeShader;
    private ComputeShaderWrapper<Kernels, Uniforms> _shaderWrapper;

    // RWStructuredBuffer
    private GraphicsBuffer _curFBuffer, _prevFBuffer, _velocityRhoBuffer, _temperatureBuffer, _fieldBuffer;

    // StructuredBuffer
    private GraphicsBuffer _heatSourceDataBuffer; // StructuredBuffer<HeatSourceData>

    // RWTexture3D
    private RenderTexture _velocityTexture, _thermalTexture;

    // RWStructuredBuffer
    public GraphicsBuffer CurFBuffer => _curFBuffer;                      // [nx*ny*nz*(19+7)], float
    public GraphicsBuffer PrevFBuffer => _prevFBuffer;                    // [nx*ny*nz*(19+7)], float
    public GraphicsBuffer VelocityRhoBuffer => _velocityRhoBuffer;        // [nx*ny*nz], float4
    public GraphicsBuffer TemperatureBuffer => _temperatureBuffer;        // [nx*ny*nz], float
    public GraphicsBuffer FieldBuffer => _fieldBuffer;                    // [nx*ny*nz], uint

    // StructuredBuffer
    public GraphicsBuffer HeatSourcesDataBuffer => _heatSourceDataBuffer; // [hs.count], 32Bytes struct

    // RWTexture3D
    public RenderTexture VelocityTexture => _velocityTexture;
    public RenderTexture ThermalTexture => _thermalTexture;

    private const int Q_f = 19; // D3Q19 for velocity
    private const int Q_t = 7;  // D3Q7 for temperature

    public LDCSolver(GameObject domain, ComputeShader shader,
                         float cellSize, float lid_speed, float tau_f, 
                         float tau_T, float T_ref, float beta, float gravity_y,
                         uint nx, uint ny, uint nz,
                         HeatSource[] heatSources)
    {
        _domain = domain;
        _nx = nx;
        _ny = ny;
        _nz = nz;
        _cellSize = cellSize;
        _lid_speed = lid_speed;
        _tau_f = tau_f;
        _tau_T = tau_T;
        _T_ref = T_ref;
        _beta = beta;
        _gravity_y = gravity_y;
        _lbmComputeShader = shader;

        _shaderWrapper = new ComputeShaderWrapper<Kernels, Uniforms>(_lbmComputeShader);

        InitializeHeatSourceBuffer(heatSources);
        InitializeOtherBuffers();

        SetBuffers();
        SetShaderParameters();

        DispatchInitialize();
    }

    public void Step()
    {
        DispatchSimulate(); // Execute all kernels except "initialize" in order

        (_prevFBuffer, _curFBuffer) = (_curFBuffer, _prevFBuffer); // Swap "f" buffers

        // Swap the shader buffer pointers' references to let the GPU know
        var lbmKernels = new[] { Kernels.collision, Kernels.streaming, Kernels.calculate_macro };
        _shaderWrapper.SetBuffer(lbmKernels, Uniforms.f_prev, _prevFBuffer);
        _shaderWrapper.SetBuffer(lbmKernels, Uniforms.f_cur, _curFBuffer);
    }

    public void Dispose()
    {
        _curFBuffer?.Dispose();
        _prevFBuffer?.Dispose();
        _velocityRhoBuffer?.Dispose();
        _temperatureBuffer?.Dispose();
        _fieldBuffer?.Dispose();
        _heatSourceDataBuffer?.Dispose();

        _velocityTexture?.Release();
        _thermalTexture?.Release();
    }

    public void ResetField()
    {
        DispatchInitialize();
    }

    public void RequestVelocityDataAndSaveToCSV(string fileName=null)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            _csvFileName = $"{System.DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")}";
        }
        else
        {
            _csvFileName = fileName;
        }

        AsyncGPUReadback.Request(_velocityRhoBuffer, OnCompleteReadback);
    }

    private void OnCompleteReadback(AsyncGPUReadbackRequest request)
    {
        if (request.hasError)
        {
            Debug.LogError("GPU readback error.");
            return;
        }

        var data = request.GetData<float4>();
        Debug.Log($"Successfully read back {data.Length} data points from the GPU.");

        var sb = new StringBuilder();
        sb.AppendLine("x,y,u,v");

        for (int i = 0; i < data.Length; i++)
        {
            uint z = (uint)i / (_nx * _ny);
            uint y = ((uint)i - (z * _nx * _ny)) / _nx;
            uint x = (uint)i % _nx;

            float4 velocityRho = data[i] / _lid_speed;

            if (_nz % 2 == 1)
            {
                if (z == _nz / 2)
                {
                    sb.AppendLine($"{(float)x / (_nx - 1)},{(float)y / (_ny - 1)},{velocityRho.x},{velocityRho.y}");
                }
            }
            else
            {
                if (z == _nz / 2 - 1 || z == _nz / 2)
                {
                    sb.AppendLine($"{(float)x / (_nx - 1)},{(float)y / (_ny - 1)},{velocityRho.x},{velocityRho.y}");
                }
            }
        }

        // string path = Path.Combine(Application.dataPath, "Etcs", $"{_csvFileName}.csv");
        string path = Path.Combine("Results", $"{_csvFileName}.csv");
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"Successfully saved velocity data to: {path}");

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }

    private void InitializeHeatSourceBuffer(HeatSource[] heatSources)
    {
        if (heatSources == null || heatSources.Length == 0)
        {
            int stride = Marshal.SizeOf<HeatSourceData>();
            _heatSourceDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, stride);
            return;
        }

        Vector3 domainMinCorner = _domain.transform.position - (_domain.transform.localScale / 2.0f);
        List<HeatSourceData> hsDataList = new List<HeatSourceData>();

        foreach (var hs in heatSources)
        {
            if (hs.Type == HeatSourceType.Square)
            {
                hs.CalculateBounds(_cellSize, domainMinCorner, _nx, _ny, _nz);
            }
            else if (hs.Type == HeatSourceType.Cylinder)
            { 
                hs.CalculateBoundsForCylinder(_cellSize, domainMinCorner, _nx, _ny, _nz);
                // Debug.Log($"center in idx: {hs.Center}, rad in idx: {hs.Radius}");
            }


            hsDataList.Add(new HeatSourceData
            {
                type = (uint)hs.Type,
                min = hs.MinIdx,
                max = hs.MaxIdx,
                center = hs.Center,
                radius = hs.Radius, // Convert radius to idx
                T_init = hs.Temperature
            });
        }

        if (hsDataList.Count > 0)
        {
            int stride = Marshal.SizeOf<HeatSourceData>();
            _heatSourceDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, hsDataList.Count, stride);
            _heatSourceDataBuffer.SetData(hsDataList.ToArray());
        }
    }


    // Initialize GPU buffers that connect to the compute shader
    private void InitializeOtherBuffers()
    {
        int cellCount = (int)(_nx * _ny * _nz);

        // Create buffers in the GPU memory
        _curFBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount * (Q_f + Q_t), sizeof(float));
        _prevFBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount * (Q_f + Q_t), sizeof(float));
        _velocityRhoBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, Marshal.SizeOf<float4>());
        _temperatureBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(float));
        _fieldBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(uint));

        _velocityTexture = CreateTexture((int)_nx, (int)_ny, (int)_nz, RenderTextureFormat.ARGBFloat);
        _thermalTexture = CreateTexture((int)_nx, (int)_ny, (int)_nz, RenderTextureFormat.RFloat);
    }

    private void SetBuffers()
    {
        var allKernels = new[] { Kernels.initialize, Kernels.collision,
                              Kernels.streaming, Kernels.calculate_macro, Kernels.visualize };
        var visualizeKernels = new[] { Kernels.initialize, Kernels.visualize };

        _shaderWrapper.SetBuffer(allKernels, Uniforms.f_prev, _prevFBuffer);
        _shaderWrapper.SetBuffer(allKernels, Uniforms.f_cur, _curFBuffer);
        _shaderWrapper.SetBuffer(allKernels, Uniforms.velocity_rho, _velocityRhoBuffer);
        _shaderWrapper.SetBuffer(allKernels, Uniforms.temperature, _temperatureBuffer);
        _shaderWrapper.SetBuffer(allKernels, Uniforms.field, _fieldBuffer);

        _shaderWrapper.SetBuffer(Kernels.initialize, Uniforms.heat_sources, _heatSourceDataBuffer);

        _shaderWrapper.SetTexture(visualizeKernels, Uniforms.velocity_texture, _velocityTexture);
        _shaderWrapper.SetTexture(visualizeKernels, Uniforms.thermal_texture, _thermalTexture);
    }

    private void SetShaderParameters()
    {
        _shaderWrapper.SetFloat(Uniforms.tau_f, _tau_f);
        _shaderWrapper.SetFloat(Uniforms.tau_T, _tau_T);
        _shaderWrapper.SetFloat(Uniforms.T_ref, _T_ref);
        _shaderWrapper.SetFloat(Uniforms.lid_speed, _lid_speed);
        _shaderWrapper.SetFloat(Uniforms.beta, _beta);
        _shaderWrapper.SetFloat(Uniforms.gravity_y, _gravity_y);
        _shaderWrapper.SetInt(Uniforms.nx, (int)_nx);
        _shaderWrapper.SetInt(Uniforms.ny, (int)_ny);
        _shaderWrapper.SetInt(Uniforms.nz, (int)_nz);
        _shaderWrapper.SetInt(Uniforms.heat_source_count, _heatSourceDataBuffer?.count ?? 0);
    }

    private void DispatchInitialize()
    {
        _shaderWrapper.Dispatch(Kernels.initialize, _nx, _ny, _nz);
    }

    private void DispatchSimulate()
    {
        _shaderWrapper.Dispatch(Kernels.collision, _nx, _ny, _nz);
        _shaderWrapper.Dispatch(Kernels.streaming, _nx, _ny, _nz);
        _shaderWrapper.Dispatch(Kernels.calculate_macro, _nx, _ny, _nz);
        _shaderWrapper.Dispatch(Kernels.visualize, _nx, _ny, _nz);
    }

    private RenderTexture CreateTexture(int x, int y, int z, RenderTextureFormat format)
    {
        RenderTexture dataTex = new RenderTexture(x, y, 0, format);
        dataTex.volumeDepth = z;
        dataTex.dimension = TextureDimension.Tex3D;
        dataTex.filterMode = FilterMode.Bilinear;
        dataTex.wrapMode = TextureWrapMode.Clamp;
        dataTex.enableRandomWrite = true;
        dataTex.Create();

        return dataTex;
    }

    void OnDestroy()
    {
        _heatSourceDataBuffer?.Dispose();
    }

    // Enums
    private enum Kernels
    {
        initialize,
        calculate_macro,
        collision,
        streaming,
        visualize,
    }

    private enum Uniforms
    {
        // Buffers
        f_prev,
        f_cur,
        field,
        velocity_rho,
        temperature,
        heat_sources,
        velocity_texture,
        thermal_texture,

        // Parameters
        nx,
        ny,
        nz,
        lid_speed,
        tau_f,
        tau_T,
        beta,
        gravity_y,
        T_cold,
        T_ref,
        heat_source_count,
    }
}
