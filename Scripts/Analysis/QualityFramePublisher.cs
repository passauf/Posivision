/// <summary>
/// Görünürlük oranı + SessionQualityScorer yapılandırma/push sarmalayıcı.
/// SaMD Class B kalite göstergesi; teşhis değildir. Zero-allocation.
/// </summary>
public sealed class QualityFramePublisher
{
    private readonly SessionQualityScorer _scorer = new SessionQualityScorer();
    private float _lastVisibilityFraction;

    public SessionQualityScorer Scorer => _scorer;
    public float LastVisibilityFraction => _lastVisibilityFraction;

    public float CurrentQualityScore01 => _scorer.LastFrameScore;
    public float SessionMeanQualityScore01 => _scorer.MeanScore;
    public SessionQualityBand CurrentQualityBand => _scorer.BandFromLastFrame;
    public SessionQualityBand SessionQualityBandMean => _scorer.BandFromMean;
    public bool QualityAllowsPeakRom => _scorer.AllowsPeakRomUpdate;

    public void Configure(
        float weightVisibility,
        float weightStability,
        float weightLean,
        float maxShoulderWidthCv,
        float reliableThreshold,
        float cautionThreshold,
        float peakGateThreshold,
        float warnLeanDegrees,
        float invalidateLeanDegrees)
    {
        _scorer.Configure(
            weightVisibility,
            weightStability,
            weightLean,
            maxShoulderWidthCv,
            reliableThreshold,
            cautionThreshold,
            peakGateThreshold,
            warnLeanDegrees,
            invalidateLeanDegrees);
    }

    public void Reset()
    {
        _scorer.Reset();
        _lastVisibilityFraction = 0f;
    }

    public float ComputeVisibilityFraction(
        bool measureRightArm,
        bool measureLeftArm,
        bool regionRightArm,
        bool regionLeftArm,
        bool regionTorso,
        bool clinicalRightOk,
        bool clinicalLeftOk,
        bool torsoOk)
    {
        int need = 0;
        int ok = 0;
        if (regionRightArm && measureRightArm)
        {
            need++;
            if (clinicalRightOk) ok++;
        }
        if (regionLeftArm && measureLeftArm)
        {
            need++;
            if (clinicalLeftOk) ok++;
        }
        if (regionTorso)
        {
            need++;
            if (torsoOk) ok++;
        }
        if (need <= 0) return 0f;
        return (float)ok / need;
    }

    public void SetVisibilityFraction(float fraction)
    {
        _lastVisibilityFraction = fraction;
    }

    public void PushFrame(
        bool torsoRegionActive,
        float spineLeanDegrees,
        bool rawShoulderWidthValid,
        float rawShoulderWidthForQuality,
        SessionReportManager reportManager)
    {
        float lean = torsoRegionActive ? spineLeanDegrees : 0f;
        float width = rawShoulderWidthValid ? rawShoulderWidthForQuality : 0f;
        _scorer.PushFrame(_lastVisibilityFraction, width, lean);
        if (reportManager != null)
            reportManager.RegisterQualitySample(_scorer.LastFrameScore);
    }
}
