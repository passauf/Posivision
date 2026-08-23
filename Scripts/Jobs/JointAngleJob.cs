using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Omuz fleksiyon açısı (XY düzlemi) için Burst-derlenmiş paralel job.
/// index 0 = sağ kol, index 1 = sol kol.
/// Z bileşeni kullanılmaz (2D kamera / SaMD kuralı).
/// </summary>
[BurstCompile]
public struct JointAngleJob : IJobParallelFor
{
    /// <summary>Anatomik elevasyon tanım üstü (sarkık 0° → overhead 180°). Sahte 175 tavanı yok.</summary>
    public const float MaxShoulderElevationDegrees = 180f;

    [ReadOnly] public NativeArray<float2> landmarks; // hip, shoulder, elbow triplets packed: [0..2]=right, [3..5]=left
    [ReadOnly] public NativeArray<float> referenceArmLengths;
    [WriteOnly] public NativeArray<float> anglesOut;
    [ReadOnly] public NativeArray<bool> enabled; // kol güvenilir mi?

    public void Execute(int index)
    {
        if (!enabled[index])
        {
            anglesOut[index] = float.NaN;
            return;
        }

        int baseIdx = index * 3;
        float2 hip = landmarks[baseIdx];
        float2 shoulder = landmarks[baseIdx + 1];
        float2 elbow = landmarks[baseIdx + 2];
        float refLen = referenceArmLengths.IsCreated && index < referenceArmLengths.Length
            ? referenceArmLengths[index]
            : 0f;
        anglesOut[index] = ShoulderFlexionElevation(hip, shoulder, elbow, refLen);
    }

    /// <summary>
    /// Omuz fleksiyonu: üst kolun gövde eksenine göre kalkış açısı (XY).
    /// Elevasyon atan2(perp, along) — acos kenar tekilliği yok; yüksek açıda Angle2D ağırlığı düşer.
    /// SaMD Class B; teşhis değildir.
    /// </summary>
    public static float ShoulderFlexionElevation(
        float2 hip, float2 shoulder, float2 elbow, float referenceArmLength)
    {
        float2 trunk = hip - shoulder;
        float2 arm = elbow - shoulder;
        float armLen = math.length(arm);
        float trunkLenSq = math.lengthsq(trunk);
        if (armLen < 1e-6f || trunkLenSq < 1e-8f) return float.NaN;

        float refLen = referenceArmLength > 1e-5f ? referenceArmLength : armLen;
        float invRef = math.rsqrt(refLen * refLen);
        float2 nTrunk = trunk * math.rsqrt(trunkLenSq);
        float alongTrunk = math.dot(arm, nTrunk);
        float2 perp = arm - nTrunk * alongTrunk;
        // 0° sarkık (along>0) → 180° overhead (along<0); yan gürültü acos'tan daha yumuşak
        float trunkElevation = math.degrees(math.atan2(math.length(perp) * invRef, alongTrunk * invRef));

        float trunkAngle = Angle2D(hip, shoulder, elbow);
        if (float.IsNaN(trunkAngle))
            return math.clamp(trunkElevation, 0f, MaxShoulderElevationDegrees);

        // Yüksek açıda hip–omuz–dirsek neredeyse hizalı → Angle2D gürültülü; elevasyona güven
        float elevWeight = 0.85f;
        if (trunkElevation >= 150f) elevWeight = 0.92f;
        if (trunkElevation >= 165f) elevWeight = 0.96f;

        float blended = math.lerp(trunkAngle, trunkElevation, elevWeight);
        return math.clamp(blended, 0f, MaxShoulderElevationDegrees);
    }

    /// <summary>Üç nokta arasında b merkezli 2D açı (derece).</summary>
    public static float Angle2D(float2 a, float2 b, float2 c)
    {
        float2 v1 = a - b;
        float2 v2 = c - b;
        float m1 = math.lengthsq(v1);
        float m2 = math.lengthsq(v2);
        if (m1 < 1e-12f || m2 < 1e-12f) return float.NaN;

        float2 n1 = math.normalize(v1);
        float2 n2 = math.normalize(v2);
        float dot = math.clamp(math.dot(n1, n2), -1f, 1f);
        return math.degrees(math.acos(dot));
    }
}

/// <summary>
/// Omurga lean açısı (midHip → midShoulder vs anatomik yukarı).
/// Ana thread'de tek seferlik çağrılabilir veya IJob olarak schedule edilebilir.
/// </summary>
[BurstCompile]
public struct SpineLeanJob : IJob
{
    public float2 leftShoulder;
    public float2 rightShoulder;
    public float2 leftHip;
    public float2 rightHip;
    public NativeArray<float> leanDegreesOut; // length 1

    public void Execute()
    {
        float2 midShoulder = (leftShoulder + rightShoulder) * 0.5f;
        float2 midHip = (leftHip + rightHip) * 0.5f;
        float2 spine = midShoulder - midHip;

        if (math.lengthsq(spine) < 1e-8f)
        {
            leanDegreesOut[0] = 0f;
            return;
        }

        float2 anatomicalUp = new float2(0f, -1f);
        float2 nSpine = math.normalize(spine);
        float2 nUp = math.normalize(anatomicalUp);
        float dot = math.clamp(math.dot(nSpine, nUp), -1f, 1f);
        leanDegreesOut[0] = math.degrees(math.acos(dot));
    }
}
