using UnityEngine;

/// <summary>
/// Ön protokol gövde yaw kapısı (opsiyonel). Yan profilde kapalı.
/// SaMD Class B; teşhis değildir. Zero-allocation.
/// </summary>
public sealed class FrontalFacingGate
{
    public struct Config
    {
        public bool enableSessionYawGate;
        public float maxBodyYawDegrees;
        public float bodyYawHysteresisDegrees;
        public bool requireFullFrontalTorso;
        public float warningCooldownSeconds;
    }

    private Config _config;
    private float _lastBodyYawDegrees;
    private bool _frontalTorsoFullyVisible;
    private bool _yawWarnSticky;
    private bool _facingGateFailed;
    private float _lastFacingWarnTime = -100f;

    public float LastBodyYawDegrees => _lastBodyYawDegrees;
    public bool FrontalTorsoFullyVisible => _frontalTorsoFullyVisible;
    public bool FacingGateFailed => _facingGateFailed;

    public void Configure(in Config config)
    {
        _config = config;
    }

    public void Reset()
    {
        _lastBodyYawDegrees = 0f;
        _frontalTorsoFullyVisible = false;
        _yawWarnSticky = false;
        _facingGateFailed = false;
        _lastFacingWarnTime = -100f;
    }

    public bool IsFacingOk(bool patientSideView)
    {
        return !_config.enableSessionYawGate || patientSideView || !_facingGateFailed;
    }

    public void Update(
        bool torsoVis,
        bool noseOk,
        bool patientSideView,
        Vector2 leftShoulder,
        Vector2 rightShoulder,
        Vector2 nose)
    {
        _frontalTorsoFullyVisible = torsoVis && (!_config.requireFullFrontalTorso || noseOk);
        if (!_config.enableSessionYawGate || patientSideView)
        {
            _lastBodyYawDegrees = 0f;
            _facingGateFailed = false;
            _yawWarnSticky = false;
            return;
        }

        if (!_frontalTorsoFullyVisible || !noseOk)
        {
            _lastBodyYawDegrees = 0f;
            _facingGateFailed = true;
            return;
        }

        Vector2 midS = (leftShoulder + rightShoulder) * 0.5f;
        float halfWidth = Vector2.Distance(leftShoulder, rightShoulder) * 0.5f;
        if (halfWidth < 1e-5f)
        {
            _lastBodyYawDegrees = 0f;
            _facingGateFailed = true;
            return;
        }

        float offsetX = Mathf.Abs(nose.x - midS.x);
        float yaw = Mathf.Atan2(offsetX, halfWidth) * Mathf.Rad2Deg;
        _lastBodyYawDegrees = yaw;

        float yawLimit = Mathf.Max(1f, _config.maxBodyYawDegrees);
        float clearYaw = Mathf.Max(0.5f, yawLimit - _config.bodyYawHysteresisDegrees);
        bool yawBad = _yawWarnSticky ? yaw > clearYaw : yaw > yawLimit;
        _yawWarnSticky = yawBad;
        _facingGateFailed = yawBad || !_frontalTorsoFullyVisible;
    }

    /// <summary>Yaw kapısı kapalıysa asla geçersiz kılmaz. true = bu kare invalid.</summary>
    public bool CheckWarnings(
        bool patientSideView,
        WarningManager warningManager,
        VoiceCoach voiceCoach,
        bool enableVoiceCoach)
    {
        if (!_config.enableSessionYawGate || patientSideView || !_facingGateFailed)
            return false;

        _ = enableVoiceCoach;
        if (Time.time > _lastFacingWarnTime + _config.warningCooldownSeconds)
        {
            _lastFacingWarnTime = Time.time;
            if (warningManager != null)
                warningManager.TriggerWarning(Loc.T("warn.faceFront"));
            if (voiceCoach != null)
                voiceCoach.Speak(CoachCue.FaceFront);
        }

        return true;
    }
}
