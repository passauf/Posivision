using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Klinisyen tarafından seçilen iki seansın anlık HTML karşılaştırma raporu.
/// TR/EN anlık geçiş (ReportHtmlLang). Seans bitişinde otomatik üretilmez.
/// Dosya yolu: Reports/Compare/{Hasta}/. İyileşme yeşil, kötüleşme kırmızı.
/// SaMD Class B; KVKK: yalnızca yerel dosya.
/// </summary>
public static class SessionCompareHtml
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const float RomImproveEps = 0.5f;
    private const float StrainImproveEps = 0.01f;
    private const float ScoreImproveEps = 0.5f;
    private const float SeriesYFloorDegrees = 30f;
    private const int SeriesGridLines = 8;

    public static string BuildHtml(
        SessionEntry a, SessionEntry b, PatientProfile profile,
        int sessionNumberA = 0, int sessionNumberB = 0)
    {
        if (a == null || b == null) return null;

        OrderByDate(a, b, sessionNumberA, sessionNumberB,
            out SessionEntry earlier, out SessionEntry later,
            out string earlierDate, out string laterDate,
            out int earlierNo, out int laterNo);

        string displayName = profile != null ? profile.DisplayName : "";
        if (string.IsNullOrEmpty(displayName))
            displayName = Loc.T("report.patient", LanguageSettings.IsEnglish ? AppLanguage.English : AppLanguage.Turkish);

        string earlierTr = SessionLabel(earlierNo, earlierDate, AppLanguage.Turkish);
        string earlierEn = SessionLabel(earlierNo, earlierDate, AppLanguage.English);
        string laterTr = SessionLabel(laterNo, laterDate, AppLanguage.Turkish);
        string laterEn = SessionLabel(laterNo, laterDate, AppLanguage.English);

        var sb = new StringBuilder(98304);
        string lang = ReportHtmlLang.InitialLangCode;
        sb.Append("<!DOCTYPE html><html lang=\"").Append(lang).Append("\"><head><meta charset=\"UTF-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append(ReportHtmlLang.EscapeHtml(Loc.T("compare.report.title")))
          .Append(" — ").Append(ReportHtmlLang.EscapeHtml(displayName)).Append("</title>");
        AppendStyles(sb);
        sb.Append("</head><body>");
        ReportHtmlLang.AppendToggleButton(sb);

        ReportHtmlLang.AppendLocText(sb, "h1", "compare.report.title");
        sb.Append("<p class=\"muted\">").Append(ReportHtmlLang.EscapeHtml(displayName)).Append(" · ")
          .Append(System.DateTime.Now.ToString("dd/MM/yyyy HH:mm")).Append("</p>");

        sb.Append("<p class=\"legend\">");
        AppendPill(sb, "up", "compare.legend.better");
        sb.Append(' ');
        AppendPill(sb, "down", "compare.legend.worse");
        sb.Append(' ');
        AppendPill(sb, "flat", "compare.legend.same");
        sb.Append("</p>");

        sb.Append("<div class=\"cols\">");
        AppendSessionCard(sb, earlier, earlierTr, earlierEn, "earlier");
        AppendSessionCard(sb, later, laterTr, laterEn, "later");
        sb.Append("</div>");

        ReportHtmlLang.AppendLocText(sb, "h2", "compare.section.metrics");
        sb.Append("<table class=\"cmp\"><thead><tr><th>");
        ReportHtmlLang.AppendLocText(sb, "span", "compare.col.metric");
        sb.Append("</th><th>");
        ReportHtmlLang.AppendBilingualText(sb, "span", earlierTr, earlierEn);
        sb.Append("</th><th>");
        ReportHtmlLang.AppendBilingualText(sb, "span", laterTr, laterEn);
        sb.Append("</th><th>");
        ReportHtmlLang.AppendLocText(sb, "span", "compare.col.delta");
        sb.Append("</th></tr></thead><tbody>");

        AppendMetricRow(sb, "compare.m.romR",
            SessionHistoryFilter.EffectiveRightMax(earlier),
            SessionHistoryFilter.EffectiveRightMax(later), "°", higherIsBetter: true);
        AppendMetricRow(sb, "compare.m.romL",
            SessionHistoryFilter.EffectiveLeftMax(earlier),
            SessionHistoryFilter.EffectiveLeftMax(later), "°", higherIsBetter: true);
        AppendMetricRow(sb, "compare.m.romAvg",
            earlier.averageROM, later.averageROM, "°", higherIsBetter: true);
        AppendMetricRow(sb, "compare.m.repsR",
            EffectiveRepsRight(earlier), EffectiveRepsRight(later), "", higherIsBetter: true, decimals: 0);
        AppendMetricRow(sb, "compare.m.repsL",
            EffectiveRepsLeft(earlier), EffectiveRepsLeft(later), "", higherIsBetter: true, decimals: 0);
        AppendMetricRow(sb, "compare.m.invalid",
            earlier.invalidReps + earlier.rightInvalidReps + earlier.leftInvalidReps,
            later.invalidReps + later.rightInvalidReps + later.leftInvalidReps,
            "", higherIsBetter: false, decimals: 0);
        AppendMetricRow(sb, "compare.m.assisted",
            earlier.assistedReps + earlier.rightAssistedReps + earlier.leftAssistedReps,
            later.assistedReps + later.rightAssistedReps + later.leftAssistedReps,
            "", higherIsBetter: false, decimals: 0);
        AppendMetricRow(sb, "compare.m.completion",
            earlier.completionRate, later.completionRate, "%", higherIsBetter: true);
        AppendMetricRow(sb, "compare.m.comp",
            earlier.compensationEvents, later.compensationEvents, "", higherIsBetter: false, decimals: 0);
        AppendMetricRow(sb, "compare.m.peakStrain",
            earlier.peakStrain * 100f, later.peakStrain * 100f, "%", higherIsBetter: false);
        AppendMetricRow(sb, "compare.m.meanStrain",
            earlier.meanStrain * 100f, later.meanStrain * 100f, "%", higherIsBetter: false);
        AppendMetricRow(sb, "compare.m.dtwR",
            earlier.movementScoreRight, later.movementScoreRight, "", higherIsBetter: true, scoreField: true);
        AppendMetricRow(sb, "compare.m.dtwL",
            earlier.movementScoreLeft, later.movementScoreLeft, "", higherIsBetter: true, scoreField: true);
        AppendMetricRow(sb, "compare.m.quality",
            earlier.qualityScoreMean, later.qualityScoreMean, "", higherIsBetter: true, scoreField: true);
        AppendMetricRow(sb, "compare.m.duration",
            earlier.durationSeconds, later.durationSeconds, "s", higherIsBetter: null, decimals: 0);
        AppendMetricRow(sb, "compare.m.trackJump",
            earlier.trackingJumpEvents, later.trackingJumpEvents, "", higherIsBetter: false, decimals: 0);
        AppendMetricRow(sb, "compare.m.secondPerson",
            earlier.secondPersonEvents, later.secondPersonEvents, "", higherIsBetter: false, decimals: 0);
        AppendMetricRow(sb, "compare.m.assistNear",
            earlier.assistNearEvents, later.assistNearEvents, "", higherIsBetter: false, decimals: 0);
        sb.Append("</tbody></table>");

        ReportHtmlLang.AppendLocText(sb, "h2", "compare.section.bars");
        AppendBarChart(sb, earlier, later, earlierTr, earlierEn, laterTr, laterEn);

        ReportHtmlLang.AppendLocText(sb, "h2", "compare.section.series");
        AppendSeriesSection(sb, earlier, later, earlierTr, earlierEn, laterTr, laterEn);

        ReportHtmlLang.AppendLocText(sb, "p", "compare.disclaimer", "class=\"muted disclaimer\"");
        ReportHtmlLang.AppendToggleScript(sb, lang, "");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string SessionLabel(int sessionNo, string date, AppLanguage lang)
    {
        string head = sessionNo > 0
            ? Loc.Format("menu.hist.sessionN", lang, sessionNo)
            : Loc.T("compare.label.session", lang);
        return head + " · " + date;
    }

    private static void AppendPill(StringBuilder sb, string cls, string locKey)
    {
        sb.Append("<span class=\"pill ").Append(cls).Append('"');
        ReportHtmlLang.AppendBilingualAttrPair(sb,
            Loc.T(locKey, AppLanguage.Turkish),
            Loc.T(locKey, AppLanguage.English));
        sb.Append('>')
          .Append(ReportHtmlLang.EscapeHtml(Loc.T(locKey)))
          .Append("</span>");
    }

    private static void OrderByDate(
        SessionEntry a, SessionEntry b,
        int sessionNumberA, int sessionNumberB,
        out SessionEntry earlier, out SessionEntry later,
        out string earlierDate, out string laterDate,
        out int earlierNo, out int laterNo)
    {
        bool aOk = SessionHistoryFilter.TryParseSessionDate(a.dateTime, out System.DateTime da);
        bool bOk = SessionHistoryFilter.TryParseSessionDate(b.dateTime, out System.DateTime db);
        bool aFirst = !aOk || !bOk || da <= db;
        earlier = aFirst ? a : b;
        later = aFirst ? b : a;
        earlierNo = aFirst ? sessionNumberA : sessionNumberB;
        laterNo = aFirst ? sessionNumberB : sessionNumberA;
        earlierDate = SafeDate(earlier.dateTime);
        laterDate = SafeDate(later.dateTime);
    }

    private static string SafeDate(string raw)
    {
        if (SessionHistoryFilter.TryParseSessionDate(raw, out System.DateTime dt))
            return dt.ToString("dd/MM/yyyy HH:mm");
        return string.IsNullOrEmpty(raw) ? "—" : raw;
    }

    private static int EffectiveRepsRight(SessionEntry s)
    {
        if (s == null) return 0;
        if (s.rightCompletedReps > 0 || s.rightArmEnabled) return Mathf.Max(0, s.rightCompletedReps);
        if (!s.leftArmEnabled && s.leftCompletedReps == 0 && s.completedReps > 0)
            return s.completedReps;
        return Mathf.Max(0, s.rightCompletedReps);
    }

    private static int EffectiveRepsLeft(SessionEntry s)
    {
        if (s == null) return 0;
        if (s.leftCompletedReps > 0 || s.leftArmEnabled) return Mathf.Max(0, s.leftCompletedReps);
        if (!s.rightArmEnabled && s.rightCompletedReps == 0 && s.completedReps > 0)
            return s.completedReps;
        return Mathf.Max(0, s.leftCompletedReps);
    }

    private static int TargetRepsPerArm(SessionEntry s)
    {
        if (s == null) return 0;
        return Mathf.Max(0, s.targetReps);
    }

    private static void AppendSessionCard(
        StringBuilder sb, SessionEntry s, string labelTr, string labelEn, string css)
    {
        float r = SessionHistoryFilter.EffectiveRightMax(s);
        float l = SessionHistoryFilter.EffectiveLeftMax(s);
        int repsR = EffectiveRepsRight(s);
        int repsL = EffectiveRepsLeft(s);
        int target = TargetRepsPerArm(s);
        int strainPct = Mathf.RoundToInt(s.peakStrain * 100f);

        sb.Append("<div class=\"card ").Append(css).Append("\"><h3>");
        ReportHtmlLang.AppendBilingualText(sb, "span", labelTr, labelEn);
        sb.Append("</h3>");

        AppendFormattedP(sb, "compare.card.rom", r.ToString("F0"), l.ToString("F0"));
        AppendFormattedP(sb, "compare.card.repsR", repsR, target);
        AppendFormattedP(sb, "compare.card.repsL", repsL, target);
        AppendFormattedP(sb, "compare.card.comp", s.compensationEvents);
        AppendFormattedP(sb, "compare.card.strain", strainPct);
        sb.Append("</div>");
    }

    private static void AppendFormattedP(StringBuilder sb, string locKey, params object[] args)
    {
        string tr = Loc.Format(locKey, AppLanguage.Turkish, args);
        string en = Loc.Format(locKey, AppLanguage.English, args);
        ReportHtmlLang.AppendBilingualText(sb, "p", tr, en);
    }

    private static void AppendMetricRow(
        StringBuilder sb, string locKey, float va, float vb, string unit,
        bool? higherIsBetter, int decimals = 1, bool scoreField = false)
    {
        bool missingA = scoreField && va < 0f;
        bool missingB = scoreField && vb < 0f;
        string sa = missingA ? "—" : va.ToString("F" + decimals, Inv) + unit;
        string sbv = missingB ? "—" : vb.ToString("F" + decimals, Inv) + unit;

        string cls = "flat";
        string deltaTxt = "—";
        if (!missingA && !missingB && higherIsBetter.HasValue)
        {
            float d = vb - va;
            float eps = scoreField ? ScoreImproveEps
                : (unit == "%" ? StrainImproveEps * 100f : RomImproveEps);
            if (Mathf.Abs(d) <= eps)
            {
                cls = "flat";
                deltaTxt = "0";
            }
            else
            {
                bool better = higherIsBetter.Value ? d > 0f : d < 0f;
                cls = better ? "up" : "down";
                deltaTxt = (d > 0f ? "+" : "") + d.ToString("F" + decimals, Inv) + unit;
            }
        }
        else if (!missingA && !missingB && !higherIsBetter.HasValue)
        {
            float d = vb - va;
            deltaTxt = (d > 0f ? "+" : "") + d.ToString("F" + decimals, Inv) + unit;
            cls = "flat";
        }

        sb.Append("<tr class=\"").Append(cls).Append("\"><td>");
        ReportHtmlLang.AppendLocText(sb, "span", locKey);
        sb.Append("</td><td>").Append(ReportHtmlLang.EscapeHtml(sa))
          .Append("</td><td>").Append(ReportHtmlLang.EscapeHtml(sbv))
          .Append("</td><td class=\"delta\"><span class=\"pill ").Append(cls).Append("\">")
          .Append(ReportHtmlLang.EscapeHtml(deltaTxt)).Append("</span></td></tr>");
    }

    private static void AppendBarChart(
        StringBuilder sb, SessionEntry earlier, SessionEntry later,
        string earlierTr, string earlierEn, string laterTr, string laterEn)
    {
        float[] ea =
        {
            SessionHistoryFilter.EffectiveRightMax(earlier),
            SessionHistoryFilter.EffectiveLeftMax(earlier),
            EffectiveRepsRight(earlier),
            EffectiveRepsLeft(earlier),
            earlier.compensationEvents,
            earlier.peakStrain * 100f
        };
        float[] eb =
        {
            SessionHistoryFilter.EffectiveRightMax(later),
            SessionHistoryFilter.EffectiveLeftMax(later),
            EffectiveRepsRight(later),
            EffectiveRepsLeft(later),
            later.compensationEvents,
            later.peakStrain * 100f
        };
        string[] labelKeys =
        {
            "compare.m.romR",
            "compare.m.romL",
            "compare.m.repsR",
            "compare.m.repsL",
            "compare.m.comp",
            "compare.m.peakStrain"
        };
        bool[] higherBetter = { true, true, true, true, false, false };

        const int w = 960, h = 340, padL = 56, padR = 28, padT = 36, padB = 78;
        float maxV = 1f;
        for (int i = 0; i < ea.Length; i++)
        {
            maxV = Mathf.Max(maxV, ea[i]);
            maxV = Mathf.Max(maxV, eb[i]);
        }
        if (maxV < 10f) maxV = 10f;

        sb.Append("<svg class=\"chart\" viewBox=\"0 0 ").Append(w).Append(' ').Append(h)
          .Append("\" width=\"100%\" role=\"img\">");
        sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(w).Append("\" height=\"").Append(h)
          .Append("\" fill=\"#0f172a\" rx=\"12\"/>");

        float groupW = (w - padL - padR) / (float)ea.Length;
        float barW = groupW * 0.30f;

        for (int i = 0; i < ea.Length; i++)
        {
            float gx = padL + i * groupW;
            float ha = (ea[i] / maxV) * (h - padT - padB);
            float hb = (eb[i] / maxV) * (h - padT - padB);
            float ya = h - padB - ha;
            float yb = h - padB - hb;
            float d = eb[i] - ea[i];
            bool better = higherBetter[i] ? d > RomImproveEps : d < -RomImproveEps;
            bool worse = higherBetter[i] ? d < -RomImproveEps : d > RomImproveEps;
            string colorB = better ? "#16a34a" : (worse ? "#dc2626" : "#64748b");

            sb.Append("<rect x=\"").Append((gx + groupW * 0.12f).ToString("F1", Inv))
              .Append("\" y=\"").Append(ya.ToString("F1", Inv))
              .Append("\" width=\"").Append(barW.ToString("F1", Inv))
              .Append("\" height=\"").Append(Mathf.Max(1f, ha).ToString("F1", Inv))
              .Append("\" fill=\"#38bdf8\" rx=\"3\"/>");
            sb.Append("<rect x=\"").Append((gx + groupW * 0.12f + barW + 6f).ToString("F1", Inv))
              .Append("\" y=\"").Append(yb.ToString("F1", Inv))
              .Append("\" width=\"").Append(barW.ToString("F1", Inv))
              .Append("\" height=\"").Append(Mathf.Max(1f, hb).ToString("F1", Inv))
              .Append("\" fill=\"").Append(colorB).Append("\" rx=\"3\"/>");

            AppendSvgText(sb, (gx + groupW * 0.5f).ToString("F1", Inv), (h - 36).ToString(Inv),
                "#94a3b8", "11", "middle",
                Loc.T(labelKeys[i], AppLanguage.Turkish),
                Loc.T(labelKeys[i], AppLanguage.English));
            AppendSvgText(sb, (gx + groupW * 0.5f).ToString("F1", Inv), (h - 18).ToString(Inv),
                "#64748b", "10", "middle",
                ea[i].ToString("F0", Inv) + "→" + eb[i].ToString("F0", Inv),
                ea[i].ToString("F0", Inv) + "→" + eb[i].ToString("F0", Inv));
        }

        string coloredTr = Loc.T("compare.chart.colored", AppLanguage.Turkish);
        string coloredEn = Loc.T("compare.chart.colored", AppLanguage.English);
        AppendSvgText(sb, padL.ToString(Inv), "22", "#38bdf8", "12", "start", earlierTr, earlierEn);
        AppendSvgText(sb, (padL + 260).ToString(Inv), "22", "#94a3b8", "12", "start",
            laterTr + " (" + coloredTr + ")",
            laterEn + " (" + coloredEn + ")");
        sb.Append("</svg>");
    }

    private static void AppendSeriesSection(
        StringBuilder sb, SessionEntry earlier, SessionEntry later,
        string earlierTr, string earlierEn, string laterTr, string laterEn)
    {
        bool hasA = HasSeries(earlier);
        bool hasB = HasSeries(later);
        if (!hasA && !hasB)
        {
            ReportHtmlLang.AppendLocText(sb, "p", "compare.series.missing", "class=\"muted\"");
            return;
        }

        ReportHtmlLang.AppendLocText(sb, "p", "compare.series.hint", "class=\"muted series-hint\"");

        sb.Append("<div class=\"series-grid\">");
        if (hasA)
            AppendSingleSessionSeries(sb, earlier, earlierTr, earlierEn, "#38bdf8", "#a78bfa");
        else
            AppendSeriesMissingCard(sb, earlierTr, earlierEn);

        if (hasB)
            AppendSingleSessionSeries(sb, later, laterTr, laterEn, "#16a34a", "#f59e0b");
        else
            AppendSeriesMissingCard(sb, laterTr, laterEn);
        sb.Append("</div>");

        // Kol bazlı üst üste karşılaştırma (aynı eksen)
        if (hasA && hasB)
        {
            ReportHtmlLang.AppendLocText(sb, "h3", "compare.series.overlayTitle");
            AppendOverlayArmChart(sb, earlier, later, earlierTr, earlierEn, laterTr, laterEn, rightArm: true);
            AppendOverlayArmChart(sb, earlier, later, earlierTr, earlierEn, laterTr, laterEn, rightArm: false);
        }
    }

    private static void AppendSeriesMissingCard(StringBuilder sb, string labelTr, string labelEn)
    {
        sb.Append("<div class=\"series-card\"><h3>");
        ReportHtmlLang.AppendBilingualText(sb, "span", labelTr, labelEn);
        sb.Append("</h3>");
        ReportHtmlLang.AppendLocText(sb, "p", "compare.series.missingOne", "class=\"muted\"");
        sb.Append("</div>");
    }

    private static void AppendSingleSessionSeries(
        StringBuilder sb, SessionEntry s,
        string labelTr, string labelEn,
        string colorR, string colorL)
    {
        const int w = 920, h = 360, padL = 58, padR = 24, padT = 44, padB = 48;

        float yMax = Mathf.Max(
            SeriesYFloorDegrees,
            MaxOf(s.seriesRight),
            MaxOf(s.seriesLeft),
            s.targetAngle > 1f ? s.targetAngle : 0f);
        yMax = Mathf.Ceil(yMax / 10f) * 10f;
        if (yMax < 40f) yMax = 40f;

        float duration = SeriesDurationSeconds(s);
        float peakR = MaxOf(s.seriesRight);
        float peakL = MaxOf(s.seriesLeft);

        sb.Append("<div class=\"series-card\"><h3>");
        ReportHtmlLang.AppendBilingualText(sb, "span", labelTr, labelEn);
        sb.Append("</h3>");
        sb.Append("<p class=\"series-meta\">");
        AppendFormattedSpan(sb, "compare.series.meta",
            duration.ToString("F0", Inv),
            peakR.ToString("F0", Inv),
            peakL.ToString("F0", Inv),
            s.targetAngle > 1f ? s.targetAngle.ToString("F0", Inv) : "—");
        sb.Append("</p>");

        sb.Append("<svg class=\"chart\" viewBox=\"0 0 ").Append(w).Append(' ').Append(h)
          .Append("\" width=\"100%\" role=\"img\">");
        sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(w).Append("\" height=\"").Append(h)
          .Append("\" fill=\"#0b1220\" rx=\"12\"/>");

        DrawSeriesGrid(sb, w, h, padL, padR, padT, padB, yMax, duration);

        if (s.targetAngle > 1f)
        {
            float ty = padT + (1f - Mathf.Clamp01(s.targetAngle / yMax)) * (h - padT - padB);
            sb.Append("<line x1=\"").Append(padL).Append("\" y1=\"").Append(ty.ToString("F1", Inv))
              .Append("\" x2=\"").Append(w - padR).Append("\" y2=\"").Append(ty.ToString("F1", Inv))
              .Append("\" stroke=\"#fbbf24\" stroke-width=\"1.5\" stroke-dasharray=\"6 4\" opacity=\"0.85\"/>");
            AppendSvgText(sb, (w - padR - 4).ToString(Inv), (ty - 6).ToString("F1", Inv),
                "#fbbf24", "11", "end",
                Loc.Format("compare.series.target", AppLanguage.Turkish, s.targetAngle.ToString("F0", Inv)),
                Loc.Format("compare.series.target", AppLanguage.English, s.targetAngle.ToString("F0", Inv)));
        }

        AppendPolyline(sb, s.seriesTimes, s.seriesRight, yMax, w, h, padL, padR, padT, padB, colorR, 2.6f);
        AppendPolyline(sb, s.seriesTimes, s.seriesLeft, yMax, w, h, padL, padR, padT, padB, colorL, 2.2f);
        AppendPeakDot(sb, s.seriesTimes, s.seriesRight, yMax, w, h, padL, padR, padT, padB, colorR);
        AppendPeakDot(sb, s.seriesTimes, s.seriesLeft, yMax, w, h, padL, padR, padT, padB, colorL);

        AppendSvgText(sb, padL.ToString(Inv), "22", colorR, "13", "start",
            Loc.T("compare.series.legR", AppLanguage.Turkish),
            Loc.T("compare.series.legR", AppLanguage.English));
        AppendSvgText(sb, (padL + 110).ToString(Inv), "22", colorL, "13", "start",
            Loc.T("compare.series.legL", AppLanguage.Turkish),
            Loc.T("compare.series.legL", AppLanguage.English));
        sb.Append("</svg></div>");
    }

    private static void AppendOverlayArmChart(
        StringBuilder sb, SessionEntry earlier, SessionEntry later,
        string earlierTr, string earlierEn, string laterTr, string laterEn,
        bool rightArm)
    {
        float[] timesA = earlier.seriesTimes;
        float[] valsA = rightArm ? earlier.seriesRight : earlier.seriesLeft;
        float[] timesB = later.seriesTimes;
        float[] valsB = rightArm ? later.seriesRight : later.seriesLeft;
        if (valsA == null || valsB == null || timesA == null || timesB == null) return;

        const int w = 920, h = 300, padL = 58, padR = 24, padT = 40, padB = 44;
        float yMax = Mathf.Max(SeriesYFloorDegrees, MaxOf(valsA), MaxOf(valsB));
        yMax = Mathf.Ceil(yMax / 10f) * 10f;
        if (yMax < 40f) yMax = 40f;

        string titleKey = rightArm ? "compare.series.overlayR" : "compare.series.overlayL";
        ReportHtmlLang.AppendLocText(sb, "h4", titleKey);

        // Normalize both to 0..1 time for overlay comparison
        sb.Append("<svg class=\"chart\" viewBox=\"0 0 ").Append(w).Append(' ').Append(h)
          .Append("\" width=\"100%\" role=\"img\">");
        sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(w).Append("\" height=\"").Append(h)
          .Append("\" fill=\"#0b1220\" rx=\"12\"/>");

        DrawSeriesGrid(sb, w, h, padL, padR, padT, padB, yMax, 1f, timeIsNormalized: true);

        AppendPolylineNormalized(sb, timesA, valsA, yMax, w, h, padL, padR, padT, padB,
            rightArm ? "#38bdf8" : "#818cf8", 2.4f);
        AppendPolylineNormalized(sb, timesB, valsB, yMax, w, h, padL, padR, padT, padB,
            rightArm ? "#16a34a" : "#f59e0b", 2.4f);

        AppendSvgText(sb, padL.ToString(Inv), "22", rightArm ? "#38bdf8" : "#818cf8", "12", "start",
            earlierTr, earlierEn);
        AppendSvgText(sb, (padL + 280).ToString(Inv), "22", rightArm ? "#16a34a" : "#f59e0b", "12", "start",
            laterTr, laterEn);
        sb.Append("</svg>");
    }

    private static void DrawSeriesGrid(
        StringBuilder sb, int w, int h, int padL, int padR, int padT, int padB,
        float yMax, float durationOrNorm, bool timeIsNormalized = false)
    {
        float plotW = w - padL - padR;
        float plotH = h - padT - padB;

        for (int g = 0; g <= SeriesGridLines; g++)
        {
            float t = g / (float)SeriesGridLines;
            float yy = padT + plotH * t;
            float ang = yMax * (1f - t);
            sb.Append("<line x1=\"").Append(padL).Append("\" y1=\"").Append(yy.ToString("F1", Inv))
              .Append("\" x2=\"").Append(w - padR).Append("\" y2=\"").Append(yy.ToString("F1", Inv))
              .Append("\" stroke=\"#1e293b\" stroke-width=\"1\"/>");
            sb.Append("<text x=\"").Append((padL - 8).ToString(Inv)).Append("\" y=\"")
              .Append((yy + 4).ToString("F1", Inv))
              .Append("\" fill=\"#94a3b8\" font-size=\"12\" text-anchor=\"end\">")
              .Append(ang.ToString("F0", Inv)).Append("°</text>");
        }

        int xTicks = timeIsNormalized ? 4 : 5;
        for (int i = 0; i <= xTicks; i++)
        {
            float u = i / (float)xTicks;
            float xx = padL + plotW * u;
            sb.Append("<line x1=\"").Append(xx.ToString("F1", Inv)).Append("\" y1=\"").Append(padT)
              .Append("\" x2=\"").Append(xx.ToString("F1", Inv)).Append("\" y2=\"").Append(h - padB)
              .Append("\" stroke=\"#1e293b\" stroke-width=\"1\"/>");
            string label = timeIsNormalized
                ? (u * 100f).ToString("F0", Inv) + "%"
                : (durationOrNorm * u).ToString("F0", Inv) + "s";
            sb.Append("<text x=\"").Append(xx.ToString("F1", Inv)).Append("\" y=\"")
              .Append((h - padB + 22).ToString(Inv))
              .Append("\" fill=\"#64748b\" font-size=\"11\" text-anchor=\"middle\">")
              .Append(label).Append("</text>");
        }
    }

    private static void AppendFormattedSpan(StringBuilder sb, string locKey, params object[] args)
    {
        string tr = Loc.Format(locKey, AppLanguage.Turkish, args);
        string en = Loc.Format(locKey, AppLanguage.English, args);
        ReportHtmlLang.AppendBilingualText(sb, "span", tr, en);
    }

    private static float SeriesDurationSeconds(SessionEntry s)
    {
        if (s == null || s.seriesTimes == null || s.seriesTimes.Length < 2)
            return Mathf.Max(1f, s != null ? s.durationSeconds : 1f);
        float span = s.seriesTimes[s.seriesTimes.Length - 1] - s.seriesTimes[0];
        if (span < 0.5f && s.durationSeconds > 0.5f) return s.durationSeconds;
        return Mathf.Max(0.5f, span);
    }

    private static void AppendSvgText(
        StringBuilder sb, string x, string y, string fill, string fontSize, string anchor,
        string tr, string en)
    {
        sb.Append("<text x=\"").Append(x).Append("\" y=\"").Append(y)
          .Append("\" fill=\"").Append(fill).Append("\" font-size=\"").Append(fontSize)
          .Append("\" text-anchor=\"").Append(anchor).Append('"');
        ReportHtmlLang.AppendBilingualAttrPair(sb, tr, en);
        sb.Append('>')
          .Append(ReportHtmlLang.EscapeHtml(LanguageSettings.IsEnglish ? en : tr))
          .Append("</text>");
    }

    private static bool HasSeries(SessionEntry s)
    {
        return s != null && s.seriesTimes != null && s.seriesTimes.Length >= 2
            && s.seriesRight != null && s.seriesLeft != null
            && s.seriesRight.Length >= 2 && s.seriesLeft.Length >= 2;
    }

    private static float MaxOf(float[] arr)
    {
        if (arr == null || arr.Length == 0) return 0f;
        float m = 0f;
        for (int i = 0; i < arr.Length; i++)
            if (arr[i] > m) m = arr[i];
        return m;
    }

    private static void AppendPeakDot(
        StringBuilder sb, float[] times, float[] values, float yMax,
        int w, int h, int padL, int padR, int padT, int padB, string color)
    {
        if (times == null || values == null) return;
        int n = Mathf.Min(times.Length, values.Length);
        if (n < 2) return;
        int peakIdx = 0;
        float peak = values[0];
        for (int i = 1; i < n; i++)
        {
            if (values[i] > peak)
            {
                peak = values[i];
                peakIdx = i;
            }
        }
        float t0 = times[0];
        float t1 = times[n - 1];
        float span = Mathf.Max(0.001f, t1 - t0);
        float plotW = w - padL - padR;
        float plotH = h - padT - padB;
        float x = padL + ((times[peakIdx] - t0) / span) * plotW;
        float y = padT + (1f - Mathf.Clamp01(values[peakIdx] / yMax)) * plotH;
        sb.Append("<circle cx=\"").Append(x.ToString("F1", Inv))
          .Append("\" cy=\"").Append(y.ToString("F1", Inv))
          .Append("\" r=\"4.5\" fill=\"").Append(color).Append("\" stroke=\"#0b1220\" stroke-width=\"1.5\"/>");
        sb.Append("<text x=\"").Append(x.ToString("F1", Inv)).Append("\" y=\"")
          .Append((y - 8).ToString("F1", Inv))
          .Append("\" fill=\"").Append(color).Append("\" font-size=\"11\" text-anchor=\"middle\">")
          .Append(peak.ToString("F0", Inv)).Append("°</text>");
    }

    private static void AppendPolyline(
        StringBuilder sb, float[] times, float[] values, float yMax,
        int w, int h, int padL, int padR, int padT, int padB, string color, float stroke)
    {
        if (times == null || values == null) return;
        int n = Mathf.Min(times.Length, values.Length);
        if (n < 2) return;
        float t0 = times[0];
        float t1 = times[n - 1];
        float span = Mathf.Max(0.001f, t1 - t0);
        float plotW = w - padL - padR;
        float plotH = h - padT - padB;

        sb.Append("<polyline fill=\"none\" stroke=\"").Append(color)
          .Append("\" stroke-width=\"").Append(stroke.ToString("F1", Inv))
          .Append("\" stroke-linejoin=\"round\" stroke-linecap=\"round\" points=\"");
        for (int i = 0; i < n; i++)
        {
            float x = padL + ((times[i] - t0) / span) * plotW;
            float y = padT + (1f - Mathf.Clamp01(values[i] / yMax)) * plotH;
            if (i > 0) sb.Append(' ');
            sb.Append(x.ToString("F1", Inv)).Append(',').Append(y.ToString("F1", Inv));
        }
        sb.Append("\"/>");
    }

    private static void AppendPolylineNormalized(
        StringBuilder sb, float[] times, float[] values, float yMax,
        int w, int h, int padL, int padR, int padT, int padB, string color, float stroke)
    {
        if (times == null || values == null) return;
        int n = Mathf.Min(times.Length, values.Length);
        if (n < 2) return;
        float t0 = times[0];
        float t1 = times[n - 1];
        float span = Mathf.Max(0.001f, t1 - t0);
        float plotW = w - padL - padR;
        float plotH = h - padT - padB;

        sb.Append("<polyline fill=\"none\" stroke=\"").Append(color)
          .Append("\" stroke-width=\"").Append(stroke.ToString("F1", Inv))
          .Append("\" stroke-linejoin=\"round\" stroke-linecap=\"round\" opacity=\"0.95\" points=\"");
        for (int i = 0; i < n; i++)
        {
            float x = padL + ((times[i] - t0) / span) * plotW;
            float y = padT + (1f - Mathf.Clamp01(values[i] / yMax)) * plotH;
            if (i > 0) sb.Append(' ');
            sb.Append(x.ToString("F1", Inv)).Append(',').Append(y.ToString("F1", Inv));
        }
        sb.Append("\"/>");
    }

    private static void AppendStyles(StringBuilder sb)
    {
        sb.Append("<style>");
        ReportHtmlLang.AppendToggleStyle(sb);
        sb.Append("body{font-family:Segoe UI,system-ui,sans-serif;background:#020617;color:#e2e8f0;margin:0;padding:24px;}");
        sb.Append("h1,h2,h3,h4{color:#f8fafc;}h2{margin-top:28px;}h3{margin:8px 0 6px;font-size:17px;}h4{margin:18px 0 8px;font-size:15px;color:#cbd5e1;}");
        sb.Append(".muted{color:#94a3b8;}.disclaimer{font-size:12px;margin-top:32px;}.series-hint{margin:4px 0 12px;}");
        sb.Append(".cols{display:flex;gap:16px;flex-wrap:wrap;}");
        sb.Append(".card{flex:1;min-width:240px;background:#0f172a;border:1px solid #1e293b;border-radius:12px;padding:16px;}");
        sb.Append(".card.earlier{border-color:#38bdf8;}.card.later{border-color:#16a34a;}");
        sb.Append("table.cmp{width:100%;border-collapse:collapse;margin-top:8px;background:#0f172a;border-radius:12px;overflow:hidden;}");
        sb.Append("table.cmp th,table.cmp td{padding:10px 12px;border-bottom:1px solid #1e293b;text-align:left;}");
        sb.Append("table.cmp th{background:#111827;color:#cbd5e1;font-size:13px;}");
        sb.Append("tr.up{background:rgba(22,163,74,0.12);}tr.down{background:rgba(220,38,38,0.12);}tr.flat{background:transparent;}");
        sb.Append(".pill{display:inline-block;padding:2px 10px;border-radius:999px;font-weight:600;font-size:13px;}");
        sb.Append(".pill.up{background:#14532d;color:#86efac;}.pill.down{background:#7f1d1d;color:#fca5a5;}.pill.flat{background:#1e293b;color:#cbd5e1;}");
        sb.Append(".legend{margin:8px 0 16px;}.chart{margin-top:8px;max-width:100%;}");
        sb.Append(".series-grid{display:flex;flex-direction:column;gap:18px;}");
        sb.Append(".series-card{background:#0f172a;border:1px solid #1e293b;border-radius:14px;padding:14px 16px;}");
        sb.Append(".series-meta{color:#94a3b8;font-size:13px;margin:0 0 8px;}");
        sb.Append("</style>");
    }
}
