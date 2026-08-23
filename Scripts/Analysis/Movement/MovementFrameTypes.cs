using UnityEngine;

/// <summary>
/// Ana thread hareket karesi girdisi. Host önceden tahsisli landmark vektörlerini verir — hot path allocation yok.
/// SaMD Class B: klinik eşikler host Configure ile gelir; teşhis değildir.
/// </summary>
public struct MovementFrameContext
{
    public float deltaTime;
    public bool swapArms;
    public bool mpRightOk;
    public bool mpLeftOk;
    public bool mpRightWristOk;
    public bool mpLeftWristOk;
    public bool clinicalRightOk;
    public bool clinicalLeftOk;
    /// <summary>JointAngleJob ham çıktısı (MP sağ/sol).</summary>
    public float jobAngleMpRight;
    public float jobAngleMpLeft;
    public Vector2 mpRightShoulder;
    public Vector2 mpRightElbow;
    public Vector2 mpRightWrist;
    public Vector2 mpLeftShoulder;
    public Vector2 mpLeftElbow;
    public Vector2 mpLeftWrist;
    /// <summary>Gövde yaw (derece); yan protokolde 0.</summary>
    public float bodyYawDegrees;
    /// <summary>Yan kamera protokolü — yaw düzeltmesi kapalı.</summary>
    public bool patientSideView;
    /// <summary>Normalize öncesi ham omuz genişliği (görüntü 0–1). Yan φ / teorik mesafe proxy.</summary>
    public float rawShoulderWidth01;
}

/// <summary>Hareket analizi çıktısı — klinisyen UI / tekrar / avatar.</summary>
public struct MovementFrameResult
{
    public float clinicalRightAngle;
    public float clinicalLeftAngle;
    public bool avatarMpRightOk;
    public float avatarMpRightAngle;
    public bool avatarMpLeftOk;
    public float avatarMpLeftAngle;
    public float repGateRight;
    public float repGateLeft;
    public bool repGateRightValid;
    public bool repGateLeftValid;
    public bool foreshortenMpRight;
    public bool foreshortenMpLeft;
    public bool foreshortenClinicalRight;
    public bool foreshortenClinicalLeft;
    public bool hasClinicalData;
    public bool notifyForeshorten;
}

/// <summary>Tekrar sayacı girdisi (tek kol).</summary>
public struct RepTickContext
{
    public float gateAngle;
    public bool gateValid;
    public float deltaTime;
    public float targetDegrees;
    public float lowerLimitDegrees;
    public float holdSeconds;
    public float enterSlackDegrees;
    public float minTravelDegrees;
    public bool invalidatePose;
    public bool anatomicalRight;
}

/// <summary>Tekrar sayacı sonucu — UI/rapor host'ta uygulanır.</summary>
public struct RepTickResult
{
    public bool countedValid;
    public bool countedInvalid;
    public float gateAngleAtCount;
}

/// <summary>Kol başına tekrar durumu (host veya policy taşıyabilir).</summary>
public struct ArmRepState
{
    public int count;
    public int invalidCount;
    public bool isUp;
    public bool repInvalid;
    public float targetHoldStreak;
    public bool repCountedAtPeak;
    public bool inTargetZone;
}
