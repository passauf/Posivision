using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Egzersiz HUD: Menüye Dön + seans başlat/bitir + seans öncesi profil paneli + zorlanma göstergesi.
/// </summary>
public class ExerciseHudController : MonoBehaviour
{
    [SerializeField] private string menuSceneName = UiTheme.MenuSceneName;
    [SerializeField] private PhysioAnalyzer analyzer;
    [SerializeField] private DataManager dataManager;
    [SerializeField] private FaceStrainAnalyzer faceStrainAnalyzer;
    [SerializeField] private SessionReportManager reportManager;

    [Header("UI (Editor'da bağlanırsa runtime üretilmez)")]
    [Tooltip("Boş bırakılırsa HUD runtime'da oluşturulur (fallback). Editor'da bağlamak tercih edilir.")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Image statusBadge;
    [SerializeField] private Button startButton;
    [SerializeField] private Button endButton;
    [SerializeField] private Button backButton;

    private TextMeshProUGUI _statusText;
    private Image _statusBadge;
    private Button _startBtn;
    private Button _endBtn;
    private Image _strainFill;
    private TextMeshProUGUI _strainLabel;
    private readonly string[] _strainLabelCache = new string[101];
    private TextMeshProUGUI _frameHintText;
    private TextMeshProUGUI _uprightText;
    private TextMeshProUGUI _qualityText;
    private GameObject _coachHudRoot;
    private AvatarBodyDriver _bodyDriverHint;
    private readonly string[] _estimateLabelCache = new string[101];

    private float _lastStrainWidth = -1f;
    private int _lastStrainColorBand = -1;
    private string _lastStrainLabelText = "";
    private string _lastFrameHintText = "";
    private string _lastQualityText = "";
    private string _lastUprightText = "";
    private int _lastShownLeanDeg = int.MinValue;
    private int _lastHealthBand = int.MinValue;
    private bool _hintLookupDone;

    private TextMeshProUGUI _startBtnLabel;
    private TextMeshProUGUI _endBtnLabel;
    private TextMeshProUGUI _backBtnLabel;
    private TextMeshProUGUI _faceBtnLabel;
    private Button _faceRecalibrateBtn;
    private Button _skipPosBtn;
    private TextMeshProUGUI _skipPosLabel;
    private GameObject _strainGaugeRoot;
    private GameObject _graphTogglesRoot;
    private Toggle _graphRightToggle;
    private Toggle _graphLeftToggle;
    private Toggle _graphStrainToggle;

    private readonly RomProgressPresenter _romProgress = new RomProgressPresenter();
    private SessionMovementPlanHud _planHud;
    private bool _awaitingNextMovement;

    private const float HudScale = 1.2f;
    private const float StrainGaugeWarnLevel = 0.55f;
    private const float StrainGaugeCautionLevel = 0.35f;
    /// <summary>Yatayda ön kamera kısa kenarda (L/R). Sayaçlar alt-orta, çentikten uzak.</summary>
    private const float RepHudBottomPad = 28f;
    private const float RepHudWidth = 420f;
    private const float RepHudHeight = 48f;
    private const float RepChipWidth = 196f;
    private const float RepChipHeight = 44f;
    private const float RepChipOffsetX = 110f;
    private const float RepChipFontSize = 22f;
    private const float CoachHudWidth = 380f;
    private const float CoachLineHeight = 28f;

    private static float S(float v) => v * HudScale;
    private static float StrainGaugeMaxWidth => S(192f);

    private void Start()
    {
        Canvas hudCanvas = FindObjectOfType<Canvas>();
        UiSafeLayout.ApplyScaler(hudCanvas);

        if (analyzer == null) analyzer = FindObjectOfType<PhysioAnalyzer>();
        if (faceStrainAnalyzer == null) faceStrainAnalyzer = FindObjectOfType<FaceStrainAnalyzer>();
        if (reportManager == null) reportManager = FindObjectOfType<SessionReportManager>();
        if (dataManager == null)
        {
            dataManager = FindObjectOfType<DataManager>();
            if (dataManager == null)
            {
                var go = new GameObject("DataManager");
                dataManager = go.AddComponent<DataManager>();
            }
        }

        RebuildStrainLabelCaches();

        BuildHud();
        HideLiveRomProgressHud();
        PolishExerciseSceneUi();
        VoiceCoach.Ensure();
        PreSessionPositionGuide.Ensure();
        ApplyHudLanguage();
        EnsurePlanHud();
        BindAnalyzerEvents();
        RefreshStatusUi();
        ApplyStartButtonGate();
    }

    private void RebuildStrainLabelCaches()
    {
        for (int i = 0; i <= 100; i++)
        {
            _strainLabelCache[i] = Loc.Format("hud.strain", i);
            _estimateLabelCache[i] = Loc.Format("hud.strain.est", i);
        }
    }

    private void Update()
    {
        RefreshStrainGauge();
        RefreshUprightHud();
        RefreshQualityHud();
        RefreshFrameHint();
        RefreshPhaseHud();
        // Orta-üst ROM kartı kaldırıldı — hasta görüşünü kapatmasın.
        // Otomatik yardımlı sezgi HUD etiketini güncelle (manuel toggle kapalıyken)
        if (_assistToggle != null && analyzer != null && analyzer.IsSessionRunning)
            RefreshAssistToggleVisual(_assistToggle.isOn);
    }

    private int _lastPhaseHudKey = int.MinValue;

    /// <summary>
    /// Sırayla iki kol: durum satırında Faz 1/2 etiketi (GC: yalnızca değişince string).
    /// </summary>
    private void RefreshPhaseHud()
    {
        if (_statusText == null || analyzer == null) return;
        if (!analyzer.IsSessionRunning || !analyzer.IsSequentialBothArms) return;

        int phase = analyzer.SequentialPhaseIndex;
        int key = 100 + phase;
        if (key == _lastPhaseHudKey) return;
        _lastPhaseHudKey = key;

        string arm = phase == 0 ? Loc.T("hud.phase.right") : Loc.T("hud.phase.left");
        _statusText.text = Loc.Format("hud.phase", phase + 1, arm);
        _statusText.color = UiTheme.Background;
    }

    private void OnEnable()
    {
        SessionStatus.Changed += RefreshStatusUi;
        LanguageSettings.LanguageChanged += OnLanguageChanged;
        PreSessionPositionGuide.PositioningCompleted += OnPositioningCompleted;
        PreSessionPositionGuide.SkipHoldChanged += RefreshPositionSkipVisual;
        BindAnalyzerEvents();
    }

    private void OnDisable()
    {
        SessionStatus.Changed -= RefreshStatusUi;
        LanguageSettings.LanguageChanged -= OnLanguageChanged;
        PreSessionPositionGuide.PositioningCompleted -= OnPositioningCompleted;
        PreSessionPositionGuide.SkipHoldChanged -= RefreshPositionSkipVisual;
        if (analyzer != null)
            analyzer.VisitSegmentCompleted -= OnVisitSegmentCompleted;
    }

    private void OnPositioningCompleted()
    {
        ApplyStartButtonGate();
        RefreshStatusUi();
    }

    private void ApplyStartButtonGate()
    {
        bool ready = PreSessionPositionGuide.IsPositioningComplete;
        bool running = analyzer != null && analyzer.IsSessionRunning;
        if (_startBtn != null)
            _startBtn.gameObject.SetActive(ready && !running && !_awaitingNextMovement);
    }

    private void OnLanguageChanged()
    {
        RebuildStrainLabelCaches();
        _lastStrainLabelText = "";
        _lastFrameHintText = "";
        _lastQualityText = "";
        _lastUprightText = "";
        _lastShownLeanDeg = int.MinValue;
        _lastHealthBand = int.MinValue;
        ApplyHudLanguage();
        if (_planHud != null && dataManager != null)
            _planHud.Relocalize(dataManager.LoadProfile());
        RefreshStatusUi();
        RefreshStrainGauge();
        RefreshUprightHud();
        _romProgress.RefreshLanguage();
    }

    private void ApplyHudLanguage()
    {
        if (_startBtnLabel != null) _startBtnLabel.text = Loc.T("hud.btn.start");
        if (_endBtnLabel != null) _endBtnLabel.text = Loc.T("hud.btn.end");
        if (_backBtnLabel != null) _backBtnLabel.text = Loc.T("hud.btn.back");
        if (_faceBtnLabel == null)
        {
            Canvas c = FindObjectOfType<Canvas>();
            if (c != null)
            {
                var faceBtn = c.transform.Find("SessionHudRoot/RecalibrateFaceButton")
                              ?? c.transform.Find("RecalibrateFaceButton");
                if (faceBtn != null) _faceBtnLabel = faceBtn.GetComponentInChildren<TextMeshProUGUI>();
            }
        }
        if (_faceBtnLabel != null) _faceBtnLabel.text = Loc.T("face.btn");
        RefreshPositionSkipVisual();
        SyncAssistToggleFromAnalyzer();
    }

    /// <summary>Eski seans HUD metinlerini klinik temaya çeker.</summary>
    private void PolishExerciseSceneUi()
    {
        if (analyzer == null) return;

        // Üst açı/slider kartları kullanılmıyor — tekrar sayaçları EnsureRepCounters
        StyleTmp(analyzer.rightRepText, UiTheme.TextPrimary, S(RepChipFontSize), FontStyles.Bold);
        StyleTmp(analyzer.leftRepText, UiTheme.TextPrimary, S(RepChipFontSize), FontStyles.Bold);
        if (analyzer.rightAngleText != null) analyzer.rightAngleText.gameObject.SetActive(false);
        if (analyzer.leftAngleText != null) analyzer.leftAngleText.gameObject.SetActive(false);
        if (analyzer.rightSlider != null) analyzer.rightSlider.gameObject.SetActive(false);
        if (analyzer.leftSlider != null) analyzer.leftSlider.gameObject.SetActive(false);
    }

    private static void StyleTmp(TextMeshProUGUI tmp, Color color, float size, FontStyles style)
    {
        if (tmp == null) return;
        tmp.color = color;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
    }

    private static void EnsureBackdrop(TextMeshProUGUI tmp, string cardName)
    {
        if (tmp == null || tmp.transform.parent == null) return;
        Transform parent = tmp.transform.parent;
        if (parent.Find(cardName) != null) return;

        GameObject card = new GameObject(cardName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(parent, false);
        card.transform.SetSiblingIndex(tmp.transform.GetSiblingIndex());

        var rt = card.GetComponent<RectTransform>();
        var src = tmp.rectTransform;
        rt.anchorMin = src.anchorMin;
        rt.anchorMax = src.anchorMax;
        rt.pivot = src.pivot;
        rt.anchoredPosition = src.anchoredPosition;
        rt.sizeDelta = src.sizeDelta + new Vector2(24f, 12f);

        var img = card.GetComponent<Image>();
        img.color = new Color(UiTheme.Card.r, UiTheme.Card.g, UiTheme.Card.b, 0.75f);
        img.raycastTarget = false;
    }

    private void BuildHud()
    {
        if (statusText != null && startButton != null && endButton != null)
        {
            _statusText = statusText;
            _statusBadge = statusBadge;
            _startBtn = startButton;
            _endBtn = endButton;

            _startBtn.onClick.AddListener(OnStartSessionClicked);
            _endBtn.onClick.AddListener(OnEndSession);
            if (backButton != null)
            {
                backButton.onClick.AddListener(() =>
                {
                    if (SessionStatus.IsActive && analyzer != null)
                        analyzer.EndSessionManually();
                    SceneManager.LoadScene(menuSceneName);
                });
            }

            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas != null)
            {
                EnsureAssistToggle(canvas.transform);
                EnsureStrainGauge(canvas.transform);
                EnsureCalibrateButton(canvas.transform);
                EnsurePositionSkipButton(canvas.transform);
                EnsureGraphToggles(canvas.transform);
                EnsureRepCounters(canvas.transform);
                EnsurePatientCoachHud(canvas.transform);
                HideLiveRomProgressHud();
            }
            return;
        }

        Canvas canvasRoot = FindObjectOfType<Canvas>();
        if (canvasRoot == null) return;
        if (canvasRoot.transform.Find("SessionHudRoot") != null)
        {
            EnsureAssistToggle(canvasRoot.transform);
            EnsureStrainGauge(canvasRoot.transform);
            EnsureCalibrateButton(canvasRoot.transform);
            EnsurePositionSkipButton(canvasRoot.transform);
            EnsureGraphToggles(canvasRoot.transform);
            EnsureRepCounters(canvasRoot.transform);
            EnsurePatientCoachHud(canvasRoot.transform);
            HideLiveRomProgressHud();
            return;
        }

        GameObject root = new GameObject("SessionHudRoot", typeof(RectTransform));
        root.transform.SetParent(canvasRoot.transform, false);
        Stretch(root.GetComponent<RectTransform>());

        CreateNavButton(root.transform, "BackToMenuButton", Loc.T("hud.btn.back"),
            new Vector2(0f, 1f), new Vector2(S(20f), S(-20f)), new Vector2(S(150f), S(50f)),
            () =>
            {
                if (SessionStatus.IsActive && analyzer != null)
                    analyzer.EndSessionManually();
                SceneManager.LoadScene(menuSceneName);
            });
        var backGo = root.transform.Find("BackToMenuButton");
        if (backGo != null) _backBtnLabel = backGo.GetComponentInChildren<TextMeshProUGUI>();

        GameObject badgeGo = new GameObject("SessionStatusBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        badgeGo.transform.SetParent(root.transform, false);
        var badgeRt = badgeGo.GetComponent<RectTransform>();
        badgeRt.anchorMin = new Vector2(1f, 1f);
        badgeRt.anchorMax = new Vector2(1f, 1f);
        badgeRt.pivot = new Vector2(1f, 1f);
        badgeRt.anchoredPosition = new Vector2(S(-200f), S(-20f));
        badgeRt.sizeDelta = new Vector2(S(180f), S(50f));
        _statusBadge = badgeGo.GetComponent<Image>();
        _statusBadge.color = UiTheme.ButtonNormal;

        GameObject statusLabelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        statusLabelGo.transform.SetParent(badgeGo.transform, false);
        Stretch(statusLabelGo.GetComponent<RectTransform>());
        _statusText = statusLabelGo.GetComponent<TextMeshProUGUI>();
        _statusText.fontSize = S(18f);
        _statusText.fontStyle = FontStyles.Bold;
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.color = UiTheme.TextPrimary;
        _statusText.raycastTarget = false;

        _startBtn = CreateNavButton(root.transform, "StartSessionButton", Loc.T("hud.btn.start"),
            new Vector2(1f, 1f), new Vector2(S(-20f), S(-20f)), new Vector2(S(170f), S(50f)), OnStartSessionClicked);
        _startBtn.GetComponent<Image>().color = UiTheme.Cta;
        _startBtnLabel = _startBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (_startBtnLabel != null) _startBtnLabel.color = UiTheme.ContrastOn(UiTheme.Cta);

        _endBtn = CreateNavButton(root.transform, "EndSessionButton", Loc.T("hud.btn.end"),
            new Vector2(1f, 1f), new Vector2(S(-20f), S(-82f)), new Vector2(S(170f), S(50f)), OnEndSession);
        _endBtn.GetComponent<Image>().color = UiTheme.Danger;
        _endBtnLabel = _endBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (_endBtnLabel != null) _endBtnLabel.color = UiTheme.ContrastOn(UiTheme.Danger);

        EnsureAssistToggle(root.transform);
        EnsureStrainGauge(root.transform);
        EnsureCalibrateButton(root.transform);
        EnsurePositionSkipButton(root.transform);
        EnsureGraphToggles(root.transform);
        EnsureRepCounters(root.transform);
        EnsurePatientCoachHud(canvasRoot.transform);
        HideLiveRomProgressHud();
    }

    private void HideLiveRomProgressHud()
    {
        _romProgress.SetVisible(false);
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;
        Transform rom = canvas.transform.Find("RomProgressHud");
        if (rom != null)
            rom.gameObject.SetActive(false);
    }

    /// <summary>
    /// Tekrar sayaçları: alt-orta (ön kamera yatayda L/R kısa kenarda).
    /// Sahne içi ±1100 merkez offset’li metinler gizlenir.
    /// </summary>
    private void EnsureRepCounters(Transform parent)
    {
        if (parent == null || analyzer == null) return;

        Transform attach = parent;
        if (parent.Find("SessionHudRoot") != null)
            attach = parent.Find("SessionHudRoot");

        HideLegacySceneRepTexts(parent);

        Canvas canvas = parent.GetComponentInParent<Canvas>();

        Transform existing = attach.Find("RepCountersHud");
        if (existing != null)
        {
            BindRepCounterTexts(existing);
            LayoutRepCountersHud(existing);
            EnsureFrameHintLabel(canvas);
            return;
        }
        float bottom = BottomSafePadding(canvas);

        GameObject root = new GameObject("RepCountersHud", typeof(RectTransform));
        root.transform.SetParent(attach, false);
        LayoutRepCountersHud(root.transform, bottom);

        CreateRepChip(root.transform, "LeftCard", -S(RepChipOffsetX), Loc.T("hud.rep.left") + " 0 / 0");
        CreateRepChip(root.transform, "RightCard", S(RepChipOffsetX), Loc.T("hud.rep.right") + " 0 / 0");
        BindRepCounterTexts(root.transform);
        EnsureFrameHintLabel(canvas);
    }

    private static void LayoutRepCountersHud(Transform hudRoot, float bottomOverride = -1f)
    {
        if (hudRoot == null) return;
        var rt = hudRoot.GetComponent<RectTransform>();
        if (rt == null) return;
        Canvas canvas = hudRoot.GetComponentInParent<Canvas>();
        float bottom = bottomOverride >= 0f ? bottomOverride : BottomSafePadding(canvas);
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, bottom);
        rt.sizeDelta = new Vector2(S(RepHudWidth), S(RepHudHeight));
    }

    private void BindRepCounterTexts(Transform hudRoot)
    {
        if (hudRoot == null || analyzer == null) return;
        Transform leftT = hudRoot.Find("LeftCard/Text");
        Transform rightT = hudRoot.Find("RightCard/Text");
        if (leftT != null) analyzer.leftRepText = leftT.GetComponent<TextMeshProUGUI>();
        if (rightT != null) analyzer.rightRepText = rightT.GetComponent<TextMeshProUGUI>();
        StyleTmp(analyzer.leftRepText, UiTheme.TextPrimary, S(RepChipFontSize), FontStyles.Bold);
        StyleTmp(analyzer.rightRepText, UiTheme.TextPrimary, S(RepChipFontSize), FontStyles.Bold);
        analyzer.RefreshArmUiForSessionState();
    }

    private static TextMeshProUGUI CreateRepChip(Transform parent, string name, float x, string initial)
    {
        GameObject card = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(parent, false);
        var rt = card.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(S(RepChipWidth), S(RepChipHeight));
        var img = card.GetComponent<Image>();
        img.color = new Color(UiTheme.Card.r, UiTheme.Card.g, UiTheme.Card.b, 0.88f);
        img.raycastTarget = false;

        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(card.transform, false);
        Stretch(textGo.GetComponent<RectTransform>());
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.text = initial;
        tmp.fontSize = S(RepChipFontSize);
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static float BottomSafePadding(Canvas canvas)
    {
        float pad = S(RepHudBottomPad);
        if (canvas == null || Screen.height < 1) return pad;
        float scale = canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
        float bottomPx = Screen.safeArea.yMin;
        return Mathf.Max(pad, bottomPx / scale + 8f);
    }

    private static void HideLegacySceneRepTexts(Transform canvasTf)
    {
        if (canvasTf == null) return;
        HideLegacyNamed(canvasTf, "RightRepText");
        HideLegacyNamed(canvasTf, "LeftRepText");
        if (canvasTf.parent != null)
        {
            HideLegacyNamed(canvasTf.parent, "RightRepText");
            HideLegacyNamed(canvasTf.parent, "LeftRepText");
        }
        Canvas canvas = canvasTf.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            HideLegacyNamed(canvas.transform, "RightRepText");
            HideLegacyNamed(canvas.transform, "LeftRepText");
        }
    }

    private static void HideLegacyNamed(Transform root, string name)
    {
        if (root == null) return;
        Transform t = root.Find(name);
        if (t == null) return;
        if (t.parent != null && t.parent.name == "RepCountersHud") return;
        t.gameObject.SetActive(false);
    }

    private Toggle _assistToggle;
    private TextMeshProUGUI _assistToggleLabel;

    private void EnsureAssistToggle(Transform parent)
    {
        if (parent == null) return;
        Transform attach = parent;
        if (parent.Find("SessionHudRoot") != null)
            attach = parent.Find("SessionHudRoot");
        Transform existing = attach.Find("AssistHelpToggle");
        if (existing != null)
        {
            _assistToggle = existing.GetComponent<Toggle>();
            var lbl = existing.Find("Label");
            if (lbl != null) _assistToggleLabel = lbl.GetComponent<TextMeshProUGUI>();
            WireAssistToggle();
            return;
        }

        _assistToggle = CreateHudToggle(attach, "AssistHelpToggle", Loc.T("hud.assist"), 0f, false);
        var rt = _assistToggle.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(S(-20f), S(-144f));
        rt.sizeDelta = new Vector2(S(200f), S(42f));
        var labelTf = _assistToggle.transform.Find("Label");
        if (labelTf != null)
        {
            _assistToggleLabel = labelTf.GetComponent<TextMeshProUGUI>();
            if (_assistToggleLabel != null)
            {
                _assistToggleLabel.fontSize = S(13f);
                var lblRt = labelTf.GetComponent<RectTransform>();
                if (lblRt != null)
                {
                    lblRt.offsetMin = new Vector2(S(32f), 0f);
                    lblRt.offsetMax = new Vector2(S(-4f), 0f);
                }
            }
        }
        WireAssistToggle();
    }

    private void WireAssistToggle()
    {
        if (_assistToggle == null) return;
        _assistToggle.onValueChanged.RemoveAllListeners();
        _assistToggle.onValueChanged.AddListener(OnAssistToggled);
        SyncAssistToggleFromAnalyzer();
    }

    private void OnAssistToggled(bool on)
    {
        if (analyzer != null)
            analyzer.AssistHelpActive = on;
        RefreshAssistToggleVisual(on);
    }

    private void SyncAssistToggleFromAnalyzer()
    {
        if (_assistToggle == null) return;
        bool on = analyzer != null && analyzer.AssistHelpActive;
        _assistToggle.SetIsOnWithoutNotify(on);
        RefreshAssistToggleVisual(on);
    }

    private void RefreshAssistToggleVisual(bool on)
    {
        bool autoMulti = analyzer != null && analyzer.IsAssistFromMultiPerson;
        bool secondOnStage = analyzer != null && analyzer.IsSecondPersonOnStage;
        bool effective = on || autoMulti || secondOnStage;
        if (_assistToggleLabel != null)
        {
            if (on)
                _assistToggleLabel.text = Loc.T("hud.assist.on");
            else if (autoMulti)
                _assistToggleLabel.text = Loc.T("hud.assist.auto");
            else if (secondOnStage)
                _assistToggleLabel.text = Loc.T("hud.assist.secondPerson");
            else
                _assistToggleLabel.text = Loc.T("hud.assist");
        }
        if (_assistToggle != null)
        {
            var bg = _assistToggle.transform.Find("Background");
            if (bg != null)
            {
                var img = bg.GetComponent<Image>();
                if (img != null)
                    img.color = effective ? UiTheme.Accent : UiTheme.ButtonNormal;
            }
        }
    }

    private void EnsureGraphToggles(Transform parent)
    {
        if (parent == null) return;
        Transform attach = parent;
        if (parent.Find("SessionHudRoot") != null)
            attach = parent.Find("SessionHudRoot");
        Transform existing = attach.Find("GraphSeriesToggles");
        if (existing != null)
        {
            _graphTogglesRoot = existing.gameObject;
            if (_graphRightToggle == null)
            {
                Transform t = existing.Find("GRight");
                if (t != null) _graphRightToggle = t.GetComponent<Toggle>();
            }
            if (_graphLeftToggle == null)
            {
                Transform t = existing.Find("GLeft");
                if (t != null) _graphLeftToggle = t.GetComponent<Toggle>();
            }
            if (_graphStrainToggle == null)
            {
                Transform t = existing.Find("GStrain");
                if (t != null) _graphStrainToggle = t.GetComponent<Toggle>();
            }
            return;
        }

        GameObject row = new GameObject("GraphSeriesToggles", typeof(RectTransform));
        row.transform.SetParent(attach, false);
        _graphTogglesRoot = row;
        var rt = row.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(S(20f), S(130f));
        rt.sizeDelta = new Vector2(S(280f), S(42f));

        _graphRightToggle = CreateHudToggle(row.transform, "GRight", Loc.T("hud.graph.right"), 0f, true);
        _graphLeftToggle = CreateHudToggle(row.transform, "GLeft", Loc.T("hud.graph.left"), S(95f), true);
        _graphStrainToggle = CreateHudToggle(row.transform, "GStrain", Loc.T("hud.graph.strain"), S(180f), true);

        _graphRightToggle.onValueChanged.AddListener(_ => ApplyGraphVisibility());
        _graphLeftToggle.onValueChanged.AddListener(_ => ApplyGraphVisibility());
        _graphStrainToggle.onValueChanged.AddListener(_ => ApplyGraphVisibility());
        ApplyGraphVisibility();
    }

    private void ApplyGraphVisibility()
    {
        if (reportManager == null) reportManager = FindObjectOfType<SessionReportManager>();
        if (reportManager == null) return;
        bool r = _graphRightToggle == null || _graphRightToggle.isOn;
        bool l = _graphLeftToggle == null || _graphLeftToggle.isOn;
        bool s = _graphStrainToggle == null || _graphStrainToggle.isOn;
        reportManager.SetGraphSeriesVisibility(r, l, s);
    }

    private static Toggle CreateHudToggle(Transform parent, string name, string label, float x, bool on)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(S(90f), S(32f));

        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bg.transform.SetParent(go.transform, false);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(0f, 0.5f);
        bgRt.sizeDelta = new Vector2(S(18f), S(18f));
        bgRt.anchoredPosition = new Vector2(S(10f), 0f);
        bg.GetComponent<Image>().color = UiTheme.ButtonNormal;

        GameObject check = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        check.transform.SetParent(bg.transform, false);
        Stretch(check.GetComponent<RectTransform>());
        check.GetComponent<Image>().color = UiTheme.Accent;

        GameObject lbl = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        var lblRt = lbl.GetComponent<RectTransform>();
        lblRt.anchorMin = Vector2.zero;
        lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = new Vector2(S(28f), 0f);
        lblRt.offsetMax = Vector2.zero;
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = S(13f);
        tmp.color = UiTheme.TextPrimary;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;

        var toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = bg.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = on;
        return toggle;
    }

    private void EnsureCalibrateButton(Transform parent)
    {
        if (parent == null) return;
        Transform existing = parent.Find("RecalibrateFaceButton");
        if (existing != null)
        {
            _faceRecalibrateBtn = existing.GetComponent<Button>();
            _faceBtnLabel = existing.GetComponentInChildren<TextMeshProUGUI>();
            if (_faceBtnLabel != null) _faceBtnLabel.text = Loc.T("face.btn");
            return;
        }

        Button faceBtn = CreateNavButton(parent, "RecalibrateFaceButton", Loc.T("face.btn"),
            new Vector2(0f, 0f), new Vector2(S(20f), S(80f)), new Vector2(S(170f), S(48f)), () =>
            {
                if (faceStrainAnalyzer == null)
                    faceStrainAnalyzer = FindObjectOfType<FaceStrainAnalyzer>();
                Canvas canvas = FindObjectOfType<Canvas>();
                if (canvas == null || faceStrainAnalyzer == null) return;
                FaceStrainCalibrationPanel.Show(canvas.transform, faceStrainAnalyzer, ok =>
                {
                    RefreshStrainGauge();
                });
            });
        _faceRecalibrateBtn = faceBtn;
        _faceBtnLabel = faceBtn.GetComponentInChildren<TextMeshProUGUI>();
    }

    private void EnsurePositionSkipButton(Transform parent)
    {
        if (parent == null) return;
        Transform attach = parent;
        Transform hudRoot = parent.Find("SessionHudRoot");
        if (hudRoot != null)
            attach = hudRoot;

        Transform existing = attach.Find("SkipPositionGuideButton");
        if (existing != null)
        {
            _skipPosBtn = existing.GetComponent<Button>();
            _skipPosLabel = existing.GetComponentInChildren<TextMeshProUGUI>();
            WirePositionSkipButton();
            return;
        }

        _skipPosBtn = CreateNavButton(attach, "SkipPositionGuideButton", Loc.T("hud.pos.skip.on"),
            new Vector2(0f, 1f), new Vector2(S(20f), S(-82f)), new Vector2(S(210f), S(50f)), OnPositionSkipClicked);
        _skipPosLabel = _skipPosBtn.GetComponentInChildren<TextMeshProUGUI>();
        RefreshPositionSkipVisual();
    }

    private void WirePositionSkipButton()
    {
        if (_skipPosBtn == null) return;
        _skipPosBtn.onClick.RemoveListener(OnPositionSkipClicked);
        _skipPosBtn.onClick.AddListener(OnPositionSkipClicked);
        RefreshPositionSkipVisual();
    }

    private void OnPositionSkipClicked()
    {
        PreSessionPositionGuide.SetSkipHoldEnabled(!PreSessionPositionGuide.SkipHoldEnabled);
    }

    private void RefreshPositionSkipVisual()
    {
        bool skip = PreSessionPositionGuide.SkipHoldEnabled;
        if (_skipPosLabel != null)
            _skipPosLabel.text = skip ? Loc.T("hud.pos.skip.off") : Loc.T("hud.pos.skip.on");
        if (_skipPosBtn != null)
        {
            Image img = _skipPosBtn.targetGraphic as Image;
            if (img != null)
                img.color = skip ? UiTheme.Warning : UiTheme.ButtonNormal;
        }
    }

    private void EnsureStrainGauge(Transform parent)
    {
        if (parent == null) return;
        Transform existing = parent.Find("StrainGaugeRoot");
        if (existing != null)
        {
            _strainGaugeRoot = existing.gameObject;
            Transform fillT = existing.Find("Fill");
            Transform labelT = existing.Find("Label");
            _strainFill = fillT != null ? fillT.GetComponent<Image>() : null;
            _strainLabel = labelT != null ? labelT.GetComponent<TextMeshProUGUI>() : null;
            return;
        }

        GameObject root = new GameObject("StrainGaugeRoot", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        _strainGaugeRoot = root;
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(S(20f), S(20f));
        rt.sizeDelta = new Vector2(S(200f), S(56f));

        GameObject bg = new GameObject("Bg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bg.transform.SetParent(root.transform, false);
        Stretch(bg.GetComponent<RectTransform>());
        bg.GetComponent<Image>().color = new Color(UiTheme.Card.r, UiTheme.Card.g, UiTheme.Card.b, 0.85f);
        bg.GetComponent<Image>().raycastTarget = false;

        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.transform.SetParent(root.transform, false);
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.anchoredPosition = new Vector2(S(4f), 0f);
        fillRt.sizeDelta = new Vector2(0f, -8f);
        _strainFill = fillGo.GetComponent<Image>();
        _strainFill.color = UiTheme.Success;
        _strainFill.raycastTarget = false;

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(root.transform, false);
        Stretch(labelGo.GetComponent<RectTransform>());
        _strainLabel = labelGo.GetComponent<TextMeshProUGUI>();
        _strainLabel.fontSize = S(14f);
        _strainLabel.fontStyle = FontStyles.Bold;
        _strainLabel.alignment = TextAlignmentOptions.Center;
        _strainLabel.color = UiTheme.TextPrimary;
        _strainLabel.raycastTarget = false;
        _strainLabel.text = _strainLabelCache[0];
    }

    private void RefreshStrainGauge()
    {
        if (_strainFill == null) return;
        // Seans dışı: güncelleme yok (gizli)
        if (analyzer == null || !analyzer.IsSessionRunning)
            return;

        float effort = 0f;
        bool hasFace = false;
        if (faceStrainAnalyzer != null)
        {
            hasFace = faceStrainAnalyzer.HasFace;
            effort = hasFace ? faceStrainAnalyzer.CurrentEffort01 : 0f;
        }

        // cmd: dirty-check — TMP/Layout her kare yazılmasın
        float width = Mathf.Lerp(0f, StrainGaugeMaxWidth, effort);
        if (Mathf.Abs(width - _lastStrainWidth) > 0.5f)
        {
            _strainFill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            _lastStrainWidth = width;
        }

        int colorBand = effort >= StrainGaugeWarnLevel ? 2
            : (effort >= StrainGaugeCautionLevel ? 1 : 0);
        if (colorBand != _lastStrainColorBand)
        {
            _lastStrainColorBand = colorBand;
            if (colorBand == 2) _strainFill.color = UiTheme.Warning;
            else if (colorBand == 1) _strainFill.color = UiTheme.Warning;
            else _strainFill.color = UiTheme.Success;
        }

        if (_strainLabel != null)
        {
            int pct = Mathf.Clamp(Mathf.RoundToInt(effort * 100f), 0, 100);
            string next;
            if (!hasFace) next = Loc.T("hud.strain.noface");
            else if (faceStrainAnalyzer != null && !faceStrainAnalyzer.HasCalibrationProfile)
                next = _estimateLabelCache[pct];
            else
                next = _strainLabelCache[pct];

            if (next != _lastStrainLabelText)
            {
                _strainLabel.text = next;
                _lastStrainLabelText = next;
            }
        }
    }

    private void RefreshFrameHint()
    {
        if (!_hintLookupDone)
        {
            if (_bodyDriverHint == null)
                _bodyDriverHint = FindObjectOfType<AvatarBodyDriver>();
            EnsureFrameHintLabel(FindObjectOfType<Canvas>());
            _hintLookupDone = _frameHintText != null;
        }

        if (_frameHintText == null) return;
        if (analyzer == null || !analyzer.IsSessionRunning)
        {
            if (_lastFrameHintText.Length > 0)
            {
                _frameHintText.text = "";
                _lastFrameHintText = "";
            }
            return;
        }

        string next = "";
        var vis = analyzer.RegionVisibility;
        var mask = analyzer.RegionMask;
        bool needHint = false;
        if (mask.rightArm && analyzer.IsMeasuringRightArm && !vis.rightArm) needHint = true;
        if (mask.leftArm && analyzer.IsMeasuringLeftArm && !vis.leftArm) needHint = true;
        if (mask.torso && !vis.torso) needHint = true;
        if (needHint)
            next = Loc.T("hud.frame.hint");

        if (next != _lastFrameHintText)
        {
            _frameHintText.text = next;
            _lastFrameHintText = next;
            _frameHintText.color = UiTheme.Warning;
        }
    }

    private void RefreshQualityHud()
    {
        EnsurePatientCoachHud(FindObjectOfType<Canvas>()?.transform);
        if (_qualityText == null) return;
        if (analyzer == null || !analyzer.IsSessionRunning)
        {
            if (_lastQualityText.Length > 0)
            {
                _qualityText.text = "";
                _lastQualityText = "";
            }
            return;
        }

        var vis = analyzer.RegionVisibility;
        var mask = analyzer.RegionMask;
        bool needFrameHint = false;
        if (mask.rightArm && analyzer.IsMeasuringRightArm && !vis.rightArm) needFrameHint = true;
        if (mask.leftArm && analyzer.IsMeasuringLeftArm && !vis.leftArm) needFrameHint = true;
        if (mask.torso && !vis.torso) needFrameHint = true;

        string next = "";
        if (!needFrameHint)
        {
            switch (analyzer.CurrentQualityBand)
            {
                case SessionQualityBand.Reliable:
                    next = Loc.T("hud.quality.reliable");
                    break;
                case SessionQualityBand.Caution:
                    next = Loc.T("hud.quality.caution");
                    break;
                case SessionQualityBand.Invalid:
                    next = Loc.T("hud.quality.invalid");
                    break;
            }
        }

        if (next != _lastQualityText)
        {
            _qualityText.text = next;
            _lastQualityText = next;
            if (analyzer.CurrentQualityBand == SessionQualityBand.Invalid)
                _qualityText.color = UiTheme.Danger;
            else if (analyzer.CurrentQualityBand == SessionQualityBand.Caution)
                _qualityText.color = UiTheme.Warning;
            else if (analyzer.CurrentQualityBand == SessionQualityBand.Reliable)
                _qualityText.color = UiTheme.Success;
            else
                _qualityText.color = UiTheme.TextMuted;
        }
    }

    private void EnsureFrameHintLabel(Canvas canvas)
    {
        if (_frameHintText != null) return;
        if (canvas == null) return;

        Transform legacy = canvas.transform.Find("BodyFrameHint");
        if (legacy != null)
        {
            _frameHintText = legacy.GetComponent<TextMeshProUGUI>();
            legacy.name = "FrameHintHud";
        }

        if (_frameHintText == null)
        {
            GameObject go = new GameObject("FrameHintHud", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(canvas.transform, false);
            _frameHintText = go.GetComponent<TextMeshProUGUI>();
        }

        _frameHintText.fontSize = S(14f);
        _frameHintText.alignment = TextAlignmentOptions.Center;
        _frameHintText.color = UiTheme.Warning;
        _frameHintText.raycastTarget = false;
        _frameHintText.enableWordWrapping = true;

        RepositionFrameHint(canvas);
    }

    private void RepositionFrameHint(Canvas canvas)
    {
        if (_frameHintText == null || canvas == null) return;
        var rt = _frameHintText.rectTransform;
        float bottom = BottomSafePadding(canvas);
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, bottom + S(RepHudHeight) + S(10f));
        rt.sizeDelta = new Vector2(S(560f), S(32f));
    }

    private void EnsurePatientCoachHud(Transform canvasRoot)
    {
        if (canvasRoot == null) return;

        Transform existingRoot = canvasRoot.Find("PatientCoachHud");
        if (existingRoot != null)
        {
            _coachHudRoot = existingRoot.gameObject;
            Transform uprightT = existingRoot.Find("UprightLine");
            Transform qualityT = existingRoot.Find("QualityLine");
            if (uprightT != null) _uprightText = uprightT.GetComponent<TextMeshProUGUI>();
            if (qualityT != null) _qualityText = qualityT.GetComponent<TextMeshProUGUI>();
            LayoutPatientCoachHud(canvasRoot);
            return;
        }

        Transform legacyUpright = canvasRoot.Find("UprightLeanHud");
        if (legacyUpright != null)
            legacyUpright.gameObject.SetActive(false);

        GameObject root = new GameObject("PatientCoachHud", typeof(RectTransform));
        root.transform.SetParent(canvasRoot, false);
        _coachHudRoot = root;

        _uprightText = CreateCoachLine(root.transform, "UprightLine", 0);
        _qualityText = CreateCoachLine(root.transform, "QualityLine", 1);
        LayoutPatientCoachHud(canvasRoot);
    }

    private static TextMeshProUGUI CreateCoachLine(Transform parent, string name, int lineIndex)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(0f, -S(CoachLineHeight) * lineIndex);
        rt.sizeDelta = new Vector2(S(CoachHudWidth), S(CoachLineHeight));
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = S(15f);
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = true;
        tmp.color = UiTheme.TextPrimary;
        tmp.raycastTarget = false;
        tmp.text = "";
        return tmp;
    }

    private void LayoutPatientCoachHud(Transform canvasRoot)
    {
        if (_coachHudRoot == null || canvasRoot == null) return;
        Canvas canvas = canvasRoot.GetComponent<Canvas>();
        if (canvas == null)
            canvas = canvasRoot.GetComponentInParent<Canvas>();
        float topInset = TopSafePadding(canvas);
        float belowNavStack = S(156f);
        var rt = _coachHudRoot.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(S(20f), -Mathf.Max(topInset, belowNavStack));
        rt.sizeDelta = new Vector2(S(CoachHudWidth), S(CoachLineHeight * 2f + 8f));

        if (_uprightText != null)
        {
            var lineRt = _uprightText.rectTransform;
            lineRt.anchoredPosition = Vector2.zero;
            lineRt.sizeDelta = new Vector2(S(CoachHudWidth), S(CoachLineHeight));
        }
        if (_qualityText != null)
        {
            var lineRt = _qualityText.rectTransform;
            lineRt.anchoredPosition = new Vector2(0f, -S(CoachLineHeight + 4f));
            lineRt.sizeDelta = new Vector2(S(CoachHudWidth), S(CoachLineHeight));
        }
    }

    private static float TopSafePadding(Canvas canvas)
    {
        float pad = S(72f);
        if (canvas == null || Screen.height < 1) return pad;
        float scale = canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
        float topPx = Screen.safeArea.yMax;
        if (topPx < Screen.height - 1f)
            pad = Mathf.Max(pad, (Screen.height - topPx) / scale + S(8f));
        return pad;
    }

    private void EnsureUprightHudLabel()
    {
        EnsurePatientCoachHud(FindObjectOfType<Canvas>()?.transform);
    }

    private void RefreshUprightHud()
    {
        EnsureUprightHudLabel();
        if (_uprightText == null) return;

        if (analyzer == null || !analyzer.IsSessionRunning)
        {
            if (_lastUprightText.Length > 0)
            {
                _uprightText.text = "";
                _lastUprightText = "";
                _lastShownLeanDeg = int.MinValue;
            }
            return;
        }

        int leanI = Mathf.RoundToInt(analyzer.CurrentSpineLeanDegrees);
        float uprightH = analyzer.UprightHealth01;
        int healthBand = Mathf.RoundToInt(uprightH * 10f);
        if (leanI == _lastShownLeanDeg && healthBand == _lastHealthBand) return;

        _lastShownLeanDeg = leanI;
        _lastHealthBand = healthBand;

        string next = BuildUprightPatientLine(leanI, uprightH);
        if (next != _lastUprightText)
        {
            _uprightText.text = next;
            _lastUprightText = next;
        }

        _uprightText.color = HealthToPatientColor(uprightH);
    }

    private static string BuildUprightPatientLine(int leanDeg, float health01)
    {
        if (health01 >= 0.85f)
            return Loc.Format("hud.posture.upright.good", leanDeg);
        if (health01 >= 0.35f)
            return Loc.Format("hud.posture.upright.warn", leanDeg);
        return Loc.Format("hud.posture.upright.bad", leanDeg);
    }

    private static string BuildFacingPatientLine(int yawDeg, float health01, bool facingOk)
    {
        if (!facingOk && health01 <= 0.01f)
            return Loc.T("hud.posture.facing.missing");
        if (health01 >= 0.85f)
            return Loc.Format("hud.posture.facing.good", yawDeg);
        if (health01 >= 0.35f)
            return Loc.Format("hud.posture.facing.warn", yawDeg);
        return Loc.Format("hud.posture.facing.bad", yawDeg);
    }

    /// <summary>1=Success (eşikten uzak), 0=Danger (eşik aşımı); orta = Warning.</summary>
    private static Color HealthToPatientColor(float health01)
    {
        health01 = Mathf.Clamp01(health01);
        if (health01 >= 0.5f)
            return Color.Lerp(UiTheme.Warning, UiTheme.Success, (health01 - 0.5f) * 2f);
        return Color.Lerp(UiTheme.Danger, UiTheme.Warning, health01 * 2f);
    }

    private void BindAnalyzerEvents()
    {
        if (analyzer == null) return;
        analyzer.VisitSegmentCompleted -= OnVisitSegmentCompleted;
        analyzer.VisitSegmentCompleted += OnVisitSegmentCompleted;
    }

    private void EnsurePlanHud()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;
        _planHud = SessionMovementPlanHud.Ensure(canvas.transform);
        if (dataManager != null && _planHud != null)
            _planHud.RefreshList(dataManager.LoadProfile());
    }

    private void BeginPreparedSession(PatientProfile profile)
    {
        if (analyzer == null || profile == null) return;
        SessionLaunchIntent.Consume();
        _awaitingNextMovement = false;
        if (_planHud != null)
            _planHud.HideComplete();
        if (profile.hasSessionTargets)
            analyzer.SetSessionTargets(profile.lastSessionTargetAngle, profile.lastSessionTargetReps);
        analyzer.BeginSession(profile);
        VoiceCoach.Ensure();
        if (_planHud != null)
            _planHud.RefreshList(profile);
        RefreshStatusUi();
        ApplyStartButtonGate();
    }

    private void OnVisitSegmentCompleted(MovementId finishedId, int finishedIndex, int total)
    {
        _awaitingNextMovement = true;
        PatientProfile profile = dataManager != null ? dataManager.LoadProfile() : null;
        MovementId nextId = ExerciseCatalog.DefaultMovementId;
        if (profile != null)
            profile.TryGetCurrentPlannedMovement(out nextId);

        EnsurePlanHud();
        if (_planHud != null)
        {
            _planHud.RefreshList(profile);
            _planHud.ShowSegmentComplete(finishedId, finishedIndex, total, nextId, ContinueNextVisitMovement);
        }
        RefreshStatusUi();
        ApplyStartButtonGate();
    }

    private void ContinueNextVisitMovement()
    {
        PatientProfile profile = dataManager != null ? dataManager.LoadProfile() : null;
        if (profile == null || !profile.IsValidForSession()) return;
        BeginPreparedSession(profile);
    }

    private void OnStartSessionClicked()
    {
        if (analyzer == null) return;
        if (analyzer.IsSessionRunning) return;
        if (!PreSessionPositionGuide.IsPositioningComplete)
        {
            PreSessionPositionGuide.Ensure();
            return;
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        // Önce yüz zorlanma kalibrasyonu (yoksa veya kullanıcı yeniden ister)
        void ContinueToProfile()
        {
            PatientProfile profile = dataManager != null ? dataManager.LoadProfile() : null;
            bool valid = profile != null && profile.HasValidConsent && profile.IsValidForSession();
            bool prepared = SessionLaunchIntent.PreparedThisVisit
                || (valid && profile.hasSessionTargets);

            if (!valid)
            {
                PreSessionSetupPanel.ShowProfileOnly(canvas.transform, dataManager, result =>
                {
                    if (!result.confirmed || analyzer == null || result.profile == null) return;
                    BeginPreparedSession(result.profile);
                });
                return;
            }

            if (prepared)
            {
                BeginPreparedSession(profile);
                return;
            }

            PreSessionSetupPanel.Show(canvas.transform, dataManager, analyzer, result =>
            {
                if (!result.confirmed || analyzer == null) return;
                if (!ExerciseCatalog.IsLiveReady((MovementId)result.movementId))
                    return;
                SessionLaunchIntent.MarkPrepared();
                BeginPreparedSession(result.profile);
            });
        }

        if (faceStrainAnalyzer == null)
            faceStrainAnalyzer = FindObjectOfType<FaceStrainAnalyzer>();

        if (faceStrainAnalyzer != null && !faceStrainAnalyzer.HasCalibrationProfile)
        {
            FaceStrainCalibrationPanel.Show(canvas.transform, faceStrainAnalyzer, _ =>
            {
                ContinueToProfile();
            });
        }
        else
        {
            ContinueToProfile();
        }
    }

    private void OnEndSession()
    {
        if (analyzer == null) return;
        analyzer.EndSessionManually();
        RefreshStatusUi();
    }

    private void RefreshStatusUi()
    {
        if (_statusText == null) return;

        bool active = SessionStatus.IsActive || (analyzer != null && analyzer.IsSessionRunning);
        SetLiveSessionChromeVisible(active);

        if (_awaitingNextMovement)
        {
            _statusText.text = Loc.T("visit.complete.title");
            _statusText.color = UiTheme.Success;
            if (_statusBadge != null) _statusBadge.color = UiTheme.AccentDim;
            if (_startBtn != null) _startBtn.interactable = false;
            if (_endBtn != null) _endBtn.interactable = false;
            return;
        }

        if (active)
        {
            _lastPhaseHudKey = int.MinValue;
            if (analyzer != null && analyzer.IsSequentialBothArms)
            {
                int phase = analyzer.SequentialPhaseIndex;
                string arm = phase == 0 ? Loc.T("hud.phase.right") : Loc.T("hud.phase.left");
                _statusText.text = Loc.Format("hud.phase", phase + 1, arm);
                _lastPhaseHudKey = 100 + phase;
            }
            else
            {
                _statusText.text = Loc.T("hud.session.active");
            }
            _statusText.color = UiTheme.Background;
            if (_statusBadge != null) _statusBadge.color = UiTheme.Accent;
            if (_startBtn != null) _startBtn.interactable = false;
            if (_endBtn != null) _endBtn.interactable = true;
            if (_assistToggle != null)
            {
                _assistToggle.gameObject.SetActive(true);
                _assistToggle.interactable = true;
            }
        }
        else if (SessionStatus.Current == SessionStatus.Phase.Completed)
        {
            _statusText.text = Loc.T("hud.session.done");
            _statusText.color = UiTheme.TextPrimary;
            if (_statusBadge != null) _statusBadge.color = UiTheme.AccentDim;
            if (_startBtn != null) _startBtn.interactable = true;
            if (_endBtn != null) _endBtn.interactable = false;
            if (_assistToggle != null)
            {
                _assistToggle.SetIsOnWithoutNotify(false);
                if (analyzer != null) analyzer.AssistHelpActive = false;
                RefreshAssistToggleVisual(false);
                _assistToggle.interactable = false;
                _assistToggle.gameObject.SetActive(false);
            }
        }
        else
        {
            bool positioned = PreSessionPositionGuide.IsPositioningComplete;
            _statusText.text = positioned
                ? Loc.T("hud.session.idle")
                : Loc.T("hud.session.positioning");
            _statusText.color = UiTheme.TextMuted;
            if (_statusBadge != null) _statusBadge.color = UiTheme.ButtonNormal;
            ApplyStartButtonGate();
            if (_startBtn != null) _startBtn.interactable = positioned;
            if (_endBtn != null) _endBtn.interactable = false;
            if (_assistToggle != null)
            {
                _assistToggle.SetIsOnWithoutNotify(false);
                if (analyzer != null) analyzer.AssistHelpActive = false;
                RefreshAssistToggleVisual(false);
                _assistToggle.interactable = false;
                _assistToggle.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Seans öncesi dikkat dağınıklığını önlemek: slider/ölçek/tekrar HUD dışında
    /// zorlanma, grafik, yüz yeniden kalibrasyon ve çerçeve ipucu da kapalı.
    /// </summary>
    private void SetLiveSessionChromeVisible(bool visible)
    {
        if (_strainGaugeRoot != null)
            _strainGaugeRoot.SetActive(visible);
        if (_graphTogglesRoot != null)
            _graphTogglesRoot.SetActive(visible);
        if (_faceRecalibrateBtn != null)
            _faceRecalibrateBtn.gameObject.SetActive(visible);
        if (_frameHintText != null)
        {
            if (!visible)
            {
                _frameHintText.text = "";
                _lastFrameHintText = "";
            }
            _frameHintText.gameObject.SetActive(visible);
        }
        if (_coachHudRoot != null)
        {
            if (!visible)
            {
                if (_uprightText != null) _uprightText.text = "";
                if (_qualityText != null) _qualityText.text = "";
                _lastUprightText = "";
                _lastQualityText = "";
            }
            _coachHudRoot.SetActive(visible);
        }

        HideLiveRomProgressHud();

        if (analyzer != null)
            analyzer.RefreshArmUiForSessionState();
    }

    private static Button CreateNavButton(
        Transform parent, string name, string label, Vector2 anchor, Vector2 anchoredPos, Vector2 size,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        Image img = go.GetComponent<Image>();
        img.color = UiTheme.ButtonNormal;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        Stretch(labelGo.GetComponent<RectTransform>());
        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = S(15f);
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
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
