using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using Mediapipe.Tasks.Components.Containers;

/// <summary>PhysioAnalyzer - Session.</summary>
public partial class PhysioAnalyzer
{
    private void ApplyPreSessionAvatarOrbit()
    {
        if (_sessionStarted && !_sessionEnded) return;
        var stage = FindObjectOfType<AvatarStageController>(true);
        if (stage == null) return;
        stage.ApplySideOrbitForMeasuredArm(measureRightArm, measureLeftArm, patientSideView);
    }

    /// <summary>
    /// Ölçülmeyen kol: açı job / filtre / avatar sürüşü kapalı — CPU tasarrufu.
    /// regionMask kolları measure bayraklarıyla hizalanır; gövde lean ayrı kalır.
    /// </summary>
    private void SyncArmMeasurementPipeline()
    {
        if (!measureRightArm && !measureLeftArm)
        {
            measureRightArm = true;
            measureLeftArm = ExerciseCatalog.AllowsSimultaneousBilateral(_selectedMovementId);
        }

        // Egzersiz maskesi — analyzer RequiredMask öncelikli
        PoseRegionMask baseMask = _movementAnalyzer != null
            ? _movementAnalyzer.RequiredMask
            : ExerciseCatalog.GetOrDefault(_selectedMovementId).BuildMask();
        regionMask = baseMask;
        regionMask.rightArm = baseMask.rightArm && measureRightArm;
        regionMask.leftArm = baseMask.leftArm && measureLeftArm;

        if (_avatarBodyDriver == null && !_avatarLookupAttempted)
        {
            _avatarBodyDriver = FindObjectOfType<AvatarBodyDriver>(true);
            _avatarLookupAttempted = true;
        }
        if (_avatarBodyDriver != null)
        {
            _avatarBodyDriver.SetRegionMask(regionMask);
            _avatarBodyDriver.SetMeasuredArms(measureRightArm, measureLeftArm);
        }
        else
        {
            var driver = FindObjectOfType<AvatarBodyDriver>(true);
            if (driver != null)
            {
                driver.SetRegionMask(regionMask);
                driver.SetMeasuredArms(measureRightArm, measureLeftArm);
            }
        }

        ApplyArmUiVisibility();
    }

    private void EnsureExerciseHud()
    {
        if (GetComponent<ExerciseHudController>() == null)
        {
            gameObject.AddComponent<ExerciseHudController>();
        }
    }

    private void RefreshUiTexts(bool force)
    {
        if (force)
        {
            _lastShownRightAngle = int.MinValue;
            _lastShownLeftAngle = int.MinValue;
            _lastShownCountR = int.MinValue;
            _lastShownCountL = int.MinValue;
            _lastShownTargetReps = int.MinValue;
        }

        if (rightAngleText != null) rightAngleText.text = "0°";
        if (leftAngleText != null) leftAngleText.text = "0°";
        if (rightRepText != null) rightRepText.text = Loc.T("hud.rep.right") + " 0 / " + targetReps.ToString();
        if (leftRepText != null) leftRepText.text = Loc.T("hud.rep.left") + " 0 / " + targetReps.ToString();
    }

    private void ConfigureFilters()
    {
        for (int i = 0; i < PoseLandmarkCount; i++)
        {
            var filter = _filters[i];
            filter.Configure(filterMinCutoff, filterBeta, filterDCutoff);
            filter.Reset();
            _filters[i] = filter;
        }
        _filtersConfigured = true;
    }

    /// <summary>Seans öncesi panelden: kişisel hedef uygulansın mı.</summary>
    public void SetApplyPersonalizedTargets(bool apply)
    {
        _pendingApplyPersonalized = apply;
    }

    /// <summary>
    /// Seans öncesi panelden seçilen hedef açı / tekrar.
    /// SaMD Class B: klinisyen/kullanıcı seçimi; otomatik öneriyi ezer.
    /// </summary>
    public void SetSessionTargets(float angleDegrees, int reps)
    {
        targetAngleDegrees = Mathf.Clamp(angleDegrees,
            PersonalizedTargetAdvisor.MinAngleDegrees,
            PersonalizedTargetAdvisor.MaxAngleDegrees);
        targetReps = Mathf.Clamp(reps, 1, 30);
        RefreshRepLowerLimitFromTarget();
        _pendingApplyPersonalized = false;
        _sliderFullDegrees = Mathf.Clamp(
            Mathf.Max(targetAngleDegrees * SliderMotivationalRatio, targetAngleDegrees + SliderMotivationalSlackDegrees),
            SliderMinFullDegrees, 180f);
        _lastShownTargetReps = int.MinValue;
        SyncMovementTargetsToAvatar();
        RefreshUiTexts(force: true);
    }

    /// <summary>Radial yay rengi/track: kişisel hedef açı (0→hedef = kırmızı→yeşil).</summary>
    private void SyncMovementTargetsToAvatar()
    {
        if (_avatarBodyDriver == null && !_avatarLookupAttempted)
        {
            _avatarBodyDriver = FindObjectOfType<AvatarBodyDriver>(true);
            _avatarLookupAttempted = true;
        }
        if (_avatarBodyDriver == null) return;

        if (_movementAnalyzer is IMovementAvatarHooks hooks)
        {
            hooks.SyncAvatarTargets(_avatarBodyDriver, targetAngleDegrees, targetAngleDegrees);
            return;
        }

        _avatarBodyDriver.SetFlexionTargets(targetAngleDegrees, targetAngleDegrees);
    }

    /// <summary>
    /// Tekrar alt eşiği (repLowerLimitDegrees). Sonraki tekrar için bu açının altına inilmeli,
    /// sonra hedefe çıkılmalı. SaMD Class B tekrar tanıma eşiği; teşhis değildir.
    /// Formül: clamp(hedef × repReturnRatio, min, max), sonra hedef − minTravel üstünde kalmasın.
    /// </summary>
    private void RefreshRepLowerLimitFromTarget()
    {
        EnsureMovementStrategy();
        if (_repPolicy != null)
        {
            _repPolicy.SetTargetDegrees(targetAngleDegrees);
            repLowerLimitDegrees = _repPolicy.LowerLimitDegrees;
        }
    }

    public PersonalizedTargetAdvisor.Suggestion PreviewPersonalizedTargets()
    {
        PatientHistory history = dataManager != null ? dataManager.LoadHistory() : null;
        return PersonalizedTargetAdvisor.Suggest(history, targetAngleDegrees, targetReps);
    }

    public void BeginSession()
    {
        PatientProfile profile = null;
        if (dataManager != null) profile = dataManager.LoadProfile();
        BeginSession(profile);
    }

    public void BeginSession(PatientProfile profile)
    {
        // cmd: KVKK — rızasız profil ile seans/PII işleme
        if (profile != null && !profile.HasValidConsent)
        {
            Debug.LogWarning("[PhysioAnalyzer] Seans başlamadı: geçerli KVKK rızası yok.");
            return;
        }

        if (profile != null)
        {
            measureRightArm = profile.measureRightArm;
            measureLeftArm = profile.measureLeftArm;
            patientHeightCm = profile.heightCm;
            patientAgeYears = profile.ageYears;
            patientFirstName = profile.firstName ?? "";
            patientLastName = profile.lastName ?? "";
            patientGender = profile.gender;

            ApplyExerciseSelectionFromProfile(profile);
            if (!ExerciseCatalog.IsLiveReady(_selectedMovementId))
            {
                ApplyExerciseSelection((int)BodyRegionId.Shoulder, (int)MovementId.ShoulderFlexion);
            }

            // Kamera protokolü katalog meta’sından
            patientSideView = ExerciseCatalog.UsesSideProfile(_selectedMovementId);

            _sequentialBothArms = profile.sequentialBothArms
                && ExerciseCatalog.AllowsBilateralSequential(_selectedMovementId);
            _plannedMeasureRight = profile.measureRightArm || _sequentialBothArms;
            _plannedMeasureLeft = profile.measureLeftArm || _sequentialBothArms;
            _sequentialPhase = 0;
            if (_sequentialBothArms)
            {
                // Önce sağ, sonra sol
                measureRightArm = true;
                measureLeftArm = false;
            }
            else if (ExerciseCatalog.RequiresExclusiveArm(_selectedMovementId))
            {
                // XOR güvenlik ağı (fleksiyon / yan profil)
                if (measureRightArm && measureLeftArm)
                    measureLeftArm = false;
            }

            var stage = FindObjectOfType<AvatarStageController>();
            if (stage != null)
            {
                stage.ApplyGenderFromProfile(profile);
                stage.ApplySideOrbitForMeasuredArm(measureRightArm, measureLeftArm, patientSideView);
            }
        }
        else
        {
            ApplyExerciseSelection((int)ExerciseCatalog.DefaultRegionId, (int)ExerciseCatalog.DefaultMovementId);
            patientSideView = ExerciseCatalog.UsesSideProfile(_selectedMovementId);
            _sequentialBothArms = false;
            _plannedMeasureRight = measureRightArm;
            _plannedMeasureLeft = measureLeftArm;
        }

        if (!measureRightArm && !measureLeftArm)
        {
            measureRightArm = true;
            measureLeftArm = ExerciseCatalog.AllowsSimultaneousBilateral(_selectedMovementId);
        }

        if (!_sequentialBothArms)
        {
            _plannedMeasureRight = measureRightArm;
            _plannedMeasureLeft = measureLeftArm;
        }

        SyncArmMeasurementPipeline();

        _voiceCoach = VoiceCoach.Ensure();
        if (_voiceCoach != null)
            _voiceCoach.SetEnabled(enableVoiceCoach);

        // Panelden SetSessionTargets gelmediyse isteğe bağlı kişisel öneri (hasta filtreli)
        PatientHistory patientHistory = null;
        if (dataManager != null)
        {
            patientHistory = PatientVault.FilterHistoryForPatient(dataManager.LoadHistory(), profile, fallbackToAll: false);
        }

        int priorSessions = patientHistory != null && patientHistory.sessions != null
            ? patientHistory.sessions.Count
            : 0;
        bool firstSessionForPatient = priorSessions == 0;

        bool useSavedTargets = profile != null && profile.hasSessionTargets
            && profile.lastSessionTargetAngle >= PersonalizedTargetAdvisor.MinAngleDegrees;

        _romAssessmentAnalyzing = firstSessionForPatient && !useSavedTargets;
        _sessionPeakRom = 0f;
        _peakLastImprovedAt = Time.time;
        _assessmentPhaseStartedAt = Time.time;
        _sliderFullDegrees = _romAssessmentAnalyzing ? SliderStartFullDegrees : 180f;

        if (useSavedTargets)
        {
            targetAngleDegrees = Mathf.Clamp(profile.lastSessionTargetAngle,
                PersonalizedTargetAdvisor.MinAngleDegrees,
                PersonalizedTargetAdvisor.MaxAngleDegrees);
            int savedReps = profile.lastSessionTargetReps > 0 ? profile.lastSessionTargetReps : targetReps;
            targetReps = Mathf.Clamp(savedReps, 1, 30);
            RefreshRepLowerLimitFromTarget();
            _pendingApplyPersonalized = false;
            _sliderFullDegrees = Mathf.Clamp(
                Mathf.Max(targetAngleDegrees * SliderMotivationalRatio, targetAngleDegrees + SliderMotivationalSlackDegrees),
                SliderMinFullDegrees, 180f);
        }

        bool usePersonal = applyPersonalizedTargets && _pendingApplyPersonalized && !firstSessionForPatient && !useSavedTargets;
        if (usePersonal && patientHistory != null)
        {
            var suggestion = PersonalizedTargetAdvisor.Suggest(
                patientHistory, targetAngleDegrees, targetReps);
            targetAngleDegrees = suggestion.targetAngle;
            targetReps = suggestion.targetReps;
            RefreshRepLowerLimitFromTarget();
            _sliderFullDegrees = Mathf.Clamp(
                Mathf.Max(targetAngleDegrees * SliderMotivationalRatio, targetAngleDegrees + SliderMotivationalSlackDegrees),
                SliderMinFullDegrees, 180f);
        }
        else if (firstSessionForPatient && !useSavedTargets)
        {
            targetAngleDegrees = PersonalizedTargetAdvisor.MaxAngleDegrees;
            targetReps = AssessmentDefaultReps;
            if (warningManager != null)
                warningManager.TriggerWarning(Loc.T("assess.live.start"));
        }

        RefreshRepLowerLimitFromTarget();
        SyncMovementTargetsToAvatar();

        while (_poseQueue.TryDequeue(out _)) { }

        _countR = 0;
        _countL = 0;
        _invalidR = 0;
        _invalidL = 0;
        _isUpR = false;
        _isUpL = false;
        _repInvalidR = false;
        _repInvalidL = false;
        _armRepR = default;
        _armRepL = default;
        ResetRepHoldState();
        _visualRight = 0f;
        _visualLeft = 0f;
        _physicRight = 0f;
        _physicLeft = 0f;
        _sessionEnded = false;
        _sessionStarted = true;
        _hasData = false;
        _almostDoneSpoken = false;
        _prevAngleTimeR = -1f;
        _prevAngleTimeL = -1f;
        _lastShownRightAngle = int.MinValue;
        _lastShownLeftAngle = int.MinValue;
        _lastShownCountR = int.MinValue;
        _lastShownCountL = int.MinValue;
        EnsureMovementStrategy();
        _movementAnalyzer?.ResetSession();
        _repPolicy?.Reset();
        ConfigureGateServices();
        _spineCompensationGate.Reset();
        _frontalFacingGate.Reset();
        _sideProfileSessionGate.Reset();
        _trackingJumpDetector.Reset();

        ConfigureQualityScorer();
        _qualityFramePublisher.Reset();
        _rawShoulderWidthValid = false;
        _rawPoseScaleValid = false;
        assistHelpActive = false;
        _latestDetectedPoseCount = 1;
        ConfigureAssistedRepDetector();
        ConfigureAssistPresenceTracker();
        _assistedRepDetector.Reset();
        _assistPresenceTracker.Reset();

        if (!_filtersConfigured) ConfigureFilters();
        for (int i = 0; i < PoseLandmarkCount; i++)
        {
            var filter = _filters[i];
            filter.Reset();
            _filters[i] = filter;
        }

        if (reportManager != null)
        {
            // Sequential: rapor her iki kolu baştan izler; örnekleme yine faz bayraklarıyla sınırlı.
            reportManager.StartSession(
                targetReps, targetAngleDegrees, _plannedMeasureRight, _plannedMeasureLeft);
            // Önceki seans pik zorlanması — geçmiş karşılaştırma
            if (dataManager != null)
            {
                PatientHistory history = dataManager.LoadHistory();
                if (history != null && history.sessions != null && history.sessions.Count > 0)
                {
                    SessionEntry prev = history.sessions[history.sessions.Count - 1];
                    reportManager.SetPreviousSessionPeakStrain(prev.peakStrain);
                }
            }
        }

        SessionStatus.MarkActive();
        var hologram = FindObjectOfType<ExampleMovementHologram>(true);
        if (hologram != null)
            hologram.NotifySessionStarted();
        ApplyArmUiVisibility();
        RefreshUiTexts(force: true);

        if (enableVoiceCoach && _voiceCoach != null)
        {
            if (usePersonal)
                _voiceCoach.SpeakTargets(targetAngleDegrees, targetReps);
            else
                _voiceCoach.Speak(CoachCue.SessionStart);
        }
    }

    /// <summary>HUD seans durumu değişince slider/açı/tekrar görünürlüğünü yeniler.</summary>
    public void RefreshArmUiForSessionState()
    {
        ApplyArmUiVisibility();
    }

    private void ApplyArmUiVisibility()
    {
        _armUiPresenter.ApplyArmUiVisibility(
            IsSessionRunning,
            measureLeftArm,
            measureRightArm,
            leftSlider,
            rightSlider,
            leftAngleText,
            rightAngleText,
            leftRepText,
            rightRepText,
            leftColorCtrl,
            rightColorCtrl);
    }

    private bool IsSessionGoalReached()
    {
        if (_romAssessmentAnalyzing) return false;
        if (targetReps <= 0) return false;

        if (_sequentialBothArms)
        {
            if (_sequentialPhase == 0)
            {
                if (measureRightArm && _countR >= targetReps)
                    AdvanceSequentialPhase();
                return false;
            }

            bool leftDone = !measureLeftArm || _countL >= targetReps;
            bool rightDone = !measureRightArm || _countR >= targetReps;
            return leftDone && rightDone;
        }

        bool rDone = !measureRightArm || _countR >= targetReps;
        bool lDone = !measureLeftArm || _countL >= targetReps;
        return rDone && lDone;
    }

    private void AdvanceSequentialPhase()
    {
        if (!_sequentialBothArms || _sequentialPhase != 0) return;
        _sequentialPhase = 1;
        measureRightArm = false;
        measureLeftArm = true;
        SyncArmMeasurementPipeline();
        var stage = FindObjectOfType<AvatarStageController>();
        if (stage != null)
            stage.ApplySideOrbitForMeasuredArm(measureRightArm, measureLeftArm, patientSideView);
        if (warningManager != null)
            warningManager.TriggerWarning(Loc.T("hud.phase.left"));
#if UNITY_EDITOR
        Debug.Log("[SaMD_Safety] Sequential phase → left arm");
#endif
    }
    public void EndSessionManually()
    {
        FinishSession(showReport: true);
    }
    public void SaveCurrentSession()
    {
        var exercise = new SessionCloseoutService.ExerciseContext
        {
            targetReps = targetReps,
            targetAngleDegrees = targetAngleDegrees,
            plannedMeasureRight = _plannedMeasureRight,
            plannedMeasureLeft = _plannedMeasureLeft,
            measureRightArm = measureRightArm,
            measureLeftArm = measureLeftArm,
            selectedMovementId = _selectedMovementId,
            selectedBodyRegionId = _selectedBodyRegionId
        };
        var counts = new SessionCloseoutService.SessionCounts
        {
            countR = _countR,
            countL = _countL,
            invalidR = _invalidR,
            invalidL = _invalidL,
            visualRight = _visualRight,
            visualLeft = _visualLeft
        };
        var identity = new SessionCloseoutService.PatientIdentity
        {
            firstName = patientFirstName,
            lastName = patientLastName,
            heightCm = patientHeightCm,
            ageYears = patientAgeYears,
            gender = patientGender
        };
        _sessionCloseoutService.SaveCurrentSession(
            in exercise, in counts, in identity,
            _qualityFramePublisher.Scorer, dataManager, reportManager);
    }

    private void TryFinishReachedGoal()
    {
        PatientProfile profile = dataManager != null ? dataManager.LoadProfile() : null;
        if (profile != null && profile.HasRemainingVisitMovements())
        {
            MovementId done = _selectedMovementId;
            int idx = profile.plannedMovementIndex;
            int total = profile.PlannedMovementCount;
            FinishSession(showReport: false, visitComplete: false);
            profile.AdvancePlannedMovement();
            dataManager.SaveProfile(profile);
            VisitSegmentCompleted?.Invoke(done, idx, total);
            return;
        }

        FinishSession(showReport: true, visitComplete: true);
    }

    private void FinishSession(bool showReport)
    {
        FinishSession(showReport, visitComplete: showReport);
    }

    private void FinishSession(bool showReport, bool visitComplete)
    {
        if (!_sessionStarted || _sessionEnded) return;
        _sessionEnded = true;

        _sessionCloseoutService.ComputeMovementScore(
            enableMovementScoring, targetAngleDegrees, movementTemplatePoints,
            measureRightArm, measureLeftArm, reportManager);

        // Ani çıkışta süreyi önce dondur; normal bitişte UI EndSessionAndShowReport dondurur.
        if (!showReport && reportManager != null)
            reportManager.EndSessionSilent();

        SaveCurrentSession();
        if (visitComplete)
            SessionStatus.MarkCompleted();
        else
            SessionStatus.MarkIdle();
        _sessionCloseoutService.ExportSessionFiles(
            dataManager, reportManager,
            new SessionCloseoutService.PatientIdentity
            {
                firstName = patientFirstName,
                lastName = patientLastName,
                heightCm = patientHeightCm,
                ageYears = patientAgeYears,
                gender = patientGender
            },
            measureRightArm, measureLeftArm,
            _selectedMovementId);
        ApplyArmUiVisibility();

        Transform canvasRoot = ResolveHudCanvas();
        if (showReport)
        {
            AssessmentFlow.OnSessionFinished(dataManager, canvasRoot, true, () =>
            {
                ReexportSessionHtmlWithSurvey();
                if (reportManager != null)
                    reportManager.EndSessionAndShowReport();
            });
        }
        else if (visitComplete)
        {
            AssessmentFlow.OnSessionFinished(dataManager, null, false, null);
        }
    }

    private void ReexportSessionHtmlWithSurvey()
    {
        if (dataManager == null || reportManager == null || !reportManager.HasData) return;
        PatientProfile profile = dataManager.LoadProfile();
        PatientHistory history = dataManager.LoadHistoryForPatient(profile);
        SurveyResponse survey = null;
        if (history != null && history.sessions != null && history.sessions.Count > 0)
            survey = SurveyResponse.FromSessionEntry(history.sessions[history.sessions.Count - 1]);
        if (survey == null) return;

        _sessionCloseoutService.ExportSessionFiles(
            dataManager, reportManager,
            new SessionCloseoutService.PatientIdentity
            {
                firstName = patientFirstName,
                lastName = patientLastName,
                heightCm = patientHeightCm,
                ageYears = patientAgeYears,
                gender = patientGender
            },
            measureRightArm, measureLeftArm,
            _selectedMovementId,
            survey);
    }

    private static Transform ResolveHudCanvas()
    {
        Canvas c = Object.FindObjectOfType<Canvas>();
        return c != null ? c.transform : null;
    }

    private void OnDisable()
    {
        // Sahne değişimi / kilitlenme: DTW + JSON + HTML/CSV (UI raporu yok)
        TryEmergencyCloseout();
    }

    private void OnApplicationQuit()
    {
        TryEmergencyCloseout();
    }

    /// <summary>
    /// Ani çıkışta tam klinik kapanış: DTW, geçmiş kaydı, yerel HTML/CSV.
    /// showReport=false — UI paneli açılmaz (sahne zaten kapanıyor olabilir).
    /// </summary>
    private void TryEmergencyCloseout()
    {
        if (!_sessionStarted || _sessionEnded) return;
        FinishSession(showReport: false);
    }
}
