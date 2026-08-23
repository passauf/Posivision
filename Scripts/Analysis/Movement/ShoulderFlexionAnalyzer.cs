using UnityEngine;

/// <summary>
/// Omuz elevasyon ailesi (fleksiyon / abdüksiyon): job açısı → teorik düzeltme → SoftFollow.
/// Burst job host'ta kalır; bu sınıf yalnızca yorumlar. SaMD Class B; teşhis değildir.
/// </summary>
public sealed class ShoulderFlexionAnalyzer : IMovementAnalyzer
{
    private readonly MovementId _id;

    public MovementId Id => _id;
    public PoseRegionMask RequiredMask => PoseRegionMask.ShoulderFlexion();

    private ShoulderFlexionAnalyzerConfig _config;
    private TheoreticalRomCorrectionConfig _romCorrection = TheoreticalRomCorrectionConfig.TheoreticalDefaults();
    private float _physicRight;
    private float _physicLeft;
    private float _refUpperArmLenR;
    private float _refUpperArmLenL;
    private float _prevElbowFlexR = -1000f;
    private float _prevElbowFlexL = -1000f;
    private float _prevGuardedFlexR;
    private float _prevGuardedFlexL;

    public ShoulderFlexionAnalyzer()
        : this(MovementId.ShoulderFlexion)
    {
    }

    public ShoulderFlexionAnalyzer(MovementId movementId)
    {
        _id = ExerciseCatalog.IsShoulderElevationFamily(movementId)
            ? movementId
            : MovementId.ShoulderFlexion;
    }

    public void Configure(in ShoulderFlexionAnalyzerConfig config)
    {
        _config = config;
    }

    public void ConfigureRomCorrection(in TheoreticalRomCorrectionConfig config)
    {
        _romCorrection = config;
    }

    public void ResetSession()
    {
        _physicRight = 0f;
        _physicLeft = 0f;
        _refUpperArmLenR = 0f;
        _refUpperArmLenL = 0f;
        _prevElbowFlexR = -1000f;
        _prevElbowFlexL = -1000f;
        _prevGuardedFlexR = 0f;
        _prevGuardedFlexL = 0f;
    }

    public float GetReferenceArmLength(int mpArmIndex)
    {
        return mpArmIndex == 0 ? _refUpperArmLenR : _refUpperArmLenL;
    }

    public void UpdateReferenceArmLength(int mpArmIndex, Vector2 shoulder, Vector2 elbow)
    {
        float len = Vector2.Distance(shoulder, elbow);
        if (len < 1e-5f) return;

        float minRatio = _config.foreshorteningRefUpdateMinRatio;
        float maxRatio = _config.foreshorteningRefUpdateMaxRatio;

        if (mpArmIndex == 0)
        {
            if (_refUpperArmLenR < 1e-5f)
                _refUpperArmLenR = len;
            else
            {
                float ratio = len / _refUpperArmLenR;
                if (ratio >= minRatio && ratio <= maxRatio)
                    _refUpperArmLenR = Mathf.Lerp(_refUpperArmLenR, len, 0.03f);
            }
        }
        else
        {
            if (_refUpperArmLenL < 1e-5f)
                _refUpperArmLenL = len;
            else
            {
                float ratio = len / _refUpperArmLenL;
                if (ratio >= minRatio && ratio <= maxRatio)
                    _refUpperArmLenL = Mathf.Lerp(_refUpperArmLenL, len, 0.03f);
            }
        }
    }

    public void ProcessFrame(in MovementFrameContext ctx, ref MovementFrameResult result)
    {
        result = default;

        float mpRightAngle = ctx.mpRightOk ? ctx.jobAngleMpRight : float.NaN;
        float mpLeftAngle = ctx.mpLeftOk ? ctx.jobAngleMpLeft : float.NaN;

        bool foreshortenMpRight = ctx.mpRightOk && ctx.mpRightWristOk && IsCameraAxisArmCollapse(
            ctx.mpRightShoulder, ctx.mpRightElbow, ctx.mpRightWrist, _refUpperArmLenR);
        bool foreshortenMpLeft = ctx.mpLeftOk && ctx.mpLeftWristOk && IsCameraAxisArmCollapse(
            ctx.mpLeftShoulder, ctx.mpLeftElbow, ctx.mpLeftWrist, _refUpperArmLenL);

        if (foreshortenMpRight)
            mpRightAngle = float.NaN;
        if (foreshortenMpLeft)
            mpLeftAngle = float.NaN;

        result.notifyForeshorten = foreshortenMpRight || foreshortenMpLeft;
        result.foreshortenMpRight = foreshortenMpRight;
        result.foreshortenMpLeft = foreshortenMpLeft;

        if (ctx.mpRightOk && ctx.mpRightWristOk && !float.IsNaN(mpRightAngle))
        {
            mpRightAngle = GuardForearmRotation(
                mpRightAngle, ctx.mpRightShoulder, ctx.mpRightElbow, ctx.mpRightWrist,
                ref _prevElbowFlexR, ref _prevGuardedFlexR);
        }
        if (ctx.mpLeftOk && ctx.mpLeftWristOk && !float.IsNaN(mpLeftAngle))
        {
            mpLeftAngle = GuardForearmRotation(
                mpLeftAngle, ctx.mpLeftShoulder, ctx.mpLeftElbow, ctx.mpLeftWrist,
                ref _prevElbowFlexL, ref _prevGuardedFlexL);
        }

        bool applyYaw = !ctx.patientSideView;
        if (!float.IsNaN(mpRightAngle))
        {
            float ratioR = ArmLengthRatio(ctx.mpRightShoulder, ctx.mpRightElbow, _refUpperArmLenR);
            mpRightAngle = TheoreticalRomCorrector.CorrectElevationDegrees(
                mpRightAngle, ctx.bodyYawDegrees, ratioR, _config.foreshorteningMinArmRatio,
                ctx.rawShoulderWidth01, in _romCorrection, applyYaw);
        }
        if (!float.IsNaN(mpLeftAngle))
        {
            float ratioL = ArmLengthRatio(ctx.mpLeftShoulder, ctx.mpLeftElbow, _refUpperArmLenL);
            mpLeftAngle = TheoreticalRomCorrector.CorrectElevationDegrees(
                mpLeftAngle, ctx.bodyYawDegrees, ratioL, _config.foreshorteningMinArmRatio,
                ctx.rawShoulderWidth01, in _romCorrection, applyYaw);
        }

        float dt = ctx.deltaTime;
        if (ctx.swapArms)
        {
            if (ctx.clinicalRightOk && !float.IsNaN(mpLeftAngle))
            {
                _physicRight = SoftFollowRomAngle(_physicRight, mpLeftAngle, dt);
                result.hasClinicalData = true;
            }
            if (ctx.clinicalLeftOk && !float.IsNaN(mpRightAngle))
            {
                _physicLeft = SoftFollowRomAngle(_physicLeft, mpRightAngle, dt);
                result.hasClinicalData = true;
            }
        }
        else
        {
            if (ctx.clinicalRightOk && !float.IsNaN(mpRightAngle))
            {
                _physicRight = SoftFollowRomAngle(_physicRight, mpRightAngle, dt);
                result.hasClinicalData = true;
            }
            if (ctx.clinicalLeftOk && !float.IsNaN(mpLeftAngle))
            {
                _physicLeft = SoftFollowRomAngle(_physicLeft, mpLeftAngle, dt);
                result.hasClinicalData = true;
            }
        }

        bool fsClinicalRight = ctx.swapArms ? foreshortenMpLeft : foreshortenMpRight;
        bool fsClinicalLeft = ctx.swapArms ? foreshortenMpRight : foreshortenMpLeft;
        result.foreshortenClinicalRight = fsClinicalRight;
        result.foreshortenClinicalLeft = fsClinicalLeft;
        if ((ctx.clinicalRightOk && fsClinicalRight) || (ctx.clinicalLeftOk && fsClinicalLeft))
            result.hasClinicalData = true;

        if (ctx.swapArms)
        {
            result.repGateRightValid = ctx.clinicalRightOk && !float.IsNaN(mpLeftAngle);
            result.repGateLeftValid = ctx.clinicalLeftOk && !float.IsNaN(mpRightAngle);
            if (result.repGateRightValid) result.repGateRight = mpLeftAngle;
            if (result.repGateLeftValid) result.repGateLeft = mpRightAngle;
        }
        else
        {
            result.repGateRightValid = ctx.clinicalRightOk && !float.IsNaN(mpRightAngle);
            result.repGateLeftValid = ctx.clinicalLeftOk && !float.IsNaN(mpLeftAngle);
            if (result.repGateRightValid) result.repGateRight = mpRightAngle;
            if (result.repGateLeftValid) result.repGateLeft = mpLeftAngle;
        }

        result.clinicalRightAngle = _physicRight;
        result.clinicalLeftAngle = _physicLeft;
        result.avatarMpRightOk = ctx.mpRightOk && !float.IsNaN(mpRightAngle);
        result.avatarMpRightAngle = mpRightAngle;
        result.avatarMpLeftOk = ctx.mpLeftOk && !float.IsNaN(mpLeftAngle);
        result.avatarMpLeftAngle = mpLeftAngle;
    }

    private static float ArmLengthRatio(Vector2 shoulder, Vector2 elbow, float refLen)
    {
        if (refLen < 1e-5f) return 1f;
        float len = Vector2.Distance(shoulder, elbow);
        if (len < 1e-5f) return 0f;
        return len / refLen;
    }

    private bool IsCameraAxisArmCollapse(
        Vector2 shoulder, Vector2 elbow, Vector2 wrist, float refLen)
    {
        if (refLen < 1e-5f) return false;

        float elbowDeg = Angle2D(shoulder, elbow, wrist);
        if (float.IsNaN(elbowDeg) || elbowDeg < _config.foreshorteningMinElbowExtensionDegrees)
            return false;

        float upperLen = Vector2.Distance(shoulder, elbow);
        float upperRatio = upperLen / refLen;
        if (upperRatio >= _config.foreshorteningMinArmRatio)
            return false;

        float chainLen = Vector2.Distance(shoulder, wrist);
        float chainRatio = chainLen / (2f * refLen);
        if (chainRatio >= _config.foreshorteningMinChainRatio)
            return false;

        return true;
    }

    private float GuardForearmRotation(
        float flexion, Vector2 shoulder, Vector2 elbow, Vector2 wrist,
        ref float prevElbowFlex, ref float prevGuardedFlex)
    {
        if (float.IsNaN(flexion)) return flexion;

        float elbowFlex = Angle2D(shoulder, elbow, wrist);
        if (_config.suppressForearmRotationArtifact && prevElbowFlex > -500f)
        {
            float dElbow = Mathf.Abs(elbowFlex - prevElbowFlex);
            float dFlex = Mathf.Abs(flexion - prevGuardedFlex);
            if (dElbow >= _config.forearmRotationElbowDeltaDegrees
                && dFlex <= _config.forearmRotationFlexionDeltaDegrees)
                flexion = prevGuardedFlex;
        }

        prevElbowFlex = elbowFlex;
        prevGuardedFlex = flexion;
        return flexion;
    }

    private static float SoftFollowRomAngle(float previous, float raw, float dt)
    {
        if (float.IsNaN(raw)) return previous;
        if (previous <= 0.01f || float.IsNaN(previous)) return raw;

        const float maxDeg = JointAngleJob.MaxShoulderElevationDegrees;
        dt = Mathf.Clamp(dt, 1f / 120f, 0.1f);
        float maxUp = 105f * dt;
        float maxDown = 130f * dt;

        // Yüksek açı: aşağı spike'a dirençli; yukarı takip eski 35°/sn'den daha açık (peak under-estimate ↓)
        if (previous >= 145f)
        {
            maxUp = 70f * dt;
            maxDown = 90f * dt;
        }
        if (previous >= 165f)
        {
            maxUp = 55f * dt;
            maxDown = 60f * dt;
            // Landmark jitter bandı — sert rate-limit yerine yumuşak çekim
            if (Mathf.Abs(raw - previous) <= 2.5f)
                return Mathf.Clamp(Mathf.Lerp(previous, raw, 0.35f), 0f, maxDeg);
        }

        float d = raw - previous;
        if (d > maxUp) d = maxUp;
        else if (d < -maxDown) d = -maxDown;
        return Mathf.Clamp(previous + d, 0f, maxDeg);
    }

    private static float Angle2D(Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 v1 = a - b;
        Vector2 v2 = c - b;
        if (v1.sqrMagnitude < 1e-12f || v2.sqrMagnitude < 1e-12f) return float.NaN;
        return Vector2.Angle(v1, v2);
    }
}

/// <summary>Omuz fleksiyon analiz eşikleri (host SerializeField → Configure).</summary>
public struct ShoulderFlexionAnalyzerConfig
{
    public bool suppressForearmRotationArtifact;
    public float forearmRotationElbowDeltaDegrees;
    public float forearmRotationFlexionDeltaDegrees;
    public float foreshorteningMinArmRatio;
    public float foreshorteningMinChainRatio;
    public float foreshorteningMinElbowExtensionDegrees;
    public float foreshorteningRefUpdateMinRatio;
    public float foreshorteningRefUpdateMaxRatio;
}
