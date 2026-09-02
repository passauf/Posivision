using UnityEngine;

/// <summary>
/// Ön kamera (abdüksiyon) ölçümüne özel: kameraya doğru düz kol foreshortening + avuç çevirme artefaktı.
/// Yan profil fleksiyonda kullanılmaz. SaMD Class B; teşhis değildir.
/// </summary>
public static class ShoulderAbductionForegroundGuards
{
    public static bool IsCameraAxisArmCollapse(
        Vector2 shoulder,
        Vector2 elbow,
        Vector2 wrist,
        float refLen,
        in ShoulderAbductionAnalyzerConfig config)
    {
        if (refLen < 1e-5f) return false;

        float elbowDeg = Angle2D(shoulder, elbow, wrist);
        if (float.IsNaN(elbowDeg) || elbowDeg < config.foreshorteningMinElbowExtensionDegrees)
            return false;

        float upperLen = Vector2.Distance(shoulder, elbow);
        float upperRatio = upperLen / refLen;
        if (upperRatio >= config.foreshorteningMinArmRatio)
            return false;

        float chainLen = Vector2.Distance(shoulder, wrist);
        float chainRatio = chainLen / (2f * refLen);
        if (chainRatio >= config.foreshorteningMinChainRatio)
            return false;

        return true;
    }

    public static float GuardForearmRotation(
        float flexion,
        Vector2 shoulder,
        Vector2 elbow,
        Vector2 wrist,
        in ShoulderAbductionAnalyzerConfig config,
        ref float prevElbowFlex,
        ref float prevGuardedFlex)
    {
        if (float.IsNaN(flexion)) return flexion;

        float elbowFlex = Angle2D(shoulder, elbow, wrist);
        if (config.suppressForearmRotationArtifact && prevElbowFlex > -500f)
        {
            float dElbow = Mathf.Abs(elbowFlex - prevElbowFlex);
            float dFlex = Mathf.Abs(flexion - prevGuardedFlex);
            if (dElbow >= config.forearmRotationElbowDeltaDegrees
                && dFlex <= config.forearmRotationFlexionDeltaDegrees)
                flexion = prevGuardedFlex;
        }

        prevElbowFlex = elbowFlex;
        prevGuardedFlex = flexion;
        return flexion;
    }

    private static float Angle2D(Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 v1 = a - b;
        Vector2 v2 = c - b;
        if (v1.sqrMagnitude < 1e-12f || v2.sqrMagnitude < 1e-12f) return float.NaN;
        return Vector2.Angle(v1, v2);
    }
}

/// <summary>Abdüksiyon (ön kamera) analiz eşikleri.</summary>
public struct ShoulderAbductionAnalyzerConfig
{
    public ShoulderElevationReferenceConfig reference;
    public bool suppressForearmRotationArtifact;
    public float forearmRotationElbowDeltaDegrees;
    public float forearmRotationFlexionDeltaDegrees;
    public float foreshorteningMinArmRatio;
    public float foreshorteningMinChainRatio;
    public float foreshorteningMinElbowExtensionDegrees;
}
