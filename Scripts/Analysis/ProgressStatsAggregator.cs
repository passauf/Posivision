using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// İlerleme CSV/HTML için deterministik istatistik katmanı (PS-1.2).
/// SaMD Class B karar-destek; teşhis değildir. KVKK: yalnızca yerel hesap.
/// </summary>
public struct ProgressStats
{
    public int sessionCount;
    public float firstMaxRom;
    public float lastMaxRom;
    /// <summary>İlk→son EffectiveMax yüzde değişim.</summary>
    public float romTrendPct;
    /// <summary>İlk→son derece farkı (last − first).</summary>
    public float romTrendDegrees;
    /// <summary>Sağ maks ilk→son derece (ölçülen seanslar).</summary>
    public float rightRomTrendDegrees;
    /// <summary>Sol maks ilk→son derece (ölçülen seanslar).</summary>
    public float leftRomTrendDegrees;
    /// <summary>0..100 ortalama tamamlanma; −1 = hesaplanamadı.</summary>
    public float meanCompletionPct;
    /// <summary>Geçersiz / (bağımsız+yardımlı+geçersiz) × 100; −1 = yok.</summary>
    public float invalidRepRatePct;
    /// <summary>Yardımlı / (bağımsız+yardımlı) × 100; −1 = yok.</summary>
    public float assistedRepRatePct;
    public int totalIndependentReps;
    public int totalAssistedReps;
    public int totalInvalidReps;
    public int totalCompensationEvents;
    public int totalTrackingJumps;
    public int totalSecondPersonEvents;
    public int totalAssistNearEvents;
    /// <summary>Kompansasyonlu seans / toplam × 100.</summary>
    public float compensationSessionRatePct;
    /// <summary>Gözlenen seans/hafta (tarih aralığına göre).</summary>
    public float sessionsPerWeekObserved;
    /// <summary>Planlanan seans/hafta; 0 = plan yok.</summary>
    public int plannedSessionsPerWeek;
    /// <summary>observed/planned × 100; −1 = plan yok.</summary>
    public float adherencePct;
    /// <summary>Kalite ağırlıklı ortalama EffectiveMax; kalite yoksa düz ortalama.</summary>
    public float qualityWeightedMeanRom;
    /// <summary>Kaliteli seansların düz ortalaması (karşılaştırma).</summary>
    public float unweightedMeanRom;
    /// <summary>qualityScoreMean ≥ 0 olan seansların ortalaması; −1 = yok.</summary>
    public float meanQualityScore;
    /// <summary>peakStrain ortalaması (0..1); −1 = yok.</summary>
    public float meanPeakStrain;
    public int sessionsWithQuality;
    public int sessionsWithCompensation;
    public float spanDays;
    public bool hasStats;
}

public static class ProgressStatsAggregator
{
    public const string FormulaVersion = "PS-1.2";

    /// <summary>
    /// completionRate: PhysioAnalyzer bazen 0–100 yazar. 1.5 üstü → yüzde kabul edilir.
    /// PS-1.1+: tamamlanma bağımsız (independent) tekrar üzerinden — yardımlı hariç.
    /// </summary>
    public static float CompletionAsPercent(SessionEntry s)
    {
        if (s == null || s.targetReps <= 0) return -1f;

        int independent = TotalIndependentReps(s);
        float fromReps = 100f * independent / Mathf.Max(1, s.targetReps);

        // Eski kayıtlarda assisted=0 → klasik davranışa yakın
        if (TotalAssistedReps(s) <= 0)
        {
            float stored = s.completionRate;
            if (stored > 1.5f)
                return Mathf.Clamp(stored, 0f, 200f);
            if (stored > 0f)
                return Mathf.Clamp(stored * 100f, 0f, 200f);
        }

        return Mathf.Clamp(fromReps, 0f, 200f);
    }

    public static int TotalValidReps(SessionEntry s)
    {
        if (s == null) return 0;
        int r = s.rightCompletedReps + s.leftCompletedReps;
        if (r > 0) return r;
        return Mathf.Max(0, s.completedReps);
    }

    public static int TotalAssistedReps(SessionEntry s)
    {
        if (s == null) return 0;
        int r = s.rightAssistedReps + s.leftAssistedReps;
        if (r > 0) return r;
        return Mathf.Max(0, s.assistedReps);
    }

    public static int TotalIndependentReps(SessionEntry s)
    {
        return Mathf.Max(0, TotalValidReps(s) - TotalAssistedReps(s));
    }

    public static int TotalInvalidReps(SessionEntry s)
    {
        if (s == null) return 0;
        int r = s.rightInvalidReps + s.leftInvalidReps;
        if (r > 0) return r;
        return Mathf.Max(0, s.invalidReps);
    }

    public static int IndependentRepsRight(SessionEntry s)
    {
        if (s == null) return 0;
        return Mathf.Max(0, s.rightCompletedReps - s.rightAssistedReps);
    }

    public static int IndependentRepsLeft(SessionEntry s)
    {
        if (s == null) return 0;
        return Mathf.Max(0, s.leftCompletedReps - s.leftAssistedReps);
    }

    /// <summary>Geçersiz / (bağımsız+yardımlı+geçersiz) × 100; −1 = tekrar yok.</summary>
    public static float SessionInvalidRepRatePct(SessionEntry s)
    {
        if (s == null) return -1f;
        int valid = TotalValidReps(s);
        int invalid = TotalInvalidReps(s);
        int denom = valid + invalid;
        if (denom <= 0) return -1f;
        return 100f * invalid / denom;
    }

    /// <summary>Yardımlı / başarılı tekrar × 100; −1 = başarılı yok.</summary>
    public static float SessionAssistedRepRatePct(SessionEntry s)
    {
        if (s == null) return -1f;
        int valid = TotalValidReps(s);
        if (valid <= 0) return -1f;
        return 100f * TotalAssistedReps(s) / valid;
    }

    /// <summary>EffectiveMax farkı (curr − prev); prev yoksa NaN.</summary>
    public static float DeltaMaxRomDegrees(SessionEntry current, SessionEntry previous)
    {
        if (current == null || previous == null) return float.NaN;
        float a = SessionHistoryFilter.EffectiveMax(current);
        float b = SessionHistoryFilter.EffectiveMax(previous);
        if (a < 1f || b < 1f) return float.NaN;
        return a - b;
    }

    public static ProgressStats Compute(PatientHistory history, int plannedSessionsPerWeek = 0)
    {
        return Compute(history != null ? history.sessions : null, plannedSessionsPerWeek);
    }

    public static ProgressStats Compute(List<SessionEntry> sessions, int plannedSessionsPerWeek = 0)
    {
        ProgressStats st = default;
        st.plannedSessionsPerWeek = Mathf.Max(0, plannedSessionsPerWeek);
        st.meanCompletionPct = -1f;
        st.invalidRepRatePct = -1f;
        st.assistedRepRatePct = -1f;
        st.adherencePct = -1f;
        st.meanQualityScore = -1f;
        st.meanPeakStrain = -1f;
        st.rightRomTrendDegrees = float.NaN;
        st.leftRomTrendDegrees = float.NaN;

        if (sessions == null || sessions.Count == 0) return st;

        st.hasStats = true;
        st.sessionCount = sessions.Count;

        ProgressSummary prog = SessionHistoryFilter.ComputeProgress(sessions);
        st.firstMaxRom = prog.firstMax;
        st.lastMaxRom = prog.lastMax;
        st.romTrendPct = prog.combinedPct;
        st.romTrendDegrees = prog.lastMax - prog.firstMax;

        float completionSum = 0f;
        int completionN = 0;
        int independentReps = 0;
        int assistedReps = 0;
        int invalidReps = 0;
        float qwSum = 0f;
        float qwWeight = 0f;
        float uwSum = 0f;
        int uwN = 0;
        float qSum = 0f;
        int qN = 0;
        float strainSum = 0f;
        int strainN = 0;
        int compEvents = 0;
        int jumpEvents = 0;
        int secondPerson = 0;
        int assistNear = 0;

        float firstRight = -1f, lastRight = -1f;
        float firstLeft = -1f, lastLeft = -1f;

        DateTime? firstDt = null;
        DateTime? lastDt = null;

        for (int i = 0; i < sessions.Count; i++)
        {
            SessionEntry s = sessions[i];
            if (s == null) continue;

            float c = CompletionAsPercent(s);
            if (c >= 0f)
            {
                completionSum += c;
                completionN++;
            }

            independentReps += TotalIndependentReps(s);
            assistedReps += TotalAssistedReps(s);
            invalidReps += TotalInvalidReps(s);
            compEvents += Mathf.Max(0, s.compensationEvents);
            jumpEvents += Mathf.Max(0, s.trackingJumpEvents);
            secondPerson += Mathf.Max(0, s.secondPersonEvents);
            assistNear += Mathf.Max(0, s.assistNearEvents);

            if (s.compensationEvents > 0) st.sessionsWithCompensation++;

            if (s.peakStrain > 0f || s.meanStrain > 0f)
            {
                strainSum += Mathf.Clamp01(s.peakStrain);
                strainN++;
            }

            float maxRom = SessionHistoryFilter.EffectiveMax(s);
            if (maxRom > 1f)
            {
                uwSum += maxRom;
                uwN++;
                if (s.qualityScoreMean >= 0f)
                {
                    float w = Mathf.Clamp01(s.qualityScoreMean);
                    qwSum += maxRom * w;
                    qwWeight += w;
                    qSum += s.qualityScoreMean;
                    qN++;
                }
            }
            else if (s.qualityScoreMean >= 0f)
            {
                qSum += s.qualityScoreMean;
                qN++;
            }

            if (SessionHistoryFilter.ShowRight(s) && s.rightMaxROM > 1f)
            {
                if (firstRight < 0f) firstRight = s.rightMaxROM;
                lastRight = s.rightMaxROM;
            }
            if (SessionHistoryFilter.ShowLeft(s) && s.leftMaxROM > 1f)
            {
                if (firstLeft < 0f) firstLeft = s.leftMaxROM;
                lastLeft = s.leftMaxROM;
            }

            if (SessionHistoryFilter.TryParseSessionDate(s.dateTime, out DateTime dt))
            {
                if (!firstDt.HasValue || dt < firstDt.Value) firstDt = dt;
                if (!lastDt.HasValue || dt > lastDt.Value) lastDt = dt;
            }
        }

        if (completionN > 0)
            st.meanCompletionPct = completionSum / completionN;

        st.totalIndependentReps = independentReps;
        st.totalAssistedReps = assistedReps;
        st.totalInvalidReps = invalidReps;
        st.totalCompensationEvents = compEvents;
        st.totalTrackingJumps = jumpEvents;
        st.totalSecondPersonEvents = secondPerson;
        st.totalAssistNearEvents = assistNear;

        int repTotal = independentReps + assistedReps + invalidReps;
        if (repTotal > 0)
            st.invalidRepRatePct = 100f * invalidReps / repTotal;

        int successReps = independentReps + assistedReps;
        if (successReps > 0)
            st.assistedRepRatePct = 100f * assistedReps / successReps;

        if (st.sessionCount > 0)
            st.compensationSessionRatePct = 100f * st.sessionsWithCompensation / st.sessionCount;

        if (firstRight >= 0f && lastRight >= 0f)
            st.rightRomTrendDegrees = lastRight - firstRight;
        if (firstLeft >= 0f && lastLeft >= 0f)
            st.leftRomTrendDegrees = lastLeft - firstLeft;

        st.sessionsWithQuality = qN;
        if (qN > 0)
            st.meanQualityScore = qSum / qN;

        if (strainN > 0)
            st.meanPeakStrain = strainSum / strainN;

        if (uwN > 0)
            st.unweightedMeanRom = uwSum / uwN;

        if (qwWeight > 1e-5f)
            st.qualityWeightedMeanRom = qwSum / qwWeight;
        else
            st.qualityWeightedMeanRom = st.unweightedMeanRom;

        if (firstDt.HasValue && lastDt.HasValue)
        {
            st.spanDays = Mathf.Max(0f, (float)(lastDt.Value.Date - firstDt.Value.Date).TotalDays);
            float weeks = st.spanDays < 1f ? 1f : Mathf.Max(1f, st.spanDays / 7f);
            st.sessionsPerWeekObserved = st.sessionCount / weeks;

            if (st.plannedSessionsPerWeek > 0)
            {
                st.adherencePct = 100f * st.sessionsPerWeekObserved / st.plannedSessionsPerWeek;
                if (st.adherencePct > 300f) st.adherencePct = 300f;
            }
        }
        else
        {
            st.sessionsPerWeekObserved = -1f;
        }

        return st;
    }

    public static void AppendCsvSummary(StringBuilder csv, ProgressStats st, CultureInfo inv)
    {
        if (csv == null) return;
        if (inv == null) inv = CultureInfo.InvariantCulture;

        csv.Append("# ProgressStats ").Append(FormulaVersion).Append('\n');
        csv.Append("Metric,Value,Unit\n");
        AppendKv(csv, "FormulaVersion", FormulaVersion, "");
        AppendKv(csv, "SessionCount", st.sessionCount.ToString(inv), "sessions");
        AppendKv(csv, "FirstMaxRom", F1(st.firstMaxRom, inv), "deg");
        AppendKv(csv, "LastMaxRom", F1(st.lastMaxRom, inv), "deg");
        AppendKv(csv, "RomTrendDegrees", F1(st.romTrendDegrees, inv), "deg");
        AppendKv(csv, "RomTrendPct", F1(st.romTrendPct, inv), "pct");
        AppendKv(csv, "RightRomTrendDegrees",
            float.IsNaN(st.rightRomTrendDegrees) ? "" : F1(st.rightRomTrendDegrees, inv), "deg");
        AppendKv(csv, "LeftRomTrendDegrees",
            float.IsNaN(st.leftRomTrendDegrees) ? "" : F1(st.leftRomTrendDegrees, inv), "deg");
        AppendKv(csv, "MeanCompletionPct", st.meanCompletionPct < 0f ? "" : F1(st.meanCompletionPct, inv), "pct");
        AppendKv(csv, "InvalidRepRatePct", st.invalidRepRatePct < 0f ? "" : F1(st.invalidRepRatePct, inv), "pct");
        AppendKv(csv, "AssistedRepRatePct", st.assistedRepRatePct < 0f ? "" : F1(st.assistedRepRatePct, inv), "pct");
        AppendKv(csv, "TotalIndependentReps", st.totalIndependentReps.ToString(inv), "reps");
        AppendKv(csv, "TotalAssistedReps", st.totalAssistedReps.ToString(inv), "reps");
        AppendKv(csv, "TotalInvalidReps", st.totalInvalidReps.ToString(inv), "reps");
        AppendKv(csv, "Note", "MeanCompletionPct uses independent reps only (PS-1.2)", "");
        AppendKv(csv, "SpanDays", F1(st.spanDays, inv), "days");
        AppendKv(csv, "SessionsPerWeekObserved",
            st.sessionsPerWeekObserved < 0f ? "" : F2(st.sessionsPerWeekObserved, inv), "per_week");
        AppendKv(csv, "PlannedSessionsPerWeek",
            st.plannedSessionsPerWeek > 0 ? st.plannedSessionsPerWeek.ToString(inv) : "", "per_week");
        AppendKv(csv, "AdherencePct", st.adherencePct < 0f ? "" : F1(st.adherencePct, inv), "pct");
        AppendKv(csv, "UnweightedMeanRom", F1(st.unweightedMeanRom, inv), "deg");
        AppendKv(csv, "QualityWeightedMeanRom", F1(st.qualityWeightedMeanRom, inv), "deg");
        AppendKv(csv, "MeanQualityScore",
            st.meanQualityScore < 0f ? "" : F3(st.meanQualityScore, inv), "0_1");
        AppendKv(csv, "MeanPeakStrain",
            st.meanPeakStrain < 0f ? "" : F3(st.meanPeakStrain, inv), "0_1");
        AppendKv(csv, "SessionsWithQuality", st.sessionsWithQuality.ToString(inv), "sessions");
        AppendKv(csv, "SessionsWithCompensation", st.sessionsWithCompensation.ToString(inv), "sessions");
        AppendKv(csv, "CompensationSessionRatePct", F1(st.compensationSessionRatePct, inv), "pct");
        AppendKv(csv, "TotalCompensationEvents", st.totalCompensationEvents.ToString(inv), "events");
        AppendKv(csv, "TotalTrackingJumps", st.totalTrackingJumps.ToString(inv), "events");
        AppendKv(csv, "TotalSecondPersonEvents", st.totalSecondPersonEvents.ToString(inv), "events");
        AppendKv(csv, "TotalAssistNearEvents", st.totalAssistNearEvents.ToString(inv), "events");
        csv.Append('\n');
        csv.Append("# Session rows follow. completionRate in rows may be 0-100 (legacy).\n");
        csv.Append("# Derived: TamamlanmaPct=independent/target; GecersizOranPct; YardimliOranPct; DeltaMaksROM.\n");
        csv.Append("# QualityWeightedMeanRom uses qualityScoreMean as weight when >= 0; else unweighted.\n");
        csv.Append("# AdherencePct = SessionsPerWeekObserved / PlannedSessionsPerWeek * 100 when plan > 0.\n");
        csv.Append('\n');
    }

    private static void AppendKv(StringBuilder csv, string key, string value, string unit)
    {
        csv.Append(key).Append(',').Append(value).Append(',').Append(unit).Append('\n');
    }

    private static string F1(float v, CultureInfo inv) => v.ToString("F1", inv);
    private static string F2(float v, CultureInfo inv) => v.ToString("F2", inv);
    private static string F3(float v, CultureInfo inv) => v.ToString("F3", inv);
}
