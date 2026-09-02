using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using Mediapipe.Tasks.Components.Containers;

/// <summary>PhysioAnalyzer - RepCoordinator.</summary>
public partial class PhysioAnalyzer
{
    void Update()
    {
        if (!_sessionStarted || _sessionEnded) return;

        // Kuyruktan yalnızca en güncel kareyi al (gecikme birikmesin)
        PoseLandmarkSample latest = default;
        bool gotSample = false;
        while (_poseQueue.TryDequeue(out var sample))
        {
            latest = sample;
            gotSample = true;
        }

        if (gotSample)
        {
            ProcessSampleOnMainThread(latest);
        }

        float repDt = Time.unscaledDeltaTime;
        bool warnLeanRep = false;
        bool invalidateLeanEarly = _torsoRegionActive && _spineCompensationGate.Evaluate(
            _lastSpineLeanDegrees, patientAgeYears, out warnLeanRep,
            warningManager, _voiceCoach, enableVoiceCoach, reportManager);
        bool invalidateFacingEarly = _movementProtocolProfile.enableYawGate
            && _frontalFacingGate.CheckWarnings(
                patientSideView, warningManager, _voiceCoach, enableVoiceCoach);
        bool invalidateStrainEarly = CheckFaceStrainWarning();
        bool invalidateSideEarly = _movementProtocolProfile.enableSideProfileGate
            && patientSideView
            && !_sideProfileSessionGate.MeasurementValid;
        if (invalidateSideEarly)
            _sideProfileSessionGate.MaybeWarnInvalid(warningManager);
        else if (_movementProtocolProfile.enableSideProfileGate && patientSideView)
            _sideProfileSessionGate.MaybeWarnSoft(warningManager);

        bool swapArmsEarly = ShouldSwapArmLaterality();
        bool fsClinicalRightEarly = swapArmsEarly ? _foreshortenMpLeft : _foreshortenMpRight;
        bool fsClinicalLeftEarly = swapArmsEarly ? _foreshortenMpRight : _foreshortenMpLeft;

        var invalidationInput = new MovementInvalidationAssembler.Input
        {
            protocol = _movementProtocolProfile,
            patientSideView = patientSideView,
            torsoActive = _torsoRegionActive,
            invalidateLean = invalidateLeanEarly,
            invalidateFacing = invalidateFacingEarly,
            invalidateStrain = invalidateStrainEarly,
            sideMeasurementValid = _sideProfileSessionGate.MeasurementValid,
            foreshortenClinicalRight = fsClinicalRightEarly,
            foreshortenClinicalLeft = fsClinicalLeftEarly,
            measureRightArm = measureRightArm,
            measureLeftArm = measureLeftArm
        };
        MovementInvalidationAssembler.Evaluate(in invalidationInput, out MovementInvalidationAssembler.Output invalidation);

        if (!_romAssessmentAnalyzing)
        {
            if (measureRightArm)
                TickRepViaPolicy(
                    _lastRepGateRight, _repGateRightValid, repDt,
                    rightRepText, Loc.T("hud.rep.right"),
                    ref _armRepR, ref _lastShownCountR, ref _cachedRightRep,
                    invalidation.invalidateRightRep, true);
            if (measureLeftArm)
                TickRepViaPolicy(
                    _lastRepGateLeft, _repGateLeftValid, repDt,
                    leftRepText, Loc.T("hud.rep.left"),
                    ref _armRepL, ref _lastShownCountL, ref _cachedLeftRep,
                    invalidation.invalidateLeftRep, false);
        }
        else if (_romAssessmentAnalyzing)
        {
            string analyzeLabel = Loc.T("assess.live.measuring");
            if (measureRightArm && rightRepText != null && _cachedRightRep != analyzeLabel)
            {
                _cachedRightRep = analyzeLabel;
                rightRepText.text = analyzeLabel;
            }
            if (measureLeftArm && leftRepText != null && _cachedLeftRep != analyzeLabel)
            {
                _cachedLeftRep = analyzeLabel;
                leftRepText.text = analyzeLabel;
            }
        }

        if (!_hasData)
        {
            PushCompensationLeanVisual(false);
            return;
        }

        // Kompansasyon + ön görünüm kapısı
        bool warnLean = warnLeanRep;
        PushCompensationLeanVisual(warnLean);

        if (measureRightArm)
            UpdateArm(true, _physicRight, rightSlider, rightColorCtrl, rightAngleText,
                ref _cachedRightAngle, ref _lastShownRightAngle);
        if (measureLeftArm)
            UpdateArm(false, _physicLeft, leftSlider, leftColorCtrl, leftAngleText,
                ref _cachedLeftAngle, ref _lastShownLeftAngle);

        if (reportManager != null)
        {
            if (faceStrainAnalyzer != null && faceStrainAnalyzer.HasFace)
                reportManager.RegisterStrainSample(faceStrainAnalyzer.CurrentEffort01, _physicRight, _physicLeft);

            bool allowPeak = _qualityFramePublisher.QualityAllowsPeakRom
                && (!_movementProtocolProfile.yawAffectsPeakRom || IsFrontalFacingOk)
                && !invalidation.blockPeakRomRight
                && !invalidation.blockPeakRomLeft;
            reportManager.RegisterAngleSample(
                _physicRight, _physicLeft, measureRightArm, measureLeftArm,
                allowPeakUpdate: allowPeak,
                assistRight: IsAssistEffectiveRight,
                assistLeft: IsAssistEffectiveLeft);
        }

        if (!_sessionEnded && IsSessionGoalReached())
        {
            TryFinishReachedGoal();
        }

        _hasData = false;
    }

    /// <summary>
    /// Tekrar sayımı politikaya delege; UI/rapor yan etkileri host'ta kalır.
    /// Refactor only — klinik eşik değişikliği yok. SaMD Class B; teşhis değildir.
    /// </summary>
    private void TickRepViaPolicy(
        float gateAngle,
        bool gateValid,
        float dt,
        TextMeshProUGUI rText,
        string pref,
        ref ArmRepState state,
        ref int lastShownCount,
        ref string cachedRep,
        bool invalidatePose,
        bool anatomicalRight)
    {
        if (_repPolicy == null) EnsureMovementStrategy();
        if (_repPolicy == null) return;

        var ctx = new RepTickContext
        {
            gateAngle = gateAngle,
            gateValid = gateValid,
            deltaTime = dt,
            targetDegrees = targetAngleDegrees,
            lowerLimitDegrees = repLowerLimitDegrees,
            holdSeconds = repTargetHoldSeconds,
            enterSlackDegrees = repTargetEnterSlackDegrees,
            minTravelDegrees = repMinTravelDegrees,
            invalidatePose = invalidatePose,
            anatomicalRight = anatomicalRight
        };
        RepTickResult result = default;
        _repPolicy.Tick(in ctx, ref state, ref result);

        if (anatomicalRight)
        {
            _countR = state.count;
            _invalidR = state.invalidCount;
            _isUpR = state.isUp;
            _repInvalidR = state.repInvalid;
            _targetHoldR = state.targetHoldStreak;
            _repCountedAtPeakR = state.repCountedAtPeak;
            _inTargetZoneR = state.inTargetZone;
        }
        else
        {
            _countL = state.count;
            _invalidL = state.invalidCount;
            _isUpL = state.isUp;
            _repInvalidL = state.repInvalid;
            _targetHoldL = state.targetHoldStreak;
            _repCountedAtPeakL = state.repCountedAtPeak;
            _inTargetZoneL = state.inTargetZone;
        }

        if (result.countedInvalid)
        {
            if (reportManager != null) reportManager.RegisterInvalidRep(anatomicalRight);
            if (warningManager != null) warningManager.TriggerWarning(Loc.T("warn.repInvalid"));
            if (_voiceCoach != null) _voiceCoach.Speak(CoachCue.RepInvalid);
        }
        else if (result.countedValid)
        {
            bool assisted = anatomicalRight ? IsAssistEffectiveRight : IsAssistEffectiveLeft;
            if (reportManager != null)
            {
                reportManager.IncrementRep(anatomicalRight, assisted);
                if (!assisted && _qualityFramePublisher.QualityAllowsPeakRom)
                    reportManager.RegisterAngle(result.gateAngleAtCount, anatomicalRight);
            }
            MaybeSpeakAlmostDone();
        }

        int count = anatomicalRight ? _countR : _countL;
        if (rText != null && (count != lastShownCount || targetReps != _lastShownTargetReps))
        {
            cachedRep = pref + " " + count.ToString() + " / " + targetReps.ToString();
            rText.text = cachedRep;
            lastShownCount = count;
            _lastShownTargetReps = targetReps;
        }
    }

    private void UpdateArm(
        bool isRight,
        float rawAngle,
        Slider slider,
        SliderColorController color,
        TextMeshProUGUI aText,
        ref string cachedAngle,
        ref int lastShownAngle)
    {
        CheckRaiseTempo(isRight, rawAngle);
        UpdateRomAssessmentAndSliderScale(rawAngle);

        if (isRight)
        {
            _armUiPresenter.UpdateArmVisual(
                ref _visualRight, rawAngle, lerpSpeed, Time.deltaTime,
                slider, color, aText, ref cachedAngle, ref lastShownAngle);
        }
        else
        {
            _armUiPresenter.UpdateArmVisual(
                ref _visualLeft, rawAngle, lerpSpeed, Time.deltaTime,
                slider, color, aText, ref cachedAngle, ref lastShownAngle);
        }
    }

    /// <summary>
    /// İlk seans: zirve ROM ölç → slider doluluk ölçeğini yapabileceğin ×2 yap → eğitim hedefine geç.
    /// Örn. 5° kaldırabiliyorsa slider full ≈ 10°.
    /// </summary>
    private void UpdateRomAssessmentAndSliderScale(float rawAngle)
    {
        if (rawAngle > _sessionPeakRom + 0.5f)
        {
            _sessionPeakRom = rawAngle;
            _peakLastImprovedAt = Time.time;
        }

        if (_romAssessmentAnalyzing || _sessionPeakRom >= AssessmentMinPeakDegrees)
        {
            float desiredFull = Mathf.Max(
                _sessionPeakRom * SliderMotivationalRatio,
                _sessionPeakRom + SliderMotivationalSlackDegrees,
                SliderStartFullDegrees);
            desiredFull = Mathf.Clamp(desiredFull, SliderMinFullDegrees, 180f);
            if (desiredFull > _sliderFullDegrees)
                _sliderFullDegrees = desiredFull;
        }

        if (!_romAssessmentAnalyzing) return;
        if (_sessionPeakRom < AssessmentMinPeakDegrees) return;
        if (Time.time - _peakLastImprovedAt < AssessmentSettleSeconds) return;
        if (Time.time - _assessmentPhaseStartedAt < AssessmentSettleSeconds) return;

        // Analiz bitti → yapabileceği açıyı hedef yap; slider motivasyon ölçeğinde kalsın
        float trainable = Mathf.Clamp(
            Mathf.Round(_sessionPeakRom / PersonalizedTargetAdvisor.AngleStepDegrees)
                * PersonalizedTargetAdvisor.AngleStepDegrees,
            PersonalizedTargetAdvisor.MinAngleDegrees,
            PersonalizedTargetAdvisor.MaxAngleDegrees);
        if (trainable < AssessmentMinPeakDegrees)
            trainable = Mathf.Max(AssessmentMinPeakDegrees, _sessionPeakRom);

        targetAngleDegrees = trainable;
        targetReps = AssessmentDefaultReps;
        RefreshRepLowerLimitFromTarget();
        _sliderFullDegrees = Mathf.Clamp(
            Mathf.Max(targetAngleDegrees * SliderMotivationalRatio, targetAngleDegrees + SliderMotivationalSlackDegrees),
            SliderMinFullDegrees, 180f);
        _romAssessmentAnalyzing = false;
        SyncMovementTargetsToAvatar();
        _countR = 0;
        _countL = 0;
        _isUpR = false;
        _isUpL = false;
        _repInvalidR = false;
        _repInvalidL = false;
        _armRepR = default;
        _armRepL = default;
        ResetRepHoldState();
        _lastShownCountR = int.MinValue;
        _lastShownCountL = int.MinValue;

        if (reportManager != null && reportManager.IsSessionActive)
            reportManager.StartSession(targetReps, targetAngleDegrees, _plannedMeasureRight, _plannedMeasureLeft);

        if (warningManager != null)
            warningManager.TriggerWarning(Loc.Format("assess.live.ready", (int)targetAngleDegrees, (int)_sliderFullDegrees));
        if (_voiceCoach != null && enableVoiceCoach)
            _voiceCoach.SpeakTargets(targetAngleDegrees, targetReps);
    }

    private float CalculateAngle2D(int p1, int p2, int p3)
    {
        return Angle2D(_filteredXy[p1], _filteredXy[p2], _filteredXy[p3]);
    }

    /// <summary>
    /// Yüz zorlanması soft uyarısı. Tekrar geçersiz kılma yalnızca FaceStrainAnalyzer.invalidateOnHighStrain açıksa.
    /// SaMD Class B: karar-destek göstergesi; teşhis değildir.
    /// </summary>
    private bool CheckFaceStrainWarning()
    {
        if (faceStrainAnalyzer == null || !faceStrainAnalyzer.HasFace) return false;

        if (faceStrainAnalyzer.IsAboveWarnThreshold
            && Time.time > _lastStrainWarningTime + strainWarningCooldownSeconds)
        {
            _lastStrainWarningTime = Time.time;
            if (warningManager != null)
                warningManager.TriggerWarning(Loc.T("warn.strain"));
            if (_voiceCoach != null)
                _voiceCoach.Speak(CoachCue.HighStrain);
        }

        return faceStrainAnalyzer.IsAboveInvalidateThreshold;
    }

    private void PushCompensationLeanVisual(bool warnLean)
    {
        if (_avatarBodyDriver == null && !_avatarLookupAttempted)
        {
            _avatarBodyDriver = FindObjectOfType<AvatarBodyDriver>(true);
            _avatarLookupAttempted = true;
        }
        if (_avatarBodyDriver != null)
            _avatarBodyDriver.SetCompensationLeanVisual(warnLean);
    }

    private void CheckRaiseTempo(bool isRight, float rawAngle)
    {
        if (_voiceCoach == null || !enableVoiceCoach) return;

        float prev = isRight ? _prevAngleR : _prevAngleL;
        float prevT = isRight ? _prevAngleTimeR : _prevAngleTimeL;
        float now = Time.time;

        if (prevT > 0f)
        {
            float dt = now - prevT;
            if (dt > 0.04f && dt < 0.45f && rawAngle > prev + 1f)
            {
                float rate = (rawAngle - prev) / dt;
                if (rate > maxRaiseDegreesPerSecond)
                    _voiceCoach.Speak(CoachCue.SlowDown);
            }
        }

        if (isRight)
        {
            _prevAngleR = rawAngle;
            _prevAngleTimeR = now;
        }
        else
        {
            _prevAngleL = rawAngle;
            _prevAngleTimeL = now;
        }
    }

    private void ResetRepHoldState()
    {
        _targetHoldR = 0f;
        _targetHoldL = 0f;
        _repCountedAtPeakR = false;
        _repCountedAtPeakL = false;
        _inTargetZoneR = false;
        _inTargetZoneL = false;
        _repGateRightValid = false;
        _repGateLeftValid = false;
        _lastRepGateRight = 0f;
        _lastRepGateLeft = 0f;
        _armRepR.targetHoldStreak = 0f;
        _armRepL.targetHoldStreak = 0f;
        _armRepR.repCountedAtPeak = false;
        _armRepL.repCountedAtPeak = false;
        _armRepR.inTargetZone = false;
        _armRepL.inTargetZone = false;
    }

    private void MaybeSpeakAlmostDone()
    {
        if (_almostDoneSpoken || _voiceCoach == null || !enableVoiceCoach) return;
        if (targetReps <= 0) return;

        int need = 0;
        int done = 0;
        if (_plannedMeasureRight) { need += targetReps; done += _countR; }
        if (_plannedMeasureLeft) { need += targetReps; done += _countL; }
        if (need <= 0) return;

        // Son %20'ye girince bir kez
        if (done * 5 >= need * 4)
        {
            _almostDoneSpoken = true;
            _voiceCoach.Speak(CoachCue.AlmostDone);
        }
    }

    public void SetTargetReps(int newGoal)
    {
        targetReps = newGoal;
        _countR = 0;
        _countL = 0;
        _armRepR = default;
        _armRepL = default;
        ResetRepHoldState();
        _lastShownCountR = int.MinValue;
        _lastShownCountL = int.MinValue;
        _lastShownTargetReps = int.MinValue;

        if (reportManager != null && reportManager.IsSessionActive)
        {
            reportManager.StartSession(targetReps, targetAngleDegrees, _plannedMeasureRight, _plannedMeasureLeft);
        }
    }
}
