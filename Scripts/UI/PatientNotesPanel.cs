using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Hasta not defteri — uygulama içi düzenleme + kökteki .txt dosyası (Notepad ile açılabilir).
/// Çıkışta otomatik kaydeder (manuel kayıt yok).
/// KVKK: yalnızca yerel hasta klasörü; buluta gönderilmez. SaMD Class B karar-destek notu.
/// </summary>
public class PatientNotesPanel : MonoBehaviour
{
    private const float CardW = UiSafeLayout.LandscapeOverlayWidth;
    private const float CardH = UiSafeLayout.LandscapeOverlayHeight;

    private DataManager _dataManager;
    private TMP_InputField _input;
    private TextMeshProUGUI _pathLabel;

    public static void Show(Transform canvasRoot, DataManager dataManager)
    {
        if (canvasRoot == null || dataManager == null) return;

        var existing = canvasRoot.GetComponentsInChildren<PatientNotesPanel>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != null)
                DestroyImmediate(existing[i].gameObject);
        }

        GameObject go = new GameObject("PatientNotesPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(Canvas), typeof(GraphicRaycaster), typeof(PatientNotesPanel));
        go.transform.SetParent(canvasRoot, false);
        go.transform.SetAsLastSibling();

        var overlay = go.GetComponent<Canvas>();
        overlay.overrideSorting = true;
        overlay.sortingOrder = 275;

        go.GetComponent<PatientNotesPanel>().Build(dataManager);
    }

    private void Build(DataManager dataManager)
    {
        _dataManager = dataManager;

        var rt = GetComponent<RectTransform>();
        Stretch(rt);
        var dim = GetComponent<Image>();
        dim.color = new Color(UiTheme.Background.r, UiTheme.Background.g, UiTheme.Background.b, 0.94f);
        dim.raycastTarget = true;

        // Boş alana tıklayınca da kaydet+çık
        var dimBtn = gameObject.AddComponent<Button>();
        dimBtn.targetGraphic = dim;
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(CloseAndSave);

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        UiSafeLayout.BindCenteredCard(rt, cardRt, CardW, CardH);
        var cardImg = card.GetComponent<Image>();
        cardImg.color = UiTheme.Panel;
        cardImg.raycastTarget = true; // dim tıklamasını kart üstünde yut

        CreateLabel(card.transform, Loc.T("notes.title"), 20f, FontStyles.Bold,
            new Vector2(0f, -18f), new Vector2(600f, 28f), TextAlignmentOptions.Center, UiTheme.TextPrimary);

        // Sağ üst X — her zaman görünür çıkış
        CreateCornerClose(card.transform);

        PatientProfile profile = dataManager.LoadProfile();
        string patientLine = profile != null && !string.IsNullOrEmpty(profile.DisplayName)
            ? Loc.Format("notes.patient", profile.DisplayName)
            : Loc.T("notes.noPatient");
        CreateLabel(card.transform, patientLine, 13f, FontStyles.Normal,
            new Vector2(0f, -48f), new Vector2(680f, 22f), TextAlignmentOptions.Center, UiTheme.Accent);

        string path = PatientVault.GetNotebookPath(profile);
        _pathLabel = CreateLabel(card.transform, Loc.Format("notes.path", path ?? ""), 11f, FontStyles.Normal,
            new Vector2(0f, -72f), new Vector2(680f, 36f), TextAlignmentOptions.Center, UiTheme.TextMuted);
        _pathLabel.enableWordWrapping = true;

        // Metin alanı — altta butonlara yer bırak
        GameObject inputGo = new GameObject("Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        inputGo.transform.SetParent(card.transform, false);
        var inputRt = inputGo.GetComponent<RectTransform>();
        inputRt.anchorMin = new Vector2(0.5f, 0f);
        inputRt.anchorMax = new Vector2(0.5f, 1f);
        inputRt.pivot = new Vector2(0.5f, 0.5f);
        inputRt.offsetMin = new Vector2(-330f, 72f);
        inputRt.offsetMax = new Vector2(330f, -118f);
        inputGo.GetComponent<Image>().color = UiTheme.Card;

        GameObject textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(inputGo.transform, false);
        Stretch(textArea.GetComponent<RectTransform>());
        var areaRt = textArea.GetComponent<RectTransform>();
        areaRt.offsetMin = new Vector2(10f, 10f);
        areaRt.offsetMax = new Vector2(-10f, -10f);

        GameObject placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        placeholderGo.transform.SetParent(textArea.transform, false);
        Stretch(placeholderGo.GetComponent<RectTransform>());
        var ph = placeholderGo.GetComponent<TextMeshProUGUI>();
        ph.text = Loc.T("notes.placeholder");
        ph.fontSize = 15f;
        ph.color = new Color(UiTheme.TextMuted.r, UiTheme.TextMuted.g, UiTheme.TextMuted.b, 0.55f);
        ph.alignment = TextAlignmentOptions.TopLeft;
        ph.enableWordWrapping = true;
        ph.raycastTarget = false;

        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(textArea.transform, false);
        Stretch(textGo.GetComponent<RectTransform>());
        var body = textGo.GetComponent<TextMeshProUGUI>();
        body.fontSize = 15f;
        body.color = UiTheme.TextPrimary;
        body.alignment = TextAlignmentOptions.TopLeft;
        body.enableWordWrapping = true;
        body.raycastTarget = true;

        _input = inputGo.GetComponent<TMP_InputField>();
        _input.textViewport = areaRt;
        _input.textComponent = body;
        _input.placeholder = ph;
        _input.lineType = TMP_InputField.LineType.MultiLineNewline;
        _input.contentType = TMP_InputField.ContentType.Standard;
        _input.characterLimit = 20000;
        _input.text = PatientVault.ReadNotebook(profile);
        UiTheme.ApplyVisibleCaret(_input);
        StartCoroutine(FocusInputNextFrame());

        // Alt sıra: Yenile | Dosyayı Aç | Çıkış (kaydeder)
        CreateButton(card.transform, Loc.T("notes.btn.reload"), new Vector2(-170f, 16f), 150f, OnReload);
        CreateButton(card.transform, Loc.T("notes.btn.openFile"), new Vector2(0f, 16f), 150f, OnOpenFile);
        CreateButtonColored(card.transform, Loc.T("notes.btn.close"), new Vector2(170f, 16f), 150f,
            UiTheme.Cta, UiTheme.ContrastOn(UiTheme.Cta), CloseAndSave);
    }

    private IEnumerator FocusInputNextFrame()
    {
        yield return null;
        if (_input == null) yield break;

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_input.gameObject);

        _input.Select();
        _input.ActivateInputField();
        int len = _input.text != null ? _input.text.Length : 0;
        _input.caretPosition = len;
        _input.stringPosition = len;
        _input.selectionAnchorPosition = len;
        _input.selectionFocusPosition = len;
        yield return null;
        // İkinci kare: mesh hazır olduktan sonra tekrar odak (imleç konumlanabilsin)
        if (_input != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(_input.gameObject);
            _input.ActivateInputField();
        }
    }

    private void CreateCornerClose(Transform card)
    {
        GameObject go = new GameObject("CloseX", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(card, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f, -12f);
        rt.sizeDelta = new Vector2(44f, 44f);
        go.GetComponent<Image>().color = UiTheme.ButtonNormal;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(CloseAndSave);

        GameObject tGo = new GameObject("T", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        tGo.transform.SetParent(go.transform, false);
        Stretch(tGo.GetComponent<RectTransform>());
        var tmp = tGo.GetComponent<TextMeshProUGUI>();
        tmp.text = "X";
        tmp.fontSize = 20f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
    }

    private void CloseAndSave()
    {
        SaveSilent();
        Destroy(gameObject);
    }

    private void SaveSilent()
    {
        if (_dataManager == null || _input == null) return;
        PatientProfile profile = _dataManager.LoadProfile();
        PatientVault.WriteNotebook(profile, _input.text);
    }

    private void OnReload()
    {
        if (_dataManager == null || _input == null) return;
        PatientProfile profile = _dataManager.LoadProfile();
        _input.text = PatientVault.ReadNotebook(profile);
    }

    private void OnOpenFile()
    {
        if (_dataManager == null) return;
        SaveSilent();
        PatientProfile profile = _dataManager.LoadProfile();
        string path = PatientVault.GetNotebookPath(profile);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch { /* dış uygulama açılamazsa sessiz */ }
    }

    private static TextMeshProUGUI CreateLabel(
        Transform parent, string text, float size, FontStyles style,
        Vector2 anchoredPos, Vector2 sizeDelta, TextAlignmentOptions align, Color color)
    {
        GameObject go = new GameObject("Lbl", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void CreateButton(
        Transform parent, string label, Vector2 anchoredPos, float width,
        UnityEngine.Events.UnityAction onClick)
    {
        CreateButtonColored(parent, label, anchoredPos, width, UiTheme.ButtonNormal, UiTheme.TextPrimary, onClick);
    }

    private static void CreateButtonColored(
        Transform parent, string label, Vector2 anchoredPos, float width,
        Color bg, Color textColor, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(width, 44f);
        go.GetComponent<Image>().color = bg;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        if (onClick != null) btn.onClick.AddListener(onClick);

        GameObject tGo = new GameObject("T", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        tGo.transform.SetParent(go.transform, false);
        Stretch(tGo.GetComponent<RectTransform>());
        var tmp = tGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
