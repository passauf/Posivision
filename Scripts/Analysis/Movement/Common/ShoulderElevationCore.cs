using UnityEngine;

/// <summary>
/// Omuz elevasyon ortak ROM: referans kol uzunluğu, teorik düzeltme, SoftFollow, tekrar kapısı.
/// SaMD Class B; teşhis değildir.
/// </summary>
public static class ShoulderElevationCore
{
    public static void UpdateReferenceArmLength(
        ref float refUpperArmLenR,
        ref float refUpperArmLenL,
        int mpArmIndex,
        Vector2 shoulder,
        Vector2 elbow,
        in ShoulderElevationReferenceConfig config)
    {
        float len = Vector2.Distance(shoulder, elbow);
        if (len < 1e-5f) return;

        float minRatio = config.refUpdateMinRatio;
        float maxRatio = config.refUpdateMaxRatio;

        if (mpArmIndex == 0)
        {
            if (refUpperArmLenR < 1e-5f)
                refUpperArmLenR = len;
            else
            {
                float ratio = len / refUpperArmLenR;
                if (ratio >= minRatio && ratio <= maxRatio)
                    refUpperArmLenR = Mathf.Lerp(refUpperArmLenR, len, 0.03f);
            }
        }
        else
        {
            if (refUpperArmLenL < 1e-5f)
                refUpperArmLenL = len;
            else
            {
                float ratio = len / refUpperArmLenL;
                if (ratio >= minRatio && ratio <= maxRatio)
                    refUpperArmLenL = Mathf.Lerp(refUpperArmLenL, len, 0.03f);
            }
        }
    }

    public static float CorrectAngle(
        float angle,
        in MovementFrameContext ctx,
        Vector2 shoulder,
        Vector2 elbow,
        float refLen,
        float foreshortenRejectRatio,
        in TheoreticalRomCorrectionConfig romCorrection,
        bool applyYaw)
    {
        if (float.IsNaN(angle)) return angle;
        float ratio = ArmLengthRatio(shoulder, elbow, refLen);
        return TheoreticalRomCorrector.CorrectElevationDegrees(
            angle, ctx.bodyYawDegrees, ratio, foreshortenRejectRatio,
            ctx.rawShoulderWidth01, in romCorrection, applyYaw);
    }

    public static void FinishFrame(
        in MovementFrameContext ctx,
        ref float physicRight,
        ref float physicLeft,
        float mpRightAngle,
        float mpLeftAngle,
        bool foreshortenMpRight,
        bool foreshortenMpLeft,
        ref MovementFrameResult result)
    {
        float dt = ctx.deltaTime;
        if (ctx.swapArms)
        {
            if (ctx.clinicalRightOk && !float.IsNaN(mpLeftAngle))
            {
                physicRight = SoftFollowRomAngle(physicRight, mpLeftAngle, dt);
                result.hasClinicalData = true;
            }
            if (ctx.clinicalLeftOk && !float.IsNaN(mpRightAngle))
            {
                physicLeft = SoftFollowRomAngle(physicLeft, mpRightAngle, dt);
                result.hasClinicalData = true;
            }
        }
        else
        {
            if (ctx.clinicalRightOk && !float.IsNaN(mpRightAngle))
            {
                physicRight = SoftFollowRomAngle(physicRight, mpRightAngle, dt);
                result.hasClinicalData = true;
            }
            if (ctx.clinicalLeftOk && !float.IsNaN(mpLeftAngle))
            {
                physicLeft = SoftFollowRomAngle(physicLeft, mpLeftAngle, dt);
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

        result.clinicalRightAngle = physicRight;
        result.clinicalLeftAngle = physicLeft;
        result.avatarMpRightOk = ctx.mpRightOk && !float.IsNaN(mpRightAngle);
        result.avatarMpRightAngle = mpRightAngle;
        result.avatarMpLeftOk = ctx.mpLeftOk && !float.IsNaN(mpLeftAngle);
        result.avatarMpLeftAngle = mpLeftAngle;
    }

    public static float ArmLengthRatio(Vector2 shoulder, Vector2 elbow, float refLen)
    {
        if (refLen < 1e-5f) return 1f;
        float len = Vector2.Distance(shoulder, elbow);
        if (len < 1e-5f) return 0f;
        return len / refLen;
    }

    public static float SoftFollowRomAngle(float previous, float raw, float dt)
    {
        if (float.IsNaN(raw)) return previous;
        if (previous <= 0.01f || float.IsNaN(previous)) return raw;

        const float maxDeg = JointAngleJob.MaxShoulderElevationDegrees;
        dt = Mathf.Clamp(dt, 1f / 120f, 0.1f);
        float maxUp = 105f * dt;
        float maxDown = 130f * dt;

        if (previous >= 145f)
        {
            maxUp = 70f * dt;
            maxDown = 90f * dt;
        }
        if (previous >= 165f)
        {
            maxUp = 55f * dt;
            maxDown = 60f * dt;
            if (Mathf.Abs(raw - previous) <= 2.5f)
                return Mathf.Clamp(Mathf.Lerp(previous, raw, 0.35f), 0f, maxDeg);
        }

        float d = raw - previous;
        if (d > maxUp) d = maxUp;
        else if (d < -maxDown) d = -maxDown;
        return Mathf.Clamp(previous + d, 0f, maxDeg);
    }
}
