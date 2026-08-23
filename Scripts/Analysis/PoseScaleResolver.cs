using UnityEngine;

/// <summary>
/// Ön vs yan protokol ölçek birimi.
/// Ön: omuz genişliği (mevcut, doğru).
/// Yan: gövde boyu (orta-omuz → orta-kalça) — yan görünümde omuz genişliği çöker.
/// SaMD Class B; teşhis değildir. Zero-allocation.
/// </summary>
public enum PoseScaleBasis : byte
{
    /// <summary>Frontal: |L_shoulder − R_shoulder|.</summary>
    ShoulderWidth = 0,
    /// <summary>Side: |midShoulder − midHip| (kamera mesafesi / boy proxy).</summary>
    TorsoLength = 1
}

/// <summary>
/// Protokole göre normalize ölçeği üretir. Yan profilde omuz genişliği kullanılmaz.
/// </summary>
public static class PoseScaleResolver
{
    public const float MinScale = 1e-5f;

    public static PoseScaleBasis FromCameraProtocol(CameraProtocol camera)
    {
        return camera == CameraProtocol.SideProfile
            ? PoseScaleBasis.TorsoLength
            : PoseScaleBasis.ShoulderWidth;
    }

    public static PoseScaleBasis FromMovement(MovementId movementId)
    {
        return FromCameraProtocol(ExerciseCatalog.GetOrDefault(movementId).Camera);
    }

    public static PoseScaleBasis FromSideView(bool patientSideView)
    {
        return patientSideView ? PoseScaleBasis.TorsoLength : PoseScaleBasis.ShoulderWidth;
    }

    /// <summary>
    /// Normalize öncesi görüntü 0–1 biriminde ölçek. valid=false → çağıran 1 kullanır.
    /// </summary>
    public static float Compute(
        PoseScaleBasis basis,
        Vector2 leftShoulder,
        Vector2 rightShoulder,
        Vector2 leftHip,
        Vector2 rightHip,
        bool leftShoulderOk,
        bool rightShoulderOk,
        bool leftHipOk,
        bool rightHipOk,
        out bool valid)
    {
        if (basis == PoseScaleBasis.TorsoLength)
            return ComputeTorsoLength(
                leftShoulder, rightShoulder, leftHip, rightHip,
                leftShoulderOk, rightShoulderOk, leftHipOk, rightHipOk, out valid);

        return ComputeShoulderWidth(
            leftShoulder, rightShoulder, leftShoulderOk, rightShoulderOk, out valid);
    }

    public static float ComputeShoulderWidth(
        Vector2 leftShoulder,
        Vector2 rightShoulder,
        bool leftOk,
        bool rightOk,
        out bool valid)
    {
        if (!leftOk || !rightOk)
        {
            valid = false;
            return 0f;
        }

        float w = Vector2.Distance(leftShoulder, rightShoulder);
        valid = w > MinScale;
        return valid ? w : 0f;
    }

    /// <summary>
    /// Gövde boyu: mümkünse mid-omuz → mid-kalça; yoksa tek taraf omuz–kalça.
    /// Yan profilde omuzlar üst üste gelse bile dikey gövde uzunluğu stabil kalır.
    /// </summary>
    public static float ComputeTorsoLength(
        Vector2 leftShoulder,
        Vector2 rightShoulder,
        Vector2 leftHip,
        Vector2 rightHip,
        bool leftShoulderOk,
        bool rightShoulderOk,
        bool leftHipOk,
        bool rightHipOk,
        out bool valid)
    {
        valid = false;

        bool bothShoulders = leftShoulderOk && rightShoulderOk;
        bool bothHips = leftHipOk && rightHipOk;

        if (bothShoulders && bothHips)
        {
            Vector2 midS = (leftShoulder + rightShoulder) * 0.5f;
            Vector2 midH = (leftHip + rightHip) * 0.5f;
            float len = Vector2.Distance(midS, midH);
            if (len > MinScale)
            {
                valid = true;
                return len;
            }
        }

        // Tek taraf omuz–kalça (yan kadrajda sık)
        if (leftShoulderOk && leftHipOk)
        {
            float len = Vector2.Distance(leftShoulder, leftHip);
            if (len > MinScale)
            {
                valid = true;
                return len;
            }
        }

        if (rightShoulderOk && rightHipOk)
        {
            float len = Vector2.Distance(rightShoulder, rightHip);
            if (len > MinScale)
            {
                valid = true;
                return len;
            }
        }

        // Çapraz / karışık: herhangi omuz + herhangi kalça
        if (leftShoulderOk && rightHipOk)
        {
            float len = Vector2.Distance(leftShoulder, rightHip);
            if (len > MinScale)
            {
                valid = true;
                return len;
            }
        }

        if (rightShoulderOk && leftHipOk)
        {
            float len = Vector2.Distance(rightShoulder, leftHip);
            if (len > MinScale)
            {
                valid = true;
                return len;
            }
        }

        return 0f;
    }
}
