using UnityEngine;

/// <summary>
/// 2. kişi varlığı + yardımcı pose önbelleği + yakınlık uyarısı glue.
/// AssistedRepDetector ayrı kalır. SaMD Class B; teşhis değildir. Zero-allocation.
/// </summary>
public sealed class AssistPresenceTracker
{
    public struct Config
    {
        public bool warnOnSecondPerson;
        public int secondPersonMinFrames;
        public float secondPersonWarningCooldownSeconds;
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
        _cachedHasHelperPose = sample.hasHelperPose && sample.detectedPoseCount >= 2;
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
    /// Sahnede 2+ pose → ekran uyarısı. SaMD Class B bilgilendirme; teşhis değildir.
    /// </summary>
    public void UpdateSecondPersonPresence(
        int poseCount,
        WarningManager warningManager,
        SessionReportManager reportManager)
    {
        if (poseCount >= 2)
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
}
