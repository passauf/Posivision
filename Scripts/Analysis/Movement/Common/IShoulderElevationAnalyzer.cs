using UnityEngine;

/// <summary>
/// Omuz elevasyon ailesi (fleksiyon / abdüksiyon): üst kol referans uzunluğu + ROM düzeltme.
/// </summary>
public interface IShoulderElevationAnalyzer : IMovementAnalyzer
{
    void ConfigureRomCorrection(in TheoreticalRomCorrectionConfig config);
    float GetReferenceArmLength(int mpArmIndex);
    void UpdateReferenceArmLength(int mpArmIndex, Vector2 shoulder, Vector2 elbow);
}

/// <summary>Üst kol referans uzunluğu güncelleme — her iki omuz hareketi paylaşır.</summary>
public struct ShoulderElevationReferenceConfig
{
    public float refUpdateMinRatio;
    public float refUpdateMaxRatio;
}
