using UnityEngine;

/// <summary>
/// 2D kamera projeksiyon hatası için teorik ROM düzeltmesi (gonyometre/IMU kalibrasyonu yok).
/// Formül (hareket düzlemi kameraya α/φ ile eğikken):
///   θ_corr = atan2(sin θ_2D, cos θ_2D · cos α)
/// Yaw (gövde dönüşü φ) aynı geometriyle uygulanır.
/// SaMD Class B: karar-destek; teşhis değildir. Klinik kalibrasyon gelince kazançlar güncellenir.
/// </summary>
public static class TheoreticalRomCorrector
{
    private const float MinCos = 0.08f;
    /// <summary>Ölçüm tavanı = anatomik elevasyon üstü (175 sahte tavan kaldırıldı).</summary>
    private const float MaxAngleDeg = JointAngleJob.MaxShoulderElevationDegrees;

    /// <summary>
    /// 2D elevasyon açısını teorik düzlem düzeltmesiyle iyileştirir.
    /// </summary>
    public static float CorrectElevationDegrees(
        float angle2dDegrees,
        float bodyYawDegrees,
        float upperArmRatio,
        float foreshortenRejectRatio,
        float rawShoulderWidth01,
        in TheoreticalRomCorrectionConfig cfg,
        bool applyYaw)
    {
        if (!cfg.enabled || float.IsNaN(angle2dDegrees))
            return angle2dDegrees;

        float theta = Mathf.Clamp(angle2dDegrees, 0f, MaxAngleDeg);
        float raw = theta;

        // 1) Hafif foreshortening: r = cos α ≈ L_proj/L_ref → düzlem açısını geri getir
        if (cfg.correctForeshortening
            && upperArmRatio > foreshortenRejectRatio + 0.01f
            && upperArmRatio < 0.995f)
        {
            float cosAlpha = Mathf.Clamp(upperArmRatio, MinCos, 1f);
            float plane = ProjectOutOfPlane(theta, cosAlpha);
            theta = Mathf.Lerp(theta, plane, Mathf.Clamp01(cfg.foreshortenGain));
        }

        // 2) Gövde yaw: hareket düzlemi kamera düzlemine φ ile eğik
        if (applyYaw && cfg.correctYaw)
        {
            float phi = Mathf.Abs(bodyYawDegrees);
            if (phi >= cfg.minYawDegrees && phi <= cfg.maxYawCorrectionDegrees)
            {
                float cosPhi = Mathf.Cos(phi * Mathf.Deg2Rad);
                if (cosPhi >= MinCos)
                {
                    float yawCorr = ProjectOutOfPlane(theta, cosPhi);
                    theta = Mathf.Lerp(theta, yawCorr, Mathf.Clamp01(cfg.yawGain));
                }
            }
        }

        // 3) Mesafe proxy (omuz genişliği 0–1): uzaktayken düzeltmeyi yumuşat
        if (cfg.correctDistanceProxy && rawShoulderWidth01 > 1e-5f && cfg.idealShoulderWidth01 > 1e-5f)
        {
            float ratio = rawShoulderWidth01 / cfg.idealShoulderWidth01;
            float trust = Mathf.Clamp(ratio, 0.6f, 1.4f);
            float blend = Mathf.Clamp01(cfg.distanceBlendStrength);
            if (trust < 1f && blend > 0f)
            {
                float pullBack = (1f - trust) * blend;
                theta = Mathf.Lerp(theta, raw, pullBack);
            }
        }

        return Mathf.Clamp(theta, 0f, MaxAngleDeg);
    }

    /// <summary>θ_plane = atan2(sin θ, cos θ · cos α) — tan tekilliği yok.</summary>
    public static float ProjectOutOfPlane(float angle2dDegrees, float cosOutOfPlane)
    {
        float cosA = Mathf.Clamp(cosOutOfPlane, MinCos, 1f);
        float th = angle2dDegrees * Mathf.Deg2Rad;
        float s = Mathf.Sin(th);
        float c = Mathf.Cos(th);
        float corr = Mathf.Atan2(s, c * cosA) * Mathf.Rad2Deg;
        if (corr < 0f) corr += 360f;
        if (corr > MaxAngleDeg) corr = MaxAngleDeg;
        return corr;
    }
}

/// <summary>Teorik ROM düzeltme parametreleri (klinik fit yok — geometri varsayılanları).</summary>
public struct TheoreticalRomCorrectionConfig
{
    public bool enabled;
    public bool correctForeshortening;
    public bool correctYaw;
    public bool correctDistanceProxy;
    public float foreshortenGain;
    public float yawGain;
    public float minYawDegrees;
    public float maxYawCorrectionDegrees;
    public float idealShoulderWidth01;
    public float distanceBlendStrength;

    public static TheoreticalRomCorrectionConfig TheoreticalDefaults()
    {
        return new TheoreticalRomCorrectionConfig
        {
            enabled = true,
            correctForeshortening = true,
            correctYaw = true,
            correctDistanceProxy = true,
            foreshortenGain = 1f,
            yawGain = 1f,
            minYawDegrees = 3f,
            maxYawCorrectionDegrees = 25f,
            idealShoulderWidth01 = 0.22f,
            distanceBlendStrength = 0.2f
        };
    }
}
