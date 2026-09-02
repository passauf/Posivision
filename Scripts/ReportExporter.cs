using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Seans ve ilerleme HTML + CSV + Excel (.xlsx) raporları.
/// KVKK: yalnızca persistentDataPath/Reports; buluta gönderilmez.
/// SaMD Class B: karar destek; teşhis değildir.
/// </summary>
public static class ReportExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const int ChartWidth = 900;
    private const int ChartHeight = 360;
    private const int PadLeft = 55;
    private const int PadRight = 55;
    private const int PadTop = 25;
    private const int PadBottom = 40;
    private const float AxisMaxAngle = 180f;

    public static string ReportsDirectory
    {
        get
        {
            string dir = Path.Combine(Application.persistentDataPath, "Reports");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Hasta bazlı klasör: Reports/Patients/{Ad_Soyad}/</summary>
    public static string PatientDirectory(PatientProfile profile)
    {
        return PatientVault.GetPatientDirectory(profile);
    }

    /// <summary>
    /// SeansEntry tarihine göre yerel HTML seans raporu arar (yyyyMMdd eşleşmesi).
    /// Önce hasta klasörü (.enc / .html), sonra eski kök Reports.
    /// Seans dosyası tercih edilir; aynı günkü ilerleme raporu yedek olarak kullanılır.
    /// </summary>
    public static string TryFindSessionHtml(SessionEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.dateTime)) return null;
        if (!SessionHistoryFilter.TryParseSessionDate(entry.dateTime, out System.DateTime dt))
            return null;

        string stamp = dt.ToString("yyyyMMdd", Inv);
        string patientDir = PatientVault.GetPatientDirectory(entry);
        MovementId move = ExerciseCatalog.ResolveStoredMovementId(entry.bodyRegionId, entry.movementId);
        string moveHtmlDir = Path.Combine(patientDir, PatientVault.SubdirMovements,
            PatientVault.MovementFolderSlug(move), PatientVault.SubdirHtml);

        string found = FindLatestMatching(moveHtmlDir, "*Seans*_" + stamp + ".html*");
        if (string.IsNullOrEmpty(found))
            found = FindLatestMatching(moveHtmlDir, "*_" + stamp + ".html*");
        string htmlDir = Path.Combine(patientDir, PatientVault.SubdirHtml);

        // Önce Html/ altı seans raporu (eski konum)
        if (string.IsNullOrEmpty(found))
            found = FindLatestMatching(htmlDir, "*Seans*_" + stamp + ".html*");
        if (string.IsNullOrEmpty(found))
            found = FindLatestMatching(htmlDir, "*_" + stamp + ".html*");
        if (string.IsNullOrEmpty(found))
            found = FindLatestMatching(patientDir, "*Seans*_" + stamp + ".html*");
        if (string.IsNullOrEmpty(found))
            found = FindLatestMatching(patientDir, "*_" + stamp + ".html*");
        if (!string.IsNullOrEmpty(found)) return found;

        found = FindLatestMatching(ReportsDirectory, "*Seans*_" + stamp + ".html");
        if (!string.IsNullOrEmpty(found)) return found;
        return FindLatestMatching(ReportsDirectory, "*_" + stamp + ".html");
    }

    private static string FindLatestMatching(string dir, string pattern)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        string[] files;
        try { files = Directory.GetFiles(dir, pattern); }
        catch { return null; }
        if (files == null || files.Length == 0) return null;
        string best = files[0];
        System.DateTime bestWrite = File.GetLastWriteTimeUtc(best);
        for (int i = 1; i < files.Length; i++)
        {
            System.DateTime w = File.GetLastWriteTimeUtc(files[i]);
            if (w > bestWrite)
            {
                best = files[i];
                bestWrite = w;
            }
        }
        return best;
    }

    /// <summary>KVKK silme: Reports (Patients + Clinician + Compare dahil).</summary>
    public static void DeleteAllReports()
    {
        string dir = Path.Combine(Application.persistentDataPath, "Reports");
        if (!Directory.Exists(dir)) return;
        try
        {
            DeleteTree(dir);
        }
        catch { /* klasör erişimi — devam */ }
        // Kökü yeniden oluştur
        try { Directory.CreateDirectory(dir); } catch { }
    }

    private static void DeleteTree(string dir)
    {
        string[] files = Directory.GetFiles(dir);
        for (int i = 0; i < files.Length; i++)
        {
            try { File.Delete(files[i]); } catch { }
        }
        string[] subs = Directory.GetDirectories(dir);
        for (int i = 0; i < subs.Length; i++)
        {
            try { DeleteTree(subs[i]); Directory.Delete(subs[i], false); } catch { }
        }
    }

    /// <summary>
    /// Tek seans HTML raporu.
    /// Dosya: Patients/{Hasta}/Hareketler/{hareket}/Html/...
    /// </summary>
    public static string ExportSessionHtml(SessionReportManager report, PatientProfile profile, int sessionNumber)
    {
        MovementId move = profile != null
            ? ExerciseCatalog.ClampMovement(profile.preferredMovementId)
            : ExerciseCatalog.DefaultMovementId;
        return ExportSessionHtml(report, profile, sessionNumber, move, null);
    }

    public static string ExportSessionHtml(
        SessionReportManager report, PatientProfile profile, int sessionNumber, MovementId movementId)
    {
        return ExportSessionHtml(report, profile, sessionNumber, movementId, null);
    }

    public static string ExportSessionHtml(
        SessionReportManager report, PatientProfile profile, int sessionNumber, MovementId movementId,
        SurveyResponse survey)
    {
        if (report == null) return null;

        movementId = ExerciseCatalog.ClampMovement((int)movementId);
        float duration = report.SessionDurationSeconds;
        float maxAngle = report.MaxAngle;
        int invalid = report.InvalidReps;
        int comp = report.CompensationEventCount;
        float target = report.TargetAngle;
        System.DateTime now = System.DateTime.Now;

        string displayName = profile != null ? profile.DisplayName : "";
        if (string.IsNullOrEmpty(displayName)) displayName = Loc.T("report.patient");

        var sb = new StringBuilder(16384);
        string htmlLang = ReportHtmlLang.InitialLangCode;
        string invalidTr = Loc.T("report.invalid", AppLanguage.Turkish);
        string invalidEn = Loc.T("report.invalid", AppLanguage.English);
        sb.Append("<!DOCTYPE html><html lang=\"").Append(htmlLang).Append("\"><head><meta charset=\"UTF-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append(Escape(Loc.T("report.session"))).Append(" — ").Append(Escape(displayName)).Append("</title>");
        AppendStyle(sb);
        sb.Append("</head><body>");
        ReportHtmlLang.AppendToggleButton(sb);

        ReportHtmlLang.AppendLocText(sb, "h1", "report.title");
        sb.Append("<p class=\"muted\">");
        ReportHtmlLang.AppendLocText(sb, "span", "report.session");
        sb.Append(' ').Append(sessionNumber.ToString(Inv))
          .Append(" &middot; ")
          .Append(now.ToString("dd/MM/yyyy HH:mm"))
          .Append("</p>");

        AppendPatientInfoBlock(sb, profile, sessionNumber, now, movementId);

        sb.Append("<div class=\"cards\">");
        AppendCard(sb, "report.rightMax", report.RightMaxAngle.ToString("F0", Inv) + "°");
        AppendCard(sb, "report.leftMax", report.LeftMaxAngle.ToString("F0", Inv) + "°");
        AppendCard(sb, "report.rightAvg", report.RightAverageAngle.ToString("F1", Inv) + "°");
        AppendCard(sb, "report.leftAvg", report.LeftAverageAngle.ToString("F1", Inv) + "°");
        AppendRepsCard(sb, "report.rightReps", report.RightCompletedReps, report.RightInvalidReps, invalidTr, invalidEn);
        AppendRepsCard(sb, "report.leftReps", report.LeftCompletedReps, report.LeftInvalidReps, invalidTr, invalidEn);
        AppendCard(sb, "report.targetAngle", target.ToString("F0", Inv) + "°");
        AppendCard(sb, "report.compensation", comp.ToString(Inv));
        AppendCard(sb, "report.duration", FormatDuration(duration));
        if (report.StrainSampleCount > 0)
        {
            AppendCard(sb, "report.peakStrain", "%" + (report.PeakStrain * 100f).ToString("F0", Inv));
            AppendCard(sb, "report.meanStrain", "%" + (report.MeanStrain * 100f).ToString("F0", Inv));
        }
        if (report.MovementScoreRight >= 0f)
            AppendCardRaw(sb, Loc.T("menu.hist.dtw.right", AppLanguage.Turkish),
                Loc.T("menu.hist.dtw.right", AppLanguage.English),
                "%" + report.MovementScoreRight.ToString("F0", Inv));
        if (report.MovementScoreLeft >= 0f)
            AppendCardRaw(sb, Loc.T("menu.hist.dtw.left", AppLanguage.Turkish),
                Loc.T("menu.hist.dtw.left", AppLanguage.English),
                "%" + report.MovementScoreLeft.ToString("F0", Inv));
        if (report.QualitySampleCount > 0)
        {
            AppendCard(sb, "report.quality.mean",
                (report.MeanQualityScore * 100f).ToString("F0", Inv) + "%");
            AppendCardBilingualValue(sb,
                Loc.T("report.quality.band", AppLanguage.Turkish),
                Loc.T("report.quality.band", AppLanguage.English),
                QualityBandLabel(report.QualityBand, AppLanguage.Turkish),
                QualityBandLabel(report.QualityBand, AppLanguage.English));
        }
        if (report.AssistedReps > 0)
        {
            AppendCard(sb, "report.assisted.right", report.RightAssistedReps.ToString(Inv));
            AppendCard(sb, "report.assisted.left", report.LeftAssistedReps.ToString(Inv));
            AppendCard(sb, "report.assisted.total", report.AssistedReps.ToString(Inv));
        }
        if (report.TrackingJumpEventCount > 0)
            AppendCard(sb, "report.trackingJump", report.TrackingJumpEventCount.ToString(Inv));
        if (report.SecondPersonEventCount > 0)
            AppendCard(sb, "report.secondPerson", report.SecondPersonEventCount.ToString(Inv));
        if (report.AssistNearEventCount > 0)
            AppendCard(sb, "report.assistNear", report.AssistNearEventCount.ToString(Inv));
        sb.Append("</div>");

        ReportHtmlLang.AppendBilingualText(sb, "h2", "ROM + Zorlanma", "ROM + Strain");
        sb.Append("<div class=\"toggles\">");
        ReportHtmlLang.AppendLocText(sb, "strong", "report.metrics");
        sb.Append(' ');
        AppendMetricToggle(sb, "series-right", "report.series.right");
        AppendMetricToggle(sb, "series-left", "report.series.left");
        AppendMetricToggle(sb, "series-target", "report.targetAngle");
        AppendMetricToggle(sb, "series-comp", "report.compensation");
        if (report.StrainSampleCount > 0)
            AppendMetricToggle(sb, "series-strain", "report.peakStrain");
        sb.Append("</div>");

        AppendCombinedSessionChart(sb, report, target);

        ReportHtmlLang.AppendLocText(sb, "h2", "report.notes");
        sb.Append("<ul>");
        if (invalid > 0)
        {
            string tr = invalid + " tekrar gövde kompansasyonu nedeniyle <b>geçersiz</b> sayıldı.";
            string en = invalid + " reps were marked <b>invalid</b> due to trunk compensation.";
            ReportHtmlLang.AppendBilingualHtml(sb, "li", tr, en);
        }
        if (maxAngle >= target)
        {
            ReportHtmlLang.AppendBilingualHtml(sb, "li",
                "Hedef açıya ulaşıldı veya aşıldı.",
                "Target angle was reached or exceeded.");
        }
        else
        {
            string delta = (target - maxAngle).ToString("F0", Inv);
            ReportHtmlLang.AppendBilingualHtml(sb, "li",
                Loc.T("report.maxRom", AppLanguage.Turkish) + " hedefin " + delta + "° altında kaldı.",
                Loc.T("report.maxRom", AppLanguage.English) + " stayed " + delta + "° below target.");
        }
        if (comp == 0)
        {
            ReportHtmlLang.AppendBilingualHtml(sb, "li",
                "Belirgin gövde kompansasyonu kaydedilmedi.",
                "No significant trunk compensation was recorded.");
        }
        else
        {
            ReportHtmlLang.AppendBilingualHtml(sb, "li",
                comp + " kez gövde kompansasyonu tespit edildi.",
                comp + " trunk compensation event(s) detected.");
        }
        if (report.TrackingJumpEventCount > 0)
        {
            int jumps = report.TrackingJumpEventCount;
            ReportHtmlLang.AppendBilingualHtml(sb, "li",
                jumps + " kez takip/kadraj sıçraması kaydedildi — ROM güvenilirliği düşmüş olabilir.",
                jumps + " tracking/frame jump event(s) recorded — ROM reliability may be reduced.");
        }
        if (report.SecondPersonEventCount > 0)
        {
            int n = report.SecondPersonEventCount;
            ReportHtmlLang.AppendBilingualHtml(sb, "li",
                n + " kez sahnede <b>2. kişi</b> algılandı (manuel yardım kapalı olsa da not edilir).",
                n + " time(s) a <b>2nd person</b> was detected on stage (logged even if help toggle was off).");
        }
        if (report.AssistNearEventCount > 0)
        {
            int n = report.AssistNearEventCount;
            ReportHtmlLang.AppendBilingualHtml(sb, "li",
                n + " kez <b>yardımlı sezgi</b> (temas + hız vektörü + süreğenlik) kaydedildi.",
                n + " time(s) <b>helper-limb proximity</b> (auto-assist cue) was recorded.");
        }
        if (report.StrainSampleCount > 0)
        {
            string peak = (report.PeakStrain * 100f).ToString("F0", Inv);
            string mean = (report.MeanStrain * 100f).ToString("F0", Inv);
            ReportHtmlLang.AppendBilingualHtml(sb, "li",
                "Pik yüz zorlanması %" + peak + " (ort %" + mean + ").",
                "Peak face strain %" + peak + " (mean %" + mean + ").");
        }
        if (report.QualitySampleCount > 0)
        {
            string qMean = (report.MeanQualityScore * 100f).ToString("F0", Inv);
            string qMin = (report.MinQualityScore * 100f).ToString("F0", Inv);
            string bandTr = QualityBandLabel(report.QualityBand, AppLanguage.Turkish);
            string bandEn = QualityBandLabel(report.QualityBand, AppLanguage.English);
            ReportHtmlLang.AppendBilingualHtml(sb, "li",
                "Seans kalite skoru (QualityScore " + SessionQualityScorer.FormulaVersion
                + "): ort %" + qMean + ", min %" + qMin + " — " + bandTr
                + ". Düşük kalitede zirve ROM güncellenmedi.",
                "Session quality score (QualityScore " + SessionQualityScorer.FormulaVersion
                + "): mean %" + qMean + ", min %" + qMin + " — " + bandEn
                + ". Peak ROM was not updated on low-quality frames.");
        }
        if (report.AssistedReps > 0)
        {
            ReportHtmlLang.AppendBilingualHtml(sb, "li",
                "Yardımlı tekrar: sağ " + report.RightAssistedReps
                + " / sol " + report.LeftAssistedReps
                + " (toplam " + report.AssistedReps
                + "). Bağımsız: sağ " + report.RightIndependentReps
                + " / sol " + report.LeftIndependentReps
                + ". Yardımlı tekrarda zirve ROM güncellenmedi.",
                "Assisted reps: R " + report.RightAssistedReps
                + " / L " + report.LeftAssistedReps
                + " (total " + report.AssistedReps
                + "). Independent: R " + report.RightIndependentReps
                + " / L " + report.LeftIndependentReps
                + ". Peak ROM was not updated while assist was active.");
        }
        if (report.GraphCompactGenerations > 0)
        {
            ReportHtmlLang.AppendBilingualHtml(sb, "li",
                "Uzun seans grafik tamponu " + report.GraphCompactGenerations.ToString(Inv)
                + " kez sıkıştırıldı (~" + report.EffectiveSampleHz.ToString("F1", Inv)
                + " Hz). Zoom ile saniye incelemesi hâlâ mümkün.",
                "Long-session chart buffer compacted ×" + report.GraphCompactGenerations.ToString(Inv)
                + " (~" + report.EffectiveSampleHz.ToString("F1", Inv)
                + " Hz). Per-second zoom review remains possible.");
        }
        int overflowSum = report.CompensationOverflowCount + report.TrackingJumpOverflowCount
            + report.SecondPersonOverflowCount + report.AssistNearOverflowCount;
        if (overflowSum > 0)
        {
            ReportHtmlLang.AppendBilingualHtml(sb, "li",
                "Olay zaman damgası taşması: " + overflowSum.ToString(Inv)
                + " (sayaçlar korunur; grafikteki nokta sayısı sınırlı olabilir).",
                "Event timestamp overflow: " + overflowSum.ToString(Inv)
                + " (counts kept; chart markers may be capped).");
        }
        sb.Append("</ul>");

        AppendClinicianSurveySection(sb, survey);

        ReportHtmlLang.AppendLocText(sb, "p", "report.disclaimer", "class=\"disclaimer\"");

        AppendToggleScript(sb);
        ReportHtmlLang.AppendToggleScript(sb, htmlLang);
        sb.Append("</body></html>");

        string fileName = BuildSessionFileName(profile, sessionNumber, now, movementId) + ".html";
        string htmlDir = PatientVault.GetMovementHtmlDirectory(profile, movementId);
        string htmlPath = PatientVault.WriteEncrypted(htmlDir, fileName, sb.ToString());

        string baseName = BuildSessionFileName(profile, sessionNumber, now, movementId);
        WriteSessionSpreadsheet(report, profile, sessionNumber, now, movementId, baseName);

        return htmlPath;
    }

    /// <summary>
    /// Seans sonrası özbildirim: raporda gizli; klinisyen butonu açar.
    /// SaMD Class B / KVKK: Likert skor; HTML içinde varsayılan görünmez.
    /// </summary>
    private static void AppendClinicianSurveySection(StringBuilder sb, SurveyResponse survey)
    {
        if (sb == null || survey == null) return;

        sb.Append("<div class=\"clinician-gate no-print\">");
        sb.Append("<button type=\"button\" id=\"clinician-survey-btn\" class=\"clinician-btn\"");
        ReportHtmlLang.AppendBilingualAttrPair(sb,
            Loc.T("report.survey.btn", AppLanguage.Turkish),
            Loc.T("report.survey.btn", AppLanguage.English));
        sb.Append(" data-tr-open=\"").Append(ReportHtmlLang.EscapeAttr(Loc.T("report.survey.btn.hide", AppLanguage.Turkish))).Append('"')
          .Append(" data-en-open=\"").Append(ReportHtmlLang.EscapeAttr(Loc.T("report.survey.btn.hide", AppLanguage.English))).Append('"')
          .Append(" onclick=\"toggleClinicianSurvey()\">")
          .Append(Escape(Loc.T("report.survey.btn")))
          .Append("</button>");
        ReportHtmlLang.AppendLocText(sb, "p", "report.survey.hint", "class=\"muted\"");
        sb.Append("</div>");

        sb.Append("<div id=\"clinician-survey\" hidden>");
        ReportHtmlLang.AppendLocText(sb, "h2", "report.survey.title");
        sb.Append("<table class=\"survey-table\"><thead><tr>");
        ReportHtmlLang.AppendLocText(sb, "th", "report.survey.col.q");
        ReportHtmlLang.AppendLocText(sb, "th", "report.survey.col.a");
        sb.Append("</tr></thead><tbody>");
        AppendSurveyRow(sb, "survey.q.difficulty", survey.perceivedDifficulty, 10);
        AppendSurveyRow(sb, "survey.q.pain", survey.painVas, 10);
        AppendSurveyRow(sb, "survey.q.motivation", survey.motivation, 10);
        AppendSurveyRow(sb, "survey.q.fatigue", survey.fatigue, 10);
        AppendSurveyRow(sb, "survey.q.homeDays", survey.homeExerciseDays, 7);
        AppendSurveyRow(sb, "survey.q.sleep", survey.sleepQuality, 10);
        AppendSurveyRow(sb, "survey.q.confidence", survey.confidence, 10);
        AppendSurveyRow(sb, "survey.q.willingness", survey.willingness, 10);
        sb.Append("</tbody></table></div>");

        sb.Append("<script>");
        sb.Append("function toggleClinicianSurvey(){");
        sb.Append("var p=document.getElementById('clinician-survey');");
        sb.Append("var b=document.getElementById('clinician-survey-btn');");
        sb.Append("if(!p||!b)return;");
        sb.Append("var open=p.hasAttribute('hidden');");
        sb.Append("if(open)p.removeAttribute('hidden');else p.setAttribute('hidden','hidden');");
        sb.Append("var lang=document.documentElement.lang==='en'?'en':'tr';");
        sb.Append("var k=open?(lang==='en'?'data-en-open':'data-tr-open'):(lang==='en'?'data-en':'data-tr');");
        sb.Append("b.textContent=b.getAttribute(k)||b.textContent;");
        sb.Append("}");
        sb.Append("</script>");
    }

    private static void AppendSurveyRow(StringBuilder sb, string locKey, int value, int max)
    {
        sb.Append("<tr><td>");
        ReportHtmlLang.AppendLocText(sb, "span", locKey);
        sb.Append("</td><td>");
        string tr = FormatSurveyAnswer(value, max, AppLanguage.Turkish);
        string en = FormatSurveyAnswer(value, max, AppLanguage.English);
        ReportHtmlLang.AppendBilingualText(sb, "span", tr, en, "class=\"" + SurveyToneClass(value) + "\"");
        sb.Append("</td></tr>");
    }

    /// <summary>0 kötü, 5 nötr, 10 iyi. 5 altı kırmızı, 5 üstü yeşil. −1 renk yok.</summary>
    private static string SurveyToneClass(int value)
    {
        if (value < 0) return "survey-skip";
        if (value < AssessmentAnalyzer.SurveyNeutralScore) return "survey-bad";
        if (value > AssessmentAnalyzer.SurveyNeutralScore) return "survey-good";
        return "survey-neutral";
    }

    private static string FormatSurveyAnswer(int value, int max, AppLanguage lang)
    {
        if (value < 0) return Loc.T("survey.unknown", lang);
        return value.ToString(Inv) + "/" + max.ToString(Inv);
    }

    private static void WriteSessionSpreadsheet(
        SessionReportManager report,
        PatientProfile profile,
        int sessionNumber,
        DateTime when,
        MovementId movementId,
        string baseFileNameWithoutExt)
    {
        if (report == null) return;

        string[] headers =
        {
            "SeansNo", "Ad", "Soyad", "Tarih", "BoyCm", "Yas",
            "SagMaksROM", "SolMaksROM", "EffectiveMaksROM",
            "SagOrtROM", "SolOrtROM",
            "SagTekrar", "SolTekrar", "HedefTekrar",
            "SagBagimsiz", "SolBagimsiz", "BagimsizToplam",
            "SagGecersiz", "SolGecersiz",
            "TamamlanmaPct", "GecersizOranPct", "YardimliOranPct",
            "Kompansasyon", "TakipSicrama",
            "IkinciKisi", "YardimYakinligi",
            "SureSn", "HedefAci",
            "PikZorlanma", "OrtZorlanma",
            "DtwSag", "DtwSol",
            "KaliteOrt", "KaliteMin",
            "SagYardimli", "SolYardimli", "YardimliToplam",
            "StatsFormula"
        };

        string first = profile != null ? profile.firstName : "";
        string last = profile != null ? profile.lastName : "";
        float height = profile != null ? profile.heightCm : 0f;
        int age = profile != null ? profile.ageYears : 0;

        float effectiveMax = Mathf.Max(report.RightMaxAngle, report.LeftMaxAngle);
        int indepR = Mathf.Max(0, report.RightCompletedReps - report.RightAssistedReps);
        int indepL = Mathf.Max(0, report.LeftCompletedReps - report.LeftAssistedReps);
        int indepTotal = indepR + indepL;
        int validTotal = report.RightCompletedReps + report.LeftCompletedReps;
        int invalidTotal = report.RightInvalidReps + report.LeftInvalidReps;
        float completionPct = report.TargetReps > 0
            ? 100f * indepTotal / Mathf.Max(1, report.TargetReps)
            : -1f;
        float invalidRatePct = (validTotal + invalidTotal) > 0
            ? 100f * invalidTotal / (validTotal + invalidTotal)
            : -1f;
        float assistedRatePct = validTotal > 0
            ? 100f * report.AssistedReps / validTotal
            : -1f;

        var rows = new List<string[]>(1)
        {
            new[]
            {
                sessionNumber.ToString(Inv),
                first ?? "",
                last ?? "",
                when.ToString("dd/MM/yyyy HH:mm", Inv),
                height.ToString("F0", Inv),
                age.ToString(Inv),
                report.RightMaxAngle.ToString("F1", Inv),
                report.LeftMaxAngle.ToString("F1", Inv),
                effectiveMax.ToString("F1", Inv),
                report.RightAverageAngle.ToString("F1", Inv),
                report.LeftAverageAngle.ToString("F1", Inv),
                report.RightCompletedReps.ToString(Inv),
                report.LeftCompletedReps.ToString(Inv),
                report.TargetReps.ToString(Inv),
                indepR.ToString(Inv),
                indepL.ToString(Inv),
                indepTotal.ToString(Inv),
                report.RightInvalidReps.ToString(Inv),
                report.LeftInvalidReps.ToString(Inv),
                completionPct < 0f ? "" : completionPct.ToString("F1", Inv),
                invalidRatePct < 0f ? "" : invalidRatePct.ToString("F1", Inv),
                assistedRatePct < 0f ? "" : assistedRatePct.ToString("F1", Inv),
                report.CompensationEventCount.ToString(Inv),
                report.TrackingJumpEventCount.ToString(Inv),
                report.SecondPersonEventCount.ToString(Inv),
                report.AssistNearEventCount.ToString(Inv),
                report.SessionDurationSeconds.ToString("F1", Inv),
                report.TargetAngle.ToString("F0", Inv),
                report.PeakStrain.ToString("F3", Inv),
                report.MeanStrain.ToString("F3", Inv),
                report.MovementScoreRight.ToString("F1", Inv),
                report.MovementScoreLeft.ToString("F1", Inv),
                report.MeanQualityScore.ToString("F3", Inv),
                report.MinQualityScore.ToString("F3", Inv),
                report.RightAssistedReps.ToString(Inv),
                report.LeftAssistedReps.ToString(Inv),
                report.AssistedReps.ToString(Inv),
                ProgressStatsAggregator.FormulaVersion
            }
        };

        ReportSpreadsheetWriter.WriteCsvAndXlsx(
            PatientVault.GetMovementCsvDirectory(profile, movementId),
            PatientVault.GetMovementExcelDirectory(profile, movementId),
            baseFileNameWithoutExt, "Seans", headers, rows);
    }

    public static string ExportSessionHtml(SessionReportManager report)
    {
        return ExportSessionHtml(report, null, 1);
    }

    public static string ExportProgress(PatientHistory history, PatientProfile profile)
    {
        return ExportProgress(history, profile, HistoryFilterMode.All, HistoryFilterMode.All, 0);
    }

    /// <summary>Aktif bakım planından plannedSessionsPerWeek okur (yoksa 0).</summary>
    public static int ResolvePlannedSessionsPerWeek(
        DataManager dataManager, PatientProfile profile, PatientHistory history)
    {
        if (dataManager == null) return 0;
        PatientCareState care = dataManager.LoadCareState(history, profile);
        if (care == null || care.phase != CarePhase.ActiveProgram || care.plan == null)
            return 0;
        return Mathf.Max(0, care.plan.sessionsPerWeek);
    }

    public static string ExportProgress(PatientHistory history, PatientProfile profile, HistoryFilterMode initialFilter)
    {
        // Geriye uyumluluk: tek mod → tarih veya kalite olarak ayır
        bool isDate = SessionHistoryFilter.IndexOf(initialFilter, SessionHistoryFilter.DateModes) >= 0;
        if (isDate)
            return ExportProgress(history, profile, initialFilter, HistoryFilterMode.All, 0);
        return ExportProgress(history, profile, HistoryFilterMode.All, initialFilter, 0);
    }

    public static string ExportProgress(
        PatientHistory history, PatientProfile profile,
        HistoryFilterMode dateFilter, HistoryFilterMode qualityFilter)
    {
        return ExportProgress(history, profile, dateFilter, qualityFilter, plannedSessionsPerWeek: 0);
    }

    public static string ExportProgress(
        PatientHistory history, PatientProfile profile,
        HistoryFilterMode dateFilter, HistoryFilterMode qualityFilter,
        int plannedSessionsPerWeek)
    {
        return ExportProgress(history, profile, dateFilter, qualityFilter, HistoryFilterMode.All, plannedSessionsPerWeek);
    }

    public static string ExportProgress(
        PatientHistory history, PatientProfile profile,
        HistoryFilterMode dateFilter, HistoryFilterMode qualityFilter,
        HistoryFilterMode exerciseFilter,
        int plannedSessionsPerWeek)
    {
        SessionHistoryFilter.SplitExerciseFilter(
            exerciseFilter, out HistoryFilterMode regionFilter, out HistoryFilterMode movementFilter);
        return ExportProgress(history, profile, dateFilter, qualityFilter, regionFilter, movementFilter, plannedSessionsPerWeek);
    }

    public static string ExportProgress(
        PatientHistory history, PatientProfile profile,
        HistoryFilterMode dateFilter, HistoryFilterMode qualityFilter,
        HistoryFilterMode regionFilter, HistoryFilterMode movementFilter,
        int plannedSessionsPerWeek)
    {
        history = PatientVault.FilterHistoryForPatient(history, profile);
        string htmlPath = ProgressReportHtml.Build(
            history, profile, dateFilter, qualityFilter, regionFilter, movementFilter, plannedSessionsPerWeek);
        if (string.IsNullOrEmpty(htmlPath)) return null;

        string dir = Path.GetDirectoryName(htmlPath);
        string file = Path.GetFileName(htmlPath);
        if (file != null && file.EndsWith(PatientVault.EncExtension, StringComparison.OrdinalIgnoreCase))
            file = file.Substring(0, file.Length - PatientVault.EncExtension.Length);

        // İlerleme HTML kökte kalır; CSV → Csv/, Excel → Excel/
        string baseName = Path.GetFileNameWithoutExtension(file ?? "progress");
        BuildProgressTables(history, plannedSessionsPerWeek,
            out string[] summaryHeaders, out List<string[]> summaryRows,
            out string[] sessionHeaders, out List<string[]> sessionRows);
        ReportSpreadsheetWriter.WriteProgressCsvAndXlsx(
            PatientVault.GetCsvDirectory(profile),
            PatientVault.GetExcelDirectory(profile),
            baseName, summaryHeaders, summaryRows, sessionHeaders, sessionRows);

        return htmlPath;
    }

    public static string ExportProgress(PatientHistory history)
    {
        return ExportProgress(history, null, HistoryFilterMode.All, HistoryFilterMode.All, 0);
    }

    /// <summary>
    /// Klinisyenin seçtiği iki seans için anlık karşılaştırma HTML'i.
    /// Seans bitişinde otomatik çağrılmaz. Dosya: Reports/Compare/{Hasta}/
    /// SaMD Class B; KVKK: yerel.
    /// </summary>
    public static string ExportSessionCompare(
        SessionEntry a, SessionEntry b, PatientProfile profile,
        PatientHistory history = null)
    {
        if (a == null || b == null) return null;
        int noA = ResolveCompareSessionNumber(history, a);
        int noB = ResolveCompareSessionNumber(history, b);
        string html = SessionCompareHtml.BuildHtml(a, b, profile, noA, noB);
        if (string.IsNullOrEmpty(html)) return null;

        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv);
        string baseName = SanitizeFilePart(profile != null ? profile.FileNameSafe : "Patient")
                          + (LanguageSettings.IsEnglish ? "_Compare_" : "_Karsilastirma_")
                          + stamp + ".html";
        string dir = GetCompareDirectory(profile);
        return PatientVault.WriteEncrypted(dir, baseName, html);
    }

    /// <summary>Hasta seans raporlarından ayrı karşılaştırma klasörü: Reports/Compare/{Hasta}/</summary>
    public static string GetCompareDirectory(PatientProfile profile)
    {
        string root = Path.Combine(ReportsDirectory, "Compare");
        if (!Directory.Exists(root)) Directory.CreateDirectory(root);
        string safe = SanitizeFilePart(profile != null ? profile.FileNameSafe : "Hasta");
        string dir = Path.Combine(root, safe);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        try { File.SetAttributes(dir, File.GetAttributes(dir) | FileAttributes.Hidden); } catch { }
        return dir;
    }

    private static int ResolveCompareSessionNumber(PatientHistory history, SessionEntry s)
    {
        if (history == null || history.sessions == null || s == null) return 0;
        int idx = history.sessions.IndexOf(s);
        if (idx >= 0) return idx + 1;
        for (int i = 0; i < history.sessions.Count; i++)
        {
            SessionEntry e = history.sessions[i];
            if (e != null && e.dateTime == s.dateTime)
                return i + 1;
        }
        return 0;
    }

    private static void AppendPatientInfoBlock(
        StringBuilder sb, PatientProfile profile, int sessionNumber, System.DateTime when, MovementId movementId)
    {
        sb.Append("<div class=\"patient\">");
        ReportHtmlLang.AppendLocText(sb, "h2", "report.person");
        sb.Append("<table class=\"info\"><tbody>");

        string name = profile != null ? profile.DisplayName : "";
        string height = profile != null ? profile.heightCm.ToString("F0", Inv) + " cm" : "—";
        string age = profile != null && profile.ageYears > 0 ? profile.ageYears.ToString(Inv) : "—";

        string genderTr = "—";
        string genderEn = "—";
        if (profile != null)
        {
            if (profile.gender == PatientProfile.GenderFemale)
            {
                genderTr = Loc.T("report.female", AppLanguage.Turkish);
                genderEn = Loc.T("report.female", AppLanguage.English);
            }
            else
            {
                genderTr = Loc.T("report.male", AppLanguage.Turkish);
                genderEn = Loc.T("report.male", AppLanguage.English);
            }
        }

        string armsTr = "—";
        string armsEn = "—";
        if (profile != null)
        {
            if (profile.sequentialBothArms
                || (profile.measureRightArm && profile.measureLeftArm))
            {
                if (profile.sequentialBothArms)
                {
                    armsTr = Loc.T("report.protocol.sequential", AppLanguage.Turkish)
                        + " (" + Loc.T("report.arms.both", AppLanguage.Turkish) + ")";
                    armsEn = Loc.T("report.protocol.sequential", AppLanguage.English)
                        + " (" + Loc.T("report.arms.both", AppLanguage.English) + ")";
                }
                else
                {
                    armsTr = Loc.T("report.arms.both", AppLanguage.Turkish);
                    armsEn = Loc.T("report.arms.both", AppLanguage.English);
                }
            }
            else if (profile.measureRightArm)
            {
                armsTr = Loc.T("report.arms.right", AppLanguage.Turkish);
                armsEn = Loc.T("report.arms.right", AppLanguage.English);
            }
            else if (profile.measureLeftArm)
            {
                armsTr = Loc.T("report.arms.left", AppLanguage.Turkish);
                armsEn = Loc.T("report.arms.left", AppLanguage.English);
            }
        }

        MovementId move = ExerciseCatalog.ClampMovement((int)movementId);
        ExerciseDefinition def = ExerciseCatalog.GetOrDefault(move);
        string movementTr = Loc.T(def.LocKey, AppLanguage.Turkish);
        string movementEn = Loc.T(def.LocKey, AppLanguage.English);

        string protocolKey = def.ResolveProtocolLocKey();
        string protocolTr = Loc.T(protocolKey, AppLanguage.Turkish);
        string protocolEn = Loc.T(protocolKey, AppLanguage.English);

        AppendInfoRowLoc(sb, "report.name", string.IsNullOrEmpty(name) ? "—" : name);
        string reason = profile != null
            ? PatientProfile.NormalizeReasonForCare(profile.reasonForCare)
            : "";
        if (!string.IsNullOrEmpty(reason))
            AppendInfoRowLocHtml(sb, "report.reasonForCare", EscapePreserveNewlines(reason));
        AppendInfoRowLoc(sb, "report.height", height);
        AppendInfoRowLoc(sb, "report.age", age);
        AppendInfoRowBilingualValue(sb, "report.gender", genderTr, genderEn);
        AppendInfoRowBilingualValue(sb, "report.movement", movementTr, movementEn);
        AppendInfoRowBilingualValue(sb, "report.protocol", protocolTr, protocolEn);
        AppendInfoRowBilingualValue(sb, "report.arms", armsTr, armsEn);
        AppendInfoRowLoc(sb, "report.sessionNo", sessionNumber.ToString(Inv));
        AppendInfoRowLoc(sb, "report.date", when.ToString("dd/MM/yyyy HH:mm"));
        sb.Append("</tbody></table></div>");
    }

    private static void AppendInfoRowLoc(StringBuilder sb, string locKey, string value)
    {
        sb.Append("<tr><th>");
        ReportHtmlLang.AppendLocText(sb, "span", locKey);
        sb.Append("</th><td>").Append(Escape(value)).Append("</td></tr>");
    }

    /// <summary>Value already HTML-escaped (may contain &lt;br/&gt;).</summary>
    private static void AppendInfoRowLocHtml(StringBuilder sb, string locKey, string htmlValue)
    {
        sb.Append("<tr><th>");
        ReportHtmlLang.AppendLocText(sb, "span", locKey);
        sb.Append("</th><td>").Append(htmlValue).Append("</td></tr>");
    }

    private static string EscapePreserveNewlines(string s)
    {
        return Escape(s).Replace("\r\n", "<br/>").Replace("\n", "<br/>").Replace("\r", "<br/>");
    }

    private static void AppendInfoRowBilingualValue(StringBuilder sb, string locKey, string valueTr, string valueEn)
    {
        sb.Append("<tr><th>");
        ReportHtmlLang.AppendLocText(sb, "span", locKey);
        sb.Append("</th><td>");
        ReportHtmlLang.AppendBilingualText(sb, "span", valueTr, valueEn);
        sb.Append("</td></tr>");
    }

    private static void AppendInfoRow(StringBuilder sb, string key, string value)
    {
        sb.Append("<tr><th>").Append(Escape(key)).Append("</th><td>")
          .Append(Escape(value)).Append("</td></tr>");
    }

    private static readonly string[] ProgressSessionHeaders =
    {
        "Seans", "Ad", "Soyad", "Tarih", "BoyCm", "Yas",
        "SagMaksROM", "SolMaksROM", "EffectiveMaksROM",
        "SagOrtROM", "SolOrtROM",
        "SagTekrar", "SolTekrar", "HedefTekrar",
        "SagBagimsiz", "SolBagimsiz", "BagimsizToplam",
        "SagGecersiz", "SolGecersiz",
        "TamamlanmaPct", "GecersizOranPct", "YardimliOranPct", "DeltaMaksROM",
        "Kompansasyon",
        "SureSn", "HedefAci",
        "PikZorlanma", "OrtZorlanma",
        "PikZorlanmaAcisiSag", "PikZorlanmaAcisiSol",
        "DtwSag", "DtwSol",
        "KaliteOrt", "KaliteMin", "KaliteBand", "KaliteFormula",
        "SagYardimli", "SolYardimli", "YardimliToplam", "TakipSicrama",
        "IkinciKisi", "YardimYakinligi",
        "StatsFormula"
    };

    private static readonly string[] ProgressSummaryHeaders = { "Metric", "Value", "Unit" };

    private static void BuildProgressTables(
        PatientHistory history,
        int plannedSessionsPerWeek,
        out string[] summaryHeaders,
        out List<string[]> summaryRows,
        out string[] sessionHeaders,
        out List<string[]> sessionRows)
    {
        summaryHeaders = ProgressSummaryHeaders;
        summaryRows = new List<string[]>(24);
        sessionHeaders = ProgressSessionHeaders;
        sessionRows = new List<string[]>(32);

        ProgressStats stats = ProgressStatsAggregator.Compute(history, plannedSessionsPerWeek);
        if (stats.hasStats)
            AppendProgressSummaryRows(summaryRows, stats);

        if (history == null || history.sessions == null) return;
        for (int i = 0; i < history.sessions.Count; i++)
        {
            SessionEntry s = history.sessions[i];
            SessionEntry prev = i > 0 ? history.sessions[i - 1] : null;
            float completion = ProgressStatsAggregator.CompletionAsPercent(s);
            float invalidRate = ProgressStatsAggregator.SessionInvalidRepRatePct(s);
            float assistedRate = ProgressStatsAggregator.SessionAssistedRepRatePct(s);
            float deltaRom = ProgressStatsAggregator.DeltaMaxRomDegrees(s, prev);
            float effectiveMax = SessionHistoryFilter.EffectiveMax(s);

            sessionRows.Add(new[]
            {
                (i + 1).ToString(Inv),
                s.firstName ?? "",
                s.lastName ?? "",
                s.dateTime ?? "",
                s.heightCm.ToString("F0", Inv),
                s.ageYears.ToString(Inv),
                s.rightMaxROM.ToString("F1", Inv),
                s.leftMaxROM.ToString("F1", Inv),
                effectiveMax.ToString("F1", Inv),
                s.rightAverageROM.ToString("F1", Inv),
                s.leftAverageROM.ToString("F1", Inv),
                s.rightCompletedReps.ToString(Inv),
                s.leftCompletedReps.ToString(Inv),
                s.targetReps.ToString(Inv),
                ProgressStatsAggregator.IndependentRepsRight(s).ToString(Inv),
                ProgressStatsAggregator.IndependentRepsLeft(s).ToString(Inv),
                ProgressStatsAggregator.TotalIndependentReps(s).ToString(Inv),
                s.rightInvalidReps.ToString(Inv),
                s.leftInvalidReps.ToString(Inv),
                completion < 0f ? "" : completion.ToString("F1", Inv),
                invalidRate < 0f ? "" : invalidRate.ToString("F1", Inv),
                assistedRate < 0f ? "" : assistedRate.ToString("F1", Inv),
                float.IsNaN(deltaRom) ? "" : deltaRom.ToString("F1", Inv),
                s.compensationEvents.ToString(Inv),
                s.durationSeconds.ToString("F1", Inv),
                s.targetAngle.ToString("F0", Inv),
                s.peakStrain.ToString("F3", Inv),
                s.meanStrain.ToString("F3", Inv),
                s.angleAtPeakStrainR.ToString("F1", Inv),
                s.angleAtPeakStrainL.ToString("F1", Inv),
                s.movementScoreRight.ToString("F1", Inv),
                s.movementScoreLeft.ToString("F1", Inv),
                s.qualityScoreMean.ToString("F3", Inv),
                s.qualityScoreMin.ToString("F3", Inv),
                s.qualityBand.ToString(Inv),
                s.qualityFormulaVersion ?? "",
                s.rightAssistedReps.ToString(Inv),
                s.leftAssistedReps.ToString(Inv),
                s.assistedReps.ToString(Inv),
                s.trackingJumpEvents.ToString(Inv),
                s.secondPersonEvents.ToString(Inv),
                s.assistNearEvents.ToString(Inv),
                ProgressStatsAggregator.FormulaVersion
            });
        }
    }

    private static void AppendProgressSummaryRows(List<string[]> rows, ProgressStats st)
    {
        void Kv(string key, string value, string unit) => rows.Add(new[] { key, value ?? "", unit ?? "" });

        Kv("FormulaVersion", ProgressStatsAggregator.FormulaVersion, "");
        Kv("SessionCount", st.sessionCount.ToString(Inv), "sessions");
        Kv("FirstMaxRom", st.firstMaxRom.ToString("F1", Inv), "deg");
        Kv("LastMaxRom", st.lastMaxRom.ToString("F1", Inv), "deg");
        Kv("RomTrendDegrees", st.romTrendDegrees.ToString("F1", Inv), "deg");
        Kv("RomTrendPct", st.romTrendPct.ToString("F1", Inv), "pct");
        Kv("RightRomTrendDegrees",
            float.IsNaN(st.rightRomTrendDegrees) ? "" : st.rightRomTrendDegrees.ToString("F1", Inv), "deg");
        Kv("LeftRomTrendDegrees",
            float.IsNaN(st.leftRomTrendDegrees) ? "" : st.leftRomTrendDegrees.ToString("F1", Inv), "deg");
        Kv("MeanCompletionPct", st.meanCompletionPct < 0f ? "" : st.meanCompletionPct.ToString("F1", Inv), "pct");
        Kv("InvalidRepRatePct", st.invalidRepRatePct < 0f ? "" : st.invalidRepRatePct.ToString("F1", Inv), "pct");
        Kv("AssistedRepRatePct", st.assistedRepRatePct < 0f ? "" : st.assistedRepRatePct.ToString("F1", Inv), "pct");
        Kv("TotalIndependentReps", st.totalIndependentReps.ToString(Inv), "reps");
        Kv("TotalAssistedReps", st.totalAssistedReps.ToString(Inv), "reps");
        Kv("TotalInvalidReps", st.totalInvalidReps.ToString(Inv), "reps");
        Kv("SpanDays", st.spanDays.ToString("F1", Inv), "days");
        Kv("SessionsPerWeekObserved",
            st.sessionsPerWeekObserved < 0f ? "" : st.sessionsPerWeekObserved.ToString("F2", Inv), "per_week");
        Kv("PlannedSessionsPerWeek",
            st.plannedSessionsPerWeek > 0 ? st.plannedSessionsPerWeek.ToString(Inv) : "", "per_week");
        Kv("AdherencePct", st.adherencePct < 0f ? "" : st.adherencePct.ToString("F1", Inv), "pct");
        Kv("UnweightedMeanRom", st.unweightedMeanRom.ToString("F1", Inv), "deg");
        Kv("QualityWeightedMeanRom", st.qualityWeightedMeanRom.ToString("F1", Inv), "deg");
        Kv("MeanQualityScore",
            st.meanQualityScore < 0f ? "" : st.meanQualityScore.ToString("F3", Inv), "0_1");
        Kv("MeanPeakStrain",
            st.meanPeakStrain < 0f ? "" : st.meanPeakStrain.ToString("F3", Inv), "0_1");
        Kv("SessionsWithQuality", st.sessionsWithQuality.ToString(Inv), "sessions");
        Kv("SessionsWithCompensation", st.sessionsWithCompensation.ToString(Inv), "sessions");
        Kv("CompensationSessionRatePct", st.compensationSessionRatePct.ToString("F1", Inv), "pct");
        Kv("TotalCompensationEvents", st.totalCompensationEvents.ToString(Inv), "events");
        Kv("TotalTrackingJumps", st.totalTrackingJumps.ToString(Inv), "events");
        Kv("TotalSecondPersonEvents", st.totalSecondPersonEvents.ToString(Inv), "events");
        Kv("TotalAssistNearEvents", st.totalAssistNearEvents.ToString(Inv), "events");
    }

    /// <summary>Geriye uyumluluk — yalnızca CSV metni üretir.</summary>
    private static string BuildProgressCsvContent(PatientHistory history)
    {
        return BuildProgressCsvContent(history, plannedSessionsPerWeek: 0);
    }

    private static string BuildProgressCsvContent(PatientHistory history, int plannedSessionsPerWeek)
    {
        BuildProgressTables(history, plannedSessionsPerWeek,
            out string[] summaryHeaders, out List<string[]> summaryRows,
            out string[] sessionHeaders, out List<string[]> sessionRows);
        var csv = new StringBuilder(6144);
        if (summaryRows != null && summaryRows.Count > 0)
        {
            csv.Append(ReportSpreadsheetWriter.BuildCsv(summaryHeaders, summaryRows));
            csv.Append('\n');
        }
        csv.Append(ReportSpreadsheetWriter.BuildCsv(sessionHeaders, sessionRows));
        csv.Append('\n');
        csv.Append("# End ProgressStats ").Append(ProgressStatsAggregator.FormulaVersion).Append('\n');
        return csv.ToString();
    }

    /// <summary>
    /// İlerleme HTML'sini açarken aynı temp klasöre seans HTML'lerini de çözer.
    /// Böylece tarayıcıdaki göreli "Seans Raporunu Aç" linkleri .enc'e değil düz HTML'e gider.
    /// PIN oturumu (HasSessionUnlock) gerekir — uygulama içi şifreli açış bozulmaz.
    /// </summary>
    public static bool TryOpenProgressReportWithSessions(string progressEncOrPlainPath, PatientProfile profile)
    {
        if (string.IsNullOrEmpty(progressEncOrPlainPath) || !File.Exists(progressEncOrPlainPath))
            return false;
        if (!PatientVault.HasSessionUnlock)
            return false;

        if (!PatientVault.TryDecryptToTemp(progressEncOrPlainPath, out string decryptedProgress)
            || string.IsNullOrEmpty(decryptedProgress)
            || !File.Exists(decryptedProgress))
            return false;

        string bundleDir = Path.Combine(
            Application.temporaryCachePath,
            "ReportOpenLang",
            "progress_" + Guid.NewGuid().ToString("N"));
        try
        {
            if (Directory.Exists(bundleDir))
                Directory.Delete(bundleDir, true);
            Directory.CreateDirectory(bundleDir);
        }
        catch (Exception e)
        {
            Debug.LogWarning("TryOpenProgressReportWithSessions bundle dir: " + e.Message);
            return TryOpenReportFile(progressEncOrPlainPath);
        }

        // Seans detay HTML (.enc → düz) — göreli href = dosya adı
        PatientVault.MaterializeSessionHtmlBeside(profile, bundleDir);

        string progressName = "progress.html";
        string progressOut = Path.Combine(bundleDir, progressName);
        try
        {
            string html = File.ReadAllText(decryptedProgress, new UTF8Encoding(false));
            try { html = ReportHtmlLang.ForceAppLanguageOnOpen(html); }
            catch { /* dil enjeksiyonu opsiyonel */ }
            File.WriteAllText(progressOut, html, new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Debug.LogWarning("TryOpenProgressReportWithSessions write progress: " + e.Message);
            return TryOpenReportFile(progressEncOrPlainPath);
        }

        return OpenLocalFile(progressOut);
    }

    /// <summary>Şifreli veya düz rapor yolunu tarayıcıda açar (PIN oturumu gerekir).</summary>
    public static bool TryOpenReportFile(string encOrPlainPath)
    {
        if (string.IsNullOrEmpty(encOrPlainPath) || !File.Exists(encOrPlainPath)) return false;
        if (!PatientVault.TryDecryptToTemp(encOrPlainPath, out string decryptedPath))
            return false;

        if (string.IsNullOrEmpty(decryptedPath) || !File.Exists(decryptedPath))
            return false;

        // .enc asla açılmaz
        if (decryptedPath.EndsWith(PatientVault.EncExtension, StringComparison.OrdinalIgnoreCase))
            return false;

        // Tarayıcıya her zaman ASCII adlı temp HTML ver (Unicode hasta adı + .enc URL hatasını önler)
        string toOpen = decryptedPath;
        if (IsHtmlPath(decryptedPath) || LooksLikeHtmlContent(decryptedPath))
        {
            try
            {
                string tempDir = Path.Combine(Application.temporaryCachePath, "ReportOpenLang");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                CleanupStaleEncInOpenDir(tempDir);

                string tempHtml = Path.Combine(tempDir, "report_" + Guid.NewGuid().ToString("N") + ".html");
                string html = File.ReadAllText(decryptedPath, new UTF8Encoding(false));
                try { html = ReportHtmlLang.ForceAppLanguageOnOpen(html); }
                catch { /* dil enjeksiyonu opsiyonel */ }
                File.WriteAllText(tempHtml, html, new UTF8Encoding(false));
                toOpen = tempHtml;
            }
            catch (Exception e)
            {
                Debug.LogWarning("TryOpenReportFile temp copy failed: " + e.Message);
                // Unicode yollu orijinali OpenURL ile açma — Windows Process.Start dene
                toOpen = decryptedPath;
            }
        }

        if (!File.Exists(toOpen)) return false;
        if (toOpen.EndsWith(PatientVault.EncExtension, StringComparison.OrdinalIgnoreCase))
            return false;

        return OpenLocalFile(toOpen);
    }

    private static bool IsHtmlPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        // .html.enc EndsWith(".html") false — doğru
        return path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHtmlContent(string path)
    {
        try
        {
            using (var fs = File.OpenRead(path))
            {
                byte[] buf = new byte[64];
                int n = fs.Read(buf, 0, buf.Length);
                if (n < 15) return false;
                string head = Encoding.ASCII.GetString(buf, 0, n).TrimStart();
                return head.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                       || head.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { return false; }
    }

    private static void CleanupStaleEncInOpenDir(string tempDir)
    {
        try
        {
            string[] stale = Directory.GetFiles(tempDir, "*.enc");
            for (int i = 0; i < stale.Length; i++)
            {
                try { File.Delete(stale[i]); } catch { }
            }
        }
        catch { }
    }

    private static bool OpenLocalFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        if (path.EndsWith(PatientVault.EncExtension, StringComparison.OrdinalIgnoreCase))
            return false;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("OpenLocalFile Process.Start failed: " + e.Message);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c start \"\" \"" + path + "\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("OpenLocalFile cmd start failed: " + e.Message);
        }
#endif
        try
        {
            Application.OpenURL(new Uri(path).AbsoluteUri);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("OpenLocalFile OpenURL failed: " + e.Message);
            return false;
        }
    }

    /// <summary>Windows Explorer ile klasör aç (file:/// ve explorer tırnak hatalarını önler).</summary>
    public static bool TryOpenFolder(string directory)
    {
        if (string.IsNullOrEmpty(directory)) return false;
        try { directory = Path.GetFullPath(directory); }
        catch { return false; }
        if (!Directory.Exists(directory)) return false;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        // 1) Klasör yolunu doğrudan aç (en güvenilir)
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true,
                Verb = "open"
            });
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("TryOpenFolder verb=open failed: " + e.Message);
        }

        // 2) explorer /root,yol — tırnaklı Arguments bug'ını önler
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/root," + directory,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("TryOpenFolder explorer /root failed: " + e.Message);
        }
#endif
        try
        {
            Application.OpenURL(new Uri(directory).AbsoluteUri);
            return true;
        }
        catch { return false; }
    }

    private static void AppendCombinedSessionChart(StringBuilder sb, SessionReportManager report, float target)
    {
        int count = report.SampleCount;
        if (count < 2)
        {
            ReportHtmlLang.AppendBilingualText(sb, "p",
                "Grafik için yeterli örnek yok.",
                "Not enough samples for the chart.",
                "class=\"muted\"");
            return;
        }

        float[] times = report.SampleTimes;
        float maxTime = Mathf.Max(0.001f, times[count - 1]);
        bool hasStrain = report.StrainSampleCount > 0;
        float sessionDur = Mathf.Max(0f, report.SessionDurationSeconds);
        int compactGen = report.GraphCompactGenerations;
        float effHz = report.EffectiveSampleHz;

        // Klinik uyarı: sıkıştırma / kapsama / olay taşması
        bool spanGap = sessionDur > 30f && maxTime < sessionDur * 0.9f;
        bool hasOverflow = report.CompensationOverflowCount
            + report.TrackingJumpOverflowCount
            + report.SecondPersonOverflowCount
            + report.AssistNearOverflowCount > 0;
        if (compactGen > 0 || spanGap || hasOverflow)
        {
            sb.Append("<div class=\"chart-alert\">");
            if (compactGen > 0)
            {
                ReportHtmlLang.AppendBilingualText(sb, "p",
                    "Uzun seans: grafik tamponu " + compactGen.ToString(Inv)
                    + " kez sıkıştırıldı (~" + effHz.ToString("F1", Inv)
                    + " Hz). Tüm süre kapsanır; ince zaman ayrıntısı azalmış olabilir.",
                    "Long session: chart buffer compacted ×" + compactGen.ToString(Inv)
                    + " (~" + effHz.ToString("F1", Inv)
                    + " Hz). Full duration covered; fine temporal detail may be reduced.");
            }
            if (spanGap)
            {
                ReportHtmlLang.AppendBilingualText(sb, "p",
                    "Grafik zaman serisi " + maxTime.ToString("F0", Inv)
                    + " sn; seans süresi " + sessionDur.ToString("F0", Inv) + " sn.",
                    "Chart series spans " + maxTime.ToString("F0", Inv)
                    + " s; session duration " + sessionDur.ToString("F0", Inv) + " s.");
            }
            if (hasOverflow)
            {
                ReportHtmlLang.AppendBilingualText(sb, "p",
                    "Bazı olay zaman damgaları tampon limitine takıldı (sayaçlar tam kaydedildi).",
                    "Some event timestamps hit buffer limits (counts were still recorded).");
            }
            sb.Append("</div>");
        }

        // Zoom kontrolleri (klinisyen: uzun seanslarda saniye düzeyinde inceleme)
        sb.Append("<div class=\"chart-toolbar no-print\">");
        sb.Append("<div class=\"chart-toolbar-row\">");
        sb.Append("<button type=\"button\" class=\"chart-btn\" onclick=\"chartZoom(0.7)\" title=\"Zoom in\">+</button>");
        sb.Append("<button type=\"button\" class=\"chart-btn\" onclick=\"chartZoom(1.4)\" title=\"Zoom out\">−</button>");
        sb.Append("<button type=\"button\" class=\"chart-btn\" onclick=\"chartSetWindow(30)\">");
        ReportHtmlLang.AppendBilingualText(sb, "span", "30 sn", "30 s");
        sb.Append("</button>");
        sb.Append("<button type=\"button\" class=\"chart-btn\" onclick=\"chartSetWindow(60)\">");
        ReportHtmlLang.AppendBilingualText(sb, "span", "1 dk", "1 min");
        sb.Append("</button>");
        sb.Append("<button type=\"button\" class=\"chart-btn\" onclick=\"chartSetWindow(300)\">");
        ReportHtmlLang.AppendBilingualText(sb, "span", "5 dk", "5 min");
        sb.Append("</button>");
        sb.Append("<button type=\"button\" class=\"chart-btn\" onclick=\"chartResetView()\">");
        ReportHtmlLang.AppendBilingualText(sb, "span", "Tümü", "All");
        sb.Append("</button>");
        sb.Append("<label class=\"chart-jump\">");
        ReportHtmlLang.AppendBilingualText(sb, "span", "Saniye: ", "Second: ");
        sb.Append("<input type=\"number\" id=\"chart-jump-sec\" min=\"0\" max=\"")
          .Append(Mathf.CeilToInt(maxTime).ToString(Inv))
          .Append("\" step=\"1\" value=\"0\"/>");
        sb.Append("<button type=\"button\" class=\"chart-btn\" onclick=\"chartJumpToSec()\">");
        ReportHtmlLang.AppendBilingualText(sb, "span", "Git", "Go");
        sb.Append("</button></label>");
        sb.Append("<span id=\"chart-range-label\" class=\"chart-range\"></span>");
        sb.Append("</div>");
        sb.Append("<p class=\"muted chart-hint\">");
        ReportHtmlLang.AppendBilingualText(sb, "span",
            "Tekerlek: yakınlaştır · Sürükle: kaydır · Alt şerit: görünür aralık · Yazdırırken tam görünüm",
            "Wheel: zoom · Drag: pan · Bottom strip: visible range · Print uses full view");
        sb.Append("</p></div>");

        sb.Append("<div id=\"session-chart-host\" class=\"chart-host\"></div>");
        sb.Append("<div id=\"session-chart-overview\" class=\"chart-overview no-print\" title=\"Drag viewport\"></div>");

        // Veri: istemci tarafında zoom/pan ile yeniden çizilir (CDN yok, yerel HTML)
        sb.Append("<script type=\"application/json\" id=\"session-chart-data\">");
        sb.Append("{\"maxT\":").Append(F(maxTime));
        sb.Append(",\"sessionDur\":").Append(F(sessionDur));
        sb.Append(",\"compact\":").Append(compactGen.ToString(Inv));
        sb.Append(",\"hz\":").Append(effHz.ToString("F2", Inv));
        sb.Append(",\"target\":").Append(F(target));
        sb.Append(",\"hasStrain\":").Append(hasStrain ? "true" : "false");
        sb.Append(",\"t\":");
        AppendJsonFloatArray(sb, times, count);
        sb.Append(",\"r\":");
        AppendJsonFloatArray(sb, report.RightAngles, count);
        sb.Append(",\"l\":");
        AppendJsonFloatArray(sb, report.LeftAngles, count);
        sb.Append(",\"ar\":");
        AppendJsonBool01Array(sb, report.AssistRightFlags, count);
        sb.Append(",\"al\":");
        AppendJsonBool01Array(sb, report.AssistLeftFlags, count);
        if (hasStrain)
        {
            sb.Append(",\"s\":");
            AppendJsonFloatArray(sb, report.StrainSamples, count);
        }
        sb.Append(",\"c\":");
        AppendJsonFloatArray(sb, report.CompensationTimes, report.CompensationEventCount);
        sb.Append('}');
        sb.Append("</script>");

        sb.Append("<p class=\"legend\">");
        AppendLegendSwatch(sb, "#2e86de");
        ReportHtmlLang.AppendBilingualText(sb, "span", "Sağ kol (sol eksen °) ", "R arm (left axis °) ");
        AppendLegendSwatch(sb, "#27ae60");
        ReportHtmlLang.AppendBilingualText(sb, "span", "Sol kol ", "L arm ");
        AppendLegendSwatch(sb, "#e74c3c");
        ReportHtmlLang.AppendBilingualText(sb, "span", "Yardımlı aralık ", "Assisted interval ");
        AppendLegendSwatch(sb, "#e67e22");
        ReportHtmlLang.AppendBilingualText(sb, "span", "Hedef ", "Target ");
        AppendLegendSwatch(sb, "#c0392b");
        ReportHtmlLang.AppendLocText(sb, "span", "report.compensation");
        if (hasStrain)
        {
            sb.Append(' ');
            AppendLegendSwatch(sb, "#8e44ad");
            ReportHtmlLang.AppendBilingualText(sb, "span", "Zorlanma (sağ eksen %)", "Strain (right axis %)");
            sb.Append("</p><p class=\"muted\">");
            string peak = (report.PeakStrain * 100f).ToString("F0", Inv);
            string mean = (report.MeanStrain * 100f).ToString("F0", Inv);
            string ar = report.AngleAtPeakStrainRight.ToString("F0", Inv);
            string al = report.AngleAtPeakStrainLeft.ToString("F0", Inv);
            ReportHtmlLang.AppendBilingualText(sb, "span",
                "Pik zorlanma %" + peak + " · Ort %" + mean + " · Pik açı sağ " + ar + "° / sol " + al + "°",
                "Peak strain %" + peak + " · Avg %" + mean + " · Peak angle R " + ar + "° / L " + al + "°");
            sb.Append("</p>");
        }
        else
        {
            sb.Append("</p>");
        }
    }

    /// <summary>
    /// Legend sembolü: inline SVG daire — ekranda ve yazdırmada renk korunur
    /// (CSS background noktaları tarayıcıda baskıda kaybolur).
    /// </summary>
    private static void AppendLegendSwatch(StringBuilder sb, string hexColor)
    {
        sb.Append("<svg class=\"dot\" width=\"10\" height=\"10\" viewBox=\"0 0 10 10\" aria-hidden=\"true\" xmlns=\"http://www.w3.org/2000/svg\">")
          .Append("<circle cx=\"5\" cy=\"5\" r=\"5\" fill=\"").Append(hexColor).Append("\"/>")
          .Append("</svg> ");
    }

    private static void AppendJsonFloatArray(StringBuilder sb, float[] arr, int count)
    {
        sb.Append('[');
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            float v = arr[i];
            if (float.IsNaN(v) || float.IsInfinity(v))
                sb.Append("null");
                else
                sb.Append(v.ToString("F1", Inv));
        }
        sb.Append(']');
    }

    private static void AppendJsonBool01Array(StringBuilder sb, bool[] arr, int count)
    {
        sb.Append('[');
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(arr != null && i < arr.Length && arr[i] ? '1' : '0');
        }
        sb.Append(']');
    }

    private static void AppendStrainPercentAxis(StringBuilder sb)
    {
        for (int pct = 0; pct <= 100; pct += 20)
        {
            float y = MapYNorm(pct / 100f);
            sb.Append("<text x=\"").Append(ChartWidth - PadRight + 8).Append("\" y=\"").Append(F(y + 4))
              .Append("\" font-size=\"11\" fill=\"#8e44ad\" text-anchor=\"start\">%")
              .Append(pct).Append("</text>");
        }
    }

    private static void AppendProgressChart(StringBuilder sb, PatientHistory history)
    {
        int n = history.sessions.Count;
        if (n < 2)
        {
            ReportHtmlLang.AppendBilingualText(sb, "p",
                "İlerleme grafiği için en az 2 seans gerekir.",
                "At least 2 sessions are required for the progress chart.",
                "class=\"muted\"");
            return;
        }

        BeginSvg(sb);
        AppendAngleGrid(sb);
        AppendProgressSeries(sb, "series-prog-max", history, n, true, "#2e86de");
        AppendProgressSeries(sb, "series-prog-avg", history, n, false, "#27ae60");
        AppendAxisLabelBilingual(sb, "seans", "session", ChartWidth - PadRight, ChartHeight - 10);
        EndSvg(sb);

        sb.Append("<p class=\"legend\">");
        AppendLegendSwatch(sb, "#2e86de");
        ReportHtmlLang.AppendLocText(sb, "span", "report.maxRom");
        sb.Append(' ');
        AppendLegendSwatch(sb, "#27ae60");
        ReportHtmlLang.AppendLocText(sb, "span", "report.avgRom");
        sb.Append("</p>");
    }

    private static void BeginSvg(StringBuilder sb)
    {
        sb.Append("<svg viewBox=\"0 0 ").Append(ChartWidth).Append(' ').Append(ChartHeight)
          .Append("\" class=\"chart\" xmlns=\"http://www.w3.org/2000/svg\">");
        sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(ChartWidth).Append("\" height=\"")
          .Append(ChartHeight).Append("\" fill=\"#fafafa\"/>");
    }

    private static void EndSvg(StringBuilder sb)
    {
        sb.Append("</svg>");
    }

    private static void AppendAngleGrid(StringBuilder sb)
    {
        for (int deg = 0; deg <= 180; deg += 30)
        {
            float y = MapY(deg);
            sb.Append("<line x1=\"").Append(PadLeft).Append("\" y1=\"").Append(F(y))
              .Append("\" x2=\"").Append(ChartWidth - PadRight).Append("\" y2=\"").Append(F(y))
              .Append("\" stroke=\"#e0e0e0\" stroke-width=\"1\"/>");
            sb.Append("<text x=\"").Append(PadLeft - 8).Append("\" y=\"").Append(F(y + 4))
              .Append("\" font-size=\"11\" fill=\"#888\" text-anchor=\"end\">")
              .Append(deg).Append("°</text>");
        }
    }

    private static void AppendTimeAxis(StringBuilder sb, float maxTime)
    {
        const int timeTicks = 5;
        for (int i = 0; i <= timeTicks; i++)
        {
            float t = maxTime * (i / (float)timeTicks);
            float x = MapX(t, maxTime);
            int seconds = Mathf.RoundToInt(t);
            sb.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(ChartHeight - 14)
              .Append("\" font-size=\"11\" fill=\"#666\" text-anchor=\"middle\">")
              .Append(seconds).Append("s</text>");
        }
    }

    private static void AppendSeries(
        StringBuilder sb, string id, float[] times, float[] values, int count, float maxTime, string color, float valueMax)
    {
        AppendSeriesWithAssist(sb, id, times, values, null, count, maxTime, color, color, valueMax);
    }

    /// <summary>
    /// Kol serisi: yardımlı örnek aralıklarında kırmızı (#e74c3c), aksi halde normal renk.
    /// İlk yardım algısından yardım bitene kadar segment kırmızı kalır.
    /// </summary>
    private static void AppendSeriesWithAssist(
        StringBuilder sb, string id, float[] times, float[] values, bool[] assist,
        int count, float maxTime, string normalColor, string assistColor, float valueMax)
    {
        sb.Append("<g id=\"").Append(id).Append("\">");
        for (int i = 1; i < count; i++)
        {
            if (float.IsNaN(values[i - 1]) || float.IsNaN(values[i])) continue;
            bool assisted = assist != null && i < assist.Length
                && (assist[i] || assist[i - 1]);
            string color = assisted ? assistColor : normalColor;
            float x0 = MapX(times[i - 1], maxTime);
            float y0 = valueMax <= 1.01f ? MapYNorm(Mathf.Clamp01(values[i - 1])) : MapY(values[i - 1]);
            float x1 = MapX(times[i], maxTime);
            float y1 = valueMax <= 1.01f ? MapYNorm(Mathf.Clamp01(values[i])) : MapY(values[i]);
            sb.Append("<line x1=\"").Append(F(x0)).Append("\" y1=\"").Append(F(y0))
              .Append("\" x2=\"").Append(F(x1)).Append("\" y2=\"").Append(F(y1))
              .Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"2\" stroke-linecap=\"round\"/>");
        }
        sb.Append("</g>");
    }

    private static void AppendProgressSeries(
        StringBuilder sb, string id, PatientHistory history, int n, bool useMax, string color)
    {
        float denom = Mathf.Max(1, n - 1);
        sb.Append("<g id=\"").Append(id).Append("\">");
        sb.Append("<polyline fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"2\" points=\"");
        for (int i = 0; i < n; i++)
        {
            float v = useMax ? history.sessions[i].maxROM : history.sessions[i].averageROM;
            float x = PadLeft + (ChartWidth - PadLeft - PadRight) * (i / denom);
            float y = MapY(v);
            if (i > 0) sb.Append(' ');
            sb.Append(F(x)).Append(',').Append(F(y));
        }
        sb.Append("\"/>");

        for (int i = 0; i < n; i++)
        {
            float v = useMax ? history.sessions[i].maxROM : history.sessions[i].averageROM;
            float x = PadLeft + (ChartWidth - PadLeft - PadRight) * (i / denom);
            float y = MapY(v);
            sb.Append("<circle cx=\"").Append(F(x)).Append("\" cy=\"").Append(F(y))
              .Append("\" r=\"3\" fill=\"").Append(color).Append("\"/>");
        }
        sb.Append("</g>");
    }

    private static void AppendAxisLabel(StringBuilder sb, string text, float x, float y)
    {
        sb.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y))
          .Append("\" font-size=\"11\" fill=\"#888\" text-anchor=\"end\">").Append(text).Append("</text>");
    }

    private static void AppendAxisLabelBilingual(StringBuilder sb, string tr, string en, float x, float y)
    {
        sb.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y))
          .Append("\" font-size=\"11\" fill=\"#888\" text-anchor=\"end\"");
        ReportHtmlLang.AppendBilingualAttrPair(sb, tr, en);
        sb.Append('>').Append(Escape(LanguageSettings.IsEnglish ? en : tr)).Append("</text>");
    }

    private static float MapX(float t, float maxTime)
    {
        return PadLeft + (ChartWidth - PadLeft - PadRight) * Mathf.Clamp01(t / maxTime);
    }

    private static float MapY(float angle)
    {
        float norm = Mathf.Clamp01(angle / AxisMaxAngle);
        return (ChartHeight - PadBottom) - (ChartHeight - PadTop - PadBottom) * norm;
    }

    private static float MapYNorm(float norm01)
    {
        return (ChartHeight - PadBottom) - (ChartHeight - PadTop - PadBottom) * Mathf.Clamp01(norm01);
    }

    private static void AppendStyle(StringBuilder sb)
    {
        sb.Append("<style>"
            + "body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222;background:#fff;}"
            + "h1{font-size:22px;margin:0 0 4px;}h2{font-size:17px;margin:24px 0 8px;border-bottom:1px solid #eee;padding-bottom:4px;}"
            + ".muted{color:#888;font-size:12px;margin:0 0 16px;}"
            + ".patient{background:#f8fafc;border:1px solid #e8ecf0;border-radius:8px;padding:12px 16px;margin:12px 0;}"
            + ".patient h2{margin-top:0;border:none;}"
            + "table.info{border-collapse:collapse;font-size:14px;}"
            + "table.info th{text-align:left;padding:4px 16px 4px 0;color:#666;font-weight:600;}"
            + "table.info td{padding:4px 0;}"
            + ".toggles{margin:8px 0 12px;padding:10px 14px;background:#f4f6f8;border-radius:8px;font-size:14px;}"
            + ".toggles label{margin-right:16px;cursor:pointer;user-select:none;}"
            + ".cards{display:flex;flex-wrap:wrap;gap:12px;margin:12px 0;}"
            + ".card{background:#f4f6f8;border-radius:8px;padding:12px 16px;min-width:120px;}"
            + ".card .k{font-size:12px;color:#888;}.card .v{font-size:20px;font-weight:600;}"
            + ".chart{width:100%;max-width:900px;border:1px solid #eee;border-radius:8px;}"
            + ".chart-host{width:100%;max-width:900px;border:1px solid #eee;border-radius:8px;overflow:hidden;touch-action:none;}"
            + ".chart-host svg{display:block;width:100%;height:auto;cursor:grab;user-select:none;}"
            + ".chart-host svg.dragging{cursor:grabbing;}"
            + ".chart-overview{width:100%;max-width:900px;height:36px;margin:6px 0 10px;border:1px solid #e5e7eb;border-radius:6px;background:#f8fafc;position:relative;cursor:pointer;}"
            + ".chart-overview svg{display:block;width:100%;height:100%;}"
            + ".chart-toolbar{margin:0 0 8px;max-width:900px;}"
            + ".chart-toolbar-row{display:flex;flex-wrap:wrap;align-items:center;gap:6px;}"
            + ".chart-btn{border:1px solid #d0d7de;background:#fff;border-radius:6px;padding:4px 10px;font-size:13px;cursor:pointer;}"
            + ".chart-btn:hover{background:#f0f4f8;}"
            + ".chart-jump{display:inline-flex;align-items:center;gap:4px;margin-left:8px;font-size:13px;color:#555;}"
            + ".chart-jump input{width:88px;padding:3px 6px;border:1px solid #d0d7de;border-radius:6px;}"
            + ".chart-range{margin-left:auto;font-size:12px;color:#334155;font-variant-numeric:tabular-nums;}"
            + ".chart-hint{margin:6px 0 0;}"
            + ".chart-alert{max-width:900px;margin:0 0 10px;padding:10px 12px;background:#fff7ed;border:1px solid #fdba74;border-radius:8px;font-size:13px;color:#9a3412;}"
            + ".chart-alert p{margin:4px 0;}"
            + ".legend{font-size:12px;color:#555;}"
            + ".dot{display:inline-block;width:10px;height:10px;margin:0 4px 0 12px;vertical-align:middle;}"
            + "table{border-collapse:collapse;width:100%;font-size:13px;}"
            + "th,td{border:1px solid #e0e0e0;padding:6px 10px;text-align:center;}th{background:#f4f6f8;}"
            + ".disclaimer{margin-top:24px;font-size:11px;color:#aaa;font-style:italic;}"
            + ".clinician-gate{margin:22px 0 8px;}"
            + ".clinician-btn{border:1px solid #c4a574;background:#fff8f0;color:#7c4a12;border-radius:8px;padding:10px 16px;font-size:14px;font-weight:600;cursor:pointer;}"
            + ".clinician-btn:hover{background:#ffefd6;}"
            + "#clinician-survey{margin:8px 0 16px;background:#fff8f0;border:1px solid #f0d9b5;border-radius:8px;padding:12px 16px;}"
            + "#clinician-survey[hidden]{display:none!important;}"
            + "table.survey-table{margin-top:8px;}"
            + "table.survey-table th,table.survey-table td{text-align:left;}"
            + ".survey-bad{color:#b91c1c;font-weight:700;}"
            + ".survey-good{color:#15803d;font-weight:700;}"
            + ".survey-neutral{color:#57534e;}"
            + ".survey-skip{color:#888;}"
            + "@media print{"
            + ".toggles,.no-print{display:none!important;}"
            + "#clinician-survey[hidden]{display:none!important;}"
            + ".card{border:1px solid #ccc;}"
            + ".chart-host svg{cursor:default;}"
            + ".dot{print-color-adjust:exact;-webkit-print-color-adjust:exact;}"
            + "}");
        ReportHtmlLang.AppendToggleStyle(sb);
        sb.Append("</style>");
    }

    private static void AppendToggleScript(StringBuilder sb)
    {
        sb.Append("<script>");
        // --- Metrik görünürlük + etkileşimli zoom/pan seans grafiği ---
        sb.Append("var chartVis={right:true,left:true,target:true,comp:true,strain:true};");
        sb.Append("function toggleSeries(id,show){");
        sb.Append("if(id==='series-right')chartVis.right=!!show;");
        sb.Append("else if(id==='series-left')chartVis.left=!!show;");
        sb.Append("else if(id==='series-target')chartVis.target=!!show;");
        sb.Append("else if(id==='series-comp')chartVis.comp=!!show;");
        sb.Append("else if(id==='series-strain')chartVis.strain=!!show;");
        sb.Append("if(window.redrawSessionChart)redrawSessionChart();");
        sb.Append("}");

        sb.Append("(function(){");
        sb.Append("var raw=document.getElementById('session-chart-data');");
        sb.Append("if(!raw)return;");
        sb.Append("var D=JSON.parse(raw.textContent);");
        sb.Append("var W=900,H=360,pl=55,pr=55,pt=25,pb=40,amax=180;");
        sb.Append("var maxT=Math.max(0.001,D.maxT||1);");
        sb.Append("var view0=0,view1=maxT;");
        sb.Append("var minWin=5;");
        sb.Append("var host=document.getElementById('session-chart-host');");
        sb.Append("var ohost=document.getElementById('session-chart-overview');");
        sb.Append("var label=document.getElementById('chart-range-label');");
        sb.Append("var dragging=false,lastX=0,ovDrag=false,raf=0,dirty=false;");

        sb.Append("function scheduleRedraw(){");
        sb.Append("if(dirty)return;dirty=true;");
        sb.Append("raf=requestAnimationFrame(function(){dirty=false;redrawSessionChart();});");
        sb.Append("}");

        sb.Append("function clampView(){");
        sb.Append("if(view1-view0<minWin){var mid=(view0+view1)/2;view0=mid-minWin/2;view1=mid+minWin/2;}");
        sb.Append("if(view0<0){view1-=view0;view0=0;}");
        sb.Append("if(view1>maxT){view0-=(view1-maxT);view1=maxT;}");
        sb.Append("if(view0<0)view0=0;");
        sb.Append("}");

        sb.Append("function fmtT(t){");
        sb.Append("t=Math.max(0,t);");
        sb.Append("if(view1-view0<=20)return t.toFixed(1)+'s';");
        sb.Append("var s=Math.round(t),m=Math.floor(s/60),r=s%60;");
        sb.Append("return (m>0?(m+'m '+r+'s'):(s+'s'));");
        sb.Append("}");

        sb.Append("function mapX(t){return pl+(W-pl-pr)*((t-view0)/Math.max(1e-6,view1-view0));}");
        sb.Append("function mapY(a){return (H-pb)-(H-pt-pb)*Math.max(0,Math.min(1,a/amax));}");
        sb.Append("function mapYs(v){return (H-pb)-(H-pt-pb)*Math.max(0,Math.min(1,v));}");
        sb.Append("function invX(px){return view0+(view1-view0)*((px-pl)/Math.max(1,(W-pl-pr)));}");

        // Binary search first index >= t
        sb.Append("function lowerBound(arr,t){var lo=0,hi=arr.length;while(lo<hi){var m=(lo+hi)>>1;if(arr[m]<t)lo=m+1;else hi=m;}return lo;}");

        sb.Append("function drawSeries(parts,times,vals,assist,color,assistColor,yMap,maxSeg){");
        sb.Append("var n=times.length;");
        sb.Append("var i0=Math.max(0,lowerBound(times,view0)-1);");
        sb.Append("var i1=Math.min(n-1,lowerBound(times,view1)+1);");
        sb.Append("var span=Math.max(1,i1-i0);");
        sb.Append("var step=span>maxSeg?Math.ceil(span/maxSeg):1;");
        sb.Append("var i=i0;");
        sb.Append("while(i<i1){");
        sb.Append("var j=Math.min(i1,i+step);");
        sb.Append("while(j<i1 && (vals[j]==null||vals[i]==null))j++;");
        sb.Append("if(vals[i]==null||vals[j]==null){i=j+step;continue;}");
        sb.Append("var as=(assist&&(assist[i]||assist[j]));");
        sb.Append("var c=as?assistColor:color;");
        sb.Append("var x0=mapX(times[i]),y0=yMap(vals[i]),x1=mapX(times[j]),y1=yMap(vals[j]);");
        sb.Append("if(x1<pl||x0>W-pr){i=j;continue;}");
        sb.Append("parts.push('<line x1=\"'+x0.toFixed(1)+'\" y1=\"'+y0.toFixed(1)+'\" x2=\"'+x1.toFixed(1)+'\" y2=\"'+y1.toFixed(1)+'\" stroke=\"'+c+'\" stroke-width=\"2\" stroke-linecap=\"round\"/>');");
        sb.Append("i=j;");
        sb.Append("}");
        sb.Append("}");

        sb.Append("function redrawSessionChart(){");
        sb.Append("clampView();");
        sb.Append("var parts=[];");
        sb.Append("parts.push('<svg viewBox=\"0 0 '+W+' '+H+'\" class=\"chart\" xmlns=\"http://www.w3.org/2000/svg\">');");
        sb.Append("parts.push('<rect x=\"0\" y=\"0\" width=\"'+W+'\" height=\"'+H+'\" fill=\"#fafafa\"/>');");
        // clip
        sb.Append("parts.push('<defs><clipPath id=\"plotClip\"><rect x=\"'+pl+'\" y=\"'+pt+'\" width=\"'+(W-pl-pr)+'\" height=\"'+(H-pt-pb)+'\"/></clipPath></defs>');");
        // grid
        sb.Append("for(var deg=0;deg<=180;deg+=30){var y=mapY(deg);");
        sb.Append("parts.push('<line x1=\"'+pl+'\" y1=\"'+y.toFixed(1)+'\" x2=\"'+(W-pr)+'\" y2=\"'+y.toFixed(1)+'\" stroke=\"#e0e0e0\"/>');");
        sb.Append("parts.push('<text x=\"'+(pl-8)+'\" y=\"'+(y+4).toFixed(1)+'\" font-size=\"11\" fill=\"#888\" text-anchor=\"end\">'+deg+'°</text>');}");
        sb.Append("if(D.hasStrain){for(var p=0;p<=100;p+=20){var yp=mapYs(p/100);");
        sb.Append("parts.push('<text x=\"'+(W-pr+8)+'\" y=\"'+(yp+4).toFixed(1)+'\" font-size=\"11\" fill=\"#8e44ad\" text-anchor=\"start\">%'+p+'</text>');}}");

        sb.Append("parts.push('<g clip-path=\"url(#plotClip)\">');");
        sb.Append("var maxSeg=2200;");
        sb.Append("if(chartVis.right)drawSeries(parts,D.t,D.r,D.ar,'#2e86de','#e74c3c',mapY,maxSeg);");
        sb.Append("if(chartVis.left)drawSeries(parts,D.t,D.l,D.al,'#27ae60','#e74c3c',mapY,maxSeg);");
        sb.Append("if(chartVis.target&&D.target>0){var ty=mapY(D.target);");
        sb.Append("parts.push('<line x1=\"'+pl+'\" y1=\"'+ty.toFixed(1)+'\" x2=\"'+(W-pr)+'\" y2=\"'+ty.toFixed(1)+'\" stroke=\"#e67e22\" stroke-width=\"1.5\" stroke-dasharray=\"6 4\"/>');}");
        sb.Append("if(chartVis.comp&&D.c){for(var ci=0;ci<D.c.length;ci++){var ct=D.c[ci];if(ct<view0||ct>view1)continue;");
        sb.Append("var bi=lowerBound(D.t,ct);if(bi>=D.t.length)bi=D.t.length-1;if(bi>0&&Math.abs(D.t[bi-1]-ct)<Math.abs(D.t[bi]-ct))bi--;");
        sb.Append("var ra=D.r[bi],la=D.l[bi];var ang=((ra==null?0:ra)+(la==null?0:la))*0.5;");
        sb.Append("parts.push('<circle cx=\"'+mapX(ct).toFixed(1)+'\" cy=\"'+mapY(ang).toFixed(1)+'\" r=\"5\" fill=\"#c0392b\" stroke=\"#fff\" stroke-width=\"1\"/>');}}");
        sb.Append("if(chartVis.strain&&D.hasStrain&&D.s)drawSeries(parts,D.t,D.s,null,'#8e44ad','#8e44ad',mapYs,maxSeg);");
        sb.Append("parts.push('</g>');");

        // time axis ticks
        sb.Append("var ticks=6;for(var ti=0;ti<=ticks;ti++){var tt=view0+(view1-view0)*(ti/ticks);var xx=mapX(tt);");
        sb.Append("parts.push('<text x=\"'+xx.toFixed(1)+'\" y=\"'+(H-14)+'\" font-size=\"11\" fill=\"#666\" text-anchor=\"middle\">'+fmtT(tt)+'</text>');}");
        sb.Append("parts.push('<text x=\"'+(W-pr-8)+'\" y=\"'+(H-4)+'\" font-size=\"11\" fill=\"#888\" text-anchor=\"end\">s</text>');");
        sb.Append("parts.push('<text x=\"'+(pl-8)+'\" y=\"'+(pt-6)+'\" font-size=\"11\" fill=\"#888\" text-anchor=\"end\">°</text>');");
        sb.Append("if(D.hasStrain)parts.push('<text x=\"'+(W-8)+'\" y=\"'+(pt-6)+'\" font-size=\"11\" fill=\"#888\" text-anchor=\"end\">%</text>');");
        // crosshair readouts handled via title on hover rect
        sb.Append("parts.push('<rect id=\"chartHit\" x=\"'+pl+'\" y=\"'+pt+'\" width=\"'+(W-pl-pr)+'\" height=\"'+(H-pt-pb)+'\" fill=\"transparent\"/>');");
        sb.Append("parts.push('</svg>');");
        sb.Append("host.innerHTML=parts.join('');");
        sb.Append("var svgEl=host.querySelector('svg');");
        sb.Append("if(svgEl&&dragging)svgEl.classList.add('dragging');");
        sb.Append("drawOverview();");
        sb.Append("if(label){var win=(view1-view0);");
        sb.Append("label.textContent=fmtT(view0)+' – '+fmtT(view1)+'  ('+ (win<20?win.toFixed(1)+'s':Math.round(win)+'s') +')';}");
        sb.Append("}");

        sb.Append("function drawOverview(){");
        sb.Append("if(!ohost)return;");
        sb.Append("var ow=900,oh=36,opl=4,opr=4;");
        sb.Append("function ox(t){return opl+(ow-opl-opr)*(t/maxT);}");
        sb.Append("var p=['<svg viewBox=\"0 0 '+ow+' '+oh+'\" xmlns=\"http://www.w3.org/2000/svg\">'];");
        sb.Append("p.push('<rect width=\"'+ow+'\" height=\"'+oh+'\" fill=\"#f1f5f9\"/>');");
        sb.Append("if(D.r){var step=Math.max(1,Math.ceil(D.t.length/400));");
        sb.Append("for(var i=step;i<D.t.length;i+=step){");
        sb.Append("if(D.r[i]==null||D.r[i-step]==null)continue;");
        sb.Append("var y0=oh-4-(oh-8)*(Math.max(0,Math.min(1,D.r[i-step]/amax)));");
        sb.Append("var y1=oh-4-(oh-8)*(Math.max(0,Math.min(1,D.r[i]/amax)));");
        sb.Append("p.push('<line x1=\"'+ox(D.t[i-step]).toFixed(1)+'\" y1=\"'+y0.toFixed(1)+'\" x2=\"'+ox(D.t[i]).toFixed(1)+'\" y2=\"'+y1.toFixed(1)+'\" stroke=\"#93c5fd\" stroke-width=\"1\"/>');}}");
        sb.Append("var x0=ox(view0),x1=ox(view1);");
        sb.Append("p.push('<rect x=\"'+x0.toFixed(1)+'\" y=\"1\" width=\"'+Math.max(2,x1-x0).toFixed(1)+'\" height=\"'+(oh-2)+'\" fill=\"rgba(37,99,235,0.18)\" stroke=\"#2563eb\" stroke-width=\"1.5\"/>');");
        sb.Append("p.push('</svg>');");
        sb.Append("ohost.innerHTML=p.join('');");
        sb.Append("}");

        sb.Append("function overviewMove(e){");
        sb.Append("if(!ohost)return;");
        sb.Append("var rect=ohost.getBoundingClientRect();");
        sb.Append("var ratio=Math.max(0,Math.min(1,(e.clientX-rect.left)/rect.width));");
        sb.Append("var win=view1-view0;");
        sb.Append("var mid=ratio*maxT;");
        sb.Append("view0=mid-win/2;view1=mid+win/2;clampView();scheduleRedraw();");
        sb.Append("}");

        // Olaylar bir kez bağlanır (redraw innerHTML yeniler, listener host'ta kalır)
        sb.Append("host.addEventListener('wheel',function(e){");
        sb.Append("e.preventDefault();");
        sb.Append("var rect=host.getBoundingClientRect();");
        sb.Append("var px=(e.clientX-rect.left)*(W/rect.width);");
        sb.Append("var focus=invX(px);");
        sb.Append("var factor=e.deltaY<0?0.8:1.25;");
        sb.Append("var half=(view1-view0)*factor*0.5;");
        sb.Append("view0=focus-half;view1=focus+half;clampView();scheduleRedraw();");
        sb.Append("},{passive:false});");
        sb.Append("host.addEventListener('mousedown',function(e){");
        sb.Append("dragging=true;lastX=e.clientX;");
        sb.Append("var s=host.querySelector('svg');if(s)s.classList.add('dragging');");
        sb.Append("e.preventDefault();");
        sb.Append("});");
        sb.Append("window.addEventListener('mouseup',function(){");
        sb.Append("dragging=false;ovDrag=false;");
        sb.Append("var s=host.querySelector('svg');if(s)s.classList.remove('dragging');");
        sb.Append("});");
        sb.Append("window.addEventListener('mousemove',function(e){");
        sb.Append("if(dragging){");
        sb.Append("var rect=host.getBoundingClientRect();");
        sb.Append("var dx=(e.clientX-lastX)*(W/rect.width);");
        sb.Append("lastX=e.clientX;");
        sb.Append("var dt=-dx/Math.max(1,W-pl-pr)*(view1-view0);");
        sb.Append("view0+=dt;view1+=dt;clampView();scheduleRedraw();");
        sb.Append("}else if(ovDrag){overviewMove(e);}");
        sb.Append("});");
        sb.Append("if(ohost){");
        sb.Append("ohost.addEventListener('mousedown',function(e){ovDrag=true;overviewMove(e);e.preventDefault();});");
        sb.Append("}");

        sb.Append("window.chartZoom=function(factor){");
        sb.Append("var mid=(view0+view1)/2;var half=(view1-view0)*factor*0.5;");
        sb.Append("view0=mid-half;view1=mid+half;clampView();redrawSessionChart();");
        sb.Append("};");
        sb.Append("window.chartSetWindow=function(sec){");
        sb.Append("sec=Math.max(minWin,Math.min(maxT,sec));");
        sb.Append("var mid=(view0+view1)/2;");
        sb.Append("view0=mid-sec/2;view1=mid+sec/2;clampView();redrawSessionChart();");
        sb.Append("};");
        sb.Append("window.chartResetView=function(){view0=0;view1=maxT;redrawSessionChart();};");
        sb.Append("window.chartJumpToSec=function(){");
        sb.Append("var inp=document.getElementById('chart-jump-sec');");
        sb.Append("var sec=parseFloat(inp&&inp.value);");
        sb.Append("if(isNaN(sec))return;");
        sb.Append("sec=Math.max(0,Math.min(maxT,sec));");
        sb.Append("var win=Math.max(minWin,view1-view0);");
        sb.Append("if(win>=maxT*0.95)win=Math.min(60,maxT);");
        sb.Append("view0=sec-win/2;view1=sec+win/2;clampView();redrawSessionChart();");
        sb.Append("};");
        sb.Append("var jumpInp=document.getElementById('chart-jump-sec');");
        sb.Append("if(jumpInp)jumpInp.addEventListener('keydown',function(e){if(e.key==='Enter')chartJumpToSec();});");
        sb.Append("window.redrawSessionChart=redrawSessionChart;");
        // Uzun seans: ilk açılışta son 10 dk (klinisyen yakın geçmişi görür; Tümü ile genişletir)
        sb.Append("if(maxT>600){view0=Math.max(0,maxT-600);view1=maxT;}");
        sb.Append("window.addEventListener('beforeprint',function(){view0=0;view1=maxT;redrawSessionChart();});");
        sb.Append("redrawSessionChart();");
        sb.Append("})();");
        sb.Append("</script>");
    }

    private static void AppendCard(StringBuilder sb, string locKey, string value)
    {
        sb.Append("<div class=\"card\"><div class=\"k\">");
        ReportHtmlLang.AppendLocText(sb, "span", locKey);
        sb.Append("</div><div class=\"v\">").Append(Escape(value)).Append("</div></div>");
    }

    private static void AppendCardRaw(StringBuilder sb, string keyTr, string keyEn, string value)
    {
        sb.Append("<div class=\"card\"><div class=\"k\">");
        ReportHtmlLang.AppendBilingualText(sb, "span", keyTr, keyEn);
        sb.Append("</div><div class=\"v\">").Append(Escape(value)).Append("</div></div>");
    }

    /// <summary>Kart değeri TR/EN ayrı (dil toggle ile değişir; "Güvenilir / Reliable" birleşik yazılmaz).</summary>
    private static void AppendCardBilingualValue(
        StringBuilder sb, string keyTr, string keyEn, string valueTr, string valueEn)
    {
        sb.Append("<div class=\"card\"><div class=\"k\">");
        ReportHtmlLang.AppendBilingualText(sb, "span", keyTr, keyEn);
        sb.Append("</div><div class=\"v\">");
        ReportHtmlLang.AppendBilingualText(sb, "span", valueTr, valueEn);
        sb.Append("</div></div>");
    }

    private static void AppendRepsCard(
        StringBuilder sb, string locKey, int reps, int invalid, string invalidTr, string invalidEn)
    {
        sb.Append("<div class=\"card\"><div class=\"k\">");
        ReportHtmlLang.AppendLocText(sb, "span", locKey);
        sb.Append("</div><div class=\"v\">").Append(reps.ToString(Inv)).Append(" (");
        ReportHtmlLang.AppendBilingualText(sb, "span", invalidTr, invalidEn);
        sb.Append(' ').Append(invalid).Append(")</div></div>");
    }

    private static void AppendMetricToggle(StringBuilder sb, string seriesId, string locKey)
    {
        sb.Append("<label><input type=\"checkbox\" checked onchange=\"toggleSeries('")
          .Append(seriesId).Append("', this.checked)\"> ");
        ReportHtmlLang.AppendLocText(sb, "span", locKey);
        sb.Append("</label> ");
    }

    private static string BuildSessionFileName(
        PatientProfile profile, int sessionNumber, System.DateTime when, MovementId movementId)
    {
        string namePart = SanitizeFilePart(profile != null ? profile.FileNameSafe : "Hasta");
        string movePart = PatientVault.MovementFolderSlug(movementId);
        return namePart + "_" + movePart + "_Seans" + sessionNumber.ToString(Inv) + "_" + when.ToString("yyyyMMdd");
    }

    private static string SanitizeFilePart(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Hasta";
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                sb.Append(c);
            else if (c == ' ')
                sb.Append('_');
        }
        string s = sb.ToString();
        return s.Length > 0 ? s : "Hasta";
    }

    private static string FormatDuration(float seconds)
    {
        int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
        int m = total / 60;
        int s = total % 60;
        return m.ToString("00", Inv) + ":" + s.ToString("00", Inv);
    }

    private static string F(float v)
    {
        return v.ToString("F1", Inv);
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string QualityBandLabel(SessionQualityBand band, AppLanguage lang)
    {
        switch (band)
        {
            case SessionQualityBand.Reliable:
                return Loc.T("report.quality.reliable", lang);
            case SessionQualityBand.Caution:
                return Loc.T("report.quality.caution", lang);
            case SessionQualityBand.Invalid:
                return Loc.T("report.quality.invalid", lang);
            default:
                return Loc.T("report.quality.unknown", lang);
        }
    }

    private static string CsvField(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0)
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
