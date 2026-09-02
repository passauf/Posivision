using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using Mediapipe.Tasks.Components.Containers;

/// <summary>PhysioAnalyzer - PosePipeline.</summary>
public partial class PhysioAnalyzer
{
    /// <summary>
    /// MediaPipe LIVE_STREAM callback'inden çağrılır (arka plan thread).
    /// Unity API kullanılmaz; yalnızca değer kopyalanıp kuyruğa alınır.
    /// detectedPoseCount: bu karede üretilen pose sayısı.
    /// helperLandmarks: 2. kişi (yardımcı) — yakınlık + eş hareket sezgisi; null liste ise yok sayılır.
    /// </summary>
    public void AnalyzeBothArms(
        NormalizedLandmarks landmarks,
        long timestampMs,
        int detectedPoseCount,
        NormalizedLandmarks helperLandmarks)
    {
        if (!_sessionStarted || _sessionEnded) return;
        // Pose model 33 nokta üretir; en az sağ kalça indeksine (24) ihtiyaç var
        if (landmarks.landmarks == null || landmarks.landmarks.Count <= IdxRightHip) return;

        PoseLandmarkSample sample = default;
        sample.timestampSeconds = timestampMs > 0 ? timestampMs * 0.001f : 0f;
        sample.detectedPoseCount = detectedPoseCount < 1 ? 1 : detectedPoseCount;
        sample.leftShoulder = CopyPoint(landmarks.landmarks[IdxLeftShoulder]);
        sample.rightShoulder = CopyPoint(landmarks.landmarks[IdxRightShoulder]);
        sample.leftElbow = CopyPoint(landmarks.landmarks[IdxLeftElbow]);
        sample.rightElbow = CopyPoint(landmarks.landmarks[IdxRightElbow]);
        if (landmarks.landmarks.Count > IdxRightWrist)
        {
            sample.leftWrist = CopyPoint(landmarks.landmarks[IdxLeftWrist]);
            sample.rightWrist = CopyPoint(landmarks.landmarks[IdxRightWrist]);
        }
        sample.leftHip = CopyPoint(landmarks.landmarks[IdxLeftHip]);
        sample.rightHip = CopyPoint(landmarks.landmarks[IdxRightHip]);
        if (landmarks.landmarks.Count > IdxNose)
            sample.nose = CopyPoint(landmarks.landmarks[IdxNose]);

        if (helperLandmarks.landmarks != null
            && helperLandmarks.landmarks.Count > IdxRightElbow
            && sample.detectedPoseCount >= 2)
        {
            sample.hasHelperPose = true;
            if (helperLandmarks.landmarks.Count > IdxRightShoulder)
            {
                sample.helperLeftShoulder = CopyPoint(helperLandmarks.landmarks[IdxLeftShoulder]);
                sample.helperRightShoulder = CopyPoint(helperLandmarks.landmarks[IdxRightShoulder]);
            }
            sample.helperLeftElbow = CopyPoint(helperLandmarks.landmarks[IdxLeftElbow]);
            sample.helperRightElbow = CopyPoint(helperLandmarks.landmarks[IdxRightElbow]);
            if (helperLandmarks.landmarks.Count > IdxRightWrist)
            {
                sample.helperLeftWrist = CopyPoint(helperLandmarks.landmarks[IdxLeftWrist]);
                sample.helperRightWrist = CopyPoint(helperLandmarks.landmarks[IdxRightWrist]);
            }
            if (helperLandmarks.landmarks.Count > IdxRightIndex)
            {
                sample.helperLeftIndex = CopyPoint(helperLandmarks.landmarks[IdxLeftIndex]);
                sample.helperRightIndex = CopyPoint(helperLandmarks.landmarks[IdxRightIndex]);
            }
            if (helperLandmarks.landmarks.Count > IdxRightHip)
            {
                sample.helperLeftHip = CopyPoint(helperLandmarks.landmarks[IdxLeftHip]);
                sample.helperRightHip = CopyPoint(helperLandmarks.landmarks[IdxRightHip]);
            }
        }

        _poseQueue.Enqueue(sample);
    }

    /// <summary>Geriye uyumluluk — yardımcı pose yok.</summary>
    public void AnalyzeBothArms(NormalizedLandmarks landmarks, long timestampMs, int detectedPoseCount)
    {
        AnalyzeBothArms(landmarks, timestampMs, detectedPoseCount, default);
    }

    /// <summary>Geriye uyumluluk — tek pose varsayılır.</summary>
    public void AnalyzeBothArms(NormalizedLandmarks landmarks, long timestampMs)
    {
        AnalyzeBothArms(landmarks, timestampMs, 1, default);
    }

    /// <summary>Geriye uyumluluk — timestamp MediaPipe'dan gelmezse ana thread'de tamamlanır.</summary>
    public void AnalyzeBothArms(NormalizedLandmarks landmarks)
    {
        AnalyzeBothArms(landmarks, -1L, 1, default);
    }

    private static LandmarkPoint CopyPoint(NormalizedLandmark lm)
    {
        LandmarkPoint p;
        p.x = lm.x;
        p.y = lm.y;
        p.hasVisibility = lm.visibility.HasValue;
        p.visibility = lm.visibility.HasValue ? lm.visibility.Value : 1f;
        p.hasPresence = lm.presence.HasValue;
        p.presence = lm.presence.HasValue ? lm.presence.Value : 1f;
        return p;
    }

    private void ProcessSampleOnMainThread(PoseLandmarkSample sample)
    {
        _latestDetectedPoseCount = sample.detectedPoseCount < 1 ? 1 : sample.detectedPoseCount;

        float timestamp = sample.timestampSeconds;
        if (timestamp <= 0f)
        {
            timestamp = Time.realtimeSinceStartup;
        }

        // MediaPipe landmark görünürlüğü (indeks tarafı — henüz anatomik değil)
        bool mpRightVis = IsPointConfident(sample.rightShoulder)
                          && IsPointConfident(sample.rightElbow)
                          && IsPointConfident(sample.rightHip);
        bool mpLeftVis = IsPointConfident(sample.leftShoulder)
                         && IsPointConfident(sample.leftElbow)
                         && IsPointConfident(sample.leftHip);
        bool torsoVis = IsPointConfident(sample.leftShoulder)
                        && IsPointConfident(sample.rightShoulder)
                        && IsPointConfident(sample.leftHip)
                        && IsPointConfident(sample.rightHip);

        // cmd: ön kamera flip → MediaPipe L/R anatomik ters; UI/rapor anatomik, avatar MP-native
        bool swap = ShouldSwapArmLaterality();
        bool anatRightVis = swap ? mpLeftVis : mpRightVis;
        bool anatLeftVis = swap ? mpRightVis : mpLeftVis;

        bool mpRightWristOk = mpRightVis && sample.rightWrist.hasVisibility && IsPointConfident(sample.rightWrist);
        bool mpLeftWristOk = mpLeftVis && sample.leftWrist.hasVisibility && IsPointConfident(sample.leftWrist);

        _regionVisibility.rightArm = anatRightVis;
        _regionVisibility.leftArm = anatLeftVis;
        _regionVisibility.torso = torsoVis;
        _regionVisibility.rightForearm = swap ? mpLeftWristOk : mpRightWristOk;
        _regionVisibility.leftForearm = swap ? mpRightWristOk : mpLeftWristOk;
        _regionVisibility.legs = false;
        _regionVisibility.head = false;

        bool wantAnatRight = regionMask.rightArm && measureRightArm;
        bool wantAnatLeft = regionMask.leftArm && measureLeftArm;
        bool clinicalRightOk = wantAnatRight && anatRightVis;
        bool clinicalLeftOk = wantAnatLeft && anatLeftVis;

        // Job'lar MediaPipe indeksleriyle çalışır
        bool mpRightOk = mpRightVis && (swap ? wantAnatLeft : wantAnatRight);
        bool mpLeftOk = mpLeftVis && (swap ? wantAnatRight : wantAnatLeft);
        _torsoRegionActive = regionMask.torso && torsoVis;

        if (!mpRightOk && !mpLeftOk && !_torsoRegionActive)
        {
            _lastSpineLeanDegrees = 0f;
            _spineCompensationGate.ClearSticky();
            _frontalFacingGate.Reset();
            _rawShoulderWidthValid = false;
            _rawPoseScaleValid = false;
            _qualityFramePublisher.SetVisibilityFraction(
                _qualityFramePublisher.ComputeVisibilityFraction(
                    measureRightArm, measureLeftArm,
                    regionMask.rightArm, regionMask.leftArm, regionMask.torso,
                    false, false, false));
            _assistedRepDetector.ClearTransientStreaks();
            _assistPresenceTracker.ClearHelperCache();
            _trackingJumpDetector.Reset();
            PushQualityFrame();
            return;
        }

        // Omuzlar: her protokolde; yan ölçek için kalçalar erken filtrelenir
        bool needLeftShoulder = measureLeftArm || _torsoRegionActive || measureRightArm;
        bool needRightShoulder = measureRightArm || _torsoRegionActive || measureLeftArm;
        if (needLeftShoulder && IsPointConfident(sample.leftShoulder))
        {
            _filteredXy[IdxLeftShoulder] = FilterPoint(IdxLeftShoulder, sample.leftShoulder.x, sample.leftShoulder.y, timestamp);
        }
        if (needRightShoulder && IsPointConfident(sample.rightShoulder))
        {
            _filteredXy[IdxRightShoulder] = FilterPoint(IdxRightShoulder, sample.rightShoulder.x, sample.rightShoulder.y, timestamp);
        }
        bool noseOk = IsPointConfident(
            sample.nose,
            patientSideView ? headLandmarkVisibilityThreshold : landmarkVisibilityThreshold);
        if (noseOk)
            _filteredXy[IdxNose] = FilterPoint(IdxNose, sample.nose.x, sample.nose.y, timestamp);

        bool leftShoulderOk = needLeftShoulder && IsPointConfident(sample.leftShoulder);
        bool rightShoulderOk = needRightShoulder && IsPointConfident(sample.rightShoulder);
        bool leftHipOk = IsPointConfident(sample.leftHip);
        bool rightHipOk = IsPointConfident(sample.rightHip);

        // Yan / gövde: kalçalar ölçek (torso L) için omuzlarla aynı anda lazım
        _activeScaleBasis = PoseScaleResolver.FromSideView(patientSideView);
        bool needHipsForScale = _activeScaleBasis == PoseScaleBasis.TorsoLength
            || _torsoRegionActive
            || mpRightOk
            || mpLeftOk;
        if (needHipsForScale)
        {
            if (leftHipOk)
                _filteredXy[IdxLeftHip] = FilterPoint(IdxLeftHip, sample.leftHip.x, sample.leftHip.y, timestamp);
            if (rightHipOk)
                _filteredXy[IdxRightHip] = FilterPoint(IdxRightHip, sample.rightHip.x, sample.rightHip.y, timestamp);
        }

        // Ham omuz genişliği: yan φ kapısı + (ön) kalite; normalize için değil
        float rawWidth = PoseScaleResolver.ComputeShoulderWidth(
            _filteredXy[IdxLeftShoulder], _filteredXy[IdxRightShoulder],
            leftShoulderOk, rightShoulderOk, out bool shoulderWOk);
        _rawShoulderWidthValid = shoulderWOk;
        _rawShoulderWidthForQuality = shoulderWOk ? rawWidth : 0f;

        float scaleLen = PoseScaleResolver.Compute(
            _activeScaleBasis,
            _filteredXy[IdxLeftShoulder], _filteredXy[IdxRightShoulder],
            _filteredXy[IdxLeftHip], _filteredXy[IdxRightHip],
            leftShoulderOk, rightShoulderOk, leftHipOk, rightHipOk,
            out bool scaleOk);
        _rawPoseScaleValid = scaleOk;
        _rawPoseScaleLength = scaleOk ? scaleLen : 0f;

        // Normalize divisor: protokol ölçeği (ön=omuz w, yan=gövde L)
        _shoulderWidth = scaleOk ? scaleLen : 1f;
        if (_shoulderWidth < PoseScaleResolver.MinScale)
            _shoulderWidth = 1f;

        _sideProfileSessionGate.Evaluate(
            _movementProtocolProfile.enableSideProfileGate && patientSideView,
            rawWidth,
            shoulderWOk,
            _rawPoseScaleValid ? _rawPoseScaleLength : 0f,
            noseOk,
            measureRightArm,
            measureLeftArm,
            anatRightVis,
            anatLeftVis,
            warningManager);

        float inv = 1f / _shoulderWidth;

        if (mpRightOk)
        {
            _filteredXy[IdxRightElbow] = FilterPoint(IdxRightElbow, sample.rightElbow.x, sample.rightElbow.y, timestamp);
            if (!needHipsForScale)
                _filteredXy[IdxRightHip] = FilterPoint(IdxRightHip, sample.rightHip.x, sample.rightHip.y, timestamp);
            if (mpRightWristOk)
                _filteredXy[IdxRightWrist] = FilterPoint(IdxRightWrist, sample.rightWrist.x, sample.rightWrist.y, timestamp);
        }

        if (mpLeftOk)
        {
            _filteredXy[IdxLeftElbow] = FilterPoint(IdxLeftElbow, sample.leftElbow.x, sample.leftElbow.y, timestamp);
            if (!needHipsForScale)
                _filteredXy[IdxLeftHip] = FilterPoint(IdxLeftHip, sample.leftHip.x, sample.leftHip.y, timestamp);
            if (mpLeftWristOk)
                _filteredXy[IdxLeftWrist] = FilterPoint(IdxLeftWrist, sample.leftWrist.x, sample.leftWrist.y, timestamp);
        }

        // Gövde lean: kalçalar henüz yoksa (yalnız gövde, scale shoulder ise)
        if (_torsoRegionActive)
        {
            if (!mpRightOk && !needHipsForScale)
                _filteredXy[IdxRightHip] = FilterPoint(IdxRightHip, sample.rightHip.x, sample.rightHip.y, timestamp);
            if (!mpLeftOk && !needHipsForScale)
                _filteredXy[IdxLeftHip] = FilterPoint(IdxLeftHip, sample.leftHip.x, sample.leftHip.y, timestamp);
        }

        // Normalize in-place (protokol ölçeği)
        if (leftShoulderOk) _filteredXy[IdxLeftShoulder] *= inv;
        if (rightShoulderOk) _filteredXy[IdxRightShoulder] *= inv;
        if (noseOk) _filteredXy[IdxNose] *= inv;
        if (mpLeftOk || _torsoRegionActive || needHipsForScale)
        {
            if (mpLeftOk) _filteredXy[IdxLeftElbow] *= inv;
            if (mpLeftOk && mpLeftWristOk) _filteredXy[IdxLeftWrist] *= inv;
            if (mpLeftOk || _torsoRegionActive || (needHipsForScale && leftHipOk))
                _filteredXy[IdxLeftHip] *= inv;
        }
        if (mpRightOk || _torsoRegionActive || needHipsForScale)
        {
            if (mpRightOk) _filteredXy[IdxRightElbow] *= inv;
            if (mpRightOk && mpRightWristOk) _filteredXy[IdxRightWrist] *= inv;
            if (mpRightOk || _torsoRegionActive || (needHipsForScale && rightHipOk))
                _filteredXy[IdxRightHip] *= inv;
        }

        // SaMD Class B: yardımcı pose önbelleği — açı sonrası üçlü koşul (yakınlık+eş hareket+kaldırma)
        _assistPresenceTracker.CacheHelperPose(in sample, inv);
        _assistPresenceTracker.UpdateSecondPersonPresence(in sample, warningManager, reportManager);

        // Kadraj/takip sıçraması: ölçek birimi protokole göre (yan: gövde boyu — omuz w değil)
        bool trackingJump = _trackingJumpDetector.Evaluate(
            timestamp, _rawPoseScaleValid ? _rawPoseScaleLength : 0f, _filteredXy,
            mpRightOk, mpLeftOk, mpRightWristOk, mpLeftWristOk,
            leftShoulderOk,
            rightShoulderOk,
            _torsoRegionActive,
            _onTrackingJumpDetected,
            warningManager);

        // Yaw önce — teorik ROM düzeltmesi aynı karede güncel φ kullanır
        _frontalFacingGate.Update(
            torsoVis, noseOk, patientSideView,
            _filteredXy[IdxLeftShoulder], _filteredXy[IdxRightShoulder], _filteredXy[IdxNose]);

        if (!trackingJump)
        {
            ScheduleAngleAndLeanJobs(mpRightOk, mpLeftOk, mpRightWristOk, mpLeftWristOk,
                _torsoRegionActive, swap, clinicalRightOk, clinicalLeftOk);
        }

        _qualityFramePublisher.SetVisibilityFraction(
            _qualityFramePublisher.ComputeVisibilityFraction(
                measureRightArm, measureLeftArm,
                regionMask.rightArm, regionMask.leftArm, regionMask.torso,
                clinicalRightOk, clinicalLeftOk, _torsoRegionActive));
        PushQualityFrame();
    }

    private void ConfigureAssistedRepDetector()
    {
        // Eski sahneler: contact min frames taşınmamışsa legacy değerden doldur
        if (assistContactMinFrames < 1 && assistMultiPersonMinFrames > 0)
            assistContactMinFrames = assistMultiPersonMinFrames;

        var cfg = new AssistedRepDetectorConfig
        {
            proximityShoulderWidths = assistProximityShoulderWidths,
            minContactFrames = assistContactMinFrames > 0 ? assistContactMinFrames : 2,
            minJointSpeedShoulderWidthsPerSec = assistMinJointSpeedShoulderWidthsPerSec,
            minAssistRepFraction = assistMinRepFraction,
            minActiveMotionFrames = assistMinActiveMotionFrames
        };
        _assistedRepDetector.Configure(in cfg);
    }

    /// <summary>
    /// Klinik açı sonrası: Katman 2–4 (temas + hız vektörü + süreğenlik).
    /// SaMD Class B: yardım bağlamı; teşhis değildir.
    /// </summary>
    private void UpdateAssistedRepAfterAngles(
        bool swap,
        bool mpRightOk,
        bool mpLeftOk,
        bool mpRightWristOk,
        bool mpLeftWristOk)
    {
        if (!autoAssistFromMultiPerson)
        {
            _assistedRepDetector.Reset();
            _assistPresenceTracker.ClearProximityWarnLatch();
            return;
        }

        ConfigureAssistedRepDetector();
        float dt = Time.unscaledDeltaTime;
        float inv = _assistPresenceTracker.CachedInvShoulderWidth;
        int poseCount = _latestDetectedPoseCount;
        bool hasHelper = _assistPresenceTracker.HasHelperPose;
        ref AssistedHelperPose helper = ref _assistPresenceTracker.HelperPose;

        bool anatRightTrack = swap ? mpLeftOk : mpRightOk;
        bool anatLeftTrack = swap ? mpRightOk : mpLeftOk;
        bool anatRightWrist = swap ? mpLeftWristOk : mpRightWristOk;
        bool anatLeftWrist = swap ? mpRightWristOk : mpLeftWristOk;
        Vector2 elbowR = swap ? _filteredXy[IdxLeftElbow] : _filteredXy[IdxRightElbow];
        Vector2 elbowL = swap ? _filteredXy[IdxRightElbow] : _filteredXy[IdxLeftElbow];
        Vector2 wristR = swap ? _filteredXy[IdxLeftWrist] : _filteredXy[IdxRightWrist];
        Vector2 wristL = swap ? _filteredXy[IdxRightWrist] : _filteredXy[IdxLeftWrist];

        _assistedRepDetector.UpdateArm(
            anatomicalRight: true,
            armTrackingOk: anatRightTrack && measureRightArm,
            wristOk: anatRightWrist,
            patientElbowNorm: elbowR,
            patientWristNorm: wristR,
            patientAngleDegrees: anatRightTrack ? _physicRight : float.NaN,
            deltaTime: dt,
            lowerLimitDegrees: repLowerLimitDegrees,
            hasHelperPose: hasHelper,
            detectedPoseCount: poseCount,
            helper: in helper,
            invShoulderWidth: inv);

        _assistedRepDetector.UpdateArm(
            anatomicalRight: false,
            armTrackingOk: anatLeftTrack && measureLeftArm,
            wristOk: anatLeftWrist,
            patientElbowNorm: elbowL,
            patientWristNorm: wristL,
            patientAngleDegrees: anatLeftTrack ? _physicLeft : float.NaN,
            deltaTime: dt,
            lowerLimitDegrees: repLowerLimitDegrees,
            hasHelperPose: hasHelper,
            detectedPoseCount: poseCount,
            helper: in helper,
            invShoulderWidth: inv);

        _assistPresenceTracker.MaybeWarnProximity(
            IsAssistFromMultiPerson, warningManager, reportManager);
    }

    private void OnTrackingJumpDetected()
    {
        // One Euro geçmişi bozuk kareye yapışmasın; uyarı TrackingJumpDetector içinde
        for (int i = 0; i < PoseLandmarkCount; i++)
        {
            var filter = _filters[i];
            filter.Reset();
            _filters[i] = filter;
        }

        if (reportManager != null)
            reportManager.RegisterTrackingJumpEvent();
    }

    private void ConfigureQualityScorer()
    {
        float ageMul = _spineCompensationGate.ElderToleranceMultiplier(patientAgeYears);
        _qualityFramePublisher.Configure(
            qualityWeightVisibility,
            qualityWeightStability,
            qualityWeightLean,
            qualityMaxShoulderWidthCv,
            qualityReliableThreshold,
            qualityCautionThreshold,
            qualityPeakGateThreshold,
            maxSpineLeanDegrees * ageMul,
            invalidateLeanDegrees * ageMul);
    }

    private void PushQualityFrame()
    {
        _qualityFramePublisher.PushFrame(
            _torsoRegionActive,
            _lastSpineLeanDegrees,
            _rawPoseScaleValid,
            _rawPoseScaleLength,
            reportManager);
    }

    /// <summary>
    /// Ön kamera yatay çevirince MediaPipe L/R hasta anatomisine göre terslenir.
    /// </summary>
    private bool ShouldSwapArmLaterality()
    {
        if (!autoSwapArmsForMirroredCamera) return false;
        try
        {
            var src = Mediapipe.Unity.Sample.ImageSourceProvider.ImageSource;
            if (src == null) return true;
            return src.GetTransformationOptions().flipHorizontally;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Burst job: sağ/sol omuz fleksiyon + omurga lean (yalnızca XY).
    /// NativeArray'ler önceden tahsisli — hot path'te allocation yok.
    /// swap=true: job MP indeksi; _physic* ve avatar anatomik sağ/sol.
    /// </summary>
    private void ScheduleAngleAndLeanJobs(
        bool mpRightOk, bool mpLeftOk, bool mpRightWristOk, bool mpLeftWristOk,
        bool torsoOk, bool swap, bool clinicalRightOk, bool clinicalLeftOk)
    {
        if (!_nativeReady) AllocateNative();
        EnsureMovementStrategy();

        var scheduleInput = new ShoulderElevationAnglePipeline.ScheduleInput
        {
            mpRightOk = mpRightOk,
            mpLeftOk = mpLeftOk,
            mpRightWristOk = mpRightWristOk,
            mpLeftWristOk = mpLeftWristOk,
            torsoOk = torsoOk,
            swap = swap,
            clinicalRightOk = clinicalRightOk,
            clinicalLeftOk = clinicalLeftOk,
            bodyYawDegrees = CurrentBodyYawDegrees,
            patientSideView = patientSideView,
            rawShoulderWidth01 = _rawShoulderWidthValid ? _rawShoulderWidthForQuality : 0f,
            rightHip = _filteredXy[IdxRightHip],
            rightShoulder = _filteredXy[IdxRightShoulder],
            rightElbow = _filteredXy[IdxRightElbow],
            rightWrist = _filteredXy[IdxRightWrist],
            leftHip = _filteredXy[IdxLeftHip],
            leftShoulder = _filteredXy[IdxLeftShoulder],
            leftElbow = _filteredXy[IdxLeftElbow],
            leftWrist = _filteredXy[IdxLeftWrist],
            leanLeftShoulder = _filteredXy[IdxLeftShoulder],
            leanRightShoulder = _filteredXy[IdxRightShoulder],
            leanLeftHip = _filteredXy[IdxLeftHip],
            leanRightHip = _filteredXy[IdxRightHip],
            elevationAnalyzer = _movementAnalyzer as IShoulderElevationAnalyzer,
            movementAnalyzer = _movementAnalyzer,
            jobLandmarks = _jobLandmarks,
            jobAngles = _jobAngles,
            jobEnabled = _jobEnabled,
            jobRefArmLengths = _jobRefArmLengths,
            jobLeanOut = _jobLeanOut
        };

        ShoulderElevationAnglePipeline.ScheduleOutput pipelineOut;
        if (_movementAnalyzer == null
            || !MovementFramePipelineDispatcher.TryScheduleAngles(
                _movementAnalyzer.Family, in scheduleInput, out pipelineOut))
        {
            if (torsoOk)
            {
                _lastSpineLeanDegrees = 0f;
                _spineCompensationGate.ClearSticky();
            }
            return;
        }

        if (torsoOk)
            _lastSpineLeanDegrees = pipelineOut.spineLeanDegrees;
        else
        {
            _lastSpineLeanDegrees = 0f;
            _spineCompensationGate.ClearSticky();
        }

        MovementFrameResult frameResult = pipelineOut.frameResult;
        _physicRight = frameResult.clinicalRightAngle;
        _physicLeft = frameResult.clinicalLeftAngle;
        if (frameResult.hasClinicalData)
            _hasData = true;

        _foreshortenMpRight = frameResult.foreshortenMpRight;
        _foreshortenMpLeft = frameResult.foreshortenMpLeft;
        if (frameResult.notifyForeshorten && _movementProtocolProfile.foreshortenWarnFeedback)
            NotifyForeshorteningFeedback();

        _repGateRightValid = frameResult.repGateRightValid;
        _repGateLeftValid = frameResult.repGateLeftValid;
        if (_repGateRightValid) _lastRepGateRight = frameResult.repGateRight;
        if (_repGateLeftValid) _lastRepGateLeft = frameResult.repGateLeft;

        UpdateAssistedRepAfterAngles(swap, mpRightOk, mpLeftOk, mpRightWristOk, mpLeftWristOk);

        PushAnglesToAvatar(
            swap,
            frameResult.avatarMpRightOk, frameResult.avatarMpRightAngle,
            frameResult.avatarMpLeftOk, frameResult.avatarMpLeftAngle);
    }

    private void PushAnglesToAvatar(
        bool swap,
        bool mpRightOk, float mpRightAngle, bool mpLeftOk, float mpLeftAngle)
    {
        // cmd: FindObjectOfType her kare yasak — bir kez dene, yoksa bırak
        if (_avatarBodyDriver == null && !_avatarLookupAttempted)
        {
            _avatarBodyDriver = FindObjectOfType<AvatarBodyDriver>(true);
            _avatarLookupAttempted = true;
        }
        if (_avatarBodyDriver == null) return;
        // Seans öncesi örnek demo MediaPipe açısını ezmesin
        if (_avatarBodyDriver.IsExampleDemoMode) return;

        _avatarBodyDriver.SetMeasuredArms(measureRightArm, measureLeftArm);
        MovementAvatarDriver driver = ExerciseCatalog.GetAvatarDriver(_selectedMovementId);
        // Ön kamera: MP L/R görüntü tarafıdır; model hasta anatomisini izlemeli (sağ→sağ).
        if (swap)
        {
            _avatarBodyDriver.ApplyMeasuredArmAngles(
                driver,
                mpLeftOk, mpLeftAngle,
                mpRightOk, mpRightAngle,
                targetAngleDegrees, targetAngleDegrees);
            return;
        }

        _avatarBodyDriver.ApplyMeasuredArmAngles(
            driver,
            mpRightOk, mpRightAngle,
            mpLeftOk, mpLeftAngle,
            targetAngleDegrees, targetAngleDegrees);
    }

    private void NotifyForeshorteningFeedback()
    {
        if (Time.time <= _lastForeshortenWarnTime + foreshorteningWarningCooldownSeconds)
            return;
        _lastForeshortenWarnTime = Time.time;
        // Speak zaten altyazı gösterir — çift uyarı olmasın
        if (_voiceCoach != null && enableVoiceCoach)
            _voiceCoach.Speak(CoachCue.DepthCollapse);
        else if (warningManager != null)
            warningManager.TriggerWarning(Loc.T("warn.depthCollapse"));
    }

    private static float2 ToFloat2(Vector2 v)
    {
        return new float2(v.x, v.y);
    }

    private Vector2 FilterPoint(int index, float x, float y, float timestamp)
    {
        var filter = _filters[index];
        Vector2 result = filter.Filter(x, y, timestamp);
        _filters[index] = filter;
        return result;
    }

    private bool IsPointConfident(LandmarkPoint p)
    {
        return IsPointConfident(p, landmarkVisibilityThreshold);
    }

    private bool IsPointConfident(LandmarkPoint p, float visibilityThreshold)
    {
        if (!enableConfidenceGate) return true;
        float thr = Mathf.Clamp01(visibilityThreshold);
        if (p.hasVisibility && p.visibility < thr) return false;
        if (requirePresenceScore && p.hasPresence && p.presence < thr) return false;
        return true;
    }

    private static float Angle2D(Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 v1 = a - b;
        Vector2 v2 = c - b;
        if (v1.sqrMagnitude < 1e-12f || v2.sqrMagnitude < 1e-12f) return float.NaN;
        return Vector2.Angle(v1, v2);
    }
}
