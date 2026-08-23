using UnityEngine;

/// <summary>
/// 4 katmanlı otomatik yardımlı tekrar sezgisi (Class B; teşhis değil):
/// 1) Person lock host'ta — 2) El temas bölgesi — 3) Vektörel hız (dot&gt;0) —
/// 4) Tekrar aktif hareket süresinin min oranı boyunca süreğenlik.
/// Poz benzerliği / tüm vücut elevasyonu kullanılmaz; terapist eli ↔ hasta çalışan eklem.
/// Zero-allocation hot path.
/// </summary>
public sealed class AssistedRepDetector
{
    private AssistedRepDetectorConfig _config;

    private ArmAssistAccum _right;
    private ArmAssistAccum _left;

    public float AssistRatioRight => Ratio(in _right);
    public float AssistRatioLeft => Ratio(in _left);
    public bool LatchRight => _right.latch;
    public bool LatchLeft => _left.latch;

    public void Configure(in AssistedRepDetectorConfig config)
    {
        _config = config;
    }

    public void Reset()
    {
        _right = default;
        _left = default;
    }

    public void ClearTransientStreaks()
    {
        ClearVelState(ref _right);
        ClearVelState(ref _left);
        _right.contactStreak = 0;
        _left.contactStreak = 0;
    }

    /// <summary>
    /// Anatomik kol. patient* omuz-normalize XY. helper ham 0–1 (inv ile normalize edilir).
    /// Çalışan eklem: omuz fleksiyonunda bilek (yoksa dirsek).
    /// </summary>
    public void UpdateArm(
        bool anatomicalRight,
        bool armTrackingOk,
        bool wristOk,
        Vector2 patientElbowNorm,
        Vector2 patientWristNorm,
        float patientAngleDegrees,
        float deltaTime,
        float lowerLimitDegrees,
        bool hasHelperPose,
        int detectedPoseCount,
        in AssistedHelperPose helper,
        float invShoulderWidth)
    {
        ref ArmAssistAccum arm = ref anatomicalRight ? ref _right : ref _left;

        if (!armTrackingOk || deltaTime <= 1e-5f || invShoulderWidth <= 0f)
        {
            arm.contactStreak = 0;
            ClearVelState(ref arm);
            return;
        }

        bool repInProgress = !float.IsNaN(patientAngleDegrees)
            && patientAngleDegrees > lowerLimitDegrees;

        if (!repInProgress)
        {
            // Döngü bitti / henüz başlamadı — süreğenlik sayaçları ve latch sıfır
            arm = default;
            // Hız geçmişini bir sonraki tekrar için temiz tut
            return;
        }

        bool helperReady = hasHelperPose && detectedPoseCount >= 2;
        if (!helperReady)
        {
            arm.contactStreak = 0;
            ClearVelState(ref arm);
            // Tekrar sürüyor ama yardımcı yok — aktif kare sayılmaz (oran bozulmasın)
            return;
        }

        // --- Katman 2: Temas (terapist eli → hasta çalışan eklem) ---
        Vector2 workingJoint = wristOk ? patientWristNorm : patientElbowNorm;
        float thresh = Mathf.Max(0.05f, _config.proximityShoulderWidths);
        float threshSq = thresh * thresh;

        bool contactElbow = IsAnyHelperHandNear(in helper, patientElbowNorm, invShoulderWidth, threshSq);
        bool contactWrist = wristOk
            && IsAnyHelperHandNear(in helper, patientWristNorm, invShoulderWidth, threshSq);
        bool contact = contactElbow || contactWrist;

        arm.contactStreak = contact ? arm.contactStreak + 1 : 0;
        int minContact = Mathf.Max(1, _config.minContactFrames);
        bool contactStable = arm.contactStreak >= minContact;

        // En yakın terapist eli (bilek/işaret) — hız için
        bool hasHand = TryNearestHelperHand(
            in helper, workingJoint, invShoulderWidth,
            out Vector2 helperHandNorm);

        // --- Katman 3: Vektörel hız eşleşmesi ---
        Vector2 vPatient = default;
        Vector2 vHelper = default;
        bool velValid = false;
        float dot = 0f;
        float patientSpeed = 0f;
        float helperSpeed = 0f;

        if (hasHand && arm.hasPrevWorking && arm.hasPrevHelperHand)
        {
            vPatient = (workingJoint - arm.prevWorking) / deltaTime;
            vHelper = (helperHandNorm - arm.prevHelperHand) / deltaTime;
            patientSpeed = vPatient.magnitude;
            helperSpeed = vHelper.magnitude;
            dot = vPatient.x * vHelper.x + vPatient.y * vHelper.y;
            velValid = true;
        }

        arm.prevWorking = workingJoint;
        arm.hasPrevWorking = true;
        if (hasHand)
        {
            arm.prevHelperHand = helperHandNorm;
            arm.hasPrevHelperHand = true;
        }
        else
        {
            arm.hasPrevHelperHand = false;
        }

        float minSpeed = Mathf.Max(0.05f, _config.minJointSpeedShoulderWidthsPerSec);
        bool patientMoving = velValid && patientSpeed >= minSpeed;
        bool helperMoving = velValid && helperSpeed >= minSpeed * 0.5f;
        // Yön uyumu: aynı yarı düzlem (dot > 0) + ikisi de hareketli
        bool directionMatch = velValid && patientMoving && helperMoving && dot > 0f;

        // --- Katman 4: Süreğenlik (aktif hareket kareleri üzerinden) ---
        if (patientMoving)
        {
            arm.activeMotionFrames++;
            if (contactStable && directionMatch)
            {
                arm.matchedMotionFrames++;
                arm.hadDirectionMatchThisRep = true;
            }
        }
        else if (contactStable && arm.hadDirectionMatchThisRep && velValid
                 && patientSpeed < minSpeed && helperSpeed < minSpeed)
        {
            // Tutuş / kısa durak: temas sürüyorsa destek devamı sayılır
            arm.holdSupportFrames++;
            arm.holdTotalFrames++;
        }
        else if (!patientMoving)
        {
            arm.holdTotalFrames++;
        }

        float ratio = ComputeAssistFraction(in arm);
        int minActive = Mathf.Max(1, _config.minActiveMotionFrames);
        float need = Mathf.Clamp01(_config.minAssistRepFraction);
        if (arm.activeMotionFrames >= minActive
            && arm.hadDirectionMatchThisRep
            && ratio >= need)
        {
            arm.latch = true;
        }
    }

    /// <summary>Otomatik yardımlı efektif (süreğenlik eşiği / latch).</summary>
    public bool IsAutoAssistEffective(bool anatomicalRight)
    {
        ref ArmAssistAccum arm = ref anatomicalRight ? ref _right : ref _left;
        if (arm.latch) return true;
        int minActive = Mathf.Max(1, _config.minActiveMotionFrames);
        if (arm.activeMotionFrames < minActive || !arm.hadDirectionMatchThisRep)
            return false;
        return ComputeAssistFraction(in arm) >= Mathf.Clamp01(_config.minAssistRepFraction);
    }

    private static float Ratio(in ArmAssistAccum arm)
    {
        return ComputeAssistFraction(in arm);
    }

    private static float ComputeAssistFraction(in ArmAssistAccum arm)
    {
        // Ağırlık: aktif hareket + (eşleşmeden sonraki) tutuş destek kareleri
        int den = arm.activeMotionFrames + arm.holdSupportFrames;
        if (den <= 0) return 0f;
        int num = arm.matchedMotionFrames + arm.holdSupportFrames;
        return num / (float)den;
    }

    private static void ClearVelState(ref ArmAssistAccum arm)
    {
        arm.hasPrevWorking = false;
        arm.hasPrevHelperHand = false;
    }

    /// <summary>Yalnızca terapist elleri (bilek + işaret) — dirsek/gövde yok.</summary>
    private static bool IsAnyHelperHandNear(
        in AssistedHelperPose helper,
        Vector2 patientNormXy,
        float invShoulderWidth,
        float threshSq)
    {
        return IsNear(helper.leftWrist, patientNormXy, invShoulderWidth, threshSq)
            || IsNear(helper.rightWrist, patientNormXy, invShoulderWidth, threshSq)
            || IsNear(helper.leftIndex, patientNormXy, invShoulderWidth, threshSq)
            || IsNear(helper.rightIndex, patientNormXy, invShoulderWidth, threshSq);
    }

    private static bool TryNearestHelperHand(
        in AssistedHelperPose helper,
        Vector2 patientNormXy,
        float invShoulderWidth,
        out Vector2 helperHandNorm)
    {
        helperHandNorm = default;
        float best = float.MaxValue;
        bool found = false;
        ConsiderHand(helper.leftWrist, patientNormXy, invShoulderWidth, ref best, ref found, ref helperHandNorm);
        ConsiderHand(helper.rightWrist, patientNormXy, invShoulderWidth, ref best, ref found, ref helperHandNorm);
        ConsiderHand(helper.leftIndex, patientNormXy, invShoulderWidth, ref best, ref found, ref helperHandNorm);
        ConsiderHand(helper.rightIndex, patientNormXy, invShoulderWidth, ref best, ref found, ref helperHandNorm);
        return found;
    }

    private static void ConsiderHand(
        in AssistedLandmark pt,
        Vector2 patient,
        float invShoulderWidth,
        ref float bestSq,
        ref bool found,
        ref Vector2 bestPos)
    {
        if (!pt.confident) return;
        float hx = pt.x * invShoulderWidth;
        float hy = pt.y * invShoulderWidth;
        float dx = hx - patient.x;
        float dy = hy - patient.y;
        float d2 = dx * dx + dy * dy;
        if (d2 >= bestSq) return;
        bestSq = d2;
        found = true;
        bestPos = new Vector2(hx, hy);
    }

    private static bool IsNear(
        in AssistedLandmark helper,
        Vector2 patientNormXy,
        float invShoulderWidth,
        float threshSq)
    {
        if (!helper.confident) return false;
        float hx = helper.x * invShoulderWidth;
        float hy = helper.y * invShoulderWidth;
        float dx = hx - patientNormXy.x;
        float dy = hy - patientNormXy.y;
        return (dx * dx + dy * dy) <= threshSq;
    }

    private struct ArmAssistAccum
    {
        public int contactStreak;
        public int activeMotionFrames;
        public int matchedMotionFrames;
        public int holdSupportFrames;
        public int holdTotalFrames;
        public bool hadDirectionMatchThisRep;
        public bool latch;
        public Vector2 prevWorking;
        public Vector2 prevHelperHand;
        public bool hasPrevWorking;
        public bool hasPrevHelperHand;
    }
}

/// <summary>4 katmanlı yardımlı sezgi eşikleri — host SerializeField ile doldurur.</summary>
public struct AssistedRepDetectorConfig
{
    /// <summary>Temas yarıçapı (omuz genişliği birimi). ~0.45–0.55 ≈ 15–20 cm.</summary>
    public float proximityShoulderWidths;
    /// <summary>Temas debounce (kare).</summary>
    public int minContactFrames;
    /// <summary>Hasta çalışan eklem min hızı (omuz-normalize birim / sn).</summary>
    public float minJointSpeedShoulderWidthsPerSec;
    /// <summary>Tekrarda eşleşen kare oranı eşiği (0.5–0.6).</summary>
    public float minAssistRepFraction;
    /// <summary>Karar için asgari aktif hareket karesi.</summary>
    public int minActiveMotionFrames;
}

/// <summary>Ham görüntü 0–1 landmark (henüz omuz-normalize değil).</summary>
public struct AssistedLandmark
{
    public float x;
    public float y;
    public bool confident;
}

/// <summary>2. kişi (terapist) noktaları — stack struct, referans tip yok.</summary>
public struct AssistedHelperPose
{
    public AssistedLandmark leftShoulder;
    public AssistedLandmark rightShoulder;
    public AssistedLandmark leftElbow;
    public AssistedLandmark rightElbow;
    public AssistedLandmark leftWrist;
    public AssistedLandmark rightWrist;
    public AssistedLandmark leftIndex;
    public AssistedLandmark rightIndex;
    public AssistedLandmark leftHip;
    public AssistedLandmark rightHip;
}
