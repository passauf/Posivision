using System.Text;

/// <summary>
/// HTML raporlarında anlık TR/EN geçişi.
/// data-tr / data-en (metin) ve data-html-tr / data-html-en (HTML) öznitelikleri.
/// </summary>
public static class ReportHtmlLang
{
    public const string StorageKey = "physio_report_lang";

    public static string InitialLangCode =>
        LanguageSettings.IsEnglish ? "en" : "tr";

    public static void AppendToggleButton(StringBuilder sb)
    {
        string label = LanguageSettings.IsEnglish ? "TR" : "EN";
        sb.Append("<button type=\"button\" id=\"lang-toggle\" class=\"lang-toggle\" ")
          .Append("onclick=\"toggleReportLang()\" title=\"TR / EN\">")
          .Append(label).Append("</button>");
    }

    public static void AppendToggleStyle(StringBuilder sb)
    {
        sb.Append(
            ".lang-toggle{position:fixed;top:14px;right:14px;z-index:100;"
            + "padding:8px 14px;border:1px solid #ccd;border-radius:8px;"
            + "background:#1e2e46;color:#fff;font-size:13px;font-weight:700;"
            + "cursor:pointer;box-shadow:0 2px 8px rgba(0,0,0,.15);}"
            + ".lang-toggle:hover{background:#2a4060;}"
            + "@media print{.lang-toggle{display:none;}}");
    }

    public static void AppendToggleScript(StringBuilder sb, string initialLang, string onAppliedJs = null)
    {
        if (string.IsNullOrEmpty(initialLang) || (initialLang != "en" && initialLang != "tr"))
            initialLang = InitialLangCode;

        string after = string.IsNullOrEmpty(onAppliedJs) ? "" : onAppliedJs;
        sb.Append("<script>");
        sb.Append("var REPORT_LANG='").Append(initialLang).Append("';");
        sb.Append("function applyReportLang(lang){");
        sb.Append("if(lang!=='en'&&lang!=='tr')lang='tr';");
        sb.Append("REPORT_LANG=lang;");
        sb.Append("document.documentElement.lang=lang;");
        sb.Append("try{localStorage.setItem('").Append(StorageKey).Append("',lang);}catch(e){}");
        sb.Append("var nodes=document.querySelectorAll('[data-tr][data-en]');");
        sb.Append("for(var i=0;i<nodes.length;i++){");
        sb.Append("var el=nodes[i];");
        sb.Append("el.textContent=lang==='en'?el.getAttribute('data-en'):el.getAttribute('data-tr');");
        sb.Append("}");
        sb.Append("var htmls=document.querySelectorAll('[data-html-tr][data-html-en]');");
        sb.Append("for(var j=0;j<htmls.length;j++){");
        sb.Append("var h=htmls[j];");
        sb.Append("h.innerHTML=lang==='en'?h.getAttribute('data-html-en'):h.getAttribute('data-html-tr');");
        sb.Append("}");
        sb.Append("var btn=document.getElementById('lang-toggle');");
        sb.Append("if(btn)btn.textContent=lang==='en'?'TR':'EN';");
        if (!string.IsNullOrEmpty(after))
            sb.Append(after);
        sb.Append("}");
        sb.Append("function toggleReportLang(){applyReportLang(REPORT_LANG==='en'?'tr':'en');}");
        // Uygulama dilini kullan — tarayıcı localStorage eski tercihi ezmesin
        sb.Append("(function(){applyReportLang('").Append(initialLang).Append("');})();");
        sb.Append("</script>");
    }

    /// <summary>
    /// Açılmadan önce HTML'i uygulama diline hizalar (eski raporlar + localStorage).
    /// </summary>
    public static string ForceAppLanguageOnOpen(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        string lang = InitialLangCode;
        string inject =
            "<script>try{localStorage.setItem('" + StorageKey + "','" + lang
            + "');}catch(e){}var REPORT_LANG='" + lang + "';</script>";

        int bodyIdx = html.IndexOf("<body", System.StringComparison.OrdinalIgnoreCase);
        if (bodyIdx < 0)
            return inject + html;

        int gt = html.IndexOf('>', bodyIdx);
        if (gt < 0) return inject + html;
        return html.Substring(0, gt + 1) + inject + html.Substring(gt + 1);
    }

    /// <summary>Metin düğümü: data-tr + data-en + başlangıç içeriği.</summary>
    public static void AppendBilingualText(StringBuilder sb, string tag, string tr, string en, string extraAttrs = null)
    {
        sb.Append('<').Append(tag);
        if (!string.IsNullOrEmpty(extraAttrs))
            sb.Append(' ').Append(extraAttrs);
        sb.Append(" data-tr=\"").Append(EscapeAttr(tr))
          .Append("\" data-en=\"").Append(EscapeAttr(en)).Append("\">")
          .Append(EscapeHtml(LanguageSettings.IsEnglish ? en : tr))
          .Append("</").Append(tag).Append('>');
    }

    /// <summary>Loc anahtarı ile kısa etiket.</summary>
    public static void AppendLocText(StringBuilder sb, string tag, string locKey, string extraAttrs = null)
    {
        AppendBilingualText(sb, tag, Loc.T(locKey, AppLanguage.Turkish), Loc.T(locKey, AppLanguage.English), extraAttrs);
    }

    /// <summary>HTML içeriği (ör. &lt;b&gt; içeren notlar).</summary>
    public static void AppendBilingualHtml(StringBuilder sb, string tag, string trHtml, string enHtml, string extraAttrs = null)
    {
        sb.Append('<').Append(tag);
        if (!string.IsNullOrEmpty(extraAttrs))
            sb.Append(' ').Append(extraAttrs);
        sb.Append(" data-html-tr=\"").Append(EscapeAttr(trHtml))
          .Append("\" data-html-en=\"").Append(EscapeAttr(enHtml)).Append("\">")
          .Append(LanguageSettings.IsEnglish ? enHtml : trHtml)
          .Append("</").Append(tag).Append('>');
    }

    public static void AppendBilingualAttrPair(StringBuilder sb, string tr, string en)
    {
        sb.Append(" data-tr=\"").Append(EscapeAttr(tr))
          .Append("\" data-en=\"").Append(EscapeAttr(en)).Append('"');
    }

    public static string EscapeAttr(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
    }

    public static string EscapeHtml(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
