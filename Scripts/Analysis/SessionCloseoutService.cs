using UnityEngine;

/// <summary>
/// Seans kapanışı: kayıt, DTW skor, yerel HTML/CSV dışa aktarım.
/// KVKK: hasta verisi yalnızca yerel; buluta gönderilmez. SaMD Class B.
/// </summary>
public sealed class SessionCloseoutService
{
    private readonly MovementDtw _movementDtw = new MovementDtw();

    public struct PatientIdentity
    {
        public string firstName;
        public string lastName;
        public float heightCm;
        public int ageYears;
        public int gender;
    }

    public struct SessionCounts
    {
        public int countR;
        public int countL;
        public int invalidR;
        public int invalidL;
        public float visualRight;
        public float visualLeft;
    }

    public struct ExerciseContext
    {
        public int targetReps;
        public float targetAngleDegrees;
        public bool plannedMeasureRight;
        public bool plannedMeasureLeft;
        public bool measureRightArm;
        public bool measureLeftArm;
        public MovementId selectedMovementId;
        public BodyRegionId selectedBodyRegionId;
    }

    public void SaveCurrentSession(
        in ExerciseContext exercise,
        in SessionCounts counts,
        in PatientIdentity identity,
        SessionQualityScorer qualityScorer,
        DataManager dataManager,
        SessionReportManager reportManager)
    {
        if (exercise.targetReps <= 0) return;

        bool saveRight = exercise.plannedMeasureRight;
        bool saveLeft = exercise.plannedMeasureLeft;
        int repsR = saveRight ? counts.countR : 0;
        int repsL = saveLeft ? counts.countL : 0;
        if (repsR == 0 && repsL == 0) return;

        bool hasReport = reportManager != null && reportManager.HasData;

        float maxR = hasReport ? reportManager.RightMaxAngle : counts.visualRight;
        float maxL = hasReport ? reportManager.LeftMaxAngle : counts.visualLeft;
        float avgR = hasReport ? reportManager.RightAverageAngle : maxR;
        float avgL = hasReport ? reportManager.LeftAverageAngle : maxL;

        float legacyMax = Mathf.Max(saveRight ? maxR : 0f, saveLeft ? maxL : 0f);
        int legacyReps = Mathf.Max(repsR, repsL);

        SessionEntry newEntry = new SessionEntry
        {
            dateTime = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
            maxROM = legacyMax,
            averageROM = (avgR + avgL) * 0.5f,
            completedReps = legacyReps,
            invalidReps = (saveRight ? counts.invalidR : 0) + (saveLeft ? counts.invalidL : 0),
            targetReps = exercise.targetReps,
            completionRate = exercise.targetReps > 0 ? ((float)legacyReps / exercise.targetReps) * 100f : 0f,
            durationSeconds = reportManager != null ? reportManager.SessionDurationSeconds : 0f,
            compensationEvents = reportManager != null ? reportManager.CompensationEventCount : 0,
            targetAngle = exercise.targetAngleDegrees,
            rightMaxROM = maxR,
            leftMaxROM = maxL,
            rightAverageROM = avgR,
            leftAverageROM = avgL,
            rightCompletedReps = repsR,
            leftCompletedReps = repsL,
            rightInvalidReps = saveRight ? counts.invalidR : 0,
            leftInvalidReps = saveLeft ? counts.invalidL : 0,
            rightArmEnabled = saveRight,
            leftArmEnabled = saveLeft,
            firstName = identity.firstName,
            lastName = identity.lastName,
            heightCm = identity.heightCm,
            ageYears = identity.ageYears,
            gender = identity.gender,
            peakStrain = hasReport ? reportManager.PeakStrain : 0f,
            meanStrain = hasReport ? reportManager.MeanStrain : 0f,
            angleAtPeakStrainR = hasReport ? reportManager.AngleAtPeakStrainRight : 0f,
            angleAtPeakStrainL = hasReport ? reportManager.AngleAtPeakStrainLeft : 0f,
            movementScoreRight = hasReport ? reportManager.MovementScoreRight : -1f,
            movementScoreLeft = hasReport ? reportManager.MovementScoreLeft : -1f,
            qualityScoreMean = qualityScorer != null && qualityScorer.FrameCount > 0 ? qualityScorer.MeanScore : -1f,
            qualityScoreMin = qualityScorer != null && qualityScorer.FrameCount > 0 ? qualityScorer.MinScore : -1f,
            qualityBand = qualityScorer != null
                ? SessionQualityScorer.ToStoredBand(qualityScorer.BandFromMean)
                : 0,
            qualityFormulaVersion = qualityScorer != null && qualityScorer.FrameCount > 0
                ? SessionQualityScorer.FormulaVersion
                : "",
            rightAssistedReps = hasReport ? reportManager.RightAssistedReps : 0,
            leftAssistedReps = hasReport ? reportManager.LeftAssistedReps : 0,
            assistedReps = hasReport ? reportManager.AssistedReps : 0,
            trackingJumpEvents = reportManager != null ? reportManager.TrackingJumpEventCount : 0,
            secondPersonEvents = reportManager != null ? reportManager.SecondPersonEventCount : 0,
            assistNearEvents = reportManager != null ? reportManager.AssistNearEventCount : 0,
            movementId = (int)exercise.selectedMovementId,
            bodyRegionId = (int)exercise.selectedBodyRegionId
        };

        if (hasReport)
            reportManager.CopySeriesToEntry(newEntry);

        if (dataManager != null)
            dataManager.SaveSession(newEntry);
    }

    /// <summary>
    /// Hasta açı serisini hedef şablonla DTW karşılaştırır.
    /// SaMD Class B: klinik karar-destek; tanı değildir.
    /// </summary>
    public void ComputeMovementScore(
        bool enableMovementScoring,
        float targetAngleDegrees,
        int movementTemplatePoints,
        bool measureRightArm,
        bool measureLeftArm,
        SessionReportManager reportManager)
    {
        if (!enableMovementScoring || reportManager == null || !reportManager.HasData) return;

        float[] template = MovementDtw.BuildIdealRepTemplate(targetAngleDegrees, movementTemplatePoints);
        int count = reportManager.SampleCount;

        float rightScore = -1f;
        float leftScore = -1f;

        if (measureRightArm)
        {
            MovementDtw.Result r = _movementDtw.Compare(template, template.Length, reportManager.RightAngles, count);
            if (r.valid) rightScore = r.similarity;
        }
        if (measureLeftArm)
        {
            MovementDtw.Result l = _movementDtw.Compare(template, template.Length, reportManager.LeftAngles, count);
            if (l.valid) leftScore = l.similarity;
        }

        reportManager.SetMovementScore(rightScore, leftScore);
    }

    /// <summary>
    /// Yerel HTML/CSV/Excel. KVKK: dışarı gönderilmez.
    /// </summary>
    public void ExportSessionFiles(
        DataManager dataManager,
        SessionReportManager reportManager,
        in PatientIdentity identity,
        bool measureRightArm,
        bool measureLeftArm,
        MovementId movementId,
        SurveyResponse survey = null)
    {
        if (reportManager == null || !reportManager.HasData) return;

        try
        {
            PatientProfile profile = null;
            PatientHistory history = null;
            int sessionNumber = 1;

            if (dataManager != null)
            {
                profile = dataManager.LoadProfile();
                history = dataManager.LoadHistory();
                sessionNumber = CountSessionsForPatientAndMovement(history, profile, movementId);
            }

            if (profile == null)
            {
                profile = new PatientProfile
                {
                    firstName = identity.firstName,
                    lastName = identity.lastName,
                    heightCm = identity.heightCm,
                    ageYears = identity.ageYears,
                    gender = identity.gender,
                    measureRightArm = measureRightArm,
                    measureLeftArm = measureLeftArm
                };
            }

            ReportExporter.ExportSessionHtml(
                reportManager, profile, Mathf.Max(1, sessionNumber), movementId, survey);

            if (history != null)
            {
                int planned = ReportExporter.ResolvePlannedSessionsPerWeek(dataManager, profile, history);
                ReportExporter.ExportProgress(history, profile, HistoryFilterMode.All, HistoryFilterMode.All, planned);
            }
        }
        catch (System.Exception)
        {
            // Rapor üretimi seans akışını bloklamamalı
        }
    }

    public static int CountSessionsForPatient(PatientHistory history, PatientProfile profile)
    {
        if (history == null || history.sessions == null) return 1;
        PatientHistory filtered = PatientVault.FilterHistoryForPatient(history, profile, fallbackToAll: false);
        int n = filtered != null && filtered.sessions != null ? filtered.sessions.Count : 0;
        return Mathf.Max(1, n);
    }

    public static int CountSessionsForPatientAndMovement(
        PatientHistory history, PatientProfile profile, MovementId movementId)
    {
        if (history == null || history.sessions == null) return 1;
        PatientHistory filtered = PatientVault.FilterHistoryForPatient(history, profile, fallbackToAll: false);
        if (filtered == null || filtered.sessions == null) return 1;
        int n = 0;
        for (int i = 0; i < filtered.sessions.Count; i++)
        {
            SessionEntry s = filtered.sessions[i];
            if (s == null) continue;
            MovementId id = ExerciseCatalog.ResolveStoredMovementId(s.bodyRegionId, s.movementId);
            if (id == movementId) n++;
        }
        return Mathf.Max(1, n);
    }
}
