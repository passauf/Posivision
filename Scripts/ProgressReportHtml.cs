using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// İlerleme HTML raporu: dropdown filtre, R/L tablo, gelişim %, tıklanabilir seans özeti.
/// Grafik: Y = açı (°), X = seans no (tick 1 / 5 / 10 / 15 / 20 / 30).
/// KVKK: yalnızca yerel dosya; SaMD Class B karar-destek.
/// </summary>
public static class ProgressReportHtml
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Build(
        PatientHistory history, PatientProfile profile,
        HistoryFilterMode dateFilter, HistoryFilterMode qualityFilter)
    {
        return Build(history, profile, dateFilter, qualityFilter, HistoryFilterMode.All, HistoryFilterMode.All, plannedSessionsPerWeek: 0);
    }

    public static string Build(
        PatientHistory history, PatientProfile profile,
        HistoryFilterMode dateFilter, HistoryFilterMode qualityFilter,
        int plannedSessionsPerWeek)
    {
        return Build(history, profile, dateFilter, qualityFilter, HistoryFilterMode.All, HistoryFilterMode.All, plannedSessionsPerWeek);
    }

    public static string Build(
        PatientHistory history, PatientProfile profile,
        HistoryFilterMode dateFilter, HistoryFilterMode qualityFilter,
        HistoryFilterMode exerciseFilter,
        int plannedSessionsPerWeek)
    {
        SessionHistoryFilter.SplitExerciseFilter(
            exerciseFilter, out HistoryFilterMode regionFilter, out HistoryFilterMode movementFilter);
        return Build(history, profile, dateFilter, qualityFilter, regionFilter, movementFilter, plannedSessionsPerWeek);
    }

    public static string Build(
        PatientHistory history, PatientProfile profile,
        HistoryFilterMode dateFilter, HistoryFilterMode qualityFilter,
        HistoryFilterMode regionFilter, HistoryFilterMode movementFilter,
        int plannedSessionsPerWeek)
    {
        if (history == null || history.sessions == null || history.sessions.Count == 0) return null;

        int n = history.sessions.Count;
        System.DateTime now = System.DateTime.Now;
        string displayName = profile != null ? profile.DisplayName : "";
        if (string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(history.sessions[n - 1].firstName))
            displayName = (history.sessions[n - 1].firstName + " " + history.sessions[n - 1].lastName).Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = Loc.T("report.patient");

        var sb = new StringBuilder(49152);
        string htmlLang = ReportHtmlLang.InitialLangCode;
        sb.Append("<!DOCTYPE html><html lang=\"").Append(htmlLang).Append("\"><head><meta charset=\"UTF-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append(Escape(Loc.T("report.progress.title"))).Append(" — ").Append(Escape(displayName)).Append("</title>");
        AppendBaseStyle(sb);
        sb.Append("</head><body>");
        ReportHtmlLang.AppendToggleButton(sb);

        ReportHtmlLang.AppendLocText(sb, "h1", "report.progress.title");
        sb.Append("<p class=\"muted\">").Append(n).Append(' ');
        ReportHtmlLang.AppendBilingualText(sb, "span", "seans · Oluşturma: ", "sessions · Created: ");
        sb.Append(now.ToString("dd/MM/yyyy HH:mm")).Append("</p>");

        string reason = profile != null
            ? PatientProfile.NormalizeReasonForCare(profile.reasonForCare)
            : "";
        if (!string.IsNullOrEmpty(reason))
        {
            sb.Append("<p><strong>");
            ReportHtmlLang.AppendLocText(sb, "span", "report.reasonForCare");
            sb.Append(":</strong> ")
              .Append(Escape(reason).Replace("\r\n", "<br/>").Replace("\n", "<br/>").Replace("\r", "<br/>"))
              .Append("</p>");
        }

        ProgressStats stats = ProgressStatsAggregator.Compute(history, plannedSessionsPerWeek);
        AppendStatsSection(sb, stats);

        sb.Append("<div class=\"toggles filter-bar\">");
        AppendSelect(sb, "date-select", "filter.date", SessionHistoryFilter.DateModes, dateFilter, "setDateFilter");
        AppendSelect(sb, "quality-select", "filter.quality", SessionHistoryFilter.QualityModes, qualityFilter, "setQualityFilter");
        AppendSelect(sb, "region-select", "filter.region", SessionHistoryFilter.RegionModes, regionFilter, "setRegionFilter");
        AppendSelect(sb, "movement-select", "filter.exercise",
            SessionHistoryFilter.GetMovementModes(regionFilter), movementFilter, "setMovementFilter");
        sb.Append("</div>");

        sb.Append("<div class=\"cards\" id=\"summary-cards\"></div>");

        ReportHtmlLang.AppendLocText(sb, "h2", "report.progress.chart");
        sb.Append("<div class=\"toggles\"><strong>");
        ReportHtmlLang.AppendLocText(sb, "span", "report.metrics");
        sb.Append("</strong> ");
        sb.Append("<label><input type=\"checkbox\" id=\"chk-r\" checked onchange=\"redrawChart()\"> ");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.rightMax");
        sb.Append("</label> ");
        sb.Append("<label><input type=\"checkbox\" id=\"chk-l\" checked onchange=\"redrawChart()\"> ");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.leftMax");
        sb.Append("</label> ");
        sb.Append("<label><input type=\"checkbox\" id=\"chk-avg\" onchange=\"redrawChart()\"> ");
        ReportHtmlLang.AppendLocText(sb, "span", "report.avgRom");
        sb.Append("</label> ");
        sb.Append("<label><input type=\"checkbox\" id=\"chk-s\" checked onchange=\"redrawChart()\"> ");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.strain");
        sb.Append(" (%)</label>");
        sb.Append("</div><div id=\"chart-host\"></div>");
        sb.Append("<p class=\"legend\">");
        AppendLegendSwatch(sb, "#27ae60");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.rightMax");
        sb.Append(' ');
        AppendLegendSwatch(sb, "#2e86de");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.leftMax");
        sb.Append(' ');
        AppendLegendSwatch(sb, "#e67e22");
        ReportHtmlLang.AppendLocText(sb, "span", "report.avgRom");
        sb.Append(' ');
        AppendLegendSwatch(sb, "#8e44ad");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.strain");
        sb.Append(" %</p>");
        ReportHtmlLang.AppendLocText(sb, "p", "report.progress.clickHint", "class=\"muted\"");

        ReportHtmlLang.AppendLocText(sb, "h2", "report.progress.history");
        sb.Append("<table><thead><tr>")
          .Append("<th>#</th><th>");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.date");
        sb.Append("</th><th>");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.rightMax");
        sb.Append("</th><th>");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.leftMax");
        sb.Append("</th><th>");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.rightReps");
        sb.Append("</th><th>");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.leftReps");
        sb.Append("</th><th>");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.invalidRL");
        sb.Append("</th><th>");
        ReportHtmlLang.AppendLocText(sb, "span", "report.compensation");
        sb.Append("</th><th>");
        ReportHtmlLang.AppendLocText(sb, "span", "report.col.strain");
        sb.Append("</th><th>");
        ReportHtmlLang.AppendLocText(sb, "span", "report.duration");
        sb.Append("</th></tr></thead><tbody id=\"hist-body\">");

        for (int i = 0; i < n; i++)
        {
            SessionEntry s = history.sessions[i];
            float rMax = SessionHistoryFilter.EffectiveRightMax(s);
            float lMax = SessionHistoryFilter.EffectiveLeftMax(s);
            int rRep = s.rightCompletedReps > 0 || s.rightArmEnabled ? s.rightCompletedReps
                : (s.leftArmEnabled ? 0 : s.completedReps);
            int lRep = s.leftCompletedReps > 0 || s.leftArmEnabled ? s.leftCompletedReps : 0;
            sb.Append("<tr class=\"click-row\" data-i=\"").Append(i)
              .Append("\" onclick=\"openSession(").Append(i).Append(")\" title=\"")
              .Append(Escape(Loc.T("report.progress.clickHint"))).Append("\">");
            sb.Append("<td class=\"idx\"></td><td>").Append(Escape(s.dateTime)).Append("</td>");
            sb.Append("<td>").Append(rMax.ToString("F0", Inv)).Append("°</td>");
            sb.Append("<td>").Append(lMax.ToString("F0", Inv)).Append("°</td>");
            sb.Append("<td>").Append(rRep).Append('/').Append(s.targetReps).Append("</td>");
            sb.Append("<td>").Append(lRep).Append('/').Append(s.targetReps).Append("</td>");
            sb.Append("<td>").Append(s.rightInvalidReps).Append('/').Append(s.leftInvalidReps).Append("</td>");
            sb.Append("<td>").Append(s.compensationEvents).Append("</td>");
            sb.Append("<td>%").Append((s.peakStrain * 100f).ToString("F0", Inv))
              .Append(" @").Append(s.angleAtPeakStrainR.ToString("F0", Inv)).Append("°/")
              .Append(s.angleAtPeakStrainL.ToString("F0", Inv)).Append("°</td>");
            sb.Append("<td>").Append(FormatDuration(s.durationSeconds)).Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
        ReportHtmlLang.AppendLocText(sb, "p", "report.disclaimer.short", "class=\"disclaimer\"");

        sb.Append("<div id=\"detail-modal\" class=\"modal\" onclick=\"backdropClose(event)\">");
        sb.Append("<div class=\"modal-card\" role=\"dialog\">");
        sb.Append("<button type=\"button\" class=\"modal-close\" onclick=\"closeSession()\">&times;</button>");
        sb.Append("<h2 id=\"d-title\"></h2>");
        sb.Append("<p id=\"d-sub\" class=\"muted\"></p>");
        sb.Append("<div id=\"d-metrics\" class=\"cards\"></div>");
        sb.Append("<div id=\"d-chart\"></div>");
        sb.Append("<p id=\"d-chart-legend\" class=\"muted chart-legend\"></p>");
        ReportHtmlLang.AppendLocText(sb, "h3", "detail.summary.title");
        sb.Append("<pre id=\"d-notes\" class=\"notes\"></pre>");
        sb.Append("<p id=\"d-html-wrap\"><a id=\"d-html\" href=\"#\" target=\"_blank\" rel=\"noopener\">");
        ReportHtmlLang.AppendLocText(sb, "span", "detail.btn.html");
        sb.Append("</a></p>");
        sb.Append("<button type=\"button\" class=\"btn-back\" onclick=\"closeSession()\">");
        ReportHtmlLang.AppendLocText(sb, "span", "detail.btn.back");
        sb.Append("</button>");
        sb.Append("</div></div>");

        sb.Append("<script>var SESSIONS=[");
        for (int i = 0; i < n; i++)
        {
            SessionEntry s = history.sessions[i];
            SessionEntry prev = i > 0 ? history.sessions[i - 1] : null;
            if (i > 0) sb.Append(',');
            bool hasR = SessionHistoryFilter.ShowRight(s) || s.rightMaxROM > 0f || s.rightCompletedReps > 0;
            bool hasL = SessionHistoryFilter.ShowLeft(s) || s.leftMaxROM > 0f || s.leftCompletedReps > 0;
            int rRep = s.rightCompletedReps > 0 || s.rightArmEnabled ? s.rightCompletedReps
                : (s.leftArmEnabled ? 0 : s.completedReps);
            int lRep = s.leftCompletedReps > 0 || s.leftArmEnabled ? s.leftCompletedReps : 0;
            int done = s.completedReps;
            if (done == 0 && (s.rightCompletedReps > 0 || s.leftCompletedReps > 0))
                done = s.rightCompletedReps + s.leftCompletedReps;
            float target = s.targetAngle > 1f ? s.targetAngle : 160f;
            string notesTr = SessionClinicalSummary.Build(s, prev, AppLanguage.Turkish);
            string notesEn = SessionClinicalSummary.Build(s, prev, AppLanguage.English);
            string htmlFile = "";
            string found = ReportExporter.TryFindSessionHtml(s);
            if (!string.IsNullOrEmpty(found))
            {
                htmlFile = Path.GetFileName(found);
                if (htmlFile.EndsWith(PatientVault.EncExtension, StringComparison.OrdinalIgnoreCase))
                    htmlFile = htmlFile.Substring(0, htmlFile.Length - PatientVault.EncExtension.Length);
            }

            sb.Append("{dt:\"").Append(EscapeJs(s.dateTime)).Append("\",")
              .Append("rMax:").Append(SessionHistoryFilter.EffectiveRightMax(s).ToString("F1", Inv)).Append(',')
              .Append("lMax:").Append(SessionHistoryFilter.EffectiveLeftMax(s).ToString("F1", Inv)).Append(',')
              .Append("avg:").Append(s.averageROM.ToString("F1", Inv)).Append(',')
              .Append("max:").Append(SessionHistoryFilter.EffectiveMax(s).ToString("F1", Inv)).Append(',')
              .Append("strain:").Append(s.peakStrain.ToString("F3", Inv)).Append(',')
              .Append("meanS:").Append(s.meanStrain.ToString("F3", Inv)).Append(',')
              .Append("dtwR:").Append(s.movementScoreRight.ToString("F1", Inv)).Append(',')
              .Append("dtwL:").Append(s.movementScoreLeft.ToString("F1", Inv)).Append(',')
              .Append("comp:").Append(s.compensationEvents).Append(',')
              .Append("reps:").Append(done).Append(',')
              .Append("rRep:").Append(rRep).Append(',')
              .Append("lRep:").Append(lRep).Append(',')
              .Append("invR:").Append(s.rightInvalidReps).Append(',')
              .Append("invL:").Append(s.leftInvalidReps).Append(',')
              .Append("target:").Append(s.targetReps).Append(',')
              .Append("tAngle:").Append(target.ToString("F0", Inv)).Append(',')
              .Append("rate:").Append(s.completionRate.ToString("F1", Inv)).Append(',')
              .Append("dur:\"").Append(EscapeJs(FormatDuration(s.durationSeconds))).Append("\",")
              .Append("hasR:").Append(hasR ? "1" : "0").Append(',')
              .Append("hasL:").Append(hasL ? "1" : "0").Append(',')
              .Append("region:").Append(s.bodyRegionId).Append(',')
              .Append("move:").Append(s.movementId).Append(',')
              .Append("html:\"").Append(EscapeJs(htmlFile)).Append("\",")
              .Append("notesTr:\"").Append(EscapeJs(notesTr)).Append("\",")
              .Append("notesEn:\"").Append(EscapeJs(notesEn)).Append("\"}");
        }
        sb.Append("];var INITIAL_DATE='").Append(SessionHistoryFilter.ModeJsId(dateFilter)).Append("';");
        sb.Append("var INITIAL_QUALITY='").Append(SessionHistoryFilter.ModeJsId(qualityFilter)).Append("';");
        sb.Append("var INITIAL_REGION='").Append(SessionHistoryFilter.ModeJsId(regionFilter)).Append("';");
        sb.Append("var INITIAL_MOVEMENT='").Append(SessionHistoryFilter.ModeJsId(movementFilter)).Append("';");
        sb.Append("var HIGH_STRAIN=").Append(SessionHistoryFilter.HighStrainThreshold.ToString("F2", Inv)).Append(';');
        AppendMovementOptionsJson(sb);
        AppendI18nDict(sb);
        sb.Append(FilterScript);
        sb.Append("</script>");
        ReportHtmlLang.AppendToggleScript(sb, htmlLang,
            "if(typeof onReportLangChanged==='function')onReportLangChanged();");
        sb.Append("</body></html>");

        string baseName = Sanitize(profile != null ? profile.FileNameSafe : displayName)
                          + (LanguageSettings.IsEnglish ? "_Progress_" : "_Ilerleme_")
                          + now.ToString("yyyyMMdd");
        string patientDir = PatientVault.GetPatientDirectory(profile);
        return PatientVault.WriteEncrypted(patientDir, baseName + ".html", sb.ToString());
    }

    private const string FilterScript = @"
var dateFilter=INITIAL_DATE, qualityFilter=INITIAL_QUALITY, regionFilter=INITIAL_REGION, movementFilter=INITIAL_MOVEMENT, openSessionIdx=-1;
function L(k){var pack=I18N[typeof REPORT_LANG!=='undefined'?REPORT_LANG:'tr']||I18N.tr;return pack[k]||k;}
function onReportLangChanged(){applyFilter();if(openSessionIdx>=0)openSession(openSessionIdx);}
function sessionNotes(s){return (REPORT_LANG==='en'?(s.notesEn||s.notesTr):(s.notesTr||s.notesEn))||'';}
function parseDt(s){if(!s)return null;var m=s.match(/(\d{1,2})[./](\d{1,2})[./](\d{4})\s+(\d{1,2}):(\d{2})/);if(m)return new Date(+m[3],+m[2]-1,+m[1],+m[4],+m[5]);var d=Date.parse(s);return isNaN(d)?null:new Date(d);}
function takeLast(n,count){var start=Math.max(0,n-count),out=[];for(var i=start;i<n;i++)out.push(i);return out;}
function byDays(days){var cut=new Date();cut.setHours(0,0,0,0);cut.setDate(cut.getDate()-days);var out=[];for(var i=0;i<SESSIONS.length;i++){var d=parseDt(SESSIONS[i].dt);if(!d||d>=cut)out.push(i);}return out;}
function dateIdx(){
  var n=SESSIONS.length;
  switch(dateFilter){
    case 'week': return byDays(7);
    case 'month': return byDays(30);
    case 'quarter': return byDays(90);
    case 'last5': return takeLast(n,5);
    case 'last10': return takeLast(n,10);
    case 'last20': return takeLast(n,20);
    default: var a=[];for(var i=0;i<n;i++)a.push(i);return a;
  }
}
function qualityPass(s){
  switch(qualityFilter){
    case 'withComp': return s.comp>0;
    case 'noComp': return s.comp<=0;
    case 'highStrain': return s.strain>=HIGH_STRAIN;
    case 'incomplete': return s.target>0&&(s.reps<s.target||s.rate<99.5);
    case 'rightArm': return !!s.hasR;
    case 'leftArm': return !!s.hasL;
    default: return true;
  }
}
function regionIdFromFilter(f){
  switch(f){
    case 'regionShoulder': return 0;
    case 'regionArm': return 1;
    case 'regionElbow': return 2;
    case 'regionNeck': return 3;
    case 'regionLeg': return 4;
    case 'regionAnkle': return 5;
    default: return -1;
  }
}
function moveIdFromFilter(f){
  if(!f||f==='all') return -1;
  if(f.indexOf('move')!==0) return -1;
  var n=parseInt(f.substring(4),10);
  return isNaN(n)?-1:n;
}
function regionPass(s){
  if(regionFilter==='all') return true;
  var want=regionIdFromFilter(regionFilter);
  if(want<0) return true;
  var r=s.region|0, m=s.move|0;
  if(want===0 && r===0 && m===0) return true;
  return r===want;
}
function movementPass(s){
  if(movementFilter==='all') return true;
  var want=moveIdFromFilter(movementFilter);
  if(want<0) return true;
  var m=s.move|0, r=s.region|0;
  if(want===0 && r===0 && m===0) return true;
  return m===want;
}
function exercisePass(s){ return regionPass(s)&&movementPass(s); }
function filteredIdx(){var base=dateIdx(),out=[];for(var k=0;k<base.length;k++){var i=base[k];if(qualityPass(SESSIONS[i])&&exercisePass(SESSIONS[i]))out.push(i);}return out;}
function pct(a,b){if(a<1)return 0;return ((b-a)/a)*100;}
function fmtPct(p){return (p>=0?'+':'')+Math.round(p)+'%';}
function setDateFilter(f){dateFilter=f;var sel=document.getElementById('date-select');if(sel&&sel.value!==f)sel.value=f;applyFilter();}
function setQualityFilter(f){qualityFilter=f;var sel=document.getElementById('quality-select');if(sel&&sel.value!==f)sel.value=f;applyFilter();}
function setRegionFilter(f){regionFilter=f;var sel=document.getElementById('region-select');if(sel&&sel.value!==f)sel.value=f;rebuildMovementSelect();applyFilter();}
function setMovementFilter(f){movementFilter=f;var sel=document.getElementById('movement-select');if(sel&&sel.value!==f)sel.value=f;applyFilter();}
function rebuildMovementSelect(){
  var sel=document.getElementById('movement-select'); if(!sel||!MOVEMENT_OPTIONS) return;
  var opts=MOVEMENT_OPTIONS[regionFilter]||MOVEMENT_OPTIONS.all||[];
  var keep=movementFilter;
  sel.innerHTML='';
  for(var i=0;i<opts.length;i++){
    var o=document.createElement('option');
    o.value=opts[i].id;
    o.textContent=(REPORT_LANG==='en'?opts[i].en:opts[i].tr);
    if(opts[i].id===keep) o.selected=true;
    sel.appendChild(o);
  }
  var found=false;
  for(var j=0;j<opts.length;j++){ if(opts[j].id===keep){found=true;break;} }
  if(!found){ movementFilter='all'; sel.value='all'; }
}
function applyFilter(){var idx=filteredIdx();var rows=document.querySelectorAll('#hist-body tr');var show={};for(var k=0;k<idx.length;k++)show[idx[k]]=true;var visible=0;for(var i=0;i<rows.length;i++){var on=!!show[i];rows[i].style.display=on?'':'none';if(on){visible++;rows[i].querySelector('.idx').textContent=visible;}}var cards=document.getElementById('summary-cards');if(idx.length<2){cards.innerHTML='<div class=""card""><div class=""k"">'+L('sessions')+'</div><div class=""v"">'+idx.length+'</div></div><div class=""card""><div class=""k"">'+L('pr')+'</div><div class=""v"">'+L('need')+'</div></div>';}else{var first=SESSIONS[idx[0]],last=SESSIONS[idx[idx.length-1]];var pr=fmtPct(pct(first.rMax>1?first.rMax:first.max,last.rMax>1?last.rMax:last.max));var pl=fmtPct(pct(first.lMax>1?first.lMax:first.max,last.lMax>1?last.lMax:last.max));var chg=last.max-first.max;var chgs=(chg>=0?'+':'')+Math.round(chg)+'\u00B0';cards.innerHTML='<div class=""card""><div class=""k"">'+L('sessions')+'</div><div class=""v"">'+idx.length+'</div></div><div class=""card""><div class=""k"">'+L('pr')+'</div><div class=""v"">'+pr+'</div></div><div class=""card""><div class=""k"">'+L('pl')+'</div><div class=""v"">'+pl+'</div></div><div class=""card""><div class=""k"">'+L('chg')+'</div><div class=""v"">'+chgs+'</div></div>';}redrawChart();}
function xTickStep(n){
  if(n<=12) return 1;
  if(n<=25) return 5;
  if(n<=40) return 10;
  if(n<=60) return 15;
  if(n<=90) return 20;
  return 30;
}
function yScaleFromData(idx, showR, showL, showAvg){
  var m=0;
  for(var i=0;i<idx.length;i++){
    var s=SESSIONS[idx[i]];
    if(showR) m=Math.max(m,s.rMax||0);
    if(showL) m=Math.max(m,s.lMax||0);
    if(showAvg) m=Math.max(m,s.avg>0?s.avg:s.max||0);
    m=Math.max(m,s.tAngle||0);
  }
  if(m<=0) m=180;
  if(m<=90) return {max:90, step:15};
  if(m<=120) return {max:120, step:20};
  if(m<=150) return {max:150, step:30};
  return {max:180, step:30};
}
function redrawChart(){
  var idx=filteredIdx();
  var host=document.getElementById('chart-host');
  if(idx.length<1){host.innerHTML='<p class=""muted"">'+L('need')+'</p>';return;}
  var showR=document.getElementById('chk-r').checked;
  var showL=document.getElementById('chk-l').checked;
  var showAvg=document.getElementById('chk-avg').checked;
  var showS=document.getElementById('chk-s').checked;
  var W=900,H=380,pl=58,pr=showS?58:28,pt=28,pb=48;
  var ys=yScaleFromData(idx,showR,showL,showAvg);
  var ymax=ys.max, ystep=ys.step;
  var n=idx.length;
  var xStep=xTickStep(n);
  function mapX(i){return pl+(W-pl-pr)*(n<=1?0.5:(i/Math.max(1,n-1)));}
  function mapY(a){return (H-pb)-(H-pt-pb)*Math.max(0,Math.min(1,a/ymax));}
  function mapYs(s){return (H-pb)-(H-pt-pb)*Math.max(0,Math.min(1,s));}
  function poly(getter,color,yMap){
    var pts=[],cir='';
    for(var i=0;i<n;i++){
      var v=getter(SESSIONS[idx[i]]);
      var x=mapX(i), y=yMap(v);
      pts.push(x+','+y);
      cir+='<circle class=""pt"" cx=""'+x+'"" cy=""'+y+'"" r=""5"" fill=""'+color+'"" stroke=""#fff"" stroke-width=""1.5"" data-i=""'+idx[i]+'"" onclick=""openSession('+idx[i]+')""/>';
    }
    return '<g><polyline fill=""none"" stroke=""'+color+'"" stroke-width=""2"" points=""'+pts.join(' ')+'""/>'+cir+'</g>';
  }
  var svg='<svg viewBox=""0 0 '+W+' '+H+'"" style=""width:100%;max-width:900px;border:1px solid #eee;border-radius:8px;background:#fafafa"" xmlns=""http://www.w3.org/2000/svg"">';
  for(var d=0;d<=ymax;d+=ystep){
    var y=mapY(d);
    svg+='<line x1=""'+pl+'"" y1=""'+y+'"" x2=""'+(W-pr)+'"" y2=""'+y+'"" stroke=""#e8e8e8""/>';
    svg+='<text x=""'+(pl-8)+'"" y=""'+(y+4)+'"" font-size=""11"" fill=""#666"" text-anchor=""end"">'+d+'\u00B0</text>';
  }
  if(showS){
    for(var p=0;p<=100;p+=25){
      var y2=mapYs(p/100);
      svg+='<text x=""'+(W-pr+8)+'"" y=""'+(y2+4)+'"" font-size=""11"" fill=""#8e44ad"" text-anchor=""start"">'+p+'%</text>';
    }
  }
  for(var t=1;t<=n;t++){
    if(t!==1 && t!==n && (t%xStep)!==0) continue;
    var xi=mapX(t-1);
    svg+='<line x1=""'+xi+'"" y1=""'+(H-pb)+'"" x2=""'+xi+'"" y2=""'+(H-pb+5)+'"" stroke=""#aaa""/>';
    svg+='<text x=""'+xi+'"" y=""'+(H-pb+18)+'"" font-size=""11"" fill=""#666"" text-anchor=""middle"">'+t+'</text>';
  }
  svg+='<text x=""'+(W/2)+'"" y=""'+(H-6)+'"" font-size=""12"" fill=""#444"" text-anchor=""middle"">'+L('xAxis')+'</text>';
  svg+='<text x=""14"" y=""'+(H/2)+'"" font-size=""12"" fill=""#444"" text-anchor=""middle"" transform=""rotate(-90 14 '+(H/2)+')"">'+L('yAxis')+'</text>';
  if(showR)svg+=poly(function(s){return s.rMax;},'#27ae60',mapY);
  if(showL)svg+=poly(function(s){return s.lMax;},'#2e86de',mapY);
  if(showAvg)svg+=poly(function(s){return s.avg>0?s.avg:s.max;},'#e67e22',mapY);
  if(showS)svg+=poly(function(s){return s.strain;},'#8e44ad',mapYs);
  svg+='</svg>';
  host.innerHTML=svg;
}
function openSession(i){
  if(i<0||i>=SESSIONS.length) return;
  openSessionIdx=i;
  var s=SESSIONS[i];
  var modal=document.getElementById('detail-modal');
  document.getElementById('d-title').textContent=L('detail')+' #'+(i+1);
  document.getElementById('d-sub').textContent=s.dt||'';
  var sp=Math.round((s.strain||0)*100);
  var dtwR=s.dtwR>=0?Math.round(s.dtwR)+'%':'—';
  var dtwL=s.dtwL>=0?Math.round(s.dtwL)+'%':'—';
  document.getElementById('d-metrics').innerHTML=
    '<div class=""card""><div class=""k"">'+L('rMax')+'</div><div class=""v"">'+Math.round(s.rMax)+'\u00B0</div></div>'+
    '<div class=""card""><div class=""k"">'+L('lMax')+'</div><div class=""v"">'+Math.round(s.lMax)+'\u00B0</div></div>'+
    '<div class=""card""><div class=""k"">'+L('tgt')+'</div><div class=""v"">'+Math.round(s.tAngle)+'\u00B0</div></div>'+
    '<div class=""card""><div class=""k"">'+L('reps')+'</div><div class=""v"">'+s.reps+'/'+s.target+'</div></div>'+
    '<div class=""card""><div class=""k"">'+L('comp')+'</div><div class=""v"">'+s.comp+'</div></div>'+
    '<div class=""card""><div class=""k"">'+L('strain')+'</div><div class=""v"">%'+sp+'</div></div>'+
    '<div class=""card""><div class=""k"">'+L('dtwR')+'</div><div class=""v"">'+dtwR+'</div></div>'+
    '<div class=""card""><div class=""k"">'+L('dtwL')+'</div><div class=""v"">'+dtwL+'</div></div>';
  document.getElementById('d-notes').textContent=sessionNotes(s);
  var chartHost=document.getElementById('d-chart');
  var leg=document.getElementById('d-chart-legend');
  if(leg) leg.textContent=L('chartLeg');
  var end=i+1;
  var bw=640,bh=180,padL=36,padR=12,padT=12,padB=28;
  var plotW=bw-padL-padR, plotH=bh-padT-padB;
  var denom=Math.max(1,end-1);
  function mapX(j){return padL+plotW*(j/denom);}
  function mapYa(v){return padT+plotH*(1-Math.max(0,Math.min(1,(v||0)/180)));}
  function mapYs(v){return padT+plotH*(1-Math.max(0,Math.min(1,v||0)));}
  var bsvg='<svg viewBox=""0 0 '+bw+' '+bh+'"" style=""width:100%;max-width:640px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px"" xmlns=""http://www.w3.org/2000/svg"">';
  for(var g=0;g<=180;g+=45){var gy=mapYa(g);bsvg+='<line x1=""'+padL+'"" y1=""'+gy+'"" x2=""'+(bw-padR)+'"" y2=""'+gy+'"" stroke=""#e2e8f0""/>';bsvg+='<text x=""'+(padL-4)+'"" y=""'+(gy+3)+'"" font-size=""9"" fill=""#94a3b8"" text-anchor=""end"">'+g+'</text>';}
  var tA=s.tAngle||0;
  if(tA>1){var ty=mapYa(tA);bsvg+='<line x1=""'+padL+'"" y1=""'+ty+'"" x2=""'+(bw-padR)+'"" y2=""'+ty+'"" stroke=""#e67e22"" stroke-width=""1.5"" stroke-dasharray=""5 4""/>';}
  function polyPts(getY){var p='';for(var j=0;j<end;j++){var sj=SESSIONS[j];var x=mapX(j);var y=getY(sj);if(j)p+=' ';p+=x+','+y;}return p;}
  bsvg+='<polyline fill=""none"" stroke=""#27ae60"" stroke-width=""2"" points=""'+polyPts(function(sj){return mapYa(sj.rMax);})+'""/>';
  bsvg+='<polyline fill=""none"" stroke=""#2e86de"" stroke-width=""2"" points=""'+polyPts(function(sj){return mapYa(sj.lMax);})+'""/>';
  bsvg+='<polyline fill=""none"" stroke=""#8e44ad"" stroke-width=""2"" points=""'+polyPts(function(sj){return mapYs(sj.strain);})+'""/>';
  for(var j=0;j<end;j++){
    var sj=SESSIONS[j],x=mapX(j);
    bsvg+='<circle cx=""'+x+'"" cy=""'+mapYa(sj.rMax)+'"" r=""'+(j===i?4.5:2.5)+'"" fill=""#27ae60""/>';
    bsvg+='<circle cx=""'+x+'"" cy=""'+mapYa(sj.lMax)+'"" r=""'+(j===i?4.5:2.5)+'"" fill=""#2e86de""/>';
    bsvg+='<circle cx=""'+x+'"" cy=""'+mapYs(sj.strain)+'"" r=""'+(j===i?4:2)+'"" fill=""#8e44ad""/>';
  }
  bsvg+='<text x=""'+(bw/2)+'"" y=""'+(bh-6)+'"" font-size=""11"" fill=""#64748b"" text-anchor=""middle"">'+L('xAxis')+' 1\u2013'+end+'</text>';
  bsvg+='</svg>';
  chartHost.innerHTML=bsvg;
  var a=document.getElementById('d-html');
  var wrap=document.getElementById('d-html-wrap');
  var oldHint=wrap.querySelector('.nohtml');
  if(oldHint) oldHint.remove();
  if(s.html){
    a.href=s.html;
    a.style.display='inline';
  }else{
    a.removeAttribute('href');
    a.style.display='none';
    var hint=document.createElement('span');
    hint.className='nohtml muted';
    hint.textContent=L('noHtml');
    wrap.appendChild(hint);
  }
  modal.style.display='flex';
}
function closeSession(){openSessionIdx=-1;document.getElementById('detail-modal').style.display='none';}
function backdropClose(e){if(e.target&&e.target.id==='detail-modal')closeSession();}
setDateFilter(INITIAL_DATE);setQualityFilter(INITIAL_QUALITY);setRegionFilter(INITIAL_REGION);rebuildMovementSelect();setMovementFilter(INITIAL_MOVEMENT);
";

    private static void AppendI18nDict(StringBuilder sb)
    {
        sb.Append("var REPORT_LANG='").Append(ReportHtmlLang.InitialLangCode).Append("';");
        sb.Append("var I18N={tr:{");
        AppendI18nPack(sb, AppLanguage.Turkish);
        sb.Append("},en:{");
        AppendI18nPack(sb, AppLanguage.English);
        sb.Append("}};");
    }

    private static void AppendI18nPack(StringBuilder sb, AppLanguage lang)
    {
        AppendPair(sb, "sessions", Loc.T("report.sessions.count", lang), true);
        AppendPair(sb, "pr", Loc.T("report.progress.r", lang), false);
        AppendPair(sb, "pl", Loc.T("report.progress.l", lang), false);
        AppendPair(sb, "chg", Loc.T("report.change", lang), false);
        AppendPair(sb, "need", Loc.T("progress.need2", lang), false);
        AppendPair(sb, "detail", Loc.T("detail.title", lang), false);
        AppendPair(sb, "rMax", Loc.T("report.col.rightMax", lang), false);
        AppendPair(sb, "lMax", Loc.T("report.col.leftMax", lang), false);
        AppendPair(sb, "tgt", Loc.T("report.targetAngle", lang), false);
        AppendPair(sb, "strain", Loc.T("report.col.strain", lang), false);
        AppendPair(sb, "dtw", Loc.T("menu.hist.dtw", lang), false);
        AppendPair(sb, "dtwR", Loc.T("menu.hist.dtw.right", lang), false);
        AppendPair(sb, "dtwL", Loc.T("menu.hist.dtw.left", lang), false);
        AppendPair(sb, "comp", Loc.T("report.compensation", lang), false);
        AppendPair(sb, "reps", Loc.T("menu.hist.reps", lang), false);
        AppendPair(sb, "xAxis", Loc.T("report.progress.xAxis", lang), false);
        AppendPair(sb, "yAxis", Loc.T("report.progress.yAxis", lang), false);
        AppendPair(sb, "noHtml", Loc.T("detail.html.missing", lang), false);
        AppendPair(sb, "chartLeg", Loc.T("detail.chart.legend", lang), false);
    }

    private static void AppendPair(StringBuilder sb, string key, string value, bool first)
    {
        if (!first) sb.Append(',');
        sb.Append(key).Append(":'").Append(EscapeJs(value)).Append('\'');
    }

    private static void AppendSelect(
        StringBuilder sb, string id, string labelLocKey, HistoryFilterMode[] modes,
        HistoryFilterMode selected, string onChangeFn)
    {
        sb.Append("<label for=\"").Append(id).Append("\"><strong>");
        ReportHtmlLang.AppendLocText(sb, "span", labelLocKey);
        sb.Append(":</strong></label> ");
        sb.Append("<select id=\"").Append(id).Append("\" onchange=\"")
          .Append(onChangeFn).Append("(this.value)\">");
        for (int i = 0; i < modes.Length; i++)
        {
            HistoryFilterMode mode = modes[i];
            string js = SessionHistoryFilter.ModeJsId(mode);
            string tr = SessionHistoryFilter.ModeLabel(mode, AppLanguage.Turkish);
            string en = SessionHistoryFilter.ModeLabel(mode, AppLanguage.English);
            sb.Append("<option value=\"").Append(js).Append('"');
            if (mode == selected) sb.Append(" selected");
            ReportHtmlLang.AppendBilingualAttrPair(sb, tr, en);
            sb.Append('>').Append(Escape(LanguageSettings.IsEnglish ? en : tr)).Append("</option>");
        }
        sb.Append("</select> ");
    }

    private static void AppendMovementOptionsJson(StringBuilder sb)
    {
        sb.Append("var MOVEMENT_OPTIONS={");
        AppendMovementOptionGroup(sb, "all", SessionHistoryFilter.AllMovementModes, first: true);
        HistoryFilterMode[] regionModes = SessionHistoryFilter.RegionModes;
        for (int i = 0; i < regionModes.Length; i++)
        {
            HistoryFilterMode regionMode = regionModes[i];
            if (regionMode == HistoryFilterMode.All) continue;
            AppendMovementOptionGroup(sb, SessionHistoryFilter.ModeJsId(regionMode),
                SessionHistoryFilter.GetMovementModes(regionMode), first: false);
        }
        sb.Append("};");
    }

    private static void AppendMovementOptionGroup(
        StringBuilder sb, string key, HistoryFilterMode[] modes, bool first)
    {
        if (!first) sb.Append(',');
        sb.Append('"').Append(key).Append("\":[");
        for (int i = 0; i < modes.Length; i++)
        {
            if (i > 0) sb.Append(',');
            HistoryFilterMode mode = modes[i];
            sb.Append("{id:'").Append(SessionHistoryFilter.ModeJsId(mode)).Append("',tr:'")
              .Append(EscapeJs(SessionHistoryFilter.ModeLabel(mode, AppLanguage.Turkish))).Append("',en:'")
              .Append(EscapeJs(SessionHistoryFilter.ModeLabel(mode, AppLanguage.English))).Append("'}");
        }
        sb.Append(']');
    }

    private static void AppendStatsSection(StringBuilder sb, ProgressStats st)
    {
        if (!st.hasStats) return;

        sb.Append("<div class=\"stats-box\">");
        sb.Append("<div class=\"stats-head\">");
        ReportHtmlLang.AppendLocText(sb, "span", "stats.title");
        sb.Append("</div><table class=\"info\"><tbody>");

        AppendStatRow(sb, "stats.sessions", st.sessionCount.ToString(Inv));
        AppendStatRow(sb, "stats.romTrend",
            Signed(st.romTrendDegrees) + "° / " + Signed(st.romTrendPct) + "%");
        if (!float.IsNaN(st.rightRomTrendDegrees))
            AppendStatRow(sb, "stats.romTrendRight", Signed(st.rightRomTrendDegrees) + "°");
        if (!float.IsNaN(st.leftRomTrendDegrees))
            AppendStatRow(sb, "stats.romTrendLeft", Signed(st.leftRomTrendDegrees) + "°");
        if (st.meanCompletionPct >= 0f)
            AppendStatRow(sb, "stats.completion", st.meanCompletionPct.ToString("F0", Inv) + "%");
        if (st.invalidRepRatePct >= 0f)
            AppendStatRow(sb, "stats.invalidRate", st.invalidRepRatePct.ToString("F0", Inv) + "%");
        if (st.assistedRepRatePct >= 0f)
            AppendStatRow(sb, "stats.assistedRate", st.assistedRepRatePct.ToString("F0", Inv) + "%");
        AppendStatRow(sb, "stats.repsIndep", st.totalIndependentReps.ToString(Inv));
        AppendStatRow(sb, "stats.repsAssisted", st.totalAssistedReps.ToString(Inv));
        if (st.sessionsPerWeekObserved >= 0f)
            AppendStatRow(sb, "stats.sessionsWeek", st.sessionsPerWeekObserved.ToString("F2", Inv));
        if (st.plannedSessionsPerWeek > 0)
            AppendStatRow(sb, "stats.plannedWeek", st.plannedSessionsPerWeek.ToString(Inv));
        if (st.adherencePct >= 0f)
            AppendStatRow(sb, "stats.adherence", st.adherencePct.ToString("F0", Inv) + "%");
        AppendStatRow(sb, "stats.meanRom", st.unweightedMeanRom.ToString("F1", Inv) + "°");
        AppendStatRow(sb, "stats.qwMeanRom", st.qualityWeightedMeanRom.ToString("F1", Inv) + "°");
        if (st.meanQualityScore >= 0f)
            AppendStatRow(sb, "stats.meanQuality",
                (st.meanQualityScore * 100f).ToString("F0", Inv) + "%");
        if (st.meanPeakStrain >= 0f)
            AppendStatRow(sb, "stats.meanPeakStrain",
                (st.meanPeakStrain * 100f).ToString("F0", Inv) + "%");
        AppendStatRow(sb, "stats.compSessions",
            st.sessionsWithCompensation.ToString(Inv) + " / " + st.sessionCount.ToString(Inv)
            + " (" + st.compensationSessionRatePct.ToString("F0", Inv) + "%)");
        AppendStatRow(sb, "stats.eventsComp", st.totalCompensationEvents.ToString(Inv));
        AppendStatRow(sb, "stats.eventsJump", st.totalTrackingJumps.ToString(Inv));
        AppendStatRow(sb, "stats.eventsSecond", st.totalSecondPersonEvents.ToString(Inv));
        AppendStatRow(sb, "stats.eventsAssistNear", st.totalAssistNearEvents.ToString(Inv));
        AppendStatRow(sb, "stats.formula", ProgressStatsAggregator.FormulaVersion);

        sb.Append("</tbody></table>");
        ReportHtmlLang.AppendLocText(sb, "p", "stats.note", "class=\"muted stats-foot\"");
        sb.Append("</div>");
    }

    private static void AppendStatRow(StringBuilder sb, string locKey, string value)
    {
        sb.Append("<tr><th>");
        ReportHtmlLang.AppendLocText(sb, "span", locKey);
        sb.Append("</th><td>").Append(Escape(value)).Append("</td></tr>");
    }

    private static string Signed(float v)
    {
        string sign = v > 0f ? "+" : "";
        return sign + v.ToString("F1", Inv);
    }

    private static void AppendBaseStyle(StringBuilder sb)
    {
        sb.Append("<style>"
            + "body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222;background:#fff;}"
            + "h1{font-size:22px;margin:0 0 4px;}h2{font-size:17px;margin:24px 0 8px;border-bottom:1px solid #eee;padding-bottom:4px;}"
            + "h3{font-size:15px;margin:16px 0 8px;}"
            + ".muted{color:#888;font-size:12px;margin:0 0 16px;}"
            + ".toggles{margin:8px 0 12px;padding:10px 14px;background:#f4f6f8;border-radius:8px;font-size:14px;}"
            + ".toggles label{margin-right:8px;cursor:pointer;user-select:none;}"
            + ".filter-bar select{min-width:200px;padding:8px 12px;font-size:14px;border:1px solid #ccd;border-radius:8px;background:#fff;margin:4px 16px 4px 4px;}"
            + ".cards{display:flex;flex-wrap:wrap;gap:12px;margin:12px 0;}"
            + ".card{background:#f4f6f8;border-radius:8px;padding:12px 16px;min-width:120px;}"
            + ".card .k{font-size:12px;color:#888;}.card .v{font-size:20px;font-weight:600;}"
            + ".stats-box{background:#f8fafc;border:1px solid #e2e8f0;border-radius:12px;padding:0;margin:12px 0 20px;overflow:hidden;box-shadow:0 1px 2px rgba(15,23,42,.04);}"
            + ".stats-head{padding:14px 18px;background:#1e2e46;color:#f8fafc;font-size:15px;font-weight:700;letter-spacing:.02em;}"
            + ".stats-foot{margin:0;padding:10px 18px 14px;font-size:12px;background:#fff;border-top:1px solid #e2e8f0;}"
            + ".stats-box table{width:100%;max-width:100%;border-collapse:collapse;font-size:13px;}"
            + ".stats-box tr:nth-child(even){background:#fff;}"
            + ".stats-box tr:nth-child(odd){background:#f8fafc;}"
            + ".stats-box tr:hover{background:#eef2ff;}"
            + ".stats-box th{text-align:left;padding:11px 18px;font-weight:600;color:#475569;width:52%;border-bottom:1px solid #e2e8f0;vertical-align:middle;}"
            + ".stats-box td{text-align:right;padding:11px 18px;font-weight:600;color:#0f172a;border-bottom:1px solid #e2e8f0;font-variant-numeric:tabular-nums;vertical-align:middle;}"
            + ".stats-box tr:last-child th,.stats-box tr:last-child td{border-bottom:none;}"
            + ".chart-legend{font-size:12px;margin:6px 0 12px;color:#64748b;}"
            + ".legend{font-size:12px;color:#555;}"
            + ".dot{display:inline-block;width:10px;height:10px;margin:0 4px 0 12px;vertical-align:middle;}"
            + "table{border-collapse:collapse;width:100%;font-size:13px;}"
            + "th,td{border:1px solid #e0e0e0;padding:6px 10px;text-align:center;}th{background:#f4f6f8;}"
            + "tr.click-row{cursor:pointer;}tr.click-row:hover{background:#eef8f3;}"
            + "circle.pt{cursor:pointer;}"
            + ".disclaimer{margin-top:24px;font-size:11px;color:#aaa;font-style:italic;}"
            + "@media print{"
            + ".toggles,.filter-bar,.no-print{display:none!important;}"
            + ".dot{print-color-adjust:exact;-webkit-print-color-adjust:exact;}"
            + "}"
            + ".modal{display:none;position:fixed;inset:0;background:rgba(0,0,0,.55);align-items:flex-start;justify-content:center;padding:32px 16px;overflow:auto;z-index:50;}"
            + ".modal-card{position:relative;background:#fff;border-radius:12px;padding:24px 28px;max-width:720px;width:100%;box-shadow:0 12px 40px rgba(0,0,0,.25);}"
            + ".modal-close{position:absolute;top:10px;right:14px;border:none;background:transparent;font-size:28px;line-height:1;cursor:pointer;color:#666;}"
            + ".notes{white-space:pre-wrap;font-family:Segoe UI,Arial,sans-serif;font-size:13px;line-height:1.45;background:#f8fafb;border:1px solid #eee;border-radius:8px;padding:12px 14px;max-height:280px;overflow:auto;}"
            + ".btn-back{margin-top:16px;padding:10px 18px;border:none;border-radius:8px;background:#1e2e46;color:#fff;font-size:14px;cursor:pointer;}"
            + "#d-html{display:inline-block;margin-top:8px;color:#0b6e4f;font-weight:600;}");
        ReportHtmlLang.AppendToggleStyle(sb);
        sb.Append("</style>");
    }

    private static void AppendLegendSwatch(StringBuilder sb, string hexColor)
    {
        sb.Append("<svg class=\"dot\" width=\"10\" height=\"10\" viewBox=\"0 0 10 10\" aria-hidden=\"true\" xmlns=\"http://www.w3.org/2000/svg\">")
          .Append("<circle cx=\"5\" cy=\"5\" r=\"5\" fill=\"").Append(hexColor).Append("\"/>")
          .Append("</svg> ");
    }

    private static string FormatDuration(float seconds)
    {
        int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int m = total / 60;
        int s = total % 60;
        return m.ToString("00", Inv) + ":" + s.ToString("00", Inv);
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    private static string EscapeJs(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\"", "\\\"")
                .Replace("\r", "")
                .Replace("\n", "\\n");
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Patient";
        var sb = new StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            else if (c == ' ') sb.Append('_');
        }
        return sb.Length > 0 ? sb.ToString() : "Patient";
    }
}
