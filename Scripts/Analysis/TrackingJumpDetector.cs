using UnityEngine;

/// <summary>
/// Kadraj/takip sıçraması: eklem teleport + protokol ölçek sıçraması.
/// Karşılaştırma normalize ÖNCESİ görüntü (0–1) uzayında — ölçek titremesi yanlış pozitif üretmez.
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
    private const float DefaultFallbackScale01 = 0.15f;
    private const float ScaleEmaAlpha = 0.22f;
    private const float ExtremeScaleRatio = 2.25f;

    public struct Config
    {
        public bool enabled;
        /// <summary>Ölçek biriminde eklem teleport eşiği (görüntü uzayına çarpılır).</summary>
        public float deltaScaleUnits;
        /// <summary>Aynı karede bu kadar eklem eşiği aşarsa sıçrama.</summary>
        public int minJoints;
        /// <summary>EMA ölçek oranı üst sınırı (orta düzey; eklem hareketi ile birlikte).</summary>
        public float scaleRatio;
        public float warningCooldownSeconds;
        /// <summary>Yan profil tek kol: bilek sıçraması sayılmaz.</summary>
        public bool excludeWrists;
    }

    private Config _config;
    private readonly Vector2[] _prevImageXy = new Vector2[LandmarkSlotCount];
    private readonly bool[] _prevImageValid = new bool[LandmarkSlotCount];
    private float _prevTimestamp = -1f;
    private float _prevEmaScaleLength;
    private float _emaScaleLength;
    private float _lastWarnTime = -100f;

    public void Configure(in Config config)
    {
        _config = config;
    }

    public void Reset()
    {
        _prevTimestamp = -1f;
        _prevEmaScaleLength = 0f;
        _emaScaleLength = 0f;
        for (int i = 0; i < LandmarkSlotCount; i++)
            _prevImageValid[i] = false;
    }

    /// <summary>
    /// true = bu karede sıçrama; açı job atlanmalı.
    /// imageXy: One Euro sonrası, normalize ÖNCESİ görüntü (0–1) koordinatları.
    /// </summary>
    public bool Evaluate(
        float timestamp,
        float rawScaleLength,
        Vector2[] imageXy,
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
        int jumpJoints = 0;
        int compared = 0;

        if (_config.enabled && _prevTimestamp > 0f && imageXy != null)
        {
            float dt = timestamp - _prevTimestamp;
            if (dt > 1e-4f && dt < 0.20f)
            {
                float scaleForThresh = rawScaleLength > PoseScaleResolver.MinScale
                    ? rawScaleLength
                    : (_prevEmaScaleLength > PoseScaleResolver.MinScale
                        ? _prevEmaScaleLength
                        : DefaultFallbackScale01);
                float thresh = Mathf.Max(0.035f, _config.deltaScaleUnits * scaleForThresh);
                float threshSq = thresh * thresh;

                CountJointJump(IdxLeftShoulder, leftShoulderOk, imageXy, threshSq, ref jumpJoints, ref compared);
                CountJointJump(IdxRightShoulder, rightShoulderOk, imageXy, threshSq, ref jumpJoints, ref compared);
                CountJointJump(IdxLeftElbow, mpLeftOk, imageXy, threshSq, ref jumpJoints, ref compared);
                CountJointJump(IdxRightElbow, mpRightOk, imageXy, threshSq, ref jumpJoints, ref compared);
                if (!_config.excludeWrists)
                {
                    CountJointJump(IdxLeftWrist, mpLeftOk && mpLeftWristOk, imageXy, threshSq, ref jumpJoints, ref compared);
                    CountJointJump(IdxRightWrist, mpRightOk && mpRightWristOk, imageXy, threshSq, ref jumpJoints, ref compared);
                }
                CountJointJump(IdxLeftHip, mpLeftOk || torsoOk, imageXy, threshSq, ref jumpJoints, ref compared);
                CountJointJump(IdxRightHip, mpRightOk || torsoOk, imageXy, threshSq, ref jumpJoints, ref compared);

                int minJoints = Mathf.Max(1, _config.minJoints);
                if (compared >= minJoints && jumpJoints >= minJoints)
                    jumped = true;

                // Ölçek sıçraması — EMA ile yumuşatılmış; yalnız ölçek titremesi uyarı üretmez
                if (!jumped
                    && _emaScaleLength > PoseScaleResolver.MinScale
                    && _prevEmaScaleLength > PoseScaleResolver.MinScale)
                {
                    float ratio = _emaScaleLength / _prevEmaScaleLength;
                    float limit = Mathf.Max(1.25f, _config.scaleRatio);
                    bool moderate = ratio >= limit || ratio <= 1f / limit;
                    bool extreme = ratio >= ExtremeScaleRatio || ratio <= 1f / ExtremeScaleRatio;
                    if (extreme || (moderate && jumpJoints >= 2))
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
            timestamp, rawScaleLength, imageXy,
            mpRightOk, mpLeftOk, mpRightWristOk, mpLeftWristOk,
            leftShoulderOk, rightShoulderOk, torsoOk);
        return false;
    }

    private void CountJointJump(
        int idx, bool currentOk, Vector2[] imageXy, float threshSq,
        ref int jumpJoints, ref int compared)
    {
        if (!currentOk || !_prevImageValid[idx]) return;
        compared++;
        Vector2 cur = imageXy[idx];
        Vector2 prev = _prevImageXy[idx];
        float dx = cur.x - prev.x;
        float dy = cur.y - prev.y;
        if ((dx * dx + dy * dy) >= threshSq)
            jumpJoints++;
    }

    private void StoreReference(
        float timestamp,
        float rawScaleLength,
        Vector2[] imageXy,
        bool mpRightOk,
        bool mpLeftOk,
        bool mpRightWristOk,
        bool mpLeftWristOk,
        bool leftShoulderOk,
        bool rightShoulderOk,
        bool torsoOk)
    {
        _prevTimestamp = timestamp;

        if (rawScaleLength > PoseScaleResolver.MinScale)
        {
            if (_emaScaleLength <= PoseScaleResolver.MinScale)
            {
                _emaScaleLength = rawScaleLength;
                _prevEmaScaleLength = rawScaleLength;
            }
            else
            {
                _prevEmaScaleLength = _emaScaleLength;
                _emaScaleLength = Mathf.Lerp(_emaScaleLength, rawScaleLength, ScaleEmaAlpha);
            }
        }

        for (int i = 0; i < LandmarkSlotCount; i++)
            _prevImageValid[i] = false;

        if (imageXy == null) return;
        StoreJoint(IdxLeftShoulder, leftShoulderOk, imageXy);
        StoreJoint(IdxRightShoulder, rightShoulderOk, imageXy);
        StoreJoint(IdxLeftElbow, mpLeftOk, imageXy);
        StoreJoint(IdxRightElbow, mpRightOk, imageXy);
        if (!_config.excludeWrists)
        {
            StoreJoint(IdxLeftWrist, mpLeftOk && mpLeftWristOk, imageXy);
            StoreJoint(IdxRightWrist, mpRightOk && mpRightWristOk, imageXy);
        }
        StoreJoint(IdxLeftHip, mpLeftOk || torsoOk, imageXy);
        StoreJoint(IdxRightHip, mpRightOk || torsoOk, imageXy);
    }

    private void StoreJoint(int idx, bool ok, Vector2[] imageXy)
    {
        if (!ok) return;
        _prevImageXy[idx] = imageXy[idx];
        _prevImageValid[idx] = true;
    }
}
