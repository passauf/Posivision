using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Seans geçmişi detayı: tam ekran (okunabilirlik) — yalnızca o seansın ROM zaman serisi + AI özeti.
/// Tek tıkla önceki seansla yan yana karşılaştırma. SaMD Class B; KVKK: yerel, PII loglanmaz.
/// </summary>
public class SessionDetailPanel : MonoBehaviour
{
    private const float EdgeMargin = 12f;
    private const float ContentPad = 28f;

    private TextMeshProUGUI _title;
    private TextMeshProUGUI _subtitle;
    private TextMeshProUGUI _metricRom;
    private TextMeshProUGUI _metricSession;
    private TextMeshProUGUI _metricQuality;
    private TextMeshProUGUI _chartLegend;
    private TextMeshProUGUI _summary;
    private TextMeshProUGUI _htmlHint;
    private Button _htmlButton;
    private Button _compareButton;
    private SessionGraphRenderer _chart;
    private ScrollRect _summaryScroll;
    private string _htmlPath;
    private Transform _canvasRoot;
    private SessionEntry _entry;
    private SessionEntry _previous;
    private int _sessionNumber;
    private DataManager _dataManager;

    public static SessionDetailPanel Show(Transform canvasRoot, SessionEntry entry, SessionEntry previous)
    {
        return Show(canvasRoot, entry, previous, 0);
    }

    public static SessionDetailPanel Show(
        Transform canvasRoot, SessionEntry entry, SessionEntry previous, int sessionNumber)
    {
        if (canvasRoot == null || entry == null) return null;

        var existing = canvasRoot.GetComponentInChildren<SessionDetailPanel>(true);
        if (existing != null) Destroy(existing.gameObject);

        GameObject go = new GameObject("SessionDetailPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Canvas), typeof(GraphicRaycaster), typeof(SessionDetailPanel));
        go.transform.SetParent(canvasRoot, false);
        go.transform.SetAsLastSibling();

        var overlayCanvas = go.GetComponent<Canvas>();
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = 200;

        var panel = go.GetComponent<SessionDetailPanel>();
        panel._canvasRoot = canvasRoot;
        panel._dataManager = UnityEngine.Object.FindObjectOfType<DataManager>();
        panel.BuildUi();
        panel.Bind(entry, previous, sessionNumber);
        return panel;
    }

    private void BuildUi()
    {
        var rt = GetComponent<RectTransform>();
        Stretch(rt);
        var dim = GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.82f);
        dim.raycastTarget = true;

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = Vector2.zero;
        cardRt.anchorMax = Vector2.one;
        cardRt.offsetMin = new Vector2(EdgeMargin, EdgeMargin);
        cardRt.offsetMax = new Vector2(-EdgeMargin, -EdgeMargin);
        card.GetComponent<Image>().color = UiTheme.Panel;
        card.GetComponent<Image>().raycastTarget = true;

        _title = CreateTopLabel(card.transform, "Title", Loc.T("detail.title"), 26f, FontStyles.Bold,
            -14f, 32f);
        _subtitle = CreateTopLabel(card.transform, "Subtitle", "", 16f, FontStyles.Normal,
            -48f, 24f);
        _subtitle.color = UiTheme.TextMuted;

        GameObject metricsRow = new GameObject("MetricsRow", typeof(RectTransform));
        metricsRow.transform.SetParent(card.transform, false);
        var metricsRt = metricsRow.GetComponent<RectTransform>();
        metricsRt.anchorMin = new Vector2(0f, 0.78f);
        metricsRt.anchorMax = new Vector2(1f, 0.90f);
        metricsRt.offsetMin = new Vector2(ContentPad, 4f);
        metricsRt.offsetMax = new Vector2(-ContentPad, -4f);

        _metricRom = CreateMetricBox(metricsRow.transform, "RomBox", 0f, 0.32f);
        _metricSession = CreateMetricBox(metricsRow.transform, "SessionBox", 0.34f, 0.66f);
        _metricQuality = CreateMetricBox(metricsRow.transform, "QualityBox", 0.68f, 1f);

        GameObject chartGo = new GameObject("SessionChart", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        chartGo.transform.SetParent(card.transform, false);
        var chartRt = chartGo.GetComponent<RectTransform>();
        chartRt.anchorMin = new Vector2(0f, 0.60f);
        chartRt.anchorMax = new Vector2(1f, 0.77f);
        chartRt.offsetMin = new Vector2(ContentPad, 4f);
        chartRt.offsetMax = new Vector2(-ContentPad, -4f);
        var raw = chartGo.GetComponent<RawImage>();
        raw.color = Color.white;
        _chart = chartGo.AddComponent<SessionGraphRenderer>();
        _chart.SetGraphImage(raw);
        _chart.ConfigureDetailQuality();
        _chart.ShowRight = true;
        _chart.ShowLeft = true;
        _chart.ShowStrain = true;

        _chartLegend = CreateBandLabel(card.transform, "ChartLegend", Loc.T("detail.chart.legend"), 14f, FontStyles.Normal,
            new Vector2(0f, 0.565f), new Vector2(1f, 0.60f));
        _chartLegend.color = UiTheme.TextMuted;
        _chartLegend.alignment = TextAlignmentOptions.Center;

        CreateBandLabel(card.transform, "SummaryTitle", Loc.T("detail.summary.title"), 18f, FontStyles.Bold,
            new Vector2(0f, 0.525f), new Vector2(1f, 0.565f));

        const float barW = 18f;
        GameObject scrollGo = new GameObject("SummaryScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(card.transform, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0f, 0.12f);
        scrollRt.anchorMax = new Vector2(1f, 0.525f);
        scrollRt.offsetMin = new Vector2(ContentPad, 8f);
        scrollRt.offsetMax = new Vector2(-(ContentPad + barW + 8f), -4f);
        scrollGo.GetComponent<Image>().color = UiTheme.Card;
        scrollGo.GetComponent<Image>().raycastTarget = true;
        _summaryScroll = scrollGo.GetComponent<ScrollRect>();
        _summaryScroll.horizontal = false;
        _summaryScroll.vertical = true;
        _summaryScroll.movementType = ScrollRect.MovementType.Clamped;
        _summaryScroll.scrollSensitivity = 55f;
        _summaryScroll.inertia = true;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRt = viewport.GetComponent<RectTransform>();
        Stretch(vpRt);
        var vpImg = viewport.GetComponent<Image>();
        vpImg.color = Color.white;
        vpImg.raycastTarget = true;
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 100f);
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _summary = CreateTmp(content.transform, "SummaryBody", "", 18f, FontStyles.Normal);
        _summary.alignment = TextAlignmentOptions.TopLeft;
        _summary.enableWordWrapping = true;
        _summary.overflowMode = TextOverflowModes.Overflow;
        _summary.raycastTarget = false;
        var sumRt = _summary.rectTransform;
        sumRt.anchorMin = new Vector2(0f, 1f);
        sumRt.anchorMax = new Vector2(1f, 1f);
        sumRt.pivot = new Vector2(0.5f, 1f);
        sumRt.anchoredPosition = Vector2.zero;
        sumRt.sizeDelta = new Vector2(-28f, 0f);
        var fitter = _summary.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject barGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        barGo.transform.SetParent(card.transform, false);
        var barRt = barGo.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(1f, 0.12f);
        barRt.anchorMax = new Vector2(1f, 0.525f);
        barRt.pivot = new Vector2(1f, 0.5f);
        barRt.offsetMin = new Vector2(-(ContentPad + barW), 8f);
        barRt.offsetMax = new Vector2(-ContentPad, -4f);
        barGo.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.22f, 1f);

        GameObject sliding = new GameObject("Sliding Area", typeof(RectTransform));
        sliding.transform.SetParent(barGo.transform, false);
        Stretch(sliding.GetComponent<RectTransform>());
        var slideRt = sliding.GetComponent<RectTransform>();
        slideRt.offsetMin = new Vector2(2f, 2f);
        slideRt.offsetMax = new Vector2(-2f, -2f);

        GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(sliding.transform, false);
        Stretch(handle.GetComponent<RectTransform>());
        handle.GetComponent<Image>().color = UiTheme.Accent;

        var scrollbar = barGo.GetComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle.GetComponent<RectTransform>();
        scrollbar.targetGraphic = handle.GetComponent<Image>();
        scrollbar.value = 1f;

        _summaryScroll.content = contentRt;
        _summaryScroll.viewport = vpRt;
        _summaryScroll.verticalScrollbar = scrollbar;
        _summaryScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        _htmlHint = CreateBandLabel(card.transform, "HtmlHint", "", 15f, FontStyles.Normal,
            new Vector2(0f, 0.08f), new Vector2(1f, 0.12f));
        _htmlHint.color = UiTheme.TextMuted;

        CreateButton(card.transform, "Back", Loc.T("detail.btn.back"), UiTheme.ButtonNormal,
            new Vector2(-320f, 22f), new Vector2(200f, 52f), () => Destroy(gameObject));

        _compareButton = CreateButton(card.transform, "Compare", Loc.T("detail.btn.compare"), UiTheme.Accent,
            new Vector2(0f, 22f), new Vector2(300f, 52f), OnCompareClicked);
        SetPrimaryButtonText(_compareButton, UiTheme.Background);

        _htmlButton = CreateButton(card.transform, "OpenHtml", Loc.T("detail.btn.html"), UiTheme.ButtonNormal,
            new Vector2(320f, 22f), new Vector2(260f, 52f), OnOpenHtml);
    }

    private void Bind(SessionEntry s, SessionEntry previous, int sessionNumber)
    {
        _entry = s;
        _previous = previous;
        _sessionNumber = sessionNumber;

        _title.text = sessionNumber > 0
            ? Loc.Format("menu.hist.sessionN", sessionNumber)
            : Loc.T("detail.title");

        if (SessionHistoryFilter.TryParseSessionDate(s.dateTime, out System.DateTime dt))
            _subtitle.text = dt.ToString("HH:mm") + " · " + dt.ToString("dd/MM/yyyy");
        else
            _subtitle.text = string.IsNullOrEmpty(s.dateTime) ? "—" : s.dateTime;

        float r = SessionHistoryFilter.EffectiveRightMax(s);
        float l = SessionHistoryFilter.EffectiveLeftMax(s);
        float target = s.targetAngle > 1f ? s.targetAngle : 160f;
        int done = s.completedReps;
        if (done == 0 && (s.rightCompletedReps > 0 || s.leftCompletedReps > 0))
            done = s.rightCompletedReps + s.leftCompletedReps;

        string dtwR = s.movementScoreRight >= 0f ? Mathf.RoundToInt(s.movementScoreRight).ToString() : "—";
        string dtwL = s.movementScoreLeft >= 0f ? Mathf.RoundToInt(s.movementScoreLeft).ToString() : "—";

        if (_metricRom != null)
            _metricRom.text = Loc.Format("detail.box.rom",
                r.ToString("F0"), l.ToString("F0"), target.ToString("F0"));
        if (_metricSession != null)
            _metricSession.text = Loc.Format("detail.box.session",
                done, s.targetReps, s.compensationEvents, Mathf.RoundToInt(s.peakStrain * 100f));
        if (_metricQuality != null)
            _metricQuality.text = Loc.Format("detail.box.quality", dtwR, dtwL);

        bool hasSeries = s.seriesTimes != null && s.seriesTimes.Length >= 2
            && s.seriesRight != null && s.seriesLeft != null;

        _chartLegend.text = hasSeries
            ? Loc.T("detail.chart.legend")
            : Loc.T("detail.chart.noSeries");

        if (_chart != null)
        {
            if (hasSeries)
            {
                int n = s.seriesTimes.Length;
                float[] strain = s.seriesStrain != null && s.seriesStrain.Length >= n
                    ? s.seriesStrain
                    : null;
                float[] comps = s.seriesCompTimes ?? System.Array.Empty<float>();
                _chart.ShowRight = SessionHistoryFilter.ShowRight(s);
                _chart.ShowLeft = SessionHistoryFilter.ShowLeft(s);
                _chart.ShowStrain = strain != null;
                _chart.Draw(
                    s.seriesTimes,
                    s.seriesRight,
                    s.seriesLeft,
                    strain,
                    n,
                    comps,
                    comps.Length,
                    s.targetAngle,
                    180f,
                    s.seriesAssistRight,
                    s.seriesAssistLeft);
            }
            else
            {
                _chart.Draw(
                    System.Array.Empty<float>(),
                    System.Array.Empty<float>(),
                    System.Array.Empty<float>(),
                    null,
                    0,
                    System.Array.Empty<float>(),
                    0,
                    s.targetAngle,
                    180f,
                    null,
                    null);
            }
        }

        _summary.text = SessionClinicalSummary.Build(s, previous);
        if (_summaryScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_summary.rectTransform);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_summaryScroll.content);
            _summaryScroll.verticalNormalizedPosition = 1f;
        }

        _htmlPath = ReportExporter.TryFindSessionHtml(s);
        bool hasHtml = !string.IsNullOrEmpty(_htmlPath) && File.Exists(_htmlPath);
        if (_htmlButton != null) _htmlButton.gameObject.SetActive(hasHtml);
        if (_htmlHint != null)
            _htmlHint.text = hasHtml ? Loc.T("detail.html.found") : Loc.T("detail.html.missing");

        if (_compareButton != null)
            _compareButton.interactable = true;
    }

    private void OnCompareClicked()
    {
        if (_canvasRoot == null) return;
        DataManager dm = _dataManager != null ? _dataManager : UnityEngine.Object.FindObjectOfType<DataManager>();
        if (dm == null)
        {
            if (_htmlHint != null) _htmlHint.text = Loc.T("compare.picker.needData");
            return;
        }

        PatientProfile profile = dm.LoadProfile();
        PatientHistory raw = dm.LoadHistory();
        bool hasPatient = profile != null && !string.IsNullOrWhiteSpace(profile.firstName);
        PatientHistory history = PatientVault.FilterHistoryForPatient(raw, profile, fallbackToAll: !hasPatient);
        SessionComparePickerPanel.Show(_canvasRoot, dm, history, _entry);
    }

    private void OnOpenHtml()
    {
        if (string.IsNullOrEmpty(_htmlPath) || !File.Exists(_htmlPath)) return;

        Transform root = _canvasRoot != null ? _canvasRoot : transform.parent;
        ClinicianAccessPanel.OpenEncryptedHtmlReport(root, _htmlPath, _htmlHint);
    }

    private static TextMeshProUGUI CreateMetricBox(Transform parent, string name, float xMin, float xMax)
    {
        GameObject box = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        box.transform.SetParent(parent, false);
        var rt = box.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(xMin, 0f);
        rt.anchorMax = new Vector2(xMax, 1f);
        rt.offsetMin = new Vector2(4f, 0f);
        rt.offsetMax = new Vector2(-4f, 0f);
        box.GetComponent<Image>().color = UiTheme.Card;
        box.GetComponent<Image>().raycastTarget = false;

        var tmp = CreateTmp(box.transform, "Body", "", 15f, FontStyles.Normal);
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.enableWordWrapping = true;
        tmp.richText = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        var tr = tmp.rectTransform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(14f, 8f);
        tr.offsetMax = new Vector2(-14f, -8f);
        return tmp;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateTopLabel(Transform parent, string name, string text, float size, FontStyles style,
        float yFromTop, float height)
    {
        var tmp = CreateTmp(parent, name, text, size, style);
        tmp.alignment = TextAlignmentOptions.Center;
        var rt = tmp.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, yFromTop);
        rt.sizeDelta = new Vector2(-(ContentPad * 2f), height);
        return tmp;
    }

    private static TextMeshProUGUI CreateBandLabel(Transform parent, string name, string text, float size, FontStyles style,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        var tmp = CreateTmp(parent, name, text, size, style);
        tmp.alignment = TextAlignmentOptions.Center;
        var rt = tmp.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = new Vector2(ContentPad, 0f);
        rt.offsetMax = new Vector2(-ContentPad, 0f);
        return tmp;
    }

    private static TextMeshProUGUI CreateTmp(Transform parent, string name, string text, float size, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = UiTheme.TextPrimary;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = true;
        return tmp;
    }

    private static Button CreateButton(Transform parent, string name, string label, Color bg,
        Vector2 pos, Vector2 size, Action onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = bg;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(() => onClick?.Invoke());

        var tmp = CreateTmp(go.transform, "Label", label, 16f, FontStyles.Bold);
        tmp.alignment = TextAlignmentOptions.Center;
        Stretch(tmp.rectTransform);
        tmp.color = UiTheme.ContrastOn(bg);
        return btn;
    }

    private static void SetPrimaryButtonText(Button btn, Color color)
    {
        if (btn == null) return;
        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null) tmp.color = color;
    }
}
