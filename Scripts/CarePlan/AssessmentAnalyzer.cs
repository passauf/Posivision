using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Seans + anket → uyumsuzluk notları ve plan güncelleme.
/// SaMD Class B: klinisyen karar-destek notu; teşhis değildir.
/// Hasta UI'sine ClinicianNote asla yazılmaz.
/// </summary>
public static class AssessmentAnalyzer
{
    public const float HighStrainThreshold = 0.7f;
    public const int HighCompensationThreshold = 3;
    /// <summary>Anket ölçeği: 0 kötü, 5 nötr, 10 iyi (kolay / rahat / dinç).</summary>
    public const int SurveyNeutralScore = 5;
    public const int EasyScoreMin = 7;
    public const int HardScoreMax = 3;
    public const int NoPainMin = 9;
    public const int HighPainMax = 3;
    public const int ClaimedDailyHomeMin = 6;
    public const float LowFrequencySessionsPerWeek = 1.5f;

    public static void ProcessAfterSurvey(
        PatientCareState state,
        PatientHistory history,
        SessionEntry lastSession,
        SurveyResponse survey,
        bool surveyWasMandatoryAssessment)
    {
        if (state == null || history == null) return;

        if (survey != null)
        {
            if (state.surveys == null) state.surveys = new List<SurveyResponse>();
            state.surveys.Add(survey);
            DetectDiscrepancies(state, history, lastSession, survey);
        }

        if (state.phase == CarePhase.Assessment)
        {
            if (surveyWasMandatoryAssessment)
                state.assessmentSessionCount = Mathf.Min(
                    PatientCareState.AssessmentSessionTarget,
                    state.assessmentSessionCount + 1);

            if (state.assessmentSessionCount >= PatientCareState.AssessmentSessionTarget)
            {
                state.plan = CarePlanBuilder.BuildInitial(history, state.surveys);
                state.phase = CarePhase.ActiveProgram;
                state.programVersion = 1;
                state.lastAdaptedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            }
        }
        else if (state.phase == CarePhase.ActiveProgram && lastSession != null)
        {
            CarePlanBuilder.Adapt(state.plan, lastSession, survey);
            state.programVersion = Mathf.Max(1, state.programVersion + 1);
            state.lastAdaptedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        }
    }

    /// <summary>Acil kapanışta anket yok — yalnızca sayaç (tanıma).</summary>
    public static void ProcessEmergencySession(PatientCareState state, PatientHistory history)
    {
        if (state == null || state.phase != CarePhase.Assessment) return;
        state.assessmentSessionCount = Mathf.Min(
            PatientCareState.AssessmentSessionTarget,
            Mathf.Max(state.assessmentSessionCount, history != null && history.sessions != null
                ? Mathf.Min(history.sessions.Count, PatientCareState.AssessmentSessionTarget)
                : state.assessmentSessionCount));

        if (state.assessmentSessionCount >= PatientCareState.AssessmentSessionTarget
            && (state.plan == null || state.plan.monthlyWeeks == null || state.plan.monthlyWeeks.Count == 0))
        {
            state.plan = CarePlanBuilder.BuildInitial(history, state.surveys);
            state.phase = CarePhase.ActiveProgram;
            state.programVersion = Mathf.Max(1, state.programVersion);
            state.lastAdaptedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        }
    }

    private static void DetectDiscrepancies(
        PatientCareState state, PatientHistory history, SessionEntry last, SurveyResponse survey)
    {
        if (last == null || survey == null) return;
        if (state.clinicianNotes == null) state.clinicianNotes = new List<ClinicianNote>();

        float completion01 = last.completionRate > 1.5f ? last.completionRate / 100f : last.completionRate;

        // Kolay dedi ama yüksek zorlanma (yüksek skor = kolay)
        if (survey.perceivedDifficulty >= EasyScoreMin
            && last.peakStrain >= HighStrainThreshold)
        {
            AddNote(state, last, survey, ClinicianReasonCode.SaidEasyButHighStrain,
                Loc.Format("clinician.claim.easy", survey.perceivedDifficulty),
                Loc.Format("clinician.obs.strain", Mathf.RoundToInt(last.peakStrain * 100f),
                    last.compensationEvents, last.invalidReps));
        }

        // Kolay dedi ama yüksek kompansasyon
        if (survey.perceivedDifficulty >= EasyScoreMin
            && last.compensationEvents >= HighCompensationThreshold)
        {
            AddNote(state, last, survey, ClinicianReasonCode.SaidEasyButHighComp,
                Loc.Format("clinician.claim.easy", survey.perceivedDifficulty),
                Loc.Format("clinician.obs.comp", last.compensationEvents, last.invalidReps));
        }

        // Ağrı yok dedi ama yüksek strain (yüksek skor = rahat)
        if (survey.painVas >= NoPainMin && last.peakStrain >= HighStrainThreshold)
        {
            AddNote(state, last, survey, ClinicianReasonCode.SaidNoPainButHighStrain,
                Loc.Format("clinician.claim.noPain", survey.painVas),
                Loc.Format("clinician.obs.strain", Mathf.RoundToInt(last.peakStrain * 100f),
                    last.compensationEvents, last.invalidReps));
        }

        // Çok zor dedi ama düşük yük / yüksek tamamlanma (düşük skor = zor)
        if (survey.perceivedDifficulty >= 0 && survey.perceivedDifficulty <= HardScoreMax
            && completion01 >= 0.95f && last.peakStrain < 0.4f && last.compensationEvents == 0)
        {
            AddNote(state, last, survey, ClinicianReasonCode.SaidHardButLowLoad,
                Loc.Format("clinician.claim.hard", survey.perceivedDifficulty),
                Loc.Format("clinician.obs.lowLoad", Mathf.RoundToInt(completion01 * 100f),
                    Mathf.RoundToInt(last.peakStrain * 100f)));
        }

        // Her gün evde yaptım ama seans sıklığı düşük
        if (survey.homeExerciseDays >= ClaimedDailyHomeMin && history != null && history.sessions != null)
        {
            float perWeek = EstimateSessionsPerWeek(history);
            if (perWeek >= 0f && perWeek < LowFrequencySessionsPerWeek && history.sessions.Count >= 3)
            {
                AddNote(state, last, survey, ClinicianReasonCode.SaidDailyButLowFrequency,
                    Loc.Format("clinician.claim.dailyHome", survey.homeExerciseDays),
                    Loc.Format("clinician.obs.frequency", perWeek.ToString("F1")));
            }
        }
    }

    private static void AddNote(
        PatientCareState state, SessionEntry last, SurveyResponse survey,
        string reason, string claim, string observed)
    {
        // Aynı seans + aynı reason tekrarını engelle
        for (int i = 0; i < state.clinicianNotes.Count; i++)
        {
            ClinicianNote n = state.clinicianNotes[i];
            if (n.sessionIndex == survey.sessionIndex && n.reasonCode == reason)
                return;
        }

        state.clinicianNotes.Add(new ClinicianNote
        {
            noteId = Guid.NewGuid().ToString("N").Substring(0, 12),
            sessionIndex = survey.sessionIndex,
            reasonCode = reason,
            patientClaim = claim,
            observedSummary = observed,
            createdAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
        });
    }

    private static float EstimateSessionsPerWeek(PatientHistory history)
    {
        int n = history.sessions.Count;
        if (n < 2) return -1f;
        if (!TryParse(history.sessions[0].dateTime, out DateTime first)) return -1f;
        if (!TryParse(history.sessions[n - 1].dateTime, out DateTime last)) return -1f;
        double days = Math.Max(1.0, (last - first).TotalDays);
        return (float)(n / (days / 7.0));
    }

    private static bool TryParse(string s, out DateTime dt)
    {
        dt = default;
        if (string.IsNullOrEmpty(s)) return false;
        return DateTime.TryParseExact(s, "dd/MM/yyyy HH:mm",
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out dt)
               || DateTime.TryParse(s, out dt);
    }

    public static string BuildPatientProgramText(PatientCareState state)
    {
        if (state == null) return Loc.T("careplan.empty");
        if (state.phase == CarePhase.Assessment)
        {
            return Loc.Format("careplan.assess.progress",
                state.assessmentSessionCount, PatientCareState.AssessmentSessionTarget);
        }
        CarePlan p = state.plan;
        if (p == null) return Loc.T("careplan.empty");
        var sb = new StringBuilder(320);
        if (!string.IsNullOrEmpty(p.patientSummary))
        {
            sb.AppendLine(p.patientSummary);
            sb.AppendLine();
        }
        sb.AppendLine(Loc.T("careplan.section.todayTarget"));
        sb.AppendLine(Loc.Format("careplan.daily", (int)p.dailyTargetAngle, p.dailyTargetReps));
        sb.AppendLine();
        sb.AppendLine(Loc.T("careplan.section.week"));
        sb.AppendLine(Loc.Format("careplan.weekly", p.sessionsPerWeek, IntensityLabel(p.currentIntensity)));
        sb.AppendLine();
        bool today = CarePlanBuilder.IsTrainingDay(p);
        sb.AppendLine(today ? Loc.T("careplan.today.train") : Loc.T("careplan.today.rest"));
        if (p.monthlyWeeks != null && p.monthlyWeeks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.T("careplan.section.month"));
            for (int i = 0; i < p.monthlyWeeks.Count; i++)
            {
                CarePlanWeek w = p.monthlyWeeks[i];
                sb.AppendLine(Loc.Format("careplan.monthly.weekClean",
                    w.weekIndex + 1, (int)w.targetAngle, w.targetReps, IntensityLabel(w.intensity)));
            }
        }
        return sb.ToString();
    }

    private static string IntensityLabel(CareIntensity i)
    {
        switch (i)
        {
            case CareIntensity.Easy: return Loc.T("careplan.intensity.easy");
            case CareIntensity.Deload: return Loc.T("careplan.intensity.deload");
            default: return Loc.T("careplan.intensity.standard");
        }
    }
}
