using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Klinisyen-only HTML: tanıma özeti + ClinicianNotes.
/// Hasta raporlarına not yazılmaz. SaMD Class B / KVKK yerel.
/// </summary>
public static class ClinicianReportHtml
{
    public static string Build(PatientCareState state, PatientHistory history)
    {
        return Build(state, history, null);
    }

    public static string Build(PatientCareState state, PatientHistory history, PatientProfile profile)
    {
        if (state == null) state = new PatientCareState();

        // Klinisyen dosyaları hasta seans klasöründen ayrı — Reports/Clinician/{Hasta}/
        string dir = GetClinicianDirectory(profile);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var sb = new StringBuilder(8192);
        string lang = ReportHtmlLang.InitialLangCode;
        sb.Append("<!DOCTYPE html><html lang=\"").Append(lang).Append("\"><head><meta charset=\"UTF-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append(Escape(Loc.T("clinician.report.title"))).Append("</title><style>");
        sb.Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222;}");
        sb.Append("h1{font-size:22px;}h2{font-size:16px;border-bottom:1px solid #eee;padding-bottom:4px;}");
        sb.Append(".note{background:#fff8f0;border:1px solid #f0d9b5;border-radius:8px;padding:12px;margin:8px 0;}");
        sb.Append(".muted{color:#888;font-size:12px;}.disclaimer{margin-top:24px;font-size:11px;color:#aaa;font-style:italic;}");
        ReportHtmlLang.AppendToggleStyle(sb);
        sb.Append("</style></head><body>");
        ReportHtmlLang.AppendToggleButton(sb);

        ReportHtmlLang.AppendLocText(sb, "h1", "clinician.report.title");
        if (profile != null && !string.IsNullOrEmpty(profile.DisplayName))
        {
            ReportHtmlLang.AppendBilingualText(sb, "p",
                "Hasta: " + Escape(profile.DisplayName),
                "Patient: " + Escape(profile.DisplayName),
                "class=\"muted\"");
        }
        string reason = profile != null
            ? PatientProfile.NormalizeReasonForCare(profile.reasonForCare)
            : "";
        if (!string.IsNullOrEmpty(reason))
        {
            ReportHtmlLang.AppendLocText(sb, "h2", "report.reasonForCare");
            sb.Append("<p>").Append(Escape(reason).Replace("\r\n", "<br/>").Replace("\n", "<br/>").Replace("\r", "<br/>"))
              .Append("</p>");
        }
        sb.Append("<p class=\"muted\">").Append(System.DateTime.Now.ToString("dd/MM/yyyy HH:mm")).Append("</p>");

        // Geçmiş yalnızca bu hastaya ait olmalı
        history = PatientVault.FilterHistoryForPatient(history, profile, fallbackToAll: false);

        ReportHtmlLang.AppendLocText(sb, "h2", "clinician.report.phase");
        string phaseTr = state.phase == CarePhase.Assessment
            ? "Tanıma (" + state.assessmentSessionCount + "/" + PatientCareState.AssessmentSessionTarget + ")"
            : "Aktif program v" + state.programVersion;
        string phaseEn = state.phase == CarePhase.Assessment
            ? "Assessment (" + state.assessmentSessionCount + "/" + PatientCareState.AssessmentSessionTarget + ")"
            : "Active program v" + state.programVersion;
        ReportHtmlLang.AppendBilingualText(sb, "p", phaseTr, phaseEn);

        if (state.plan != null && state.phase == CarePhase.ActiveProgram)
        {
            ReportHtmlLang.AppendLocText(sb, "h2", "clinician.report.plan");
            ReportHtmlLang.AppendBilingualText(sb, "p",
                "Hedef " + (int)state.plan.dailyTargetAngle + "° / " + state.plan.dailyTargetReps
                + " tekrar · " + state.plan.sessionsPerWeek + " seans/hafta · " + state.lastAdaptedAt,
                "Target " + (int)state.plan.dailyTargetAngle + "° / " + state.plan.dailyTargetReps
                + " reps · " + state.plan.sessionsPerWeek + " sessions/week · " + state.lastAdaptedAt);
        }

        int sessions = history != null && history.sessions != null ? history.sessions.Count : 0;
        ReportHtmlLang.AppendLocText(sb, "h2", "clinician.report.sessions");
        ReportHtmlLang.AppendBilingualText(sb, "p",
            "Kayıtlı seans: " + sessions,
            "Recorded sessions: " + sessions);

        ReportHtmlLang.AppendLocText(sb, "h2", "clinician.report.notes");
        int noteCount = state.clinicianNotes != null ? state.clinicianNotes.Count : 0;
        if (noteCount == 0)
        {
            ReportHtmlLang.AppendLocText(sb, "p", "clinician.report.noNotes", "class=\"muted\"");
        }
        else
        {
            for (int i = 0; i < noteCount; i++)
            {
                ClinicianNote n = state.clinicianNotes[i];
                sb.Append("<div class=\"note\">");
                sb.Append("<div><b>#").Append(n.sessionIndex).Append("</b> · ").Append(Escape(n.createdAt))
                  .Append(" · <code>").Append(Escape(n.reasonCode)).Append("</code></div>");
                sb.Append("<div>");
                ReportHtmlLang.AppendBilingualText(sb, "span", "Hasta ifadesi: ", "Patient claim: ");
                sb.Append(Escape(n.patientClaim)).Append("</div><div>");
                ReportHtmlLang.AppendBilingualText(sb, "span", "Gözlem: ", "Observed: ");
                sb.Append(Escape(n.observedSummary)).Append("</div></div>");
            }
        }

        ReportHtmlLang.AppendLocText(sb, "p", "clinician.report.disclaimer", "class=\"disclaimer\"");
        ReportHtmlLang.AppendToggleScript(sb, lang);
        sb.Append("</body></html>");

        string safe = profile != null ? profile.FileNameSafe : "Hasta";
        string fileName = "Clinician_" + SanitizeFilePart(safe) + "_"
                          + System.DateTime.Now.ToString("yyyyMMdd_HHmm") + ".html";
        return PatientVault.WriteEncrypted(dir, fileName, sb.ToString());
    }

    /// <summary>Hasta seans raporlarından ayrı klinisyen klasörü.</summary>
    public static string GetClinicianDirectory(PatientProfile profile)
    {
        string root = Path.Combine(ReportExporter.ReportsDirectory, "Clinician");
        if (!Directory.Exists(root)) Directory.CreateDirectory(root);
        string safe = SanitizeFilePart(profile != null ? profile.FileNameSafe : "Hasta");
        string dir = Path.Combine(root, safe);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        try { File.SetAttributes(dir, File.GetAttributes(dir) | FileAttributes.Hidden); } catch { }
        return dir;
    }

    private static string SanitizeFilePart(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Hasta";
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            else if (c == ' ') sb.Append('_');
        }
        string s = sb.ToString();
        return s.Length > 0 ? s : "Hasta";
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
