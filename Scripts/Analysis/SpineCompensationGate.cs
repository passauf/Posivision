using UnityEngine;

/// <summary>
/// Omurga lean: soft uyarı (sticky hysteresis) + kompansasyon kaydı + tekrarı geçersiz kılma.
/// SaMD Class B; teşhis değildir. Zero-allocation.
/// </summary>
public sealed class SpineCompensationGate
{
    public struct Config
    {
        public float maxSpineLeanDegrees;
        public float spineCompensationDegrees;
        public float invalidateLeanDegrees;
        public float spineWarnHysteresisDegrees;
        public float warningCooldownSeconds;
        public float elderUprightToleranceBoost;
        public int elderAgeThresholdYears;
    }

    private Config _config;
    private bool _spineWarnSticky;
    private float _lastWarningTime = -100f;

    public bool WarnSticky => _spineWarnSticky;

    public void Configure(in Config config)
    {
        _config = config;
    }

    public void Reset()
    {
        _spineWarnSticky = false;
        _lastWarningTime = -100f;
    }

    public float ElderToleranceMultiplier(int patientAgeYears)
    {
        if (patientAgeYears < _config.elderAgeThresholdYears) return 1f;
        float boost = Mathf.Clamp(_config.elderUprightToleranceBoost, 0f, 1f);
        return 1f + boost;
    }

    /// <summary>
    /// warnLean: avatar bel görseli. Dönüş: tekrarı geçersiz kıl.
    /// </summary>
    public bool Evaluate(
        float leanDegrees,
        int patientAgeYears,
        out bool warnLean,
        WarningManager warningManager,
        VoiceCoach voiceCoach,
        bool enableVoiceCoach,
        SessionReportManager reportManager)
    {
        float ageMul = ElderToleranceMultiplier(patientAgeYears);

        float warnLimit = _config.maxSpineLeanDegrees * ageMul;
        float clearLimit = Mathf.Max(0.5f, warnLimit - _config.spineWarnHysteresisDegrees);
        float compensationLimit = Mathf.Max(_config.spineCompensationDegrees * ageMul, warnLimit);
        float invalidateLimit = Mathf.Max(_config.invalidateLeanDegrees * ageMul, compensationLimit);

        if (_spineWarnSticky)
            warnLean = leanDegrees > clearLimit;
        else
            warnLean = leanDegrees > warnLimit;
        _spineWarnSticky = warnLean;

        bool compensate = leanDegrees >= compensationLimit;
        bool invalidateLean = leanDegrees > invalidateLimit;
        _ = enableVoiceCoach;

        if (warnLean && Time.time > _lastWarningTime + _config.warningCooldownSeconds)
        {
            _lastWarningTime = Time.time;
            if (warningManager != null)
                warningManager.TriggerWarning(Loc.T("warn.stand"));
            if (voiceCoach != null)
                voiceCoach.Speak(CoachCue.StandStraight);
            if (compensate && reportManager != null)
                reportManager.RegisterCompensationEvent();
        }

        return invalidateLean;
    }

    public void ClearSticky()
    {
        _spineWarnSticky = false;
    }
}
