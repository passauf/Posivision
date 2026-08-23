using UnityEngine;

/// <summary>
/// Geçmiş seanslara göre hedef açı / tekrar önerisi.
/// KVKK: yalnızca yerel SessionEntry metrikleri; kimlik kullanılmaz.
/// SaMD Class B: karar-destek önerisi; teşhis veya reçete değildir.
/// </summary>
public static class PersonalizedTargetAdvisor
{
    public const float DefaultAngle = 160f;
    public const int DefaultReps = 10;

    /// <summary>Düşük ROM hastaları için alt sınır (örn. 5–15°).</summary>
    public const float MinAngleDegrees = 10f;
    public const float MaxAngleDegrees = JointAngleJob.MaxShoulderElevationDegrees;
    public const float AngleStepDegrees = 5f;
    public const float ProgressSlackDegrees = 8f;

    public const int MinReps = 6;
    public const int MaxReps = 16;
    public const int RepStep = 2;

    public const int HighCompensationThreshold = 3;
    public const float LowCompletionRate = 0.6f;
    public const float HighPeakStrain = 0.7f;

    public struct Suggestion
    {
        public float targetAngle;
        public int targetReps;
        public bool hasHistory;
        public string summaryTr;
    }

    public static Suggestion Suggest(PatientHistory history, float fallbackAngle, int fallbackReps)
    {
        float baseAngle = fallbackAngle > 1f ? fallbackAngle : DefaultAngle;
        int baseReps = fallbackReps > 0 ? fallbackReps : DefaultReps;

        Suggestion s;
        s.targetAngle = Mathf.Clamp(baseAngle, MinAngleDegrees, MaxAngleDegrees);
        s.targetReps = Mathf.Clamp(baseReps, MinReps, MaxReps);
        s.hasHistory = false;
        s.summaryTr = Loc.T("target.default");

        if (history == null || history.sessions == null || history.sessions.Count == 0)
            return s;

        SessionEntry last = history.sessions[history.sessions.Count - 1];
        float lastMax = EffectiveMaxRom(last);
        if (lastMax < 1f)
            return s;

        s.hasHistory = true;

        float suggestedAngle = lastMax + AngleStepDegrees;
        // Son seansı rahat aştıysa adımı koru; zorlandıysa hedefi lastMax civarına çek
        bool struggled = last.completionRate < LowCompletionRate
                         || last.compensationEvents >= HighCompensationThreshold
                         || last.peakStrain >= HighPeakStrain
                         || last.invalidReps > last.completedReps;

        if (struggled)
            suggestedAngle = Mathf.Min(lastMax + ProgressSlackDegrees * 0.25f, last.targetAngle > 1f ? last.targetAngle : lastMax);
        else if (lastMax + ProgressSlackDegrees < (last.targetAngle > 1f ? last.targetAngle : baseAngle))
            suggestedAngle = lastMax + AngleStepDegrees;
        else
            suggestedAngle = Mathf.Max(last.targetAngle, lastMax) + AngleStepDegrees;

        s.targetAngle = Mathf.Clamp(Mathf.Round(suggestedAngle / AngleStepDegrees) * AngleStepDegrees,
            MinAngleDegrees, MaxAngleDegrees);

        int suggestedReps = last.targetReps > 0 ? last.targetReps : baseReps;
        if (struggled)
            suggestedReps = Mathf.Max(MinReps, suggestedReps - RepStep);
        else if (last.completionRate >= 0.95f && last.compensationEvents <= 1)
            suggestedReps = Mathf.Min(MaxReps, suggestedReps + RepStep);

        s.targetReps = Mathf.Clamp(suggestedReps, MinReps, MaxReps);

        if (struggled)
            s.summaryTr = Loc.Format("target.hard", (int)s.targetAngle, s.targetReps);
        else
            s.summaryTr = Loc.Format("target.ok", (int)s.targetAngle, s.targetReps, (int)lastMax);

        return s;
    }

    private static float EffectiveMaxRom(SessionEntry e)
    {
        float split = Mathf.Max(e.rightMaxROM, e.leftMaxROM);
        if (split > 1f) return split;
        return e.maxROM;
    }
}
