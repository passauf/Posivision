using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Mobil performans metrikleri (FPS, Pose/Face süreleri, GC).
/// Hot path: Stopwatch tick; string yok. HUD ayrı okur.
/// KVKK: hasta kimliği içermez — yalnızca teşhis metrikleri.
/// </summary>
public static class PerfStats
{
    private const float EmaAlpha = 0.15f;

    private static long _poseSubmitTicks;
    private static long _poseResultCount;
    private static long _faceResultCount;
    private static long _lastPoseResultTicks;
    private static long _lastFaceWallTicks;

    private static double _poseLatencyMsEma;
    private static double _faceMsEma;
    private static double _poseIntervalMsEma = 33.0;
    private static double _faceIntervalMsEma = 100.0;

    private static int _gc0;
    private static int _gc1;
    private static int _gc2;
    private static long _lastAllocBytes;
    private static float _allocDeltaMb;

    public static double PoseLatencyMsEma => Volatile.Read(ref _poseLatencyMsEma);
    public static double FaceMsEma => Volatile.Read(ref _faceMsEma);
    public static double PoseFpsEma
    {
        get
        {
            double interval = Volatile.Read(ref _poseIntervalMsEma);
            return interval > 1e-3 ? 1000.0 / interval : 0.0;
        }
    }
    public static double FaceHzEma
    {
        get
        {
            double interval = Volatile.Read(ref _faceIntervalMsEma);
            return interval > 1e-3 ? 1000.0 / interval : 0.0;
        }
    }
    public static long PoseResultCount => Interlocked.Read(ref _poseResultCount);
    public static long FaceResultCount => Interlocked.Read(ref _faceResultCount);
    public static float AllocDeltaMb => _allocDeltaMb;
    public static int GcCount0 => _gc0;
    public static int GcCount1 => _gc1;
    public static int GcCount2 => _gc2;

    public static void MarkPoseSubmit()
    {
        Interlocked.Exchange(ref _poseSubmitTicks, Stopwatch.GetTimestamp());
    }

    public static void MarkPoseResult()
    {
        long now = Stopwatch.GetTimestamp();
        long submit = Interlocked.Read(ref _poseSubmitTicks);
        if (submit > 0)
            UpdateEma(ref _poseLatencyMsEma, TicksToMs(now - submit));

        long prev = Interlocked.Exchange(ref _lastPoseResultTicks, now);
        if (prev > 0)
            UpdateEma(ref _poseIntervalMsEma, TicksToMs(now - prev));

        Interlocked.Increment(ref _poseResultCount);
    }

    public static void MarkFaceSample(double detectMs)
    {
        long now = Stopwatch.GetTimestamp();
        UpdateEma(ref _faceMsEma, detectMs);

        long prev = Interlocked.Exchange(ref _lastFaceWallTicks, now);
        if (prev > 0)
            UpdateEma(ref _faceIntervalMsEma, TicksToMs(now - prev));

        Interlocked.Increment(ref _faceResultCount);
    }

    /// <summary>Ana thread'de periyodik çağır (HUD).</summary>
    public static void SampleMemory()
    {
        long alloc = Profiler.GetTotalAllocatedMemoryLong();
        if (_lastAllocBytes > 0)
            _allocDeltaMb = (alloc - _lastAllocBytes) / (1024f * 1024f);
        _lastAllocBytes = alloc;

        _gc0 = System.GC.CollectionCount(0);
        _gc1 = System.GC.CollectionCount(1);
        _gc2 = System.GC.CollectionCount(2);
    }

    private static double TicksToMs(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static void UpdateEma(ref double emaField, double sample)
    {
        double prev = Volatile.Read(ref emaField);
        double next = prev <= 1e-6 ? sample : prev + EmaAlpha * (sample - prev);
        Volatile.Write(ref emaField, next);
    }
}
