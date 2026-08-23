using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Klinisyen iki seans seçer; anlık HTML karşılaştırma raporu üretilir.
/// Seans sonunda otomatik çalışmaz. SaMD Class B; KVKK: yerel, PIN kapılı açılış.
/// </summary>
public class SessionComparePickerPanel : MonoBehaviour
{
    private const float RowHeight = 64f;
    private const float CardWidth = UiSafeLayout.LandscapeOverlayWidth;
    private const float CardHeight = UiSafeLayout.LandscapeOverlayHeight;

    private DataManager _dataManager;
    private PatientHistory _history;
    private SessionEntry _preselect;
    private Transform _canvasRoot;

    private Transform _listRoot;
    private TextMeshProUGUI _slotA;
    private TextMeshProUGUI _slotB;
    private TextMeshProUGUI _status;
    private Button _generateBtn;

    private SessionEntry _sessionA;
    private SessionEntry _sessionB;
    private readonly List<SessionEntry> _ordered = new List<SessionEntry>(64);

    public static void Show(Transform canvasRoot, DataManager dataManager, PatientHistory history,
        SessionEntry preselect = null)
    {
        if (canvasRoot == null || dataManager == null) return;

        var existing = canvasRoot.GetComponentsInChildren<SessionComparePickerPanel>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != null)
                Object.DestroyImmediate(existing[i].gameObject);
        }

        GameObject go = new GameObject("SessionComparePickerPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(Canvas), typeof(GraphicRaycaster), typeof(SessionComparePickerPanel));
        go.transform.SetParent(canvasRoot, false);
        go.transform.SetAsLastSibling();

        var overlayCanvas = go.GetComponent<Canvas>();
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = 260;

        var panel = go.GetComponent<SessionComparePickerPanel>();
        panel._canvasRoot = canvasRoot;
        panel._dataManager = dataManager;
        panel._history = history;
        panel._preselect = preselect;
        panel.Build();
    }

    private void Build()
    {
        var rt = GetComponent<RectTransform>();
        Stretch(rt);
        GetComponent<Image>().color = new Color(UiTheme.Background.r, UiTheme.Background.g, UiTheme.Background.b, 0.92f);
        GetComponent<Image>().raycastTarget = true;

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        UiSafeLayout.BindCenteredCard(rt, cardRt, CardWidth, CardHeight);
        card.GetComponent<Image>().color = UiTheme.Panel;
        card.GetComponent<Image>().raycastTarget = true;

        CreateLabel(card.transform, "Title", Loc.T("compare.picker.title"), 22f, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(680f, 32f));
        var hint = CreateLabel(card.transform, "Hint", Loc.T("compare.picker.hint"), 13f, FontStyles.Normal,
            new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(680f, 36f));
        hint.color = UiTheme.TextMuted;
        hint.enableWordWrapping = true;

        _slotA = CreateLabel(card.transform, "SlotA", Loc.T("compare.picker.slotAEmpty"), 14f, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(680f, 28f));
        _slotB = CreateLabel(card.transform, "SlotB", Loc.T("compare.picker.slotBEmpty"), 14f, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(680f, 28f));

        // Scroll
        GameObject scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(card.transform, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0.5f, 0f);
        scrollRt.anchorMax = new Vector2(0.5f, 1f);
        scrollRt.pivot = new Vector2(0.5f, 0.5f);
        scrollRt.sizeDelta = new Vector2(680f, 0f);
        scrollRt.offsetMin = new Vector2(-340f, 110f);
        scrollRt.offsetMax = new Vector2(340f, -160f);
        scrollGo.GetComponent<Image>().color = UiTheme.Card;
        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollGo.transform, false);
        Stretch(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = Color.white;
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 100f);
        _listRoot = content.transform;

        scroll.content = contentRt;
        scroll.viewport = viewport.GetComponent<RectTransform>();

        _status = CreateLabel(card.transform, "Status", "", 13f, FontStyles.Normal,
            new Vector2(0.5f, 0f), new Vector2(0f, 78f), new Vector2(680f, 28f));
        _status.color = UiTheme.TextMuted;

        _generateBtn = CreateBtn(card.transform, Loc.T("compare.picker.generate"), UiTheme.Cta,
            new Vector2(-150f, 22f), new Vector2(280f, 48f), OnGenerate);
        CreateBtn(card.transform, Loc.T("detail.btn.back"), UiTheme.ButtonNormal,
            new Vector2(150f, 22f), new Vector2(200f, 48f), () => Destroy(gameObject));

        if (_preselect != null)
            _sessionA = _preselect;

        RebuildList();
        RefreshSlots();
    }

    private void RebuildList()
    {
        for (int i = _listRoot.childCount - 1; i >= 0; i--)
            Destroy(_listRoot.GetChild(i).gameObject);

        _ordered.Clear();
        if (_history != null && _history.sessions != null)
        {
            for (int i = _history.sessions.Count - 1; i >= 0; i--)
            {
                if (_history.sessions[i] != null)
                    _ordered.Add(_history.sessions[i]);
            }
        }

        var contentRt = _listRoot.GetComponent<RectTransform>();
        contentRt.sizeDelta = new Vector2(0f, Mathf.Max(RowHeight, _ordered.Count * RowHeight + 8f));

        if (_ordered.Count == 0)
        {
            CreateLabel(_listRoot, "Empty", Loc.T("compare.picker.empty"), 14f, FontStyles.Normal,
                new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(640f, 40f));
            return;
        }

        for (int i = 0; i < _ordered.Count; i++)
        {
            SessionEntry s = _ordered[i];
            int sessionNumber = _history.sessions.IndexOf(s) + 1;
            CreateRow(s, sessionNumber, i);
        }
    }

    private void CreateRow(SessionEntry s, int sessionNumber, int visualIndex)
    {
        GameObject row = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        row.transform.SetParent(_listRoot, false);
        var rt = row.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -4f - visualIndex * RowHeight);
        rt.sizeDelta = new Vector2(-12f, RowHeight - 6f);

        bool isA = ReferenceEquals(s, _sessionA) || SameSession(s, _sessionA);
        bool isB = ReferenceEquals(s, _sessionB) || SameSession(s, _sessionB);
        Color bg = isA || isB ? UiTheme.AccentDim : UiTheme.ButtonNormal;
        row.GetComponent<Image>().color = bg;

        float r = SessionHistoryFilter.EffectiveRightMax(s);
        float l = SessionHistoryFilter.EffectiveLeftMax(s);
        string mark = isA ? " [A]" : (isB ? " [B]" : "");
        string title = Loc.Format("menu.hist.sessionN", sessionNumber) + mark + " · " +
                       (string.IsNullOrEmpty(s.dateTime) ? "—" : s.dateTime);
        string sub = Loc.Format("compare.picker.rowSub",
            r.ToString("F0"), l.ToString("F0"), s.compensationEvents);

        var titleTmp = CreateTmp(row.transform, "T", title, 14f, FontStyles.Bold);
        titleTmp.alignment = TextAlignmentOptions.Left;
        var tr = titleTmp.rectTransform;
        tr.anchorMin = new Vector2(0f, 0.45f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.offsetMin = new Vector2(12f, 0f);
        tr.offsetMax = new Vector2(-12f, -4f);

        var subTmp = CreateTmp(row.transform, "S", sub, 12f, FontStyles.Normal);
        subTmp.color = UiTheme.TextMuted;
        subTmp.alignment = TextAlignmentOptions.Left;
        var sr = subTmp.rectTransform;
        sr.anchorMin = new Vector2(0f, 0f);
        sr.anchorMax = new Vector2(1f, 0.5f);
        sr.offsetMin = new Vector2(12f, 4f);
        sr.offsetMax = new Vector2(-12f, 0f);

        SessionEntry captured = s;
        row.GetComponent<Button>().onClick.AddListener(() => OnRowClicked(captured));
    }

    private void OnRowClicked(SessionEntry s)
    {
        if (s == null) return;

        if (SameSession(s, _sessionA))
        {
            _sessionA = null;
        }
        else if (SameSession(s, _sessionB))
        {
            _sessionB = null;
        }
        else if (_sessionA == null)
        {
            _sessionA = s;
        }
        else if (_sessionB == null)
        {
            if (SameSession(s, _sessionA)) return;
            _sessionB = s;
        }
        else
        {
            // Üçüncü tık: B'yi değiştir
            _sessionB = s;
        }

        RebuildList();
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        _slotA.text = _sessionA == null
            ? Loc.T("compare.picker.slotAEmpty")
            : Loc.Format("compare.picker.slotA", Describe(_sessionA));
        _slotB.text = _sessionB == null
            ? Loc.T("compare.picker.slotBEmpty")
            : Loc.Format("compare.picker.slotB", Describe(_sessionB));

        bool ready = _sessionA != null && _sessionB != null && !SameSession(_sessionA, _sessionB);
        if (_generateBtn != null) _generateBtn.interactable = ready;
        if (_status != null)
            _status.text = ready ? Loc.T("compare.picker.ready") : Loc.T("compare.picker.needTwo");
    }

    private static string Describe(SessionEntry s)
    {
        if (s == null) return "—";
        return string.IsNullOrEmpty(s.dateTime) ? "—" : s.dateTime;
    }

    private static bool SameSession(SessionEntry a, SessionEntry b)
    {
        if (a == null || b == null) return false;
        if (ReferenceEquals(a, b)) return true;
        return !string.IsNullOrEmpty(a.dateTime) && a.dateTime == b.dateTime;
    }

    private void OnGenerate()
    {
        if (_sessionA == null || _sessionB == null || SameSession(_sessionA, _sessionB)) return;

        SessionEntry a = _sessionA;
        SessionEntry b = _sessionB;
        Transform root = _canvasRoot != null ? _canvasRoot : transform.parent;
        DataManager dm = _dataManager;

        ClinicianAccessPanel.Show(root, dm, () =>
        {
            PatientProfile profile = dm != null ? dm.LoadProfile() : null;
            string path = ReportExporter.ExportSessionCompare(a, b, profile, _history);
            if (string.IsNullOrEmpty(path) || !ReportExporter.TryOpenReportFile(path))
            {
                if (_status != null) _status.text = Loc.T("vault.openFailed");
                return;
            }
            Destroy(gameObject);
        }, reportOpenMode: true);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, FontStyles style,
        Vector2 pivot, Vector2 anchored, Vector2 sizeDelta)
    {
        var tmp = CreateTmp(parent, name, text, size, style);
        tmp.alignment = TextAlignmentOptions.Center;
        var rt = tmp.rectTransform;
        rt.anchorMin = pivot;
        rt.anchorMax = pivot;
        rt.pivot = pivot;
        rt.anchoredPosition = anchored;
        rt.sizeDelta = sizeDelta;
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

    private static Button CreateBtn(Transform parent, string label, Color bg, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
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
        btn.onClick.AddListener(onClick);
        var tmp = CreateTmp(go.transform, "L", label, 15f, FontStyles.Bold);
        tmp.alignment = TextAlignmentOptions.Center;
        Stretch(tmp.rectTransform);
        tmp.color = UiTheme.ContrastOn(bg);
        return btn;
    }
}
