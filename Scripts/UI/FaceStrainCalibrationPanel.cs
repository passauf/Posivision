using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Yüz zorlanma kalibrasyonu: önce rahat yüz, sonra ~1 dk zorlanma ifadeleri.
/// KVKK: video kaydı yok; yalnızca blendshape özellik ortalaması diske yazılır.
/// </summary>
public class FaceStrainCalibrationPanel : MonoBehaviour
{
    private FaceStrainAnalyzer _analyzer;
    private System.Action<bool> _onComplete;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _hint;
    private TextMeshProUGUI _progressLabel;
    private Image _progressFill;
    private Button _startBtn;
    private Button _skipBtn;
    private Button _cancelBtn;
    private bool _running;

    private readonly string[] _pctCache = new string[101];

    public static FaceStrainCalibrationPanel Show(
        Transform canvasRoot, FaceStrainAnalyzer analyzer, System.Action<bool> onComplete)
    {
        var existing = canvasRoot.GetComponentInChildren<FaceStrainCalibrationPanel>(true);
        if (existing != null) Destroy(existing.gameObject);

        GameObject go = new GameObject("FaceStrainCalibrationPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(FaceStrainCalibrationPanel));
        go.transform.SetParent(canvasRoot, false);
        var panel = go.GetComponent<FaceStrainCalibrationPanel>();
        panel._analyzer = analyzer;
        panel._onComplete = onComplete;
        panel.BuildUi();
        return panel;
    }

    private void BuildUi()
    {
        for (int i = 0; i <= 100; i++)
            _pctCache[i] = "%" + i;

        var rt = GetComponent<RectTransform>();
        Stretch(rt);
        GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        UiSafeLayout.BindLandscapeCard(rt, cardRt, UiSafeLayout.LandscapeDialogWidth, UiSafeLayout.LandscapeDialogHeight);
        card.GetComponent<Image>().color = UiTheme.Panel;

        _title = CreateLabel(card.transform, "Title", Loc.T("face.title"), 22f, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(480f, 36f));

        _hint = CreateLabel(card.transform, "Hint", Loc.T("face.hint"),
            14f, FontStyles.Normal, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(460f, 70f));
        _hint.color = UiTheme.TextMuted;

        GameObject barBg = new GameObject("BarBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        barBg.transform.SetParent(card.transform, false);
        var barRt = barBg.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0.5f, 0.5f);
        barRt.anchorMax = new Vector2(0.5f, 0.5f);
        barRt.anchoredPosition = new Vector2(0f, -20f);
        barRt.sizeDelta = new Vector2(420f, 28f);
        barBg.GetComponent<Image>().color = UiTheme.ButtonNormal;

        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.transform.SetParent(barBg.transform, false);
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.anchoredPosition = Vector2.zero;
        fillRt.sizeDelta = new Vector2(0f, 0f);
        _progressFill = fillGo.GetComponent<Image>();
        _progressFill.color = UiTheme.Accent;

        _progressLabel = CreateLabel(card.transform, "Progress", Loc.T("face.ready"), 16f, FontStyles.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(440f, 28f));

        _startBtn = CreateButton(card.transform, "StartBtn", Loc.T("face.start"), UiTheme.Cta,
            new Vector2(0.5f, 0f), new Vector2(0f, 88f), new Vector2(280f, 44f), OnStart);
        _skipBtn = CreateButton(card.transform, "SkipBtn", Loc.T("face.skip"), UiTheme.ButtonNormal,
            new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(280f, 40f), OnSkip);
        _cancelBtn = CreateButton(card.transform, "CancelBtn", Loc.T("face.cancel"), UiTheme.Danger,
            new Vector2(0.5f, 0f), new Vector2(0f, -8f), new Vector2(160f, 36f), OnCancel);
        _cancelBtn.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_analyzer == null || !_running) return;

        var phase = _analyzer.CurrentPhase;
        float p = _analyzer.CalibrationProgress01;
        if (_progressFill != null)
        {
            var rt = _progressFill.rectTransform;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 420f * p);
        }

        int pct = Mathf.Clamp(Mathf.RoundToInt(p * 100f), 0, 100);

        if (phase == FaceStrainAnalyzer.Phase.CalibratingRest)
        {
            _title.text = Loc.T("face.rest.title");
            _hint.text = Loc.T("face.rest.hint");
            _progressLabel.text = Loc.Format("face.rest.progress", _pctCache[pct], _analyzer.CalibrationRestSamples);
            if (!_analyzer.HasFace)
                _progressLabel.text += Loc.T("face.noface");
        }
        else if (phase == FaceStrainAnalyzer.Phase.CalibratingStrain)
        {
            _title.text = Loc.T("face.strain.title");
            _hint.text = Loc.T("face.strain.hint");
            _progressLabel.text = Loc.Format("face.strain.progress", _pctCache[pct], _analyzer.CalibrationStrainSamples);
            if (!_analyzer.HasFace)
                _progressLabel.text += Loc.T("face.noface");
        }
        else if (phase == FaceStrainAnalyzer.Phase.Ready && _running)
        {
            _running = false;
            _progressLabel.text = Loc.T("face.saved");
            _onComplete?.Invoke(true);
            Destroy(gameObject);
        }
    }

    private void OnStart()
    {
        if (_analyzer == null) return;
        _running = true;
        _startBtn.gameObject.SetActive(false);
        _skipBtn.gameObject.SetActive(false);
        _cancelBtn.gameObject.SetActive(true);
        _analyzer.StartCalibration();
    }

    private void OnSkip()
    {
        _running = false;
        if (_analyzer != null) _analyzer.CancelCalibration();
        _onComplete?.Invoke(false);
        Destroy(gameObject);
    }

    private void OnCancel()
    {
        _running = false;
        if (_analyzer != null) _analyzer.CancelCalibration();
        _onComplete?.Invoke(false);
        Destroy(gameObject);
    }

    private static TextMeshProUGUI CreateLabel(
        Transform parent, string name, string text, float size, FontStyles style,
        Vector2 anchor, Vector2 pos, Vector2 dim)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = dim;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button CreateButton(
        Transform parent, string name, string label, Color color,
        Vector2 anchor, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        GameObject lbl = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 15f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.ContrastOn(color);
        tmp.raycastTarget = false;
        return btn;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
