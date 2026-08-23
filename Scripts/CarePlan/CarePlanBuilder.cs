using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tanıma metrikleri + ankete göre kural tabanlı bakım planı üretir / uyarlar.
/// SaMD Class B: karar-destek; reçete değildir. LLM yok.
/// </summary>
public static class CarePlanBuilder
{
    public const float MinAngle = 90f;
    public const float MaxAngle = JointAngleJob.MaxShoulderElevationDegrees;
    public const float AngleStep = 5f;
    public const int MinReps = 6;
    public const int MaxReps = 16;
    public const int RepStep = 2;

    public const float HighStrain = 0.7f;
    public const int HighCompensation = 3;
    public const float LowCompletion = 0.6f;
    public const float GoodCompletion = 0.95f;

    /// <summary>İlk 5 tanıma seansı sonrası plan.</summary>
    public static CarePlan BuildInitial(PatientHistory history, List<SurveyResponse> surveys)
    {
        var plan = new CarePlan();
        if (history == null || history.sessions == null || history.sessions.Count == 0)
        {
            plan.dailyTargetAngle = 110f;
            plan.dailyTargetReps = 8;
            plan.sessionsPerWeek = 3;
            plan.currentIntensity = CareIntensity.Easy;
            plan.patientSummary = Loc.T("careplan.summary.default");
            FillMonthly(plan);
            return plan;
        }

        float avgMax = 0f;
        float avgStrain = 0f;
        float avgComp = 0f;
        float avgCompletion = 0f;
        int n = Mathf.Min(PatientCareState.AssessmentSessionTarget, history.sessions.Count);
        int start = history.sessions.Count - n;
        for (int i = start; i < history.sessions.Count; i++)
        {
            SessionEntry s = history.sessions[i];
            avgMax += EffectiveMax(s);
            avgStrain += s.peakStrain;
            avgComp += s.compensationEvents;
            avgCompletion += s.completionRate > 1.5f ? s.completionRate / 100f : s.completionRate;
        }
        avgMax /= n;
        avgStrain /= n;
        avgComp /= n;
        avgCompletion /= n;

        float surveyDiff = AverageSurveyField(surveys, r => r.perceivedDifficulty);
        float surveyPain = AverageSurveyField(surveys, r => r.painVas);
        float surveyHome = AverageSurveyField(surveys, r => r.homeExerciseDays);

        bool struggled = avgStrain >= HighStrain || avgComp >= HighCompensation || avgCompletion < LowCompletion
                         || (surveyDiff >= 0f && surveyDiff <= AssessmentAnalyzer.HardScoreMax)
                         || (surveyPain >= 0f && surveyPain <= AssessmentAnalyzer.HighPainMax);

        float baseAngle = Mathf.Clamp(
            Mathf.Round((avgMax + (struggled ? 0f : AngleStep)) / AngleStep) * AngleStep,
            MinAngle, MaxAngle);
        int baseReps = struggled ? MinReps : Mathf.Clamp(8 + (avgCompletion >= GoodCompletion ? RepStep : 0), MinReps, MaxReps);

        plan.dailyTargetAngle = baseAngle;
        plan.dailyTargetReps = baseReps;
        plan.sessionsPerWeek = surveyHome >= 5f ? 5 : (surveyHome >= 3f ? 4 : 3);
        plan.currentIntensity = struggled ? CareIntensity.Easy : CareIntensity.Standard;
        plan.trainingDayMask = DefaultMaskForSessions(plan.sessionsPerWeek);
        plan.patientSummary = struggled
            ? Loc.Format("careplan.summary.cautious", (int)baseAngle, baseReps, plan.sessionsPerWeek)
            : Loc.Format("careplan.summary.progress", (int)baseAngle, baseReps, plan.sessionsPerWeek);
        FillMonthly(plan);
        return plan;
    }

    /// <summary>Seans 6+ dinamik uyarlama.</summary>
    public static void Adapt(CarePlan plan, SessionEntry last, SurveyResponse survey)
    {
        if (plan == null || last == null) return;

        float completion01 = last.completionRate > 1.5f ? last.completionRate / 100f : last.completionRate;

        bool struggled = completion01 < LowCompletion
                         || last.compensationEvents >= HighCompensation
                         || last.peakStrain >= HighStrain
                         || last.invalidReps > last.completedReps
                         || (survey != null && survey.perceivedDifficulty >= 0
                             && survey.perceivedDifficulty <= AssessmentAnalyzer.HardScoreMax)
                         || (survey != null && survey.painVas >= 0
                             && survey.painVas <= AssessmentAnalyzer.HighPainMax);

        bool strong = completion01 >= GoodCompletion
                      && last.compensationEvents <= 1
                      && last.peakStrain < HighStrain * 0.85f
                      && (survey == null || survey.perceivedDifficulty < 0
                          || survey.perceivedDifficulty >= AssessmentAnalyzer.SurveyNeutralScore);

        if (struggled)
        {
            plan.currentIntensity = CareIntensity.Deload;
            plan.dailyTargetAngle = Mathf.Clamp(plan.dailyTargetAngle - AngleStep, MinAngle, MaxAngle);
            plan.dailyTargetReps = Mathf.Max(MinReps, plan.dailyTargetReps - RepStep);
            plan.patientSummary = Loc.Format("careplan.summary.deload",
                (int)plan.dailyTargetAngle, plan.dailyTargetReps);
        }
        else if (strong)
        {
            plan.currentIntensity = CareIntensity.Standard;
            plan.dailyTargetAngle = Mathf.Clamp(plan.dailyTargetAngle + AngleStep, MinAngle, MaxAngle);
            plan.dailyTargetReps = Mathf.Min(MaxReps, plan.dailyTargetReps + RepStep);
            plan.patientSummary = Loc.Format("careplan.summary.progress",
                (int)plan.dailyTargetAngle, plan.dailyTargetReps, plan.sessionsPerWeek);
        }

        SyncCurrentWeek(plan);
    }

    public static bool TryGetTodaysTargets(CarePlan plan, out float angle, out int reps)
    {
        angle = PersonalizedTargetAdvisor.DefaultAngle;
        reps = PersonalizedTargetAdvisor.DefaultReps;
        if (plan == null) return false;

        int dow = DayOfWeekBit(System.DateTime.Now.DayOfWeek);
        bool trainingDay = (plan.trainingDayMask & (1 << dow)) != 0;
        angle = plan.dailyTargetAngle;
        reps = plan.dailyTargetReps;
        if (plan.currentIntensity == CareIntensity.Deload)
        {
            angle = Mathf.Max(MinAngle, angle - AngleStep);
            reps = Mathf.Max(MinReps, reps - RepStep);
        }
        return trainingDay || true; // hedef her zaman doldurulabilir; UI "bugün dinlenme" gösterebilir
    }

    public static bool IsTrainingDay(CarePlan plan)
    {
        if (plan == null) return false;
        int dow = DayOfWeekBit(System.DateTime.Now.DayOfWeek);
        return (plan.trainingDayMask & (1 << dow)) != 0;
    }

    private static void FillMonthly(CarePlan plan)
    {
        plan.monthlyWeeks = new List<CarePlanWeek>(4);
        for (int w = 0; w < 4; w++)
        {
            var week = new CarePlanWeek
            {
                weekIndex = w,
                targetAngle = Mathf.Clamp(plan.dailyTargetAngle + w * AngleStep, MinAngle, MaxAngle),
                targetReps = Mathf.Clamp(plan.dailyTargetReps + (w >= 2 ? RepStep : 0), MinReps, MaxReps),
                sessionsPerWeek = plan.sessionsPerWeek,
                intensity = w == 3 ? CareIntensity.Deload : plan.currentIntensity
            };
            if (w == 3)
            {
                week.targetAngle = Mathf.Max(MinAngle, plan.dailyTargetAngle);
                week.targetReps = Mathf.Max(MinReps, plan.dailyTargetReps - RepStep);
            }
            plan.monthlyWeeks.Add(week);
        }
    }

    private static void SyncCurrentWeek(CarePlan plan)
    {
        if (plan.monthlyWeeks == null || plan.monthlyWeeks.Count == 0)
            FillMonthly(plan);
        else if (plan.monthlyWeeks.Count > 0)
        {
            CarePlanWeek cur = plan.monthlyWeeks[0];
            cur.targetAngle = plan.dailyTargetAngle;
            cur.targetReps = plan.dailyTargetReps;
            cur.intensity = plan.currentIntensity;
            cur.sessionsPerWeek = plan.sessionsPerWeek;
        }
    }

    private static int DefaultMaskForSessions(int perWeek)
    {
        if (perWeek >= 5) return (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4);
        if (perWeek >= 4) return (1 << 0) | (1 << 2) | (1 << 4) | (1 << 5);
        return (1 << 0) | (1 << 2) | (1 << 4);
    }

    private static int DayOfWeekBit(System.DayOfWeek d)
    {
        // Pazartesi=0 … Pazar=6
        int i = ((int)d + 6) % 7;
        return i;
    }

    private static float EffectiveMax(SessionEntry s)
    {
        float m = Mathf.Max(s.rightMaxROM, s.leftMaxROM);
        return m > 1f ? m : s.maxROM;
    }

    private static float AverageSurveyField(List<SurveyResponse> surveys, System.Func<SurveyResponse, int> getter)
    {
        if (surveys == null || surveys.Count == 0) return -1f;
        float sum = 0f;
        int n = 0;
        for (int i = 0; i < surveys.Count; i++)
        {
            int v = getter(surveys[i]);
            if (v < 0) continue;
            sum += v;
            n++;
        }
        return n == 0 ? -1f : sum / n;
    }
}
