using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Seans öncesi hasta seçimi: arama, son kullanılanlar, onay, yeni hasta.
/// KVKK: yalnızca yerel kayıtlar. SaMD Class B: yanlış hasta riskini azaltır.
/// </summary>
public class PatientPickerPanel : MonoBehaviour
{
    private const float RowHeight = 72f;
    private const float CardWidth = UiSafeLayout.LandscapeOverlayWidth;
    private const float CardHeight = UiSafeLayout.LandscapeOverlayHeight;

    private DataManager _dataManager;
    private System.Action _onContinueToExercise;
    private System.Action _onCancel;

    private TMP_InputField _searchField;
    private Transform _listRoot;
    private TextMeshProUGUI _emptyLabel;

    private GameObject _listView;
    private GameObject _confirmView;
    private TextMeshProUGUI _confirmBody;

    private PatientRegistryData _registry;
    private List<RegisteredPatient> _allSorted = new List<RegisteredPatient>();
    private RegisteredPatient _pending;

    public static void Show(Transform canvasRoot, DataManager dataManager,
        System.Action onPatientReady, System.Action onCancel = null)
    {
        if (canvasRoot == null || dataManager == null) return;

        // Eski kopyaları anında kaldır (iç içe panel / çift başlık)
        var existing = canvasRoot.GetComponentsInChildren<PatientPickerPanel>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != null)
                DestroyImmediate(existing[i].gameObject);
        }

        GameObject go = new GameObject("PatientPickerPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(Canvas), typeof(GraphicRaycaster), typeof(PatientPickerPanel));
        go.transform.SetParent(canvasRoot, false);
        go.transform.SetAsLastSibling();

        var overlayCanvas = go.GetComponent<Canvas>();
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = 250;

        var panel = go.GetComponent<PatientPickerPanel>();
        panel._dataManager = dataManager;
        panel._onContinueToExercise = onPatientReady;
        panel._onCancel = onCancel;
        panel.Build();
    }

    private void Build()
    {
        var rt = GetComponent<RectTransform>();
        Stretch(rt);
        var dim = GetComponent<Image>();
        dim.color = new Color(UiTheme.Background.r, UiTheme.Background.g, UiTheme.Background.b, 0.92f);
        dim.raycastTarget = true;

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        UiSafeLayout.BindCenteredCard(rt, cardRt, CardWidth, CardHeight);
        card.GetComponent<Image>().color = UiTheme.Panel;
        card.GetComponent<Image>().raycastTarget = true;

        // ---- LIST VIEW (üst / orta / alt bantlar — çakışmasız) ----
        _listView = new GameObject("ListView", typeof(RectTransform));
        _listView.transform.SetParent(card.transform, false);
        Stretch(_listView.GetComponent<RectTransform>());

        // Header band: title + hint + search
        RectTransform header = CreateBand(_listView.transform, "Header", 0f, 0.78f, 1f, 1f);
        CreateLabel(header, "Title", Loc.T("picker.title"), 22f, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(1400f, 30f));
        var hint = CreateLabel(header, "Hint", Loc.T("picker.hint"), 13f, FontStyles.Normal,
            new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(1400f, 28f));
        hint.color = UiTheme.TextMuted;
        hint.enableWordWrapping = true;

        _searchField = CreateSearchInput(header);
        _searchField.onValueChanged.AddListener(_ => RebuildList());

        // Scroll band
        RectTransform scrollBand = CreateBand(_listView.transform, "ScrollBand", 0f, 0.18f, 1f, 0.78f);
        BuildScroll(scrollBand);

        _emptyLabel = CreateLabel(scrollBand, "Empty", Loc.T("picker.empty"), 14f, FontStyles.Normal,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 40f));
        _emptyLabel.color = UiTheme.TextMuted;
        _emptyLabel.gameObject.SetActive(false);

        // Footer band: buttons
        RectTransform footer = CreateBand(_listView.transform, "Footer", 0f, 0f, 1f, 0.18f);
        CreateBtn(footer, Loc.T("picker.new"), UiTheme.Cta, 58f, 300f, OnNewPatient);
        CreateBtn(footer, Loc.T("detail.btn.back"), UiTheme.ButtonNormal, 10f, 220f, OnBack);

        // ---- CONFIRM VIEW ----
        _confirmView = new GameObject("ConfirmView", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _confirmView.transform.SetParent(card.transform, false);
        Stretch(_confirmView.GetComponent<RectTransform>());
        _confirmView.GetComponent<Image>().color = UiTheme.Panel;
        _confirmView.GetComponent<Image>().raycastTarget = true;
        _confirmView.SetActive(false);

        CreateLabel(_confirmView.transform, "CTitle", Loc.T("picker.confirmTitle"), 22f, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(560f, 32f));
        _confirmBody = CreateLabel(_confirmView.transform, "CBody", "", 16f, FontStyles.Normal,
            new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(560f, 120f));
        _confirmBody.enableWordWrapping = true;
        CreateBtn(_confirmView.transform, Loc.T("picker.confirmContinue"), UiTheme.Cta, 90f, 280f, OnConfirmContinue);
        CreateBtn(_confirmView.transform, Loc.T("picker.edit"), UiTheme.ButtonNormal, 36f, 280f, OnEditPatient);
        CreateBtn(_confirmView.transform, Loc.T("picker.confirmBack"), UiTheme.ButtonNormal, -18f, 200f, OnConfirmBack);

        _registry = _dataManager.LoadRegistry();
        _allSorted = PatientRegistry.GetAllSorted(_registry);
        RebuildList();
    }

    private void BuildScroll(RectTransform parent)
    {
        GameObject scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(parent, false);
        Stretch(scrollGo.GetComponent<RectTransform>());
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.offsetMin = new Vector2(16f, 8f);
        scrollRt.offsetMax = new Vector2(-16f, -8f);
        scrollGo.GetComponent<Image>().color = UiTheme.Card;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        viewport.transform.SetParent(scrollGo.transform, false);
        Stretch(viewport.GetComponent<RectTransform>());
        var vpImg = viewport.GetComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.01f); // maske için
        vpImg.raycastTarget = true;
        scroll.viewport = viewport.GetComponent<RectTransform>();

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);

        var vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 10, 10);
        vlg.spacing = 6f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = contentRt;
        _listRoot = content.transform;
    }

    private void RebuildList()
    {
        if (_listRoot == null) return;
        for (int i = _listRoot.childCount - 1; i >= 0; i--)
            Destroy(_listRoot.GetChild(i).gameObject);

        string query = _searchField != null ? _searchField.text : "";
        bool searching = !string.IsNullOrWhiteSpace(query);

        List<RegisteredPatient> recent = PatientRegistry.GetRecent(_registry, PatientRegistry.RecentCount);
        List<RegisteredPatient> all = searching
            ? PatientRegistry.FilterBySearch(_allSorted, query)
            : _allSorted;

        int rows = 0;
        if (!searching && recent.Count > 0)
        {
            AddSectionHeader(Loc.T("picker.recent"));
            for (int i = 0; i < recent.Count; i++)
            {
                AddPatientRow(recent[i]);
                rows++;
            }
        }

        // Arama yokken ve herkes "son kullanılan"daysa tekrar listeleme — boş "Tüm hastalar" gösterme
        int extra = 0;
        if (!searching)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (!IsInRecent(recent, all[i])) extra++;
            }
        }

        if (searching || extra > 0 || recent.Count == 0)
        {
            AddSectionHeader(searching ? Loc.T("picker.results") : Loc.T("picker.all"));
            for (int i = 0; i < all.Count; i++)
            {
                if (!searching && IsInRecent(recent, all[i])) continue;
                AddPatientRow(all[i]);
                rows++;
            }
        }

        if (_emptyLabel != null)
            _emptyLabel.gameObject.SetActive(rows == 0);
    }

    private static bool IsInRecent(List<RegisteredPatient> recent, RegisteredPatient p)
    {
        if (recent == null || p == null) return false;
        for (int i = 0; i < recent.Count; i++)
        {
            if (recent[i] != null && recent[i].patientId == p.patientId) return true;
        }
        return false;
    }

    private void AddSectionHeader(string text)
    {
        GameObject go = new GameObject("Section", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(_listRoot, false);
        go.GetComponent<LayoutElement>().preferredHeight = 28f;
        go.GetComponent<LayoutElement>().minHeight = 28f;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 13f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = UiTheme.TextMuted;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
    }

    private void AddPatientRow(RegisteredPatient patient)
    {
        if (patient == null) return;
        GameObject go = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(_listRoot, false);
        go.GetComponent<LayoutElement>().preferredHeight = RowHeight;
        go.GetComponent<LayoutElement>().minHeight = RowHeight;
        go.GetComponent<Image>().color = UiTheme.ButtonNormal;
        RegisteredPatient captured = patient;
        go.GetComponent<Button>().onClick.AddListener(() => ShowConfirm(captured));

        GameObject nameGo = new GameObject("Name", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        nameGo.transform.SetParent(go.transform, false);
        var nrt = nameGo.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0f, 0.48f);
        nrt.anchorMax = new Vector2(1f, 1f);
        nrt.offsetMin = new Vector2(16f, 0f);
        nrt.offsetMax = new Vector2(-16f, -6f);
        var nameTmp = nameGo.GetComponent<TextMeshProUGUI>();
        nameTmp.text = patient.DisplayName;
        nameTmp.fontSize = 18f;
        nameTmp.fontStyle = FontStyles.Bold;
        nameTmp.color = UiTheme.TextPrimary;
        nameTmp.alignment = TextAlignmentOptions.Left;
        nameTmp.raycastTarget = false;

        GameObject subGo = new GameObject("Sub", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        subGo.transform.SetParent(go.transform, false);
        var srt = subGo.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(1f, 0.48f);
        srt.offsetMin = new Vector2(16f, 6f);
        srt.offsetMax = new Vector2(-16f, 0f);
        var subTmp = subGo.GetComponent<TextMeshProUGUI>();
        subTmp.text = PatientRegistry.FormatRowSubtitle(patient);
        subTmp.fontSize = 13f;
        subTmp.color = UiTheme.TextMuted;
        subTmp.alignment = TextAlignmentOptions.Left;
        subTmp.raycastTarget = false;
    }

    private void ShowConfirm(RegisteredPatient patient)
    {
        _pending = patient;
        if (_listView != null) _listView.SetActive(false);
        if (_confirmView != null) _confirmView.SetActive(true);
        if (_confirmBody != null)
        {
            string sub = PatientRegistry.FormatRowSubtitle(patient);
            _confirmBody.text = Loc.Format("picker.confirmBody", patient.DisplayName)
                                + (string.IsNullOrEmpty(sub) ? "" : "\n" + sub);
        }
    }

    private void OnConfirmContinue()
    {
        if (_pending == null || _dataManager == null) return;
        _dataManager.SetActivePatient(_pending);
        System.Action cont = _onContinueToExercise;
        Destroy(gameObject);
        if (cont != null) cont();
    }

    private void OnEditPatient()
    {
        if (_pending == null || _dataManager == null) return;
        Transform root = transform.parent;
        DataManager dm = _dataManager;
        System.Action cont = _onContinueToExercise;
        // patientId korunur — ClearActivePatientForNew ÇAĞRILMAZ
        dm.SetActivePatient(_pending);
        Destroy(gameObject);

        PreSessionSetupPanel.ShowEditProfile(root, dm, result =>
        {
            if (!result.confirmed) return;
            if (cont != null) cont();
        });
    }

    private void OnConfirmBack()
    {
        _pending = null;
        if (_confirmView != null) _confirmView.SetActive(false);
        if (_listView != null) _listView.SetActive(true);
    }

    private void OnNewPatient()
    {
        if (_dataManager == null) return;
        Transform root = transform.parent;
        DataManager dm = _dataManager;
        System.Action cont = _onContinueToExercise;
        dm.ClearActivePatientForNew();
        Destroy(gameObject);

        PreSessionSetupPanel.ShowProfileOnly(root, dm, result =>
        {
            if (!result.confirmed) return;
            if (cont != null) cont();
        });
    }

    private void OnBack()
    {
        System.Action cancel = _onCancel;
        Destroy(gameObject);
        if (cancel != null) cancel();
    }

    private static RectTransform CreateBand(Transform parent, string name, float xMin, float yMin, float xMax, float yMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, FontStyles style,
        Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static TMP_InputField CreateSearchInput(Transform parent)
    {
        GameObject go = new GameObject("Search", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 10f);
        rt.sizeDelta = new Vector2(560f, 40f);
        go.GetComponent<Image>().color = UiTheme.Card;

        GameObject textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(go.transform, false);
        var areaRt = textArea.GetComponent<RectTransform>();
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = new Vector2(12f, 4f);
        areaRt.offsetMax = new Vector2(-12f, -4f);

        GameObject phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        phGo.transform.SetParent(textArea.transform, false);
        Stretch(phGo.GetComponent<RectTransform>());
        var ph = phGo.GetComponent<TextMeshProUGUI>();
        ph.text = Loc.T("picker.searchPh");
        ph.fontSize = 16f;
        ph.fontStyle = FontStyles.Italic;
        ph.color = UiTheme.TextMuted;
        ph.alignment = TextAlignmentOptions.Left;
        ph.raycastTarget = false;

        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(textArea.transform, false);
        Stretch(textGo.GetComponent<RectTransform>());
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 16f;
        tmp.color = UiTheme.TextPrimary;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;

        var input = go.GetComponent<TMP_InputField>();
        input.textViewport = areaRt;
        input.textComponent = tmp;
        input.placeholder = ph;
        UiTheme.ApplyVisibleCaret(input);
        return input;
    }

    private static void CreateBtn(Transform parent, string label, Color color, float yFromBottom, float width,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, yFromBottom);
        rt.sizeDelta = new Vector2(width, 42f);
        go.GetComponent<Image>().color = color;
        go.GetComponent<Button>().onClick.AddListener(onClick);

        GameObject lbl = new GameObject("L", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 15f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.ContrastOn(color);
        tmp.raycastTarget = false;
    }
}
