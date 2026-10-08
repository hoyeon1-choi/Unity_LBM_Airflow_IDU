using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public interface IMainDashboardGpuUsageProvider
{
    string Status { get; }

    bool TryGetUsagePercent(out float usagePercent);
}

/// <summary>
/// Samples the Windows GPU Engine performance counter without reading back solver buffers.
/// Values from processes sharing the same physical engine are summed, then the busiest
/// engine is reported, matching the overall-utilization convention used by Task Manager.
/// </summary>
public sealed class MainDashboardGpuUsageMonitor : IMainDashboardGpuUsageProvider, IDisposable
{
    private const float SampleIntervalSeconds = 1.0f;
    private const string CounterPath = @"\GPU Engine(*)\Utilization Percentage";
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhValidData = 0x00000000;
    private const uint PdhNewData = 0x00000001;

    private readonly Dictionary<string, double> utilizationByEngine =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    private IntPtr query;
    private IntPtr utilizationCounter;
    private float latestUsagePercent = float.NaN;
    private float nextSampleTime;
    private bool initialized;
    private bool permanentlyUnavailable;
    private bool disposed;

    public string Status { get; private set; } = "Waiting for GPU sample";

    public void Tick()
    {
        if (disposed || permanentlyUnavailable || Time.unscaledTime < nextSampleTime)
            return;

        nextSampleTime = Time.unscaledTime + SampleIntervalSeconds;
        if (!IsWindowsRuntime())
        {
            MarkPermanentlyUnavailable("GPU usage is supported on Windows only");
            return;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!initialized)
        {
            InitializeWindowsCounter();
            return;
        }

        CollectWindowsSample();
#else
        MarkPermanentlyUnavailable("GPU usage is not available in this build");
#endif
    }

    public bool TryGetUsagePercent(out float usagePercent)
    {
        usagePercent = latestUsagePercent;
        return float.IsFinite(usagePercent);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (query != IntPtr.Zero)
            PdhCloseQuery(query);
#endif
        query = IntPtr.Zero;
        utilizationCounter = IntPtr.Zero;
        utilizationByEngine.Clear();
        latestUsagePercent = float.NaN;
    }

    private static bool IsWindowsRuntime()
    {
        return Application.platform == RuntimePlatform.WindowsEditor ||
               Application.platform == RuntimePlatform.WindowsPlayer;
    }

    private void MarkPermanentlyUnavailable(string status)
    {
        permanentlyUnavailable = true;
        latestUsagePercent = float.NaN;
        Status = status;
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private void InitializeWindowsCounter()
    {
        uint result = PdhOpenQuery(null, UIntPtr.Zero, out query);
        if (result != 0)
        {
            MarkPermanentlyUnavailable($"GPU counter unavailable (PDH 0x{result:X8})");
            return;
        }

        result = PdhAddEnglishCounter(query, CounterPath, UIntPtr.Zero, out utilizationCounter);
        if (result != 0)
        {
            PdhCloseQuery(query);
            query = IntPtr.Zero;
            MarkPermanentlyUnavailable($"GPU Engine counter unavailable (PDH 0x{result:X8})");
            return;
        }

        result = PdhCollectQueryData(query);
        if (result != 0)
        {
            PdhCloseQuery(query);
            query = IntPtr.Zero;
            utilizationCounter = IntPtr.Zero;
            MarkPermanentlyUnavailable($"GPU counter initialization failed (PDH 0x{result:X8})");
            return;
        }

        initialized = true;
        Status = "Collecting Windows GPU Engine data";
    }

    private void CollectWindowsSample()
    {
        uint result = PdhCollectQueryData(query);
        if (result != 0)
        {
            SetTransientFailure($"GPU sample failed (PDH 0x{result:X8})");
            return;
        }

        uint bufferSize = 0;
        uint itemCount = 0;
        result = PdhGetFormattedCounterArray(
            utilizationCounter,
            PdhFormatDouble,
            ref bufferSize,
            ref itemCount,
            IntPtr.Zero);
        if (result != PdhMoreData || bufferSize == 0 || itemCount == 0)
        {
            SetTransientFailure(result == 0
                ? "No active GPU engines"
                : $"GPU counter data unavailable (PDH 0x{result:X8})");
            return;
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)bufferSize));
        try
        {
            result = PdhGetFormattedCounterArray(
                utilizationCounter,
                PdhFormatDouble,
                ref bufferSize,
                ref itemCount,
                buffer);
            if (result != 0)
            {
                SetTransientFailure($"GPU counter read failed (PDH 0x{result:X8})");
                return;
            }

            utilizationByEngine.Clear();
            int itemSize = Marshal.SizeOf<PdhFormattedCounterValueItem>();
            for (uint itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                IntPtr itemAddress = IntPtr.Add(buffer, checked((int)itemIndex * itemSize));
                PdhFormattedCounterValueItem item =
                    Marshal.PtrToStructure<PdhFormattedCounterValueItem>(itemAddress);
                if (item.Value.Status != PdhValidData && item.Value.Status != PdhNewData)
                    continue;

                double value = item.Value.Value;
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
                    continue;

                string instanceName = Marshal.PtrToStringUni(item.Name) ?? string.Empty;
                string engineKey = GetPhysicalEngineKey(instanceName, itemIndex);
                utilizationByEngine.TryGetValue(engineKey, out double accumulatedValue);
                utilizationByEngine[engineKey] = accumulatedValue + value;
            }

            if (utilizationByEngine.Count == 0)
            {
                SetTransientFailure("No valid GPU engine samples");
                return;
            }

            double busiestEngineUsage = 0.0;
            foreach (double engineUsage in utilizationByEngine.Values)
                busiestEngineUsage = Math.Max(busiestEngineUsage, engineUsage);

            latestUsagePercent = Mathf.Clamp((float)busiestEngineUsage, 0.0f, 100.0f);
            Status = "System GPU · Windows PDH";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void SetTransientFailure(string status)
    {
        latestUsagePercent = float.NaN;
        Status = status;
    }

    private static string GetPhysicalEngineKey(string instanceName, uint fallbackIndex)
    {
        int luidIndex = instanceName.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        return luidIndex >= 0
            ? instanceName.Substring(luidIndex)
            : $"engine_{fallbackIndex}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValue
    {
        public uint Status;
        public double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValueItem
    {
        public IntPtr Name;
        public PdhFormattedCounterValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(
        string dataSource,
        UIntPtr userData,
        out IntPtr query);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(
        IntPtr query,
        string fullCounterPath,
        UIntPtr userData,
        out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        ref uint itemCount,
        IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
#endif
}
