using UnityEngine;

/// <summary>
/// Seans / kare kalite skoru (QualityScore 0..1).
/// Bileşenler: landmark görünürlüğü, omuz genişliği kararlılığı, gövde lean.
/// SaMD Class B: düşük kalitede zirve ROM güncellemesini kapamak için kullanılır (teşhis değildir).
/// Formül sürümü <see cref="FormulaVersion"/> — rapor dipnotunda yazılır.
/// Hot path: heap allocation yok (önceden tahsisli halka tampon).
/// </summary>
public enum SessionQualityBand : byte
{
    /// <summary>Örnek yok veya eski kayıt.</summary>
    Unknown = 0,
    Reliable = 1,
    Caution = 2,
    Invalid = 3
}

public sealed class SessionQualityScorer
{
    public const string FormulaVersion = "QS-1.0";
    public const int WidthHistoryCapacity = 32;

    private readonly float[] _widthHistory = new float[WidthHistoryCapacity];
    private int _widthCount;
    private int _widthWrite;

    private float _sumFrame;
    private float _minFrame = 1f;
    private int _frameCount;
    private float _lastFrameScore;
    private float _lastVisibility;
    private float _lastStability;
    private float _lastLeanComponent;

    private float _weightVisibility = 0.45f;
    private float _weightStability = 0.30f;
    private float _weightLean = 0.25f;
    private float _maxShoulderWidthCv = 0.18f;
    private float _reliableMeanThreshold = 0.75f;
    private float _cautionMeanThreshold = 0.50f;
    private float _peakGateThreshold = 0.50f;
    private float _warnLeanDegrees = 7f;
    private float _invalidateLeanDegrees = 10f;

    public float LastFrameScore => _lastFrameScore;
    public float LastVisibilityComponent => _lastVisibility;
    public float LastStabilityComponent => _lastStability;
    public float LastLeanComponent => _lastLeanComponent;
    public int FrameCount => _frameCount;

    public float MeanScore => _frameCount > 0 ? _sumFrame / _frameCount : -1f;
    public float MinScore => _frameCount > 0 ? _minFrame : -1f;

    public SessionQualityBand BandFromMean => BandFromScore(MeanScore);
    public SessionQualityBand BandFromLastFrame => BandFromScore(_lastFrameScore);

    public bool AllowsPeakRomUpdate =>
        _frameCount == 0 || _lastFrameScore >= _peakGateThreshold;

    public void Configure(
        float weightVisibility,
        float weightStability,
        float weightLean,
        float maxShoulderWidthCv,
        float reliableMeanThreshold,
        float cautionMeanThreshold,
        float peakGateThreshold,
        float warnLeanDegrees,
        float invalidateLeanDegrees)
    {
        float wSum = weightVisibility + weightStability + weightLean;
        if (wSum < 1e-5f)
        {
            _weightVisibility = 0.45f;
            _weightStability = 0.30f;
            _weightLean = 0.25f;
        }
        else
        {
            _weightVisibility = weightVisibility / wSum;
            _weightStability = weightStability / wSum;
            _weightLean = weightLean / wSum;
        }

        _maxShoulderWidthCv = Mathf.Max(0.01f, maxShoulderWidthCv);
        _reliableMeanThreshold = Mathf.Clamp01(reliableMeanThreshold);
        _cautionMeanThreshold = Mathf.Clamp01(Mathf.Min(cautionMeanThreshold, _reliableMeanThreshold));
        _peakGateThreshold = Mathf.Clamp01(peakGateThreshold);
        _warnLeanDegrees = Mathf.Max(0.1f, warnLeanDegrees);
        _invalidateLeanDegrees = Mathf.Max(_warnLeanDegrees, invalidateLeanDegrees);
    }

    public void Reset()
    {
        _widthCount = 0;
        _widthWrite = 0;
        _sumFrame = 0f;
        _minFrame = 1f;
        _frameCount = 0;
        _lastFrameScore = 0f;
        _lastVisibility = 0f;
        _lastStability = 1f;
        _lastLeanComponent = 1f;
    }

    /// <summary>
    /// Bir klinik kare için skoru güncelle.
    /// visibility01: gerekli bölgelerden kaçının görünür olduğu (0..1).
    /// rawShoulderWidth: normalize öncesi omuz genişliği; &lt;=0 ise kararlılık güncellenmez.
    /// leanDegrees: omurga lean (XY); gövde pasifse 0 geçilebilir.
    /// </summary>
    public float PushFrame(float visibility01, float rawShoulderWidth, float leanDegrees)
    {
        _lastVisibility = Mathf.Clamp01(visibility01);
        _lastStability = UpdateStability(rawShoulderWidth);
        _lastLeanComponent = LeanToScore(leanDegrees);

        _lastFrameScore = Mathf.Clamp01(
            _weightVisibility * _lastVisibility
            + _weightStability * _lastStability
            + _weightLean * _lastLeanComponent);

        _sumFrame += _lastFrameScore;
        _frameCount++;
        if (_lastFrameScore < _minFrame) _minFrame = _lastFrameScore;
        return _lastFrameScore;
    }

    public SessionQualityBand BandFromScore(float score01)
    {
        if (score01 < 0f)
            return SessionQualityBand.Unknown;
        if (_frameCount == 0 && score01 <= 0f && MeanScore < 0f)
            return SessionQualityBand.Unknown;
        if (score01 >= _reliableMeanThreshold) return SessionQualityBand.Reliable;
        if (score01 >= _cautionMeanThreshold) return SessionQualityBand.Caution;
        return SessionQualityBand.Invalid;
    }

    /// <summary>Rapor/CSV için QS-1.0 varsayılan eşikleri (0.75 / 0.50).</summary>
    public static SessionQualityBand BandFromMeanDefaults(float mean01)
    {
        if (mean01 < 0f) return SessionQualityBand.Unknown;
        if (mean01 >= 0.75f) return SessionQualityBand.Reliable;
        if (mean01 >= 0.50f) return SessionQualityBand.Caution;
        return SessionQualityBand.Invalid;
    }

    /// <summary>SessionEntry.qualityBand için int kod.</summary>
    public static int ToStoredBand(SessionQualityBand band) => (int)band;

    public static SessionQualityBand FromStoredBand(int band)
    {
        if (band < 0 || band > 3) return SessionQualityBand.Unknown;
        return (SessionQualityBand)band;
    }

    private float UpdateStability(float rawShoulderWidth)
    {
        if (rawShoulderWidth <= 1e-5f)
            return _widthCount > 0 ? ComputeStabilityFromHistory() : 0.5f;

        _widthHistory[_widthWrite] = rawShoulderWidth;
        _widthWrite++;
        if (_widthWrite >= WidthHistoryCapacity) _widthWrite = 0;
        if (_widthCount < WidthHistoryCapacity) _widthCount++;

        return ComputeStabilityFromHistory();
    }

    private float ComputeStabilityFromHistory()
    {
        if (_widthCount < 3) return 1f;

        float sum = 0f;
        for (int i = 0; i < _widthCount; i++)
            sum += _widthHistory[i];
        float mean = sum / _widthCount;
        if (mean < 1e-5f) return 0f;

        float varSum = 0f;
        for (int i = 0; i < _widthCount; i++)
        {
            float d = _widthHistory[i] - mean;
            varSum += d * d;
        }
        float std = Mathf.Sqrt(varSum / _widthCount);
        float cv = std / mean;
        return Mathf.Clamp01(1f - cv / _maxShoulderWidthCv);
    }

    private float LeanToScore(float leanDegrees)
    {
        float lean = Mathf.Abs(leanDegrees);
        if (lean <= _warnLeanDegrees) return 1f;
        if (lean >= _invalidateLeanDegrees) return 0.15f;
        float t = (lean - _warnLeanDegrees) / (_invalidateLeanDegrees - _warnLeanDegrees);
        return Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(t));
    }
}
