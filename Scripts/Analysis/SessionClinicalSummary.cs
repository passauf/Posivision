using System.Text;
using UnityEngine;

/// <summary>
/// SeansEntry özetinden kural tabanlı klinik karar-destek özeti.
/// LLM yok; yerel, deterministik. SaMD Class B — teşhis değildir.
/// </summary>
public static class SessionClinicalSummary
{
    public static string Build(SessionEntry s, SessionEntry previous)
    {
        return Build(s, previous, LanguageSettings.Current);
    }

    public static string Build(SessionEntry s, SessionEntry previous, AppLanguage lang)
    {
        if (s == null) return Loc.T("detail.summary.empty", lang);

        var sb = new StringBuilder(512);
        sb.AppendLine(Loc.T("detail.summary.header", lang));
        sb.AppendLine();

        float maxRom = SessionHistoryFilter.EffectiveMax(s);
        float target = s.targetAngle > 1f ? s.targetAngle : 160f;

        // Kollar
        bool r = SessionHistoryFilter.ShowRight(s) || s.rightMaxROM > 0f || s.rightCompletedReps > 0;
        bool l = SessionHistoryFilter.ShowLeft(s) || s.leftMaxROM > 0f || s.leftCompletedReps > 0;
        if (r && !l) sb.AppendLine(Loc.T("detail.note.rightOnly", lang));
        else if (!r && l) sb.AppendLine(Loc.T("detail.note.leftOnly", lang));
        else sb.AppendLine(Loc.T("detail.note.bothArms", lang));

        // Tekrar
        int done = s.completedReps;
        if (done == 0 && (s.rightCompletedReps > 0 || s.leftCompletedReps > 0))
            done = s.rightCompletedReps + s.leftCompletedReps;
        if (done <= 0)
            sb.AppendLine(Loc.T("detail.note.noReps", lang));
        else if (s.targetReps > 0 && done >= s.targetReps)
            sb.AppendLine(Loc.T("detail.note.targetRepsMet", lang));
        else if (s.targetReps > 0)
            sb.AppendLine(Loc.Format("detail.note.targetRepsPct", lang,
                Mathf.RoundToInt(100f * done / Mathf.Max(1, s.targetReps))));

        // ROM vs hedef
        if (maxRom >= target)
            sb.AppendLine(Loc.T("detail.note.romMet", lang));
        else
            sb.AppendLine(Loc.Format("detail.note.romBelow", lang, Mathf.RoundToInt(target - maxRom)));

        // Kompansasyon
        if (s.compensationEvents <= 0)
            sb.AppendLine(Loc.T("detail.note.noComp", lang));
        else if (s.compensationEvents <= 3)
            sb.AppendLine(Loc.Format("detail.note.mildComp", lang, s.compensationEvents));
        else
            sb.AppendLine(Loc.Format("detail.note.highComp", lang, s.compensationEvents));

        if (s.invalidReps > 0)
            sb.AppendLine(Loc.Format("detail.note.invalid", lang, s.invalidReps));

        // Zorlanma
        if (s.peakStrain >= SessionHistoryFilter.HighStrainThreshold)
            sb.AppendLine(Loc.Format("detail.note.highStrain", lang,
                Mathf.RoundToInt(s.peakStrain * 100f),
                Mathf.RoundToInt(s.angleAtPeakStrainR),
                Mathf.RoundToInt(s.angleAtPeakStrainL)));
        else if (s.peakStrain > 0.01f)
            sb.AppendLine(Loc.Format("detail.note.strainOk", lang,
                Mathf.RoundToInt(s.peakStrain * 100f),
                Mathf.RoundToInt(s.meanStrain * 100f)));

        // DTW hareket kalitesi (0..100)
        bool hasDtwR = s.movementScoreRight >= 0f;
        bool hasDtwL = s.movementScoreLeft >= 0f;
        if (hasDtwR || hasDtwL)
        {
            if (hasDtwR && hasDtwL)
                sb.AppendLine(Loc.Format("detail.note.dtwBoth", lang,
                    Mathf.RoundToInt(s.movementScoreRight),
                    Mathf.RoundToInt(s.movementScoreLeft)));
            else if (hasDtwR)
                sb.AppendLine(Loc.Format("detail.note.dtwRight", lang,
                    Mathf.RoundToInt(s.movementScoreRight)));
            else
                sb.AppendLine(Loc.Format("detail.note.dtwLeft", lang,
                    Mathf.RoundToInt(s.movementScoreLeft)));
        }

        if (s.qualityScoreMean >= 0f)
        {
            SessionQualityBand band = SessionQualityScorer.FromStoredBand(s.qualityBand);
            string bandLabel;
            switch (band)
            {
                case SessionQualityBand.Reliable:
                    bandLabel = Loc.T("report.quality.reliable", lang);
                    break;
                case SessionQualityBand.Caution:
                    bandLabel = Loc.T("report.quality.caution", lang);
                    break;
                case SessionQualityBand.Invalid:
                    bandLabel = Loc.T("report.quality.invalid", lang);
                    break;
                default:
                    bandLabel = Loc.T("report.quality.unknown", lang);
                    break;
            }
            sb.AppendLine(Loc.Format("detail.note.quality", lang, bandLabel,
                Mathf.RoundToInt(s.qualityScoreMean * 100f)));
        }

        int assisted = ProgressStatsAggregator.TotalAssistedReps(s);
        if (assisted > 0)
        {
            sb.AppendLine(Loc.Format("detail.note.assisted", lang, assisted,
                ProgressStatsAggregator.TotalIndependentReps(s)));
        }

        if (s.trackingJumpEvents > 0)
            sb.AppendLine(Loc.Format("detail.note.trackingJump", lang, s.trackingJumpEvents));

        if (s.secondPersonEvents > 0)
            sb.AppendLine(Loc.Format("detail.note.secondPerson", lang, s.secondPersonEvents));

        if (s.assistNearEvents > 0)
            sb.AppendLine(Loc.Format("detail.note.assistNear", lang, s.assistNearEvents));

        // Önceki seans karşılaştırması
        if (previous != null)
        {
            float prevMax = SessionHistoryFilter.EffectiveMax(previous);
            if (prevMax > 1f && maxRom > 1f)
            {
                float d = maxRom - prevMax;
                if (d >= 3f)
                    sb.AppendLine(Loc.Format("detail.note.romUp", lang, Mathf.RoundToInt(d)));
                else if (d <= -3f)
                    sb.AppendLine(Loc.Format("detail.note.romDown", lang, Mathf.RoundToInt(-d)));
                else
                    sb.AppendLine(Loc.T("detail.note.romFlat", lang));
            }

            if (previous.peakStrain >= 0f && s.peakStrain > 0.01f)
            {
                float ds = (s.peakStrain - previous.peakStrain) * 100f;
                if (ds > 5f)
                    sb.AppendLine(Loc.Format("detail.note.strainUp", lang, Mathf.RoundToInt(ds)));
                else if (ds < -5f)
                    sb.AppendLine(Loc.Format("detail.note.strainDown", lang, Mathf.RoundToInt(-ds)));
            }
        }

        sb.AppendLine();
        sb.Append(Loc.T("detail.summary.disclaimer", lang));
        return sb.ToString();
    }
}
