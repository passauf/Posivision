using UnityEngine;

/// <summary>
/// Yan profil seans kapısı: SideProfileGate math + soft/invalid/head/wrong-side uyarıları.
/// Omuz w yokken önceki φ korunur (yan’da uzak omuz kaybı ≠ öne dönmüş).
/// Soft uyarıda hysteresis. SaMD Class B; teşhis değildir. Zero-allocation.
/// </summary>
public sealed class SideProfileSessionGate
{
    public struct Config
    {
        public float warnDegrees;
        public float invalidDegrees;
        public float frontalShoulderWidth01;
        public float warningCooldownSeconds;
        public float softWarnHysteresisDegrees;
    }

    private Config _config;
    private float _lastSideSkewDegrees;
    private bool _measurementValid = true;
    private bool _lastSideHeadOk = true;
    private bool _softWarnSticky;
    private float _lastSideWarnTime = -100f;
    private float _lastHeadWarnTime = -100f;
    private float _lastWrongSideWarnTime = -100f;

    public float LastSkewDegrees => _lastSideSkewDegrees;
    public bool MeasurementValid => _measurementValid;
    public bool LastHeadOk => _lastSideHeadOk;

    public void Configure(in Config config)
    {
        _config = config;
        if (_config.softWarnHysteresisDegrees < 0.5f)
            _config.softWarnHysteresisDegrees = SideProfileGate.SoftWarnHysteresisDegrees;
    }

    public void Reset()
    {
        _lastSideSkewDegrees = 0f;
        _measurementValid = true;
        _lastSideHeadOk = true;
        _softWarnSticky = false;
        _lastSideWarnTime = -100f;
        _lastHeadWarnTime = -100f;
        _lastWrongSideWarnTime = -100f;
    }

    public void Evaluate(
        bool patientSideView,
        float rawShoulderWidth01,
        bool hasShoulderWidth,
        float torsoLength01,
        bool noseOk,
        bool measureRightArm,
        bool measureLeftArm,
        bool anatRightVis,
        bool anatLeftVis,
        WarningManager warningManager)
    {
        if (!patientSideView)
        {
            _measurementValid = true;
            _lastSideSkewDegrees = 0f;
            _lastSideHeadOk = true;
            _softWarnSticky = false;
            return;
        }

        bool workingVis = measureRightArm ? anatRightVis : (measureLeftArm && anatLeftVis);
        if (measureRightArm && measureLeftArm)
            workingVis = anatRightVis || anatLeftVis;
        bool oppositeMore = false;
        if (measureRightArm && !measureLeftArm)
            oppositeMore = anatLeftVis && !anatRightVis;
        else if (measureLeftArm && !measureRightArm)
            oppositeMore = anatRightVis && !anatLeftVis;

        SideProfileGate.Result r = SideProfileGate.Evaluate(
            rawShoulderWidth01,
            hasShoulderWidth,
            _config.frontalShoulderWidth01,
            torsoLength01,
            _config.warnDegrees,
            _config.invalidDegrees,
            noseOk,
            workingVis,
            oppositeMore);

        if (!r.hasShoulderWidth)
        {
            // Önceki φ / geçerlilik korunur — false "daha yana dön" yok
            _lastSideHeadOk = r.headOk;
            if (!r.headOk)
                _measurementValid = false;

            float cd = _config.warningCooldownSeconds;
            if (!r.headOk && Time.time > _lastHeadWarnTime + cd)
            {
                _lastHeadWarnTime = Time.time;
                if (warningManager != null)
                    warningManager.TriggerWarning(Loc.T("warn.headFrame"));
            }
            return;
        }

        _lastSideSkewDegrees = r.phiDegrees;
        _measurementValid = r.measurementValid;
        _lastSideHeadOk = r.headOk;

        float warnLimit = Mathf.Max(1f, _config.warnDegrees);
        float clearLimit = Mathf.Max(0.5f, warnLimit - _config.softWarnHysteresisDegrees);
        if (_softWarnSticky)
            _softWarnSticky = r.phiDegrees > clearLimit;
        else
            _softWarnSticky = r.phiDegrees > warnLimit;

        float cooldown = _config.warningCooldownSeconds;
        if (!r.headOk && Time.time > _lastHeadWarnTime + cooldown)
        {
            _lastHeadWarnTime = Time.time;
            if (warningManager != null)
                warningManager.TriggerWarning(Loc.T("warn.headFrame"));
        }

        if (r.wrongSideSuspect && Time.time > _lastWrongSideWarnTime + cooldown)
        {
            _lastWrongSideWarnTime = Time.time;
            if (warningManager != null)
                warningManager.TriggerWarning(Loc.T("warn.wrongSide"));
        }
    }

    public void MaybeWarnSoft(WarningManager warningManager)
    {
        if (!_softWarnSticky) return;
        if (Time.time <= _lastSideWarnTime + _config.warningCooldownSeconds) return;
        _lastSideWarnTime = Time.time;
        if (warningManager != null)
            warningManager.TriggerWarning(Loc.T("warn.sideSkew"));
    }

    public void MaybeWarnInvalid(WarningManager warningManager)
    {
        if (Time.time <= _lastSideWarnTime + _config.warningCooldownSeconds) return;
        _lastSideWarnTime = Time.time;
        if (warningManager != null)
            warningManager.TriggerWarning(
                _lastSideHeadOk ? Loc.T("warn.sideInvalid") : Loc.T("warn.headFrame"));
    }
}
