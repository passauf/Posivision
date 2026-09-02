using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Seans öncesi: tam ekran kamera + 3 sütun; orta sütunda omuzlar görünürken yeşil onay.
/// Konum onayından sonra 3 sn geri sayım → PiP + örnek hareket. SaMD Class B UI rehberi.
/// </summary>
public class PreSessionPositionGuide : MonoBehaviour
{
    public enum Phase
    {
        Positioning = 0,
        Countdown = 1,
        Ready = 2
    }

    /// <summary>Örnek hologram ve seans başlatma bunun true olmasını bekler.</summary>
    public static bool IsPositioningComplete { get; private set; }

    public static event System.Action PositioningCompleted;
    public static event System.Action SkipHoldChanged;

    /// <summary>
    /// Test kapısı: orta-sütun tutma + geri sayımı atla. Eşikler ve faz mantığı değişmez.
    /// SaMD Class B UI rehberi — klinik karar değil; varsayılan kapalı (PlayerPrefs 0).
    /// </summary>
    public static bool SkipHoldEnabled
    {
        get => PlayerPrefs.GetInt(SkipPrefKey, 0) != 0;
        set
        {
            PlayerPrefs.SetInt(SkipPrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
            SkipHoldChanged?.Invoke();
        }
    }

    public static void SetSkipHoldEnabled(bool skip)
    {
        SkipHoldEnabled = skip;
        if (skip && _active != null)
            _active.CompletePositioning();
    }

    private const float MiddleMinX = 1f / 3f;
    private const float MiddleMaxX = 2f / 3f;
    private const float CenterHoldSeconds = 1.15f;
    private const float CountdownSeconds = 3f;
    private const int CountdownStart = 3;
    private const string SkipPrefKey = "dev_skip_position_guide";

    [SerializeField] private float centerHoldSeconds = CenterHoldSeconds;
    [SerializeField] private float countdownSeconds = CountdownSeconds;

    private AvatarStageController _stage;
    private AvatarBodyDriver _body;
    private PhysioAnalyzer _analyzer;
    private Canvas _canvas;
    private RectTransform _root;
    private Image _colLeft;
    private Image _colRight;
    private Image _colMidBorder;
    private Image _midFlash;
    private Image _progressFill;
    private Image _hintBackdrop;
    private GameObject _positionChrome;
    private TextMeshProUGUI _stepTitle;
    private TextMeshProUGUI _hint;
    private GameObject _countdownRoot;
    private Image _countdownBackdrop;
    private TextMeshProUGUI _countdownNumber;
    private TextMeshProUGUI _countdownSubtitle;
    private GameObject _skipBtnGo;
    private TextMeshProUGUI _skipBtnLabel;

    private static PreSessionPositionGuide _active;

    private Phase _phase = Phase.Positioning;
    private float _centeredStreak;
    private float _countdownElapsed;
    private int _lastCountdownShown = -1;
    private string _lastHint = "";

    public Phase CurrentPhase => _phase;
    public bool IsReady => _phase == Phase.Ready;

    public static PreSessionPositionGuide Ensure()
    {
        var existing = FindObjectOfType<PreSessionPositionGuide>(true);
        if (existing != null) return existing;
        var go = new GameObject("PreSessionPositionGuide");
        return go.AddComponent<PreSessionPositionGuide>();
    }

    private void Awake()
    {
        _active = this;
        IsPositioningComplete = false;
        _stage = FindObjectOfType<AvatarStageController>(true);
        if (_stage != null) _body = _stage.BodyDriver;
        if (_body == null) _body = FindObjectOfType<AvatarBodyDriver>(true);
        _analyzer = FindObjectOfType<PhysioAnalyzer>(true);
        BuildUi();
        BeginPositioning();
    }

    private void OnEnable()
    {
        LanguageSettings.LanguageChanged += RefreshSkipLabel;
    }

    private void OnDisable()
    {
        LanguageSettings.LanguageChanged -= RefreshSkipLabel;
    }

    private void OnDestroy()
    {
        if (_active == this)
            _active = null;
        if (_root != null)
            Destroy(_root.gameObject);
    }

    private void Update()
    {
        if (_phase == Phase.Ready) return;
        if (_root != null)
            _root.SetAsLastSibling();

        if (_body == null)
        {
            _body = _stage != null ? _stage.BodyDriver : FindObjectOfType<AvatarBodyDriver>(true);
            if (_phase == Phase.Positioning)
                ApplyPositionUi(0f, false, Loc.T("pos.hint.faceCamera"));
            return;
        }

        if (_phase == Phase.Countdown)
        {
            UpdateCountdown();
            return;
        }

        UpdatePositioning();
    }

    private void UpdatePositioning()
    {
        float holdRequired = Mathf.Max(0.35f, centerHoldSeconds);
        bool has = _body.TryGetShoulderCenter01(out float midX, out bool shouldersOk);
        bool inMiddle = has && shouldersOk && midX >= MiddleMinX && midX <= MiddleMaxX;

        bool sideProtocol = ResolveSideProtocol();

        bool headOk = true;
        if (_body.TryGetHeadVisible(out bool headVis))
            headOk = headVis;

        bool sideOk = true;
        string sideHint = null;
        if (sideProtocol && _body.TryGetRawShoulderWidth01(out float shoulderW))
        {
            float torso = 0f;
            _body.TryGetRawTorsoLength01(out torso);
            float phi = SideProfileGate.EstimateSkewDegrees(
                shoulderW, SideProfileGate.DefaultFrontalShoulderWidth01, torso);
            if (phi > SideProfileGate.DefaultWarnDegrees)
            {
                sideOk = false;
                sideHint = Loc.T("pos.hint.sideMore");
            }
        }

        string hint;
        float hold01;
        bool ready;

        if (!shouldersOk || !has)
        {
            _centeredStreak = 0f;
            hint = sideProtocol ? Loc.T("pos.hint.faceCamera") : Loc.T("pos.hint.center");
            hold01 = 0f;
            ready = false;
        }
        else if (!headOk)
        {
            _centeredStreak = 0f;
            hint = Loc.T("pos.hint.head");
            hold01 = 0f;
            ready = false;
        }
        else if (!inMiddle)
        {
            _centeredStreak = 0f;
            hint = midX < MiddleMinX ? Loc.T("pos.hint.moveRight") : Loc.T("pos.hint.moveLeft");
            hold01 = 0f;
            ready = false;
        }
        else if (!sideOk)
        {
            _centeredStreak = 0f;
            hint = sideHint ?? Loc.T("pos.hint.sideMore");
            hold01 = 0f;
            ready = false;
        }
        else
        {
            _centeredStreak += Time.deltaTime;
            hold01 = Mathf.Clamp01(_centeredStreak / holdRequired);
            ready = _centeredStreak >= holdRequired;
            hint = ready
                ? Loc.T("pos.hint.holdGreen")
                : (sideProtocol ? Loc.T("pos.hint.sideOk") : Loc.T("pos.hint.center"));
        }

        ApplyPositionUi(hold01, ready, hint);

        if (ready)
            BeginCountdown();
    }

    private bool ResolveSideProtocol()
    {
        if (_analyzer == null)
            _analyzer = FindObjectOfType<PhysioAnalyzer>(true);
        if (_analyzer != null)
            return _analyzer.PatientSideView
                   || ExerciseCatalog.UsesSideProfile(_analyzer.SelectedMovementId);
        // Profil henüz yoksa fleksiyon varsayılanı (yan)
        return true;
    }

    private void RefreshFlexionPreSessionCamera()
    {
        if (_analyzer == null)
            _analyzer = FindObjectOfType<PhysioAnalyzer>(true);
        if (_stage == null)
            _stage = FindObjectOfType<AvatarStageController>(true);
        if (_analyzer == null || _stage == null) return;
        if (_analyzer.IsSessionRunning) return;

        bool side = ResolveSideProtocol();
        bool right = _analyzer.IsMeasuringRightArm;
        bool left = _analyzer.IsMeasuringLeftArm;
        if (!right && !left) { right = true; left = false; }
        if (side && right && left) left = false;

        _stage.ApplySideOrbitForMeasuredArm(right, left, side);
    }

    private void UpdateCountdown()
    {
        if (!IsPatientCentered())
        {
            CancelCountdown();
            return;
        }

        _countdownElapsed += Time.deltaTime;
        float total = Mathf.Max(1f, countdownSeconds);
        int display = CountdownStart - Mathf.FloorToInt(_countdownElapsed);
        if (display != _lastCountdownShown)
        {
            _lastCountdownShown = display;
            if (_countdownNumber != null)
                _countdownNumber.text = display.ToString();
        }

        if (_countdownElapsed >= total)
            CompletePositioning();
    }

    private bool IsPatientCentered()
    {
        if (_body == null) return false;
        bool has = _body.TryGetShoulderCenter01(out float midX, out bool shouldersOk);
        if (!has || !shouldersOk || midX < MiddleMinX || midX > MiddleMaxX) return false;
        if (_body.TryGetHeadVisible(out bool headOk) && !headOk) return false;
        if (ResolveSideProtocol() && _body.TryGetRawShoulderWidth01(out float w))
        {
            float torso = 0f;
            _body.TryGetRawTorsoLength01(out torso);
            float phi = SideProfileGate.EstimateSkewDegrees(
                w, SideProfileGate.DefaultFrontalShoulderWidth01, torso);
            if (phi > SideProfileGate.DefaultWarnDegrees) return false;
        }
        return true;
    }

    private void BeginCountdown()
    {
        _phase = Phase.Countdown;
        _countdownElapsed = 0f;
        _lastCountdownShown = -1;

        if (_positionChrome != null)
            _positionChrome.SetActive(false);
        if (_countdownRoot != null)
            _countdownRoot.SetActive(true);
        if (_countdownSubtitle != null)
            _countdownSubtitle.text = Loc.T("pos.countdown.subtitle");
        if (_countdownNumber != null)
            _countdownNumber.text = CountdownStart.ToString();
        _lastCountdownShown = CountdownStart;
        if (_skipBtnGo != null)
            _skipBtnGo.transform.SetAsLastSibling();

        SetMiddleVisual(1f, true);
    }

    private void CancelCountdown()
    {
        _phase = Phase.Positioning;
        _centeredStreak = 0f;
        _countdownElapsed = 0f;
        _lastCountdownShown = -1;

        if (_countdownRoot != null)
            _countdownRoot.SetActive(false);
        if (_positionChrome != null)
            _positionChrome.SetActive(true);

        ApplyPositionUi(0f, false, Loc.T("pos.hint.center"));
    }

    public void BeginPositioning()
    {
        IsPositioningComplete = false;
        _phase = Phase.Positioning;
        _centeredStreak = 0f;
        _countdownElapsed = 0f;
        _lastCountdownShown = -1;

        if (_stage != null)
        {
            _stage.SetWebcamFullscreen(true);
            RefreshFlexionPreSessionCamera();
        }

        if (_root != null) _root.gameObject.SetActive(true);
        if (_countdownRoot != null) _countdownRoot.SetActive(false);
        if (_positionChrome != null) _positionChrome.SetActive(true);
        if (_stepTitle != null) _stepTitle.text = Loc.T("pos.step.title");
        ApplyPositionUi(0f, false, Loc.T("pos.hint.faceCamera"));
        RefreshSkipLabel();
        if (_skipBtnGo != null)
            _skipBtnGo.SetActive(true);

        if (SkipHoldEnabled)
            CompletePositioning();
    }

    /// <summary>Tutma/geri sayımı atlayıp hazır duruma geçer. Eşikler değişmez.</summary>
    public void CompletePositioning()
    {
        if (_phase == Phase.Ready) return;
        _phase = Phase.Ready;
        IsPositioningComplete = true;

        if (_stage != null)
            _stage.SetWebcamPipCorner();

        if (_root != null) _root.gameObject.SetActive(false);

        PositioningCompleted?.Invoke();
    }

    private void ApplyPositionUi(float hold01, bool ready, string hint)
    {
        SetHint(hint);
        SetMiddleVisual(hold01, ready);
        SetProgress(hold01);
    }

    private void BuildUi()
    {
        _canvas = FindObjectOfType<Canvas>();
        if (_canvas == null) return;

        Transform existing = _canvas.transform.Find("PreSessionPositionRoot");
        if (existing != null)
            Destroy(existing.gameObject);

        GameObject rootGo = new GameObject("PreSessionPositionRoot", typeof(RectTransform));
        rootGo.transform.SetParent(_canvas.transform, false);
        _root = rootGo.GetComponent<RectTransform>();
        Stretch(_root);
        _root.SetAsLastSibling();

        _colLeft = CreateOverlay("ColLeft", new Vector2(0f, 0f), new Vector2(1f / 3f, 1f),
            new Color(0.02f, 0.04f, 0.08f, 0.55f));
        _colRight = CreateOverlay("ColRight", new Vector2(2f / 3f, 0f), new Vector2(1f, 1f),
            new Color(0.02f, 0.04f, 0.08f, 0.55f));

        _colMidBorder = CreateOverlay("ColMidBorder", new Vector2(1f / 3f, 0f), new Vector2(2f / 3f, 1f),
            new Color(1f, 1f, 1f, 0.12f));
        var midOutline = _colMidBorder.gameObject.AddComponent<Outline>();
        midOutline.effectColor = new Color(1f, 1f, 1f, 0.35f);
        midOutline.effectDistance = new Vector2(3f, 3f);

        _midFlash = CreateOverlay("ColMidFlash", new Vector2(1f / 3f, 0f), new Vector2(2f / 3f, 1f),
            new Color(0.2f, 0.9f, 0.45f, 0f));
        _midFlash.raycastTarget = false;

        _positionChrome = new GameObject("PositionChrome", typeof(RectTransform));
        _positionChrome.transform.SetParent(_root, false);
        Stretch(_positionChrome.GetComponent<RectTransform>());

        CreateProgressBar();
        CreatePositionTexts();
        CreateCountdownUi();
        BuildSkipButton();
    }

    private void BuildSkipButton()
    {
        if (_root == null) return;

        GameObject go = new GameObject("SkipPositionButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(_root, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 24f);
        rt.sizeDelta = new Vector2(280f, 48f);

        Image img = go.GetComponent<Image>();
        img.color = UiTheme.ButtonNormal;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => SetSkipHoldEnabled(true));

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        Stretch(labelGo.GetComponent<RectTransform>());
        _skipBtnLabel = labelGo.GetComponent<TextMeshProUGUI>();
        _skipBtnLabel.fontSize = 16f;
        _skipBtnLabel.fontStyle = FontStyles.Bold;
        _skipBtnLabel.alignment = TextAlignmentOptions.Center;
        _skipBtnLabel.color = UiTheme.TextPrimary;
        _skipBtnLabel.raycastTarget = false;
        _skipBtnGo = go;
        RefreshSkipLabel();
        go.transform.SetAsLastSibling();
    }

    private void CreatePositionTexts()
    {
        Transform parent = _positionChrome != null ? _positionChrome.transform : _root;

        GameObject backdropGo = new GameObject("HintBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        backdropGo.transform.SetParent(parent, false);
        var bdRt = backdropGo.GetComponent<RectTransform>();
        bdRt.anchorMin = new Vector2(0.06f, 0.68f);
        bdRt.anchorMax = new Vector2(0.94f, 0.98f);
        bdRt.offsetMin = Vector2.zero;
        bdRt.offsetMax = Vector2.zero;
        _hintBackdrop = backdropGo.GetComponent<Image>();
        _hintBackdrop.color = new Color(UiTheme.Background.r, UiTheme.Background.g, UiTheme.Background.b, 0f);
        _hintBackdrop.raycastTarget = false;

        GameObject stepGo = new GameObject("StepTitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        stepGo.transform.SetParent(parent, false);
        var srt = stepGo.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.08f, 0.88f);
        srt.anchorMax = new Vector2(0.92f, 0.97f);
        srt.offsetMin = Vector2.zero;
        srt.offsetMax = Vector2.zero;
        _stepTitle = stepGo.GetComponent<TextMeshProUGUI>();
        _stepTitle.fontSize = 19f;
        _stepTitle.fontStyle = FontStyles.Bold;
        _stepTitle.alignment = TextAlignmentOptions.Center;
        _stepTitle.color = UiTheme.TextMuted;
        _stepTitle.raycastTarget = false;
        _stepTitle.text = Loc.T("pos.step.title");
        ApplyTextOutline(_stepTitle, 0.22f);

        GameObject hintGo = new GameObject("Hint", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        hintGo.transform.SetParent(parent, false);
        var hrt = hintGo.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0.08f, 0.72f);
        hrt.anchorMax = new Vector2(0.92f, 0.87f);
        hrt.offsetMin = Vector2.zero;
        hrt.offsetMax = Vector2.zero;
        _hint = hintGo.GetComponent<TextMeshProUGUI>();
        _hint.fontSize = 28f;
        _hint.fontStyle = FontStyles.Bold;
        _hint.alignment = TextAlignmentOptions.Center;
        _hint.enableWordWrapping = true;
        _hint.color = Color.white;
        _hint.raycastTarget = false;
        ApplyTextOutline(_hint, 0.28f);
    }

    private void CreateCountdownUi()
    {
        _countdownRoot = new GameObject("CountdownRoot", typeof(RectTransform));
        _countdownRoot.transform.SetParent(_root, false);
        Stretch(_countdownRoot.GetComponent<RectTransform>());
        _countdownRoot.SetActive(false);

        GameObject backdropGo = new GameObject("CountdownBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        backdropGo.transform.SetParent(_countdownRoot.transform, false);
        Stretch(backdropGo.GetComponent<RectTransform>());
        _countdownBackdrop = backdropGo.GetComponent<Image>();
        _countdownBackdrop.color = new Color(UiTheme.Background.r, UiTheme.Background.g, UiTheme.Background.b, 0.72f);
        _countdownBackdrop.raycastTarget = false;

        GameObject numberGo = new GameObject("CountdownNumber", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        numberGo.transform.SetParent(_countdownRoot.transform, false);
        var nrt = numberGo.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0.5f, 0.5f);
        nrt.anchorMax = new Vector2(0.5f, 0.5f);
        nrt.pivot = new Vector2(0.5f, 0.5f);
        nrt.anchoredPosition = new Vector2(0f, 24f);
        nrt.sizeDelta = new Vector2(280f, 220f);
        _countdownNumber = numberGo.GetComponent<TextMeshProUGUI>();
        _countdownNumber.fontSize = 120f;
        _countdownNumber.fontStyle = FontStyles.Bold;
        _countdownNumber.alignment = TextAlignmentOptions.Center;
        _countdownNumber.color = UiTheme.Accent;
        _countdownNumber.raycastTarget = false;
        ApplyTextOutline(_countdownNumber, 0.35f);

        GameObject subGo = new GameObject("CountdownSubtitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        subGo.transform.SetParent(_countdownRoot.transform, false);
        var srt = subGo.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.1f, 0.5f);
        srt.anchorMax = new Vector2(0.9f, 0.5f);
        srt.pivot = new Vector2(0.5f, 1f);
        srt.anchoredPosition = new Vector2(0f, -48f);
        srt.sizeDelta = new Vector2(0f, 64f);
        _countdownSubtitle = subGo.GetComponent<TextMeshProUGUI>();
        _countdownSubtitle.fontSize = 30f;
        _countdownSubtitle.fontStyle = FontStyles.Bold;
        _countdownSubtitle.alignment = TextAlignmentOptions.Center;
        _countdownSubtitle.color = UiTheme.TextPrimary;
        _countdownSubtitle.raycastTarget = false;
        _countdownSubtitle.text = Loc.T("pos.countdown.subtitle");
        ApplyTextOutline(_countdownSubtitle, 0.25f);
    }

    private void RefreshSkipLabel()
    {
        if (_skipBtnLabel != null)
            _skipBtnLabel.text = Loc.T("pos.skip.btn");
    }

    private void CreateProgressBar()
    {
        Transform parent = _positionChrome != null ? _positionChrome.transform : _root;

        GameObject trackGo = new GameObject("HoldProgressTrack", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        trackGo.transform.SetParent(parent, false);
        var trt = trackGo.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(1f / 3f, 0.14f);
        trt.anchorMax = new Vector2(2f / 3f, 0.14f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(0f, 8f);
        var trackImg = trackGo.GetComponent<Image>();
        trackImg.color = new Color(1f, 1f, 1f, 0.18f);
        trackImg.raycastTarget = false;

        GameObject fillGo = new GameObject("HoldProgressFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.transform.SetParent(trackGo.transform, false);
        Stretch(fillGo.GetComponent<RectTransform>());
        _progressFill = fillGo.GetComponent<Image>();
        _progressFill.color = UiTheme.Accent;
        _progressFill.type = Image.Type.Filled;
        _progressFill.fillMethod = Image.FillMethod.Horizontal;
        _progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _progressFill.fillAmount = 0f;
        _progressFill.raycastTarget = false;
    }

    private Image CreateOverlay(string name, Vector2 aMin, Vector2 aMax, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(_root, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private void SetHint(string text)
    {
        if (_hint == null || text == _lastHint) return;
        _hint.text = text;
        _lastHint = text;
    }

    private void SetProgress(float hold01)
    {
        if (_progressFill != null)
            _progressFill.fillAmount = hold01;
    }

    private void SetMiddleVisual(float hold01, bool ready)
    {
        if (_midFlash != null)
        {
            Color c = _midFlash.color;
            c.a = ready ? 0.32f : hold01 * 0.22f;
            _midFlash.color = c;
        }
        if (_colMidBorder != null)
        {
            _colMidBorder.color = ready
                ? new Color(0.25f, 0.95f, 0.5f, 0.45f)
                : new Color(1f, 1f, 1f, 0.12f + hold01 * 0.18f);
        }

        if (_hintBackdrop != null && _positionChrome != null && _positionChrome.activeSelf)
        {
            float alpha = ready ? 0.92f : (hold01 > 0.01f ? 0.72f : 0.58f);
            _hintBackdrop.color = new Color(UiTheme.Background.r, UiTheme.Background.g, UiTheme.Background.b, alpha);
        }

        if (_stepTitle != null)
        {
            _stepTitle.color = ready ? UiTheme.Accent : UiTheme.TextPrimary;
        }

        if (_hint != null)
        {
            if (ready)
                _hint.color = UiTheme.Accent;
            else if (hold01 > 0.01f)
                _hint.color = UiTheme.TextPrimary;
            else
                _hint.color = Color.white;
        }

        if (_colLeft != null)
        {
            Color c = _colLeft.color;
            c.a = 0.55f;
            _colLeft.color = c;
        }
        if (_colRight != null)
        {
            Color c = _colRight.color;
            c.a = 0.55f;
            _colRight.color = c;
        }
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void ApplyTextOutline(TextMeshProUGUI tmp, float width)
    {
        if (tmp == null) return;
        tmp.outlineWidth = width;
        tmp.outlineColor = new Color(0.04f, 0.07f, 0.12f, 0.95f);
    }
}
