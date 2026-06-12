using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System;
using Unity.VisualScripting;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.IO.Compression;

public class LidDrivenSolver : IDisposable
{
    private uint _nx, _ny, _nz;
    private float _cellSize, _dt, _tau;
    private float _lidSpeed;

    private ComputeShader _lbmComputeShader;
    private ComputeShaderWrapper<Kernels, Uniforms> _shaderWrapper;

    private GraphicsBuffer _curFBuffer, _prevFBuffer, _velocityRhoBuffer, _fieldBuffer;
    private RenderTexture _velocityTexture;

    public GraphicsBuffer CurFBuffer => _curFBuffer;
    public GraphicsBuffer PrevFBuffer => _prevFBuffer;
    public GraphicsBuffer VelocityRhoBuffer => _velocityRhoBuffer;
    public GraphicsBuffer FieldBuffer => _fieldBuffer;
    public RenderTexture VelocityTexture => _velocityTexture;

    private const int Q = 19; // D3Q19

    public LidDrivenSolver(ComputeShader shader, float lidSpeed, float cellSize, float tau, uint nx, uint ny, uint nz)
    {
        _nx = nx;
        _ny = ny;
        _nz = nz;
        _cellSize = cellSize;
        _tau = tau;
        _lidSpeed = lidSpeed;
        _lbmComputeShader = shader;

        _shaderWrapper = new ComputeShaderWrapper<Kernels, Uniforms>(_lbmComputeShader);

        InitializeBuffers();
        SetBuffers();
        SetShaderParameters();
        DispatchInitialize();
    }

    // This function is called every frame to update the simulation state in SimulationController
    public void Step()
    {
        DispatchSimulate(); // Collision and streaming steps

        (_prevFBuffer, _curFBuffer) = (_curFBuffer, _prevFBuffer); // Swap buffers

        // Swap the shader buffer pointers' references to let the GPU know
        var lbmKernels = new[] { Kernels.collision, Kernels.streaming, Kernels.calculate_macro };
        _shaderWrapper.SetBuffer(lbmKernels, Uniforms.f_prev, _prevFBuffer);
        _shaderWrapper.SetBuffer(lbmKernels, Uniforms.f_cur, _curFBuffer);
    }

    // Clean up GPU resources when the simulation is no longer needed
    // This function is called at OnDestroy() in SimulationController
    public void Dispose()
    {
        _curFBuffer?.Dispose();
        _prevFBuffer?.Dispose();
        _velocityRhoBuffer?.Dispose();
        _fieldBuffer?.Dispose();

        _velocityTexture?.Release();
    }

    public void ResetField()
    {
        DispatchInitialize();
    }
    
    public void RequestVelocityDataAndSaveToCSV()
    {
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

            float4 velocityRho = data[i] / _lidSpeed;

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

        string path = Path.Combine(Application.dataPath, "Etcs", $"{System.DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")}.csv");
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"Successfully saved velocity data to: {path}");

        #if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
        #endif
    }

    // Initialize GPU buffers that connect to the compute shader
    private void InitializeBuffers()
    {
        int cellCount = (int)(_nx * _ny * _nz);

        // Create buffers in the GPU memory
        _curFBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount * Q, sizeof(float));
        _prevFBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount * Q, sizeof(float));
        _velocityRhoBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, Marshal.SizeOf<float4>());
        _fieldBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(uint));

        _velocityTexture = CreateTexture((int)_nx, (int)_ny, (int)_nz, RenderTextureFormat.ARGBFloat);
    }

    private void SetBuffers()
    {
        var kernels = new[] { Kernels.initialize, Kernels.collision, Kernels.streaming,
                              Kernels.calculate_macro, Kernels.visualize };

        _shaderWrapper.SetBuffer(kernels, Uniforms.field, _fieldBuffer);
        _shaderWrapper.SetBuffer(kernels, Uniforms.velocity_rho, _velocityRhoBuffer);
        _shaderWrapper.SetBuffer(kernels, Uniforms.f_prev, _prevFBuffer);
        _shaderWrapper.SetBuffer(kernels, Uniforms.f_cur, _curFBuffer);

        _shaderWrapper.SetTexture(kernels, Uniforms.velocity_texture, _velocityTexture);
    }

    private void SetShaderParameters()
    {
        _shaderWrapper.SetFloat(Uniforms.tau, _tau);
        _shaderWrapper.SetFloat(Uniforms.cell_size, _cellSize);
        _shaderWrapper.SetFloat(Uniforms.lid_speed, _lidSpeed);
        _shaderWrapper.SetInt(Uniforms.nx, (int)_nx);
        _shaderWrapper.SetInt(Uniforms.ny, (int)_ny);
        _shaderWrapper.SetInt(Uniforms.nz, (int)_nz);
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
        f_prev,
        f_cur,
        field,
        velocity_rho,
        velocity_texture,
        lid_speed,
        tau,
        cell_size,
        nx,
        ny,
        nz,
    }
}