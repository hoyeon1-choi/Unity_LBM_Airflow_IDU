using System;
using UnityEngine;

public readonly struct LbmGridMemoryEstimate
{
    public uint Nx { get; }
    public uint Ny { get; }
    public uint Nz { get; }
    public long CellCount { get; }
    public long LargestDistributionBufferBytes { get; }
    public long DistributionBytes { get; }
    public long StateBufferBytes { get; }
    public long CoreBufferBytes { get; }
    public long TextureBytes { get; }
    public long TotalBytes { get; }
    public bool IsSingleBufferSafe { get; }

    public LbmGridMemoryEstimate(
        uint nx, uint ny, uint nz, long cellCount, long largestDistributionBufferBytes,
        long distributionBytes, long stateBufferBytes, long textureBytes)
    {
        Nx = nx;
        Ny = ny;
        Nz = nz;
        CellCount = cellCount;
        LargestDistributionBufferBytes = largestDistributionBufferBytes;
        DistributionBytes = distributionBytes;
        StateBufferBytes = stateBufferBytes;
        CoreBufferBytes = distributionBytes + stateBufferBytes;
        TextureBytes = textureBytes;
        TotalBytes = CoreBufferBytes + textureBytes;
        IsSingleBufferSafe = largestDistributionBufferBytes <=
                             LbmGridMemoryEstimator.MaxGraphicsBufferBytes;
    }
}

/// <summary>
/// Mirrors ThermalSolver's volume allocations: D3Q19 + D3Q7 distributions in
/// prev/cur buffers, float4 velocity/rho, float temperature, uint field, and
/// ARGBFloat/RFloat 3D textures. Small scene-dependent and debug buffers are
/// intentionally excluded from this estimate.
/// </summary>
public static class LbmGridMemoryEstimator
{
    public const int FluidDirectionCount = 19;
    public const int ThermalDirectionCount = 7;
    public const int DistributionSetCount = 2;
    public const int LargestDistributionGroupDirectionCount = 6;
    public const long MaxGraphicsBufferBytes = 2147483648L;

    private const long BytesPerFloat = 4L;
    private const long BytesPerUint = 4L;
    private const long BytesPerFloat4 = 16L;
    private const long BytesPerVelocityTextureVoxel = 16L;
    private const long BytesPerThermalTextureVoxel = 4L;

    public static bool TryEstimate(
        Vector3 domainSize,
        float cellSize,
        out LbmGridMemoryEstimate estimate,
        out string issue)
    {
        estimate = default;
        if (!IsFinitePositive(domainSize.x) || !IsFinitePositive(domainSize.y) ||
            !IsFinitePositive(domainSize.z))
        {
            issue = "Domain size must contain three positive finite values.";
            return false;
        }

        if (!IsFinitePositive(cellSize))
        {
            issue = "Cell size must be a positive finite value.";
            return false;
        }

        if (!TryRoundDimension(domainSize.x / cellSize, out uint nx) ||
            !TryRoundDimension(domainSize.y / cellSize, out uint ny) ||
            !TryRoundDimension(domainSize.z / cellSize, out uint nz))
        {
            issue = "Estimated grid dimension exceeds the supported uint range.";
            return false;
        }

        return TryEstimate(nx, ny, nz, out estimate, out issue);
    }

    public static bool TryEstimate(
        uint nx,
        uint ny,
        uint nz,
        out LbmGridMemoryEstimate estimate,
        out string issue)
    {
        estimate = default;
        if (nx == 0 || ny == 0 || nz == 0)
        {
            issue = "Grid dimensions must be greater than zero.";
            return false;
        }

        try
        {
            long cellCount = checked((long)nx * ny * nz);
            long largestDistributionBufferBytes = checked(
                cellCount * LargestDistributionGroupDirectionCount * BytesPerFloat);
            long distributionBytes = checked(
                cellCount * (FluidDirectionCount + ThermalDirectionCount) *
                DistributionSetCount * BytesPerFloat);
            long stateBufferBytes = checked(
                cellCount * (BytesPerFloat4 + BytesPerFloat + BytesPerUint));
            long textureBytes = checked(
                cellCount * (BytesPerVelocityTextureVoxel + BytesPerThermalTextureVoxel));
            estimate = new LbmGridMemoryEstimate(
                nx, ny, nz, cellCount, largestDistributionBufferBytes,
                distributionBytes, stateBufferBytes, textureBytes);
            issue = string.Empty;
            return true;
        }
        catch (OverflowException)
        {
            issue = "Estimated grid memory exceeds the supported 64-bit range.";
            return false;
        }
    }

    public static float BytesToMiB(long bytes)
    {
        return bytes / (1024f * 1024f);
    }

    private static bool TryRoundDimension(float value, out uint dimension)
    {
        dimension = 0;
        if (!IsFinitePositive(value) || value > uint.MaxValue)
            return false;

        double rounded = Math.Round(value, MidpointRounding.ToEven);
        dimension = (uint)Math.Max(1.0, rounded);
        return true;
    }

    private static bool IsFinitePositive(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
