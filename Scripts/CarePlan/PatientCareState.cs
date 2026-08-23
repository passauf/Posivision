using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tanıma / aktif program fazı.
/// SaMD Class B: yol haritası karar-destek; reçete veya teşhis değildir.
/// KVKK: yalnızca yerel JSON.
/// </summary>
public enum CarePhase
{
    Assessment = 0,
    ActiveProgram = 1
}

public enum CareIntensity
{
    Easy = 0,
    Standard = 1,
    Deload = 2
}

/// <summary>Uyumsuzluk neden kodları — klinisyen raporunda kullanılır.</summary>
public static class ClinicianReasonCode
{
    public const string SaidEasyButHighStrain = "SaidEasyButHighStrain";
    public const string SaidEasyButHighComp = "SaidEasyButHighComp";
    public const string SaidDailyButLowFrequency = "SaidDailyButLowFrequency";
    public const string SaidNoPainButHighStrain = "SaidNoPainButHighStrain";
    public const string SaidHardButLowLoad = "SaidHardButLowLoad";
}

[Serializable]
public class CarePlanWeek
{
    public int weekIndex;
    public CareIntensity intensity = CareIntensity.Standard;
    public float targetAngle;
    public int targetReps;
    public int sessionsPerWeek = 3;
}

[Serializable]
public class CarePlan
{
    public float dailyTargetAngle = 120f;
    public int dailyTargetReps = 8;
    public int sessionsPerWeek = 3;
    /// <summary>Bit0=Pzt … Bit6=Paz. Varsayılan Pzt/Çar/Cum.</summary>
    public int trainingDayMask = (1 << 0) | (1 << 2) | (1 << 4);
    public CareIntensity currentIntensity = CareIntensity.Standard;
    public List<CarePlanWeek> monthlyWeeks = new List<CarePlanWeek>();
    public string patientSummary = "";
}

[Serializable]
public class SurveyResponse
{
    public int sessionIndex;
    public string dateTime = "";

    /// <summary>0 kötü – 5 nötr – 10 iyi; −1 = bilmiyorum / atlandı. Kolaylık, rahatlık, dinçlik aynı yönde.</summary>
    public int perceivedDifficulty = -1;
    public int painVas = -1;
    public int motivation = -1;
    public int fatigue = -1;
    public int homeExerciseDays = -1;
    public int sleepQuality = -1;
    public int confidence = -1;
    public int willingness = -1;

    public bool HasRecordedAnswers =>
        perceivedDifficulty >= 0 || painVas >= 0 || motivation >= 0 || fatigue >= 0
        || homeExerciseDays >= 0 || sleepQuality >= 0 || confidence >= 0 || willingness >= 0;

    public void CopyTo(SessionEntry entry)
    {
        if (entry == null) return;
        entry.hasPostSessionSurvey = true;
        entry.surveyDifficulty = perceivedDifficulty;
        entry.surveyPain = painVas;
        entry.surveyMotivation = motivation;
        entry.surveyFatigue = fatigue;
        entry.surveyHomeDays = homeExerciseDays;
        entry.surveySleep = sleepQuality;
        entry.surveyConfidence = confidence;
        entry.surveyWillingness = willingness;
    }

    public static SurveyResponse FromSessionEntry(SessionEntry entry)
    {
        if (entry == null || !entry.hasPostSessionSurvey) return null;
        return new SurveyResponse
        {
            dateTime = entry.dateTime,
            perceivedDifficulty = entry.surveyDifficulty,
            painVas = entry.surveyPain,
            motivation = entry.surveyMotivation,
            fatigue = entry.surveyFatigue,
            homeExerciseDays = entry.surveyHomeDays,
            sleepQuality = entry.surveySleep,
            confidence = entry.surveyConfidence,
            willingness = entry.surveyWillingness
        };
    }
}

[Serializable]
public class ClinicianNote
{
    public string noteId = "";
    public int sessionIndex;
    public string reasonCode = "";
    public string patientClaim = "";
    public string observedSummary = "";
    public string createdAt = "";
}

[Serializable]
public class PatientCareState
{
    public const int AssessmentSessionTarget = 5;

    public CarePhase phase = CarePhase.Assessment;
    public int assessmentSessionCount;
    public CarePlan plan = new CarePlan();
    public List<SurveyResponse> surveys = new List<SurveyResponse>();
    public List<ClinicianNote> clinicianNotes = new List<ClinicianNote>();
    public int programVersion;
    public string lastAdaptedAt = "";
    public string createdAt = "";

    public bool IsAssessmentPhase => phase == CarePhase.Assessment;

    public int AssessmentDisplayIndex =>
        Mathf.Clamp(assessmentSessionCount + 1, 1, AssessmentSessionTarget);

    public bool NeedsPostSessionSurvey(int historySessionCount)
    {
        if (phase == CarePhase.Assessment)
            return historySessionCount > 0 && assessmentSessionCount < AssessmentSessionTarget;
        // Aktif programda kısa check-in (zorunlu değil — AssessmentFlow karar verir)
        return true;
    }
}
