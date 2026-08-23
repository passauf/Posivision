using UnityEngine;

/// <summary>
/// Yan profil sapma φ ≈ asin(w / W_ref).
/// W_ref mesafe-bağımsız: mümkünse k × gövde boyu (yan’da omuz w çöker ama torso stabil).
/// SaMD Class B karar-destek; teşhis değildir. Zero-allocation (struct API).
/// </summary>
public static class SideProfileGate
{
    /// <summary>Soft uyarı eşiği — iyi yan açı (~0–20°) false positive olmasın.</summary>
    public const float DefaultWarnDegrees = 22f;
    /// <summary>Ölçümü geçersiz kılma eşiği.</summary>
    public const float DefaultInvalidDegrees = 38f;
    /// <summary>Gövde yokken sabit fallback (eski davranış).</summary>
    public const float DefaultFrontalShoulderWidth01 = 0.22f;
    /// <summary>Frontal’da tipik omuz genişliği / gövde boyu oranı.</summary>
    public const float DefaultFrontalShoulderToTorso = 0.82f;
    public const float MinRefWidth = 0.06f;
    public const float SoftWarnHysteresisDegrees = 4f;

    public struct Result
    {
        public float phiDegrees;
        public bool headOk;
        public bool withinWarn;
        public bool withinAccept;
        public bool measurementValid;
        public bool wrongSideSuspect;
        /// <summary>Omuz genişliği bu karede ölçüldü mü.</summary>
        public bool hasShoulderWidth;
    }

    /// <summary>
    /// φ = asin(saturate(w / W_ref)).
    /// torsoLength01 &gt; 0 ise W_ref = torso × shoulder/torso oranı (mesafe-bağımsız).
    /// </summary>
    public static float EstimateSkewDegrees(
        float rawShoulderWidth01,
        float frontalRefWidth01,
        float torsoLength01 = 0f)
    {
        float wRef = ResolveFrontalRefWidth(frontalRefWidth01, torsoLength01);
        float ratio = Mathf.Clamp01(rawShoulderWidth01 / wRef);
        return Mathf.Asin(ratio) * Mathf.Rad2Deg;
    }

    public static float ResolveFrontalRefWidth(float frontalRefWidth01, float torsoLength01)
    {
        if (torsoLength01 > MinRefWidth * 0.5f)
            return Mathf.Max(MinRefWidth, torsoLength01 * DefaultFrontalShoulderToTorso);
        return Mathf.Max(MinRefWidth, frontalRefWidth01);
    }

    public static Result Evaluate(
        float rawShoulderWidth01,
        bool hasShoulderWidth,
        float frontalRefWidth01,
        float torsoLength01,
        float warnDegrees,
        float invalidDegrees,
        bool headVisible,
        bool workingArmVisible,
        bool oppositeArmMoreVisible)
    {
        Result r = default;
        r.headOk = headVisible;
        r.hasShoulderWidth = hasShoulderWidth;

        if (!hasShoulderWidth)
        {
            // Yan profilde uzak omuz sık kaybolur — φ=90 varsayma (false "daha yana dön")
            r.measurementValid = false;
            r.phiDegrees = -1f;
            return r;
        }

        r.phiDegrees = EstimateSkewDegrees(rawShoulderWidth01, frontalRefWidth01, torsoLength01);

        float warn = Mathf.Max(1f, warnDegrees);
        float invalid = Mathf.Max(warn + 1f, invalidDegrees);

        r.withinAccept = r.phiDegrees <= warn;
        r.withinWarn = r.phiDegrees <= invalid;
        r.measurementValid = r.headOk && r.phiDegrees <= invalid;
        r.wrongSideSuspect = r.headOk
            && r.withinAccept
            && !workingArmVisible
            && oppositeArmMoreVisible;
        return r;
    }

    /// <summary>Geriye uyumluluk — torso yok, omuz w varsayılır mevcut.</summary>
    public static Result Evaluate(
        float rawShoulderWidth01,
        float frontalRefWidth01,
        float warnDegrees,
        float invalidDegrees,
        bool headVisible,
        bool workingArmVisible,
        bool oppositeArmMoreVisible)
    {
        return Evaluate(
            rawShoulderWidth01,
            hasShoulderWidth: rawShoulderWidth01 > 1e-5f,
            frontalRefWidth01,
            torsoLength01: 0f,
            warnDegrees,
            invalidDegrees,
            headVisible,
            workingArmVisible,
            oppositeArmMoreVisible);
    }
}
