using UnityEngine;

/// <summary>
/// Kadraj/takip sıçraması: eklem teleport + protokol ölçek sıçraması.
/// Ölçek birimi: ön = omuz genişliği, yan = gövde boyu (PoseScaleResolver).
/// Eşikler: ≥0.45 ölçek birimi / eklem, ≥3 eklem; ölçek oranı ≥1.55.
/// SaMD Class B kalite uyarısı; teşhis değildir. Zero-allocation hot path.
/// </summary>
public sealed class TrackingJumpDetector
{
    public const int LandmarkSlotCount = 33;
    private const int IdxLeftShoulder = 11;
    private const int IdxRightShoulder = 12;
    private const int IdxLeftElbow = 13;
    private const int IdxRightElbow = 14;
    private const int IdxLeftWrist = 15;
    private const int IdxRightWrist = 16;
    private const int IdxLeftHip = 23;
    private const int IdxRightHip = 24;

    public const float DefaultDeltaScaleUnits = 0.45f;
    public const int DefaultMinJoints = 3;
    public const float DefaultScaleRatio = 1.55f;

    public struct Config
    {
        public bool enabled;
        /// <summary>Ölçek biriminde eklem teleport eşiği (varsayılan 0.45).</summary>
        public float deltaScaleUnits;
        /// <summary>Aynı karede bu kadar eklem eşiği aşarsa sıçrama (varsayılan 3).</summary>
        public int minJoints;
        /// <summary>Ölçek uzunluğu kareler arası oran üst sınırı (varsayılan 1.55).</summary>
        public float scaleRatio;
        public float warningCooldownSeconds;
    }

    private Config _config;
    private readonly Vector2[] _prevNormXy = new Vector2[LandmarkSlotCount];
    private readonly bool[] _prevNormValid = new bool[LandmarkSlotCount];
    private float _prevTimestamp = -1f;
    private float _prevRawScaleLength;
    private float _lastWarnTime = -100f;

    public void Configure(in Config config)
    {
        _config = config;
    }

    public void Reset()
    {
        _prevTimestamp = -1f;
        _prevRawScaleLength = 0f;
        for (int i = 0; i < LandmarkSlotCount; i++)
            _prevNormValid[i] = false;
    }

    /// <summary>
    /// true = bu karede sıçrama; açı job atlanmalı.
    /// rawScaleLength: normalize öncesi protokol ölçeği (ön: omuz w, yan: torso L).
    /// filteredXy: normalize sonrası XY (ölçek biriminde).
    /// </summary>
    public bool Evaluate(
        float timestamp,
        float rawScaleLength,
        Vector2[] filteredXy,
        bool mpRightOk,
        bool mpLeftOk,
        bool mpRightWristOk,
        bool mpLeftWristOk,
        bool leftShoulderOk,
        bool rightShoulderOk,
        bool torsoOk,
        System.Action onJump,
        WarningManager warningManager)
    {
        bool jumped = false;

        if (_config.enabled && _prevTimestamp > 0f && filteredXy != null)
        {
            float dt = timestamp - _prevTimestamp;
            if (dt > 1e-4f && dt < 0.20f)
            {
                float thresh = Mathf.Max(0.15f, _config.deltaScaleUnits);
                float threshSq = thresh * thresh;
                int jumpJoints = 0;
                int compared = 0;

                CountJointJump(IdxLeftShoulder, leftShoulderOk, filteredXy, threshSq, ref jumpJoints, ref compared);
                CountJointJump(IdxRightShoulder, rightShoulderOk, filteredXy, threshSq, ref jumpJoints, ref compared);
                CountJointJump(IdxLeftElbow, mpLeftOk, filteredXy, threshSq, ref jumpJoints, ref compared);
                CountJointJump(IdxRightElbow, mpRightOk, filteredXy, threshSq, ref jumpJoints, ref compared);
                CountJointJump(IdxLeftWrist, mpLeftOk && mpLeftWristOk, filteredXy, threshSq, ref jumpJoints, ref compared);
                CountJointJump(IdxRightWrist, mpRightOk && mpRightWristOk, filteredXy, threshSq, ref jumpJoints, ref compared);
                CountJointJump(IdxLeftHip, mpLeftOk || torsoOk, filteredXy, threshSq, ref jumpJoints, ref compared);
                CountJointJump(IdxRightHip, mpRightOk || torsoOk, filteredXy, threshSq, ref jumpJoints, ref compared);

                int minJoints = Mathf.Max(1, _config.minJoints);
                if (compared >= minJoints && jumpJoints >= minJoints)
                    jumped = true;

                // Ölçek sıçraması — yan profilde gövde boyu; ön profilde omuz genişliği
                if (!jumped
                    && rawScaleLength > PoseScaleResolver.MinScale
                    && _prevRawScaleLength > PoseScaleResolver.MinScale)
                {
                    float ratio = rawScaleLength / _prevRawScaleLength;
                    float limit = Mathf.Max(1.15f, _config.scaleRatio);
                    if (ratio >= limit || ratio <= 1f / limit)
                        jumped = true;
                }
            }
        }

        if (jumped)
        {
            onJump?.Invoke();
            float cooldown = Mathf.Max(0.5f, _config.warningCooldownSeconds);
            if (warningManager != null && Time.time > _lastWarnTime + cooldown)
            {
                _lastWarnTime = Time.time;
                warningManager.TriggerWarning(Loc.T("warn.trackingJump"));
            }
            Reset();
            return true;
        }

        StoreReference(
            timestamp, rawScaleLength, filteredXy,
            mpRightOk, mpLeftOk, mpRightWristOk, mpLeftWristOk,
            leftShoulderOk, rightShoulderOk, torsoOk);
        return false;
    }

    private void CountJointJump(
        int idx, bool currentOk, Vector2[] filteredXy, float threshSq,
        ref int jumpJoints, ref int compared)
    {
        if (!currentOk || !_prevNormValid[idx]) return;
        compared++;
        Vector2 cur = filteredXy[idx];
        Vector2 prev = _prevNormXy[idx];
        float dx = cur.x - prev.x;
        float dy = cur.y - prev.y;
        if ((dx * dx + dy * dy) >= threshSq)
            jumpJoints++;
    }

    private void StoreReference(
        float timestamp,
        float rawScaleLength,
        Vector2[] filteredXy,
        bool mpRightOk,
        bool mpLeftOk,
        bool mpRightWristOk,
        bool mpLeftWristOk,
        bool leftShoulderOk,
        bool rightShoulderOk,
        bool torsoOk)
    {
        _prevTimestamp = timestamp;
        _prevRawScaleLength = rawScaleLength > PoseScaleResolver.MinScale
            ? rawScaleLength
            : _prevRawScaleLength;

        for (int i = 0; i < LandmarkSlotCount; i++)
            _prevNormValid[i] = false;

        if (filteredXy == null) return;
        StoreJoint(IdxLeftShoulder, leftShoulderOk, filteredXy);
        StoreJoint(IdxRightShoulder, rightShoulderOk, filteredXy);
        StoreJoint(IdxLeftElbow, mpLeftOk, filteredXy);
        StoreJoint(IdxRightElbow, mpRightOk, filteredXy);
        StoreJoint(IdxLeftWrist, mpLeftOk && mpLeftWristOk, filteredXy);
        StoreJoint(IdxRightWrist, mpRightOk && mpRightWristOk, filteredXy);
        StoreJoint(IdxLeftHip, mpLeftOk || torsoOk, filteredXy);
        StoreJoint(IdxRightHip, mpRightOk || torsoOk, filteredXy);
    }

    private void StoreJoint(int idx, bool ok, Vector2[] filteredXy)
    {
        if (!ok) return;
        _prevNormXy[idx] = filteredXy[idx];
        _prevNormValid[idx] = true;
    }
}
