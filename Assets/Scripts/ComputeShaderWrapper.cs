using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

public class ComputeShaderWrapper<TKernel, TUniform> where TKernel : Enum where TUniform : Enum
{
    private readonly ComputeShader _shader;

    // <Kernel enum, Kernel Function ID>
    private readonly Dictionary<TKernel, int> _kernelMap;

    // <Uniform enum, Uniform Variable ID>
    private readonly Dictionary<TUniform, int> _uniformMap;

    // <Kernel enum, Thread group size>
    private readonly Dictionary<TKernel, uint3> _threadGroupSizeMap;

    public ComputeShaderWrapper(ComputeShader shader)
    {
        _shader = shader;

        // Create mappings for kernel functions ID in the compute shader that correspond to the enum values
        _kernelMap = Enum.GetValues(typeof(TKernel)).Cast<TKernel>()
            .ToDictionary(t => t, t => _shader.FindKernel(t.ToString()));

        // Create mappings for uniform variables ID in the compute shader that correspond to the enum values
        _uniformMap = Enum.GetValues(typeof(TUniform)).Cast<TUniform>()
            .ToDictionary(t => t, t => Shader.PropertyToID(t.ToString()));

        _threadGroupSizeMap = _kernelMap.ToDictionary(
            t => t.Key, // Key : Original Kernel enum (e.g. Kernels.collision)
            t =>        // Value : Thread group size for the kernel (e.g. uint3(8, 8, 1))
            {
                _shader.GetKernelThreadGroupSizes(t.Value, out uint x, out uint y, out uint z);
                return new uint3(x, y, z);
            });
    }

    public void Dispatch(TKernel kernel, uint nx, uint ny, uint nz)
    {
        uint3 threadGroupSize = _threadGroupSizeMap[kernel];
        // Calculate the number of thread groups needed to cover the dispatch dimensions
        // Using math.ceil to ensure we cover all elements
        int3 threadGroups = new int3((int)(math.ceil((float)nx / threadGroupSize.x)),
                                     (int)(math.ceil((float)ny / threadGroupSize.y)),
                                     (int)(math.ceil((float)nz / threadGroupSize.z)));
        threadGroups = math.max(threadGroups, 1); // Ensure that at least one thread group is dispatched to avoid errors
        _shader.Dispatch(_kernelMap[kernel], threadGroups.x, threadGroups.y, threadGroups.z);
    }

    public void SetBuffer(TKernel kernel, TUniform uniform, GraphicsBuffer buffer)
    {
        _shader.SetBuffer(_kernelMap[kernel], _uniformMap[uniform], buffer);
    }

    public void SetBuffer(TKernel[] kernels, TUniform uniform, GraphicsBuffer buffer)
    {
        foreach (var kernel in kernels)
        {
            SetBuffer(kernel, uniform, buffer);
        }
    }

    public void SetFloat(TUniform uniform, float value)
    {
        _shader.SetFloat(_uniformMap[uniform], value);
    }

    public void SetInt(TUniform uniform, int value)
    {
        _shader.SetInt(_uniformMap[uniform], value);
    }

    public void SetVector(TUniform uniform, in float4 value)
    {
        _shader.SetVector(_uniformMap[uniform], value);
    }
    
    public void SetTexture(TKernel kernel, TUniform uniform, RenderTexture texture)
    {
        _shader.SetTexture(_kernelMap[kernel], _uniformMap[uniform], texture);
    }

    public void SetTexture(TKernel[] kernels, TUniform uniform, RenderTexture texture)
    {
        foreach (var kernel in kernels) SetTexture(kernel, uniform, texture);
    }
}