using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Klinisyen PIN kapısı. Menüdeki "Klinisyen Girişi" butonu veya kilitli rapor açma.
/// SaMD Class B / KVKK: notlar ve şifreli hasta klasörleri yalnızca bu kapıdan.
/// </summary>
public class ClinicianAccessPanel : MonoBehaviour
{
    private const float LongPressSeconds = 2.5f;

    private DataManager _dataManager;
    private Action _onUnlocked;
    private bool _reportOpenMode;
    private TMP_InputField _pinField;
    private TextMeshProUGUI _error;
    private TextMeshProUGUI _status;
    private TextMeshProUGUI _togglePinLabel;
    private bool _pinVisible;

    public static void AttachLongPressToTitle(TextMeshProUGUI title, DataManager dataManager, Transform canvasRoot)
    {
        if (title == null || canvasRoot == null) return;
        var trigger = title.gameObject.GetComponent<ClinicianTitleLongPress>();
        if (trigger == null) trigger = title.gameObject.AddComponent<ClinicianTitleLongPress>();
        trigger.Init(dataManager, canvasRoot, LongPressSeconds);
        title.raycastTarget = true;
    }

    /// <summary>Varsayılan: klinisyen raporu açar.</summary>
    public static void Show(Transform canvasRoot, DataManager dataManager)
    {
        Show(canvasRoot, dataManager, null, false);
    }

    /// <summary>PIN sonrası özel işlem (ilerleme HTML / hasta klasörü / seans HTML).</summary>
    public static void Show(Transform canvasRoot, DataManager dataManager, Action onUnlocked)
    {
        Show(canvasRoot, dataManager, onUnlocked, false);
    }

    /// <summary>
    /// Şifreli HTML rapor: tuşa basınca uygulama PIN ister; doğruysa decrypt edip açar.
    /// Tarayıcıya doğrudan .enc yolu gönderilmez.
    /// </summary>
    public static void OpenEncryptedHtmlReport(Transform canvasRoot, string encOrPlainPath, TextMeshProUGUI statusHint)
    {
        if (canvasRoot == null || string.IsNullOrEmpty(encOrPlainPath) || !System.IO.File.Exists(encOrPlainPath))
        {
            if (statusHint != null) statusHint.text = Loc.T("detail.html.missing");
            return;
        }

        if (statusHint != null)
            statusHint.text = Loc.T("detail.html.needPin");

        Show(canvasRoot, null, () =>
        {
            bool ok = ReportExporter.TryOpenReportFile(encOrPlainPath);
            if (!ok)
            {
                // Yanlış oturum anahtarı olabilir — temizle, bir kez daha PIN iste
                PatientVault.ClearSessionUnlock();
                if (statusHint != null) statusHint.text = Loc.T("detail.html.openFailed");
                return;
            }
            if (statusHint != null) statusHint.text = Loc.T("detail.html.opened");
        }, reportOpenMode: true);
    }

    public static void Show(Transform canvasRoot, DataManager dataManager, Action onUnlocked, bool reportOpenMode)
    {
        if (canvasRoot == null) return;
        var existing = canvasRoot.GetComponentsInChildren<ClinicianAccessPanel>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != null)
                DestroyImmediate(existing[i].gameObject);
        }

        GameObject go = new GameObject("ClinicianAccessPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(Canvas), typeof(GraphicRaycaster), typeof(ClinicianAccessPanel));
        go.transform.SetParent(canvasRoot, false);
        go.transform.SetAsLastSibling();

        // Seans detay (200) / hasta picker (250) üstünde
        var overlay = go.GetComponent<Canvas>();
        overlay.overrideSorting = true;
        overlay.sortingOrder = 300;

        var panel = go.GetComponent<ClinicianAccessPanel>();
        panel._dataManager = dataManager;
        panel._onUnlocked = onUnlocked;
        panel._reportOpenMode = reportOpenMode;
        panel.Build();
    }

    /// <summary>Oturum açık değilse PIN ister; açıksa hemen callback.</summary>
    public static void RequireUnlock(Transform canvasRoot, DataManager dataManager, Action onUnlocked)
    {
        if (onUnlocked == null) return;
        if (PatientVault.HasSessionUnlock)
        {
            onUnlocked();
            return;
        }
        Show(canvasRoot, dataManager, onUnlocked, false);
    }

    private void Build()
    {
        var rt = GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        UiSafeLayout.BindLandscapeCard(rt, cardRt, UiSafeLayout.LandscapeDialogWidth, UiSafeLayout.LandscapeDialogHeight);
        card.GetComponent<Image>().color = UiTheme.Panel;

        CreateLabel(card.transform, Loc.T("clinician.title"), 20f, FontStyles.Bold, -18f);
        string statusText;
        if (_reportOpenMode)
            statusText = Loc.T("clinician.pinForReport");
        else if (!ClinicianPin.IsConfigured)
            statusText = Loc.T("clinician.setPin");
        else if (_onUnlocked != null)
            statusText = Loc.T("vault.enterPin");
        else
            statusText = Loc.T("clinician.enterPin");
        _status = CreateLabel(card.transform, statusText, 14f, FontStyles.Normal, -56f);
        _status.color = UiTheme.TextMuted;

        _pinField = CreatePinInput(card.transform);
        _pinVisible = false;
        ApplyPinVisibility();

        _togglePinLabel = CreateTogglePinButton(card.transform);
        _error = CreateLabel(card.transform, "", 13f, FontStyles.Normal, -200f);
        _error.color = UiTheme.Danger;

        string unlockLabel = _reportOpenMode
            ? Loc.T("clinician.unlockHtml")
            : (_onUnlocked != null ? Loc.T("vault.unlock") : Loc.T("clinician.unlock"));
        CreateBtn(card.transform, unlockLabel, UiTheme.Cta, 70f, OnUnlock);
        CreateBtn(card.transform, Loc.T("detail.btn.back"), UiTheme.ButtonNormal, 20f, () => Destroy(gameObject));
    }

    private void OnTogglePinVisibility()
    {
        _pinVisible = !_pinVisible;
        ApplyPinVisibility();
        if (_togglePinLabel != null)
            _togglePinLabel.text = Loc.T(_pinVisible ? "clinician.hidePin" : "clinician.showPin");
    }

    private void ApplyPinVisibility()
    {
        if (_pinField == null) return;
        // Gizli: * ile; açık: rakamlar görünür. Her iki modda yalnızca rakam.
        _pinField.contentType = TMP_InputField.ContentType.Custom;
        _pinField.characterValidation = TMP_InputField.CharacterValidation.Digit;
        _pinField.characterLimit = 8;
        _pinField.asteriskChar = '*';
        _pinField.inputType = _pinVisible
            ? TMP_InputField.InputType.Standard
            : TMP_InputField.InputType.Password;
        _pinField.ForceLabelUpdate();
    }

    private void OnUnlock()
    {
        string pin = _pinField != null ? _pinField.text : "";
        if (!ClinicianPin.IsConfigured)
        {
            if (!ClinicianPin.SetPin(pin))
            {
                _error.text = Loc.T("clinician.err.format");
                return;
            }
            FinishUnlocked();
            return;
        }

        if (!ClinicianPin.Verify(pin))
        {
            _error.text = Loc.T("clinician.err.pin");
            return;
        }
        FinishUnlocked();
    }

    private void FinishUnlocked()
    {
        Action cb = _onUnlocked;
        DataManager dm = _dataManager;
        Transform canvasRoot = transform.parent;
        Destroy(gameObject);
        if (cb != null)
        {
            cb();
            return;
        }
        // Klinisyen Girişi: tarayıcıya çıkma — notlar uygulama içinde
        if (dm != null && canvasRoot != null)
            ClinicianNotesPanel.Show(canvasRoot, dm);
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string text, float size, FontStyles style, float y)
    {
        GameObject go = new GameObject("Lbl", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(380f, 36f);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
        return tmp;
    }

    private TMP_InputField CreatePinInput(Transform parent)
    {
        GameObject go = new GameObject("Pin", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -110f);
        rt.sizeDelta = new Vector2(260f, 44f);
        go.GetComponent<Image>().color = UiTheme.Card;

        GameObject textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(go.transform, false);
        var areaRt = textArea.GetComponent<RectTransform>();
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = new Vector2(12f, 4f);
        areaRt.offsetMax = new Vector2(-12f, -4f);

        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(textArea.transform, false);
        Stretch(textGo.GetComponent<RectTransform>());
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 22f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
        tmp.raycastTarget = false;

        var input = go.GetComponent<TMP_InputField>();
        input.textViewport = areaRt;
        input.textComponent = tmp;
        input.characterLimit = 8;
        UiTheme.ApplyVisibleCaret(input);
        return input;
    }

    private TextMeshProUGUI CreateTogglePinButton(Transform parent)
    {
        GameObject go = new GameObject("ShowPin", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -162f);
        rt.sizeDelta = new Vector2(200f, 32f);
        go.GetComponent<Image>().color = UiTheme.ButtonNormal;
        go.GetComponent<Button>().onClick.AddListener(OnTogglePinVisibility);

        GameObject lbl = new GameObject("L", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = Loc.T("clinician.showPin");
        tmp.fontSize = 13f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void CreateBtn(Transform parent, string label, Color color, float yFromBottom, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, yFromBottom);
        rt.sizeDelta = new Vector2(260f, 42f);
        go.GetComponent<Image>().color = color;
        go.GetComponent<Button>().onClick.AddListener(onClick);
        GameObject lbl = new GameObject("L", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        var lrt = lbl.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 15f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.ContrastOn(color);
    }
}

/// <summary>Başlık uzun basış — klinisyen girişi (hasta menüsünde buton yok).</summary>
public class ClinicianTitleLongPress : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    private DataManager _dm;
    private Transform _canvas;
    private float _hold;
    private bool _down;
    private float _seconds;

    public void Init(DataManager dm, Transform canvas, float seconds)
    {
        _dm = dm;
        _canvas = canvas;
        _seconds = seconds;
    }

    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData eventData)
    {
        _down = true;
        _hold = 0f;
    }

    public void OnPointerUp(UnityEngine.EventSystems.PointerEventData eventData) { _down = false; }
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData) { _down = false; }

    private void Update()
    {
        if (!_down) return;
        _hold += Time.unscaledDeltaTime;
        if (_hold >= _seconds)
        {
            _down = false;
            ClinicianAccessPanel.Show(_canvas, _dm);
        }
    }
}
