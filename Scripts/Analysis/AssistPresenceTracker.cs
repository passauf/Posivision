using UnityEngine;

/// <summary>
/// 2. kişi varlığı + yardımcı pose önbelleği + yakınlık uyarısı glue.
/// 2. kişi: yalnızca güvenilir + hastadan ayrık yardımcı pose için uyarı.
/// AssistedRepDetector ayrı kalır. SaMD Class B; teşhis değildir. Zero-allocation.
/// </summary>
public sealed class AssistPresenceTracker
{
    public const float DefaultMinShoulderSeparation01 = 0.07f;
    public const float DefaultMinHelperShoulderWidth01 = 0.045f;
    public const int DefaultSecondPersonMinFrames = 6;

    public struct Config
    {
        public bool warnOnSecondPerson;
        public int secondPersonMinFrames;
        public float secondPersonWarningCooldownSeconds;
        public float minShoulderSeparation01;
        public float minHelperShoulderWidth01;
        public bool enableConfidenceGate;
        public float landmarkVisibilityThreshold;
        public bool requirePresenceScore;
    }

    private Config _config;
    private AssistedHelperPose _cachedHelperPose;
    private bool _cachedHasHelperPose;
    private float _cachedAssistInvShoulderWidth;
    private int _secondPersonStreak;
    private float _lastSecondPersonWarnTime = -100f;
    private bool _secondPersonWarnedThisPresence;
    private bool _assistProximityWarnedThisStreak;

    public bool HasHelperPose => _cachedHasHelperPose;
    public float CachedInvShoulderWidth => _cachedAssistInvShoulderWidth;
    public ref AssistedHelperPose HelperPose => ref _cachedHelperPose;
    public int SecondPersonStreak => _secondPersonStreak;

    public void Configure(in Config config)
    {
        _config = config;
    }

    public void Reset()
    {
        _cachedHelperPose = default;
        _cachedHasHelperPose = false;
        _cachedAssistInvShoulderWidth = 0f;
        _secondPersonStreak = 0;
        _lastSecondPersonWarnTime = -100f;
        _secondPersonWarnedThisPresence = false;
        _assistProximityWarnedThisStreak = false;
    }

    public void ClearHelperCache()
    {
        _cachedHasHelperPose = false;
        _cachedHelperPose = default;
    }

    public void ClearProximityWarnLatch()
    {
        _assistProximityWarnedThisStreak = false;
    }

    public bool IsSecondPersonOnStage()
    {
        return _secondPersonStreak >= Mathf.Max(1, _config.secondPersonMinFrames);
    }

    public void CacheHelperPose(in PoseLandmarkSample sample, float invShoulderWidth)
    {
        _cachedAssistInvShoulderWidth = invShoulderWidth;
        _cachedHasHelperPose = IsConfidentDistinctHelper(in sample);
        if (!_cachedHasHelperPose)
        {
            _cachedHelperPose = default;
            return;
        }

        _cachedHelperPose = new AssistedHelperPose
        {
            leftShoulder = ToAssistedLandmark(sample.helperLeftShoulder),
            rightShoulder = ToAssistedLandmark(sample.helperRightShoulder),
            leftElbow = ToAssistedLandmark(sample.helperLeftElbow),
            rightElbow = ToAssistedLandmark(sample.helperRightElbow),
            leftWrist = ToAssistedLandmark(sample.helperLeftWrist),
            rightWrist = ToAssistedLandmark(sample.helperRightWrist),
            leftIndex = ToAssistedLandmark(sample.helperLeftIndex),
            rightIndex = ToAssistedLandmark(sample.helperRightIndex),
            leftHip = ToAssistedLandmark(sample.helperLeftHip),
            rightHip = ToAssistedLandmark(sample.helperRightHip)
        };
    }

    /// <summary>
    /// Güvenilir ve hastadan ayrık 2. pose → ekran uyarısı. SaMD Class B bilgilendirme; teşhis değildir.
    /// </summary>
    public void UpdateSecondPersonPresence(
        in PoseLandmarkSample sample,
        WarningManager warningManager,
        SessionReportManager reportManager)
    {
        if (IsConfidentDistinctHelper(in sample))
        {
            _secondPersonStreak++;
            if (!_config.warnOnSecondPerson) return;
            if (!IsSecondPersonOnStage()) return;
            if (_secondPersonWarnedThisPresence) return;

            float cooldown = Mathf.Max(1f, _config.secondPersonWarningCooldownSeconds);
            if (Time.time <= _lastSecondPersonWarnTime + cooldown) return;

            _secondPersonWarnedThisPresence = true;
            _lastSecondPersonWarnTime = Time.time;
            if (reportManager != null)
                reportManager.RegisterSecondPersonEvent();
            if (warningManager != null)
                warningManager.TriggerWarning(Loc.T("warn.secondPerson"));
        }
        else
        {
            _secondPersonStreak = 0;
            _secondPersonWarnedThisPresence = false;
        }
    }

    public void MaybeWarnProximity(
        bool isAssistFromMultiPerson,
        WarningManager warningManager,
        SessionReportManager reportManager)
    {
        if (!isAssistFromMultiPerson)
        {
            _assistProximityWarnedThisStreak = false;
            return;
        }

        if (_assistProximityWarnedThisStreak) return;
        _assistProximityWarnedThisStreak = true;
        if (reportManager != null)
            reportManager.RegisterAssistNearEvent();
        if (warningManager != null)
            warningManager.TriggerWarning(Loc.T("warn.assistNear"));
    }

    public bool IsConfidentDistinctHelper(in PoseLandmarkSample sample)
    {
        if (!sample.hasHelperPose || sample.detectedPoseCount < 2)
            return false;

        if (!IsPointConfident(sample.helperLeftShoulder)
            || !IsPointConfident(sample.helperRightShoulder)
            || !IsPointConfident(sample.helperLeftHip)
            || !IsPointConfident(sample.helperRightHip))
            return false;

        float helperWidth = ShoulderWidth(sample.helperLeftShoulder, sample.helperRightShoulder);
        float minWidth = Mathf.Max(0.02f, _config.minHelperShoulderWidth01);
        if (helperWidth < minWidth)
            return false;

        if (!IsPointConfident(sample.leftShoulder) || !IsPointConfident(sample.rightShoulder))
            return false;

        Vector2 patientMid = MidPoint(sample.leftShoulder, sample.rightShoulder);
        Vector2 helperMid = MidPoint(sample.helperLeftShoulder, sample.helperRightShoulder);
        float minSep = Mathf.Max(0.03f, _config.minShoulderSeparation01);
        float dx = patientMid.x - helperMid.x;
        float dy = patientMid.y - helperMid.y;
        return (dx * dx + dy * dy) >= minSep * minSep;
    }

    private AssistedLandmark ToAssistedLandmark(LandmarkPoint p)
    {
        AssistedLandmark a;
        a.x = p.x;
        a.y = p.y;
        a.confident = p.hasVisibility && IsPointConfident(p);
        return a;
    }

    private bool IsPointConfident(LandmarkPoint p)
    {
        if (!_config.enableConfidenceGate) return true;
        float thr = Mathf.Clamp01(_config.landmarkVisibilityThreshold);
        if (p.hasVisibility && p.visibility < thr) return false;
        if (_config.requirePresenceScore && p.hasPresence && p.presence < thr) return false;
        return true;
    }

    private static Vector2 MidPoint(LandmarkPoint a, LandmarkPoint b)
    {
        return new Vector2((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f);
    }

    private static float ShoulderWidth(LandmarkPoint left, LandmarkPoint right)
    {
        float dx = left.x - right.x;
        float dy = left.y - right.y;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }
}
