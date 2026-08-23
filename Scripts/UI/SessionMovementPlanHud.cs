using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Seans öncesi hareket sırası + segment tamamlandı kartı.
/// SaMD Class B: protokol listesi; teşhis değildir. KVKK: hareket ID/etiket, hasta adı yok.
/// </summary>
public class SessionMovementPlanHud : MonoBehaviour
{
    private const int MaxRows = PatientProfile.MaxPlannedMovements;

    private RectTransform _listRoot;
    private TextMeshProUGUI _title;
    private readonly TextMeshProUGUI[] _rowLabels = new TextMeshProUGUI[MaxRows];
    private readonly Image[] _rowBg = new Image[MaxRows];
    private int _rowCount;

    private GameObject _completeRoot;
    private TextMeshProUGUI _completeTitle;
    private TextMeshProUGUI _completeBody;
    private TextMeshProUGUI _completeNext;
    private TextMeshProUGUI _continueLabel;
    private Button _continueBtn;
    private System.Action _onContinue;

    public static SessionMovementPlanHud Ensure(Transform canvasRoot)
    {
        if (canvasRoot == null) return null;
        var existing = canvasRoot.GetComponentInChildren<SessionMovementPlanHud>(true);
        if (existing != null) return existing;

        GameObject go = new GameObject("SessionMovementPlanHud", typeof(RectTransform), typeof(SessionMovementPlanHud));
        go.transform.SetParent(canvasRoot, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var hud = go.GetComponent<SessionMovementPlanHud>();
        hud.Build();
        return hud;
    }

    private void Build()
    {
        GameObject listGo = new GameObject("PlanList", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        listGo.transform.SetParent(transform, false);
        _listRoot = listGo.GetComponent<RectTransform>();
        _listRoot.anchorMin = new Vector2(0f, 1f);
        _listRoot.anchorMax = new Vector2(0f, 1f);
        _listRoot.pivot = new Vector2(0f, 1f);
        _listRoot.anchoredPosition = new Vector2(16f, -70f);
        _listRoot.sizeDelta = new Vector2(260f, 220f);
        listGo.GetComponent<Image>().color = new Color(UiTheme.Panel.r, UiTheme.Panel.g, UiTheme.Panel.b, 0.92f);
        listGo.GetComponent<Image>().raycastTarget = false;

        _title = CreateTmp(listGo.transform, "Title", 13f, FontStyles.Bold, TextAlignmentOptions.Left);
        var titleRt = _title.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -8f);
        titleRt.sizeDelta = new Vector2(-16f, 22f);
        _title.color = UiTheme.Accent;
        _title.text = Loc.T("visit.plan.title");

        for (int i = 0; i < MaxRows; i++)
        {
            GameObject row = new GameObject("Row" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            row.transform.SetParent(listGo.transform, false);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -32f - i * 28f);
            rt.sizeDelta = new Vector2(-12f, 26f);
            _rowBg[i] = row.GetComponent<Image>();
            _rowBg[i].color = UiTheme.Card;
            _rowBg[i].raycastTarget = false;
            _rowLabels[i] = CreateTmp(row.transform, "L", 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            Stretch(_rowLabels[i].rectTransform);
            var lrt = _rowLabels[i].rectTransform;
            lrt.offsetMin = new Vector2(8f, 1f);
            lrt.offsetMax = new Vector2(-8f, -1f);
            row.SetActive(false);
        }

        BuildCompleteOverlay();
        _listRoot.gameObject.SetActive(false);
        HideComplete();
    }

    private void BuildCompleteOverlay()
    {
        _completeRoot = new GameObject("SegmentComplete", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _completeRoot.transform.SetParent(transform, false);
        Stretch(_completeRoot.GetComponent<RectTransform>());
        var dim = _completeRoot.GetComponent<Image>();
        dim.color = new Color(0.02f, 0.04f, 0.08f, 0.78f);
        dim.raycastTarget = true;

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(_completeRoot.transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        UiSafeLayout.BindLandscapeCard(_completeRoot.GetComponent<RectTransform>(), cardRt, 720f, 280f);
        card.GetComponent<Image>().color = UiTheme.Panel;

        _completeTitle = CreateTmp(card.transform, "Title", 22f, FontStyles.Bold, TextAlignmentOptions.Center);
        Place(_completeTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(380f, 32f));
        _completeTitle.color = UiTheme.Accent;

        _completeBody = CreateTmp(card.transform, "Body", 16f, FontStyles.Normal, TextAlignmentOptions.Center);
        Place(_completeBody.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(380f, 48f));
        _completeBody.color = UiTheme.TextPrimary;
        _completeBody.enableWordWrapping = true;

        _completeNext = CreateTmp(card.transform, "Next", 15f, FontStyles.Bold, TextAlignmentOptions.Center);
        Place(_completeNext.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -118f), new Vector2(380f, 40f));
        _completeNext.color = UiTheme.TextMuted;
        _completeNext.enableWordWrapping = true;

        GameObject btnGo = new GameObject("Continue", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(card.transform, false);
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0f);
        btnRt.anchorMax = new Vector2(0.5f, 0f);
        btnRt.pivot = new Vector2(0.5f, 0f);
        btnRt.anchoredPosition = new Vector2(0f, 22f);
        btnRt.sizeDelta = new Vector2(220f, 48f);
        btnGo.GetComponent<Image>().color = UiTheme.Cta;
        _continueBtn = btnGo.GetComponent<Button>();
        _continueBtn.onClick.AddListener(OnContinueClicked);
        _continueLabel = CreateTmp(btnGo.transform, "L", 16f, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(_continueLabel.rectTransform);
        _continueLabel.color = UiTheme.ContrastOn(UiTheme.Cta);
    }

    public void RefreshList(PatientProfile profile)
    {
        if (_listRoot == null) return;
        int n = profile != null ? profile.PlannedMovementCount : 0;
        if (n <= 1)
        {
            _listRoot.gameObject.SetActive(false);
            return;
        }

        _listRoot.gameObject.SetActive(true);
        _title.text = Loc.T("visit.plan.title");
        _rowCount = n < MaxRows ? n : MaxRows;
        _listRoot.sizeDelta = new Vector2(260f, 40f + _rowCount * 28f);

        int current = profile.plannedMovementIndex;
        for (int i = 0; i < MaxRows; i++)
        {
            MovementId id = ExerciseCatalog.DefaultMovementId;
            bool on = i < _rowCount && profile.TryGetPlannedMovementAt(i, out id);
            _rowBg[i].gameObject.SetActive(on);
            if (!on) continue;

            bool done = i < current;
            bool active = i == current;
            string name = Loc.T(ExerciseCatalog.GetOrDefault(id).LocKey);
            string prefix = done ? Loc.T("visit.plan.done") : (active ? Loc.T("visit.plan.current") : "");
            if (prefix.Length > 0)
                _rowLabels[i].text = (i + 1) + ". " + name + "  ·  " + prefix;
            else
                _rowLabels[i].text = (i + 1) + ". " + name;

            _rowLabels[i].color = done ? UiTheme.TextMuted : (active ? UiTheme.Background : UiTheme.TextPrimary);
            _rowBg[i].color = active ? UiTheme.AccentDim : UiTheme.Card;
        }
    }

    public void ShowSegmentComplete(MovementId finishedId, int finishedIndex, int total, MovementId nextId, System.Action onContinue)
    {
        _onContinue = onContinue;
        if (_completeRoot == null) return;
        _completeRoot.SetActive(true);
        _completeTitle.text = Loc.T("visit.complete.title");
        string finishedName = Loc.T(ExerciseCatalog.GetOrDefault(finishedId).LocKey);
        _completeBody.text = Loc.Format("visit.complete.body", finishedName, finishedIndex + 1, total);
        _completeNext.text = Loc.Format("visit.complete.next", Loc.T(ExerciseCatalog.GetOrDefault(nextId).LocKey));
        _continueLabel.text = Loc.T("visit.complete.continue");
    }

    public void HideComplete()
    {
        if (_completeRoot != null)
            _completeRoot.SetActive(false);
        _onContinue = null;
    }

    public bool IsCompleteVisible => _completeRoot != null && _completeRoot.activeSelf;

    public void Relocalize(PatientProfile profile)
    {
        if (_title != null) _title.text = Loc.T("visit.plan.title");
        RefreshList(profile);
        if (IsCompleteVisible && _continueLabel != null)
            _continueLabel.text = Loc.T("visit.complete.continue");
    }

    private void OnContinueClicked()
    {
        System.Action cb = _onContinue;
        HideComplete();
        cb?.Invoke();
    }

    private static TextMeshProUGUI CreateTmp(Transform parent, string name, float size, FontStyles style, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        tmp.color = UiTheme.TextPrimary;
        return tmp;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
