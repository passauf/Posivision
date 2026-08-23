using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Seans öncesi: profil + hedef açı / tekrar seçimi.
/// KVKK: veriler yalnızca bu cihazda saklanır; açık form onayı ile yazılır.
/// SaMD Class B: hedef seçimi karar-destek; teşhis değildir.
/// </summary>
public class PreSessionSetupPanel : MonoBehaviour
{
    public struct Result
    {
        public bool confirmed;
        public PatientProfile profile;
        public bool applyPersonalizedTargets;
        public float targetAngle;
        public int targetReps;
        public int bodyRegionId;
        public int movementId;
    }

    private const int ManualMinReps = 1;
    private const int ManualMaxReps = 30;
    private const int MaxMovesPerRegion = 8;
    private const float ConsentToggleHeight = 88f;
    private const float HeaderH = 58f;
    private const float FooterH = 84f;
    private const float PaneGap = 16f;

    private TMP_InputField _firstNameField;
    private TMP_InputField _lastNameField;
    private TMP_InputField _heightField;
    private TMP_InputField _ageField;
    private TMP_InputField _reasonField;
    private Toggle _rightToggle;
    private Toggle _leftToggle;
    private Toggle _sequentialToggle;
    private bool _armToggleSuppress;
    private Toggle _maleToggle;
    private Toggle _femaleToggle;
    private Toggle _consentToggle;
    private Toggle _personalTargetToggle;
    private TextMeshProUGUI _personalHint;
    private TextMeshProUGUI _angleValueLabel;
    private TextMeshProUGUI _repsValueLabel;
    private TextMeshProUGUI _errorText;
    private TextMeshProUGUI _exerciseHint;
    private Button _confirmBtn;
    private Transform _regionRow;
    private Transform _moveList;
    private readonly ExerciseDefinition[] _moveBuffer = new ExerciseDefinition[MaxMovesPerRegion];
    private readonly Button[] _regionButtons = new Button[6];
    private readonly Image[] _regionImages = new Image[6];
    private System.Action<Result> _onComplete;
    private DataManager _dataManager;
    private PhysioAnalyzer _analyzer;
    private bool _profileOnly;
    private bool _editMode;
    private string _editOriginalFirstName = "";
    private string _editOriginalLastName = "";

    private float _targetAngle;
    private int _targetReps;
    private PersonalizedTargetAdvisor.Suggestion _suggestion;
    private BodyRegionId _selectedRegion = BodyRegionId.Shoulder;
    private MovementId _selectedMovement = MovementId.ShoulderFlexion;
    private readonly List<MovementId> _movementQueue = new List<MovementId>(PatientProfile.MaxPlannedMovements);
    private TextMeshProUGUI _queueHint;

    public static PreSessionSetupPanel Show(Transform canvasRoot, DataManager dataManager, System.Action<Result> onComplete)
    {
        return Show(canvasRoot, dataManager, null, false, false, onComplete);
    }

    public static PreSessionSetupPanel Show(Transform canvasRoot, DataManager dataManager, PhysioAnalyzer analyzer, System.Action<Result> onComplete)
    {
        return Show(canvasRoot, dataManager, analyzer, false, false, onComplete);
    }

    /// <summary>Yeni hasta: yalnızca kişisel bilgi + rıza (açı/tekrar yok).</summary>
    public static PreSessionSetupPanel ShowProfileOnly(Transform canvasRoot, DataManager dataManager, System.Action<Result> onComplete)
    {
        return Show(canvasRoot, dataManager, null, true, false, onComplete);
    }

    /// <summary>
    /// Mevcut hastayı düzenle — patientId korunur; ad değişirse geçmiş seans isimleri güncellenir.
    /// KVKK: rıza ile kayıt. SaMD Class B: yanlış kimlik düzeltmesi.
    /// </summary>
    public static PreSessionSetupPanel ShowEditProfile(Transform canvasRoot, DataManager dataManager, System.Action<Result> onComplete)
    {
        return Show(canvasRoot, dataManager, null, true, true, onComplete);
    }

    public static PreSessionSetupPanel Show(Transform canvasRoot, DataManager dataManager, PhysioAnalyzer analyzer, bool profileOnly, System.Action<Result> onComplete)
    {
        return Show(canvasRoot, dataManager, analyzer, profileOnly, false, onComplete);
    }

    public static PreSessionSetupPanel Show(Transform canvasRoot, DataManager dataManager, PhysioAnalyzer analyzer, bool profileOnly, bool editMode, System.Action<Result> onComplete)
    {
        if (canvasRoot == null) return null;

        var existing = canvasRoot.GetComponentsInChildren<PreSessionSetupPanel>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != null)
                DestroyImmediate(existing[i].gameObject);
        }

        GameObject go = new GameObject("PreSessionSetupPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(Canvas), typeof(GraphicRaycaster), typeof(PreSessionSetupPanel));
        go.transform.SetParent(canvasRoot, false);
        go.transform.SetAsLastSibling();

        var overlay = go.GetComponent<Canvas>();
        overlay.overrideSorting = true;
        overlay.sortingOrder = 280;

        var panel = go.GetComponent<PreSessionSetupPanel>();
        panel._dataManager = dataManager;
        panel._analyzer = analyzer;
        panel._profileOnly = profileOnly;
        panel._editMode = editMode;
        panel._onComplete = onComplete;
        panel.BuildUi();
        panel.LoadExistingProfile();
        return panel;
    }

    private void BuildUi()
    {
        var rt = GetComponent<RectTransform>();
        Stretch(rt);
        var dim = GetComponent<Image>();
        dim.color = new Color(UiTheme.Background.r, UiTheme.Background.g, UiTheme.Background.b, 0.92f);
        dim.raycastTarget = true;

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        UiSafeLayout.BindLandscapeCard(GetComponent<RectTransform>(), cardRt);
        card.GetComponent<Image>().color = UiTheme.Panel;
        card.GetComponent<Image>().raycastTarget = true;

        CreateLabel(card.transform, "Başlık",
            Loc.T(_editMode ? "presession.title.edit"
                : (_profileOnly ? "presession.title.profile" : "presession.title")),
            24f, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(1400f, 32f), TextAlignmentOptions.Center);

        CreateLabel(card.transform, "Hint", PrivacyNotice.ShortHint,
            13f, FontStyles.Normal, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(1400f, 20f), TextAlignmentOptions.Center)
            .color = UiTheme.TextMuted;

        PlaceFooterButtons(card.transform, _profileOnly
            ? (_editMode ? "presession.confirm.edit" : "presession.confirm.profile")
            : "presession.confirm");

        GameObject body = new GameObject("Body", typeof(RectTransform));
        body.transform.SetParent(card.transform, false);
        var bodyRt = body.GetComponent<RectTransform>();
        bodyRt.anchorMin = Vector2.zero;
        bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = new Vector2(20f, FooterH);
        bodyRt.offsetMax = new Vector2(-20f, -HeaderH);

        Transform left = CreateColumn(body.transform, "LeftPane", 0f, 0.5f, 0f, PaneGap * 0.5f);
        Transform right = CreateColumn(body.transform, "RightPane", 0.5f, 1f, PaneGap * 0.5f, 0f);

        AddSectionTitle(left, Loc.T("presession.section.patient"));
        _firstNameField = AddLabeledInput(left, "First", Loc.T("presession.first"), Loc.T("presession.ph.first"));
        _lastNameField = AddLabeledInput(left, "Last", Loc.T("presession.last"), Loc.T("presession.ph.last"));

        Transform metrics = AddHRow(left, "MetricsRow", 72f);
        _heightField = AddLabeledInput(metrics, "Height", Loc.T("presession.height"), "170", true);
        _ageField = AddLabeledInput(metrics, "Age", Loc.T("presession.age"), "");
        _ageField.contentType = TMP_InputField.ContentType.IntegerNumber;

        _reasonField = AddLabeledMultiline(left, "Reason", Loc.T("presession.reason"), Loc.T("presession.ph.reason"), 72f);
        _reasonField.characterLimit = PatientProfile.MaxReasonForCareLength;

        AddSectionTitle(left, Loc.T("presession.gender"));
        Transform genderRow = AddHRow(left, "GenderRow", 40f);
        _maleToggle = AddFlexToggle(genderRow, "MaleToggle", Loc.T("presession.male"), true);
        _femaleToggle = AddFlexToggle(genderRow, "FemaleToggle", Loc.T("presession.female"), false);
        _maleToggle.onValueChanged.AddListener(on => { if (on) _femaleToggle.isOn = false; });
        _femaleToggle.onValueChanged.AddListener(on => { if (on) _maleToggle.isOn = false; });

        AddSectionTitle(left, Loc.T("presession.section.arms"));
        Transform armRow = AddHRow(left, "ArmRow", 40f);
        _rightToggle = AddFlexToggle(armRow, "RightToggle", Loc.T("presession.right"), true);
        _leftToggle = AddFlexToggle(armRow, "LeftToggle", Loc.T("presession.left"), false);
        _sequentialToggle = AddFlexToggle(left, "SeqToggle", Loc.T("presession.sequential"), false);
        _rightToggle.onValueChanged.AddListener(OnRightArmToggled);
        _leftToggle.onValueChanged.AddListener(OnLeftArmToggled);
        _sequentialToggle.onValueChanged.AddListener(OnSequentialToggled);

        PlaceConsentToggle(left);

        string phaseBanner = _profileOnly ? "" : LoadTargetSuggestion();
        if (!_profileOnly)
        {
            AddSectionTitle(right, Loc.T("presession.section.targets"));
            if (!string.IsNullOrEmpty(phaseBanner))
                AddBodyText(right, "PhaseBanner", phaseBanner, 14f, UiTheme.Accent);

            Transform targetRow = AddHRow(right, "TargetRow", 96f);
            _angleValueLabel = CreateStepper(targetRow, "AngleStepper", Loc.T("presession.target.angle"),
                () =>
                {
                    _targetAngle = Mathf.Max(PersonalizedTargetAdvisor.MinAngleDegrees,
                        _targetAngle - PersonalizedTargetAdvisor.AngleStepDegrees);
                    if (_personalTargetToggle != null) _personalTargetToggle.SetIsOnWithoutNotify(false);
                    RefreshTargetLabels();
                },
                () =>
                {
                    _targetAngle = Mathf.Min(PersonalizedTargetAdvisor.MaxAngleDegrees,
                        _targetAngle + PersonalizedTargetAdvisor.AngleStepDegrees);
                    if (_personalTargetToggle != null) _personalTargetToggle.SetIsOnWithoutNotify(false);
                    RefreshTargetLabels();
                });
            _repsValueLabel = CreateStepper(targetRow, "RepsStepper", Loc.T("presession.target.reps"),
                () =>
                {
                    _targetReps = Mathf.Max(ManualMinReps, _targetReps - 1);
                    if (_personalTargetToggle != null) _personalTargetToggle.SetIsOnWithoutNotify(false);
                    RefreshTargetLabels();
                },
                () =>
                {
                    _targetReps = Mathf.Min(ManualMaxReps, _targetReps + 1);
                    if (_personalTargetToggle != null) _personalTargetToggle.SetIsOnWithoutNotify(false);
                    RefreshTargetLabels();
                });

            _personalHint = AddBodyText(right, "PersonalHint", _suggestion.summaryTr, 13f, UiTheme.Accent);
            _personalTargetToggle = AddFlexToggle(right, "PersonalToggle", Loc.T("presession.personal"), true);
            _personalTargetToggle.onValueChanged.AddListener(OnPersonalToggle);
        }

        AddSectionTitle(right, Loc.T("presession.exercise"));
        _exerciseHint = AddBodyText(right, "ExHint", Loc.T("presession.exercise.hint"), 13f, UiTheme.TextMuted);
        BuildExercisePicker(right);
        if (_profileOnly)
        {
            var assessHint = AddBodyText(right, "AssessHint", Loc.T("presession.profileOnly.hint"), 15f, UiTheme.Accent);
            assessHint.enableWordWrapping = true;
        }

        RefreshTargetLabels();
        RefreshExerciseSelectionUi();
    }

    private string LoadTargetSuggestion()
    {
        float fallbackAngle = _analyzer != null ? _analyzer.targetAngleDegrees : PersonalizedTargetAdvisor.DefaultAngle;
        int fallbackReps = _analyzer != null ? _analyzer.targetReps : PersonalizedTargetAdvisor.DefaultReps;
        PatientProfile activeProfile = _dataManager != null ? _dataManager.LoadProfile() : null;
        PatientHistory hist = _dataManager != null
            ? _dataManager.LoadHistoryForPatient(activeProfile)
            : null;
        PatientCareState care = _dataManager != null
            ? _dataManager.LoadCareState(hist, activeProfile)
            : null;

        _suggestion = PersonalizedTargetAdvisor.Suggest(hist, fallbackAngle, fallbackReps);
        _targetAngle = _suggestion.targetAngle;
        _targetReps = _suggestion.targetReps;

        if (care != null && care.phase == CarePhase.ActiveProgram && care.plan != null)
        {
            if (CarePlanBuilder.TryGetTodaysTargets(care.plan, out float pa, out int pr))
            {
                _targetAngle = pa;
                _targetReps = pr;
                _suggestion.summaryTr = Loc.Format("careplan.presession.fromPlan", (int)pa, pr);
            }
        }

        if (activeProfile != null && activeProfile.hasSessionTargets
            && activeProfile.lastSessionTargetAngle >= PersonalizedTargetAdvisor.MinAngleDegrees)
        {
            _targetAngle = activeProfile.lastSessionTargetAngle;
            if (activeProfile.lastSessionTargetReps > 0)
                _targetReps = activeProfile.lastSessionTargetReps;
        }

        if (care != null && care.phase == CarePhase.Assessment)
        {
            return Loc.Format("assess.banner",
                Mathf.Min(care.assessmentSessionCount + 1, PatientCareState.AssessmentSessionTarget),
                PatientCareState.AssessmentSessionTarget);
        }
        if (care != null && care.phase == CarePhase.ActiveProgram)
        {
            return CarePlanBuilder.IsTrainingDay(care.plan)
                ? Loc.T("careplan.today.train")
                : Loc.T("careplan.today.rest");
        }
        return "";
    }

    private void OnPersonalToggle(bool on)
    {
        if (!on) return;
        _targetAngle = _suggestion.targetAngle;
        _targetReps = _suggestion.targetReps;
        RefreshTargetLabels();
        if (_personalHint != null) _personalHint.text = _suggestion.summaryTr;
    }

    private void RefreshTargetLabels()
    {
        if (_angleValueLabel != null)
            _angleValueLabel.text = Loc.Format("presession.target.deg", Mathf.RoundToInt(_targetAngle));
        if (_repsValueLabel != null)
            _repsValueLabel.text = Loc.Format("presession.target.repn", _targetReps);
    }

    private TextMeshProUGUI CreateStepper(Transform parent, string name, string caption,
        UnityEngine.Events.UnityAction onMinus, UnityEngine.Events.UnityAction onPlus)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        root.transform.SetParent(parent, false);
        root.GetComponent<Image>().color = UiTheme.Card;
        var vlg = root.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8);
        vlg.spacing = 4f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var rootLe = root.GetComponent<LayoutElement>();
        rootLe.flexibleWidth = 1f;
        rootLe.minHeight = 88f;
        rootLe.preferredHeight = 96f;

        AddLayoutLabel(root.transform, "Caption", caption, 14f, FontStyles.Bold, 22f);

        GameObject row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(root.transform, false);
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        row.GetComponent<LayoutElement>().preferredHeight = 48f;
        row.GetComponent<LayoutElement>().flexibleWidth = 1f;

        CreateStepButton(row.transform, "Minus", "-", onMinus);

        GameObject valGo = new GameObject("Value", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        valGo.transform.SetParent(row.transform, false);
        valGo.GetComponent<Image>().color = UiTheme.Panel;
        var valLe = valGo.GetComponent<LayoutElement>();
        valLe.flexibleWidth = 1f;
        valLe.minWidth = 80f;
        valLe.preferredHeight = 48f;

        GameObject txtGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        txtGo.transform.SetParent(valGo.transform, false);
        Stretch(txtGo.GetComponent<RectTransform>());
        var tmp = txtGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 26f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
        tmp.raycastTarget = false;

        CreateStepButton(row.transform, "Plus", "+", onPlus);
        return tmp;
    }

    private static void CreateStepButton(Transform parent, string name, string label,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = UiTheme.ButtonNormal;
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = 52f;
        le.preferredHeight = 48f;
        le.minWidth = 52f;
        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        GameObject lbl = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 26f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
    }

    private void BuildExercisePicker(Transform parent)
    {
        GameObject regionGo = new GameObject("RegionRow", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
        regionGo.transform.SetParent(parent, false);
        _regionRow = regionGo.transform;
        var grid = regionGo.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(200f, 40f);
        grid.spacing = new Vector2(8f, 8f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperLeft;
        var regionLe = regionGo.GetComponent<LayoutElement>();
        regionLe.minHeight = 88f;
        regionLe.preferredHeight = 88f;
        regionLe.flexibleWidth = 1f;

        BodyRegionId[] regions =
        {
            BodyRegionId.Shoulder, BodyRegionId.Arm, BodyRegionId.Elbow,
            BodyRegionId.Neck, BodyRegionId.Leg, BodyRegionId.Ankle
        };
        for (int i = 0; i < regions.Length; i++)
        {
            BodyRegionId region = regions[i];
            int captured = i;
            Button btn = CreateChipButton(_regionRow, "R" + i, Loc.T(ExerciseCatalog.RegionLocKey(region)),
                () => OnRegionClicked(region));
            _regionButtons[captured] = btn;
            _regionImages[captured] = btn.GetComponent<Image>();
        }

        GameObject listGo = new GameObject("MoveList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        listGo.transform.SetParent(parent, false);
        _moveList = listGo.transform;
        var listVlg = listGo.GetComponent<VerticalLayoutGroup>();
        listVlg.spacing = 6f;
        listVlg.childAlignment = TextAnchor.UpperLeft;
        listVlg.childControlWidth = true;
        listVlg.childControlHeight = true;
        listVlg.childForceExpandWidth = true;
        listVlg.childForceExpandHeight = false;
        var listLe = listGo.GetComponent<LayoutElement>();
        listLe.minHeight = 120f;
        listLe.preferredHeight = 200f;
        listLe.flexibleWidth = 1f;

        RebuildMoveList();
        if (!_profileOnly)
            _queueHint = AddBodyText(parent, "QueueHint", Loc.T("presession.queue.hint"), 13f, UiTheme.TextMuted);
    }

    private void OnRegionClicked(BodyRegionId region)
    {
        _selectedRegion = region;
        RebuildMoveList();
        RefreshExerciseSelectionUi();
    }

    private void OnMovementClicked(MovementId movementId)
    {
        if (!ExerciseCatalog.IsLiveReady(movementId) && !_profileOnly)
            return;

        if (_profileOnly)
        {
            _selectedMovement = movementId;
        }
        else
        {
            ToggleQueuedMovement(movementId);
            _selectedMovement = movementId;
        }

        RefreshExerciseSelectionUi();
        ApplySequentialArmUi();
        if (!QueueAllowsSequential() && _sequentialToggle != null && _sequentialToggle.isOn)
        {
            _armToggleSuppress = true;
            _sequentialToggle.isOn = false;
            _armToggleSuppress = false;
        }
        EnforceExclusiveArmIfNeeded();
        RebuildMoveList();
        RefreshQueueHint();
    }

    private bool IsMoveSelected(MovementId id)
    {
        if (_profileOnly) return id == _selectedMovement;
        return IndexInQueue(id) >= 0;
    }

    private void ToggleQueuedMovement(MovementId movementId)
    {
        int idx = IndexInQueue(movementId);
        if (idx >= 0)
        {
            if (_movementQueue.Count <= 1) return;
            _movementQueue.RemoveAt(idx);
            _selectedMovement = _movementQueue[_movementQueue.Count - 1];
            return;
        }

        if (_movementQueue.Count >= PatientProfile.MaxPlannedMovements) return;
        _movementQueue.Add(movementId);
    }

    private int IndexInQueue(MovementId movementId)
    {
        for (int i = 0; i < _movementQueue.Count; i++)
        {
            if (_movementQueue[i] == movementId) return i;
        }
        return -1;
    }

    private void EnsureQueueHasCurrent()
    {
        if (_movementQueue.Count > 0) return;
        if (ExerciseCatalog.IsLiveReady(_selectedMovement))
            _movementQueue.Add(_selectedMovement);
        else
            _movementQueue.Add(ExerciseCatalog.DefaultMovementId);
    }

    private void RefreshQueueHint()
    {
        if (_queueHint == null) return;
        EnsureQueueHasCurrent();
        _queueHint.text = Loc.T("presession.queue.hint");
    }

    private void RebuildMoveList()
    {
        if (_moveList == null) return;
        for (int i = _moveList.childCount - 1; i >= 0; i--)
            DestroyImmediate(_moveList.GetChild(i).gameObject);

        int count = ExerciseCatalog.CopyForRegion(_selectedRegion, _moveBuffer);
        for (int i = 0; i < count; i++)
        {
            ExerciseDefinition def = _moveBuffer[i];
            MovementId mid = def.MovementId;
            string label = Loc.T(def.LocKey);
            if (!def.Implemented)
                label += Loc.T("exercise.comingSoon");
            int q = IndexInQueue(mid);
            if (q >= 0 && !_profileOnly)
                label = (q + 1) + ". " + label;

            Button btn = CreateChipButton(_moveList, "M" + i, label, () => OnMovementClicked(mid));
            var img = btn.GetComponent<Image>();
            bool sel = IsMoveSelected(mid);
            if (!def.Implemented)
                img.color = new Color(UiTheme.ButtonNormal.r, UiTheme.ButtonNormal.g, UiTheme.ButtonNormal.b, 0.55f);
            else if (sel)
                img.color = UiTheme.Accent;
            else
                img.color = UiTheme.Card;

            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.fontSize = 13f;
                tmp.color = sel && def.Implemented
                    ? UiTheme.Background
                    : (def.Implemented ? UiTheme.TextPrimary : UiTheme.TextMuted);
            }
        }
    }

    private void RefreshExerciseSelectionUi()
    {
        BodyRegionId[] regions =
        {
            BodyRegionId.Shoulder, BodyRegionId.Arm, BodyRegionId.Elbow,
            BodyRegionId.Neck, BodyRegionId.Leg, BodyRegionId.Ankle
        };
        for (int i = 0; i < _regionImages.Length; i++)
        {
            if (_regionImages[i] == null) continue;
            bool on = regions[i] == _selectedRegion;
            _regionImages[i].color = on ? UiTheme.Accent : UiTheme.Card;
            var tmp = _regionButtons[i] != null
                ? _regionButtons[i].GetComponentInChildren<TextMeshProUGUI>()
                : null;
            if (tmp != null)
                tmp.color = on ? UiTheme.Background : UiTheme.TextPrimary;
        }

        // Hareket listesi renkleri
        int count = ExerciseCatalog.CopyForRegion(_selectedRegion, _moveBuffer);
        for (int i = 0; i < count && i < _moveList.childCount; i++)
        {
            Transform child = _moveList.GetChild(i);
            var img = child.GetComponent<Image>();
            var tmp = child.GetComponentInChildren<TextMeshProUGUI>();
            ExerciseDefinition def = _moveBuffer[i];
            bool sel = IsMoveSelected(def.MovementId);
            if (img != null)
            {
                if (sel && def.Implemented) img.color = UiTheme.Accent;
                else if (!def.Implemented)
                    img.color = new Color(UiTheme.ButtonNormal.r, UiTheme.ButtonNormal.g, UiTheme.ButtonNormal.b, 0.55f);
                else img.color = UiTheme.Card;
            }
            if (tmp != null)
            {
                tmp.color = sel && def.Implemented
                    ? UiTheme.Background
                    : (def.Implemented ? UiTheme.TextPrimary : UiTheme.TextMuted);
            }
        }

        bool liveOk = ExerciseCatalog.IsLiveReady(_selectedMovement);
        if (_confirmBtn != null && !_profileOnly)
            _confirmBtn.interactable = liveOk;
        if (_errorText != null && !_profileOnly && !liveOk)
            _errorText.text = Loc.T("presession.err.movement");
        else if (_errorText != null && liveOk && _errorText.text == Loc.T("presession.err.movement"))
            _errorText.text = "";

        if (_exerciseHint != null)
        {
            ExerciseDefinition hintDef = ExerciseCatalog.GetOrDefault(_selectedMovement);
            if (hintDef.UsesSideProfile)
                _exerciseHint.text = Loc.T("presession.sideGuide");
            else if (hintDef.Camera == CameraProtocol.Frontal && hintDef.Implemented)
                _exerciseHint.text = Loc.T("presession.frontGuide");
            else
                _exerciseHint.text = Loc.T("presession.exercise.hint");
        }

        ApplySequentialArmUi();
    }

    private static Button CreateChipButton(Transform parent, string name, string label,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = UiTheme.Card;
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 36f;
        le.preferredHeight = 36f;
        le.flexibleWidth = 1f;
        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        GameObject lbl = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
        tmp.raycastTarget = false;
        return btn;
    }

    private bool IsSequentialOn()
    {
        return _sequentialToggle != null && _sequentialToggle.isOn && QueueAllowsSequential();
    }

    private bool QueueAllowsSequential()
    {
        if (_profileOnly)
            return ExerciseCatalog.AllowsBilateralSequential(_selectedMovement);
        for (int i = 0; i < _movementQueue.Count; i++)
        {
            if (ExerciseCatalog.AllowsBilateralSequential(_movementQueue[i]))
                return true;
        }
        return ExerciseCatalog.AllowsBilateralSequential(_selectedMovement);
    }

    private bool QueueRequiresExclusiveArm()
    {
        if (_profileOnly)
            return ExerciseCatalog.RequiresExclusiveArm(_selectedMovement);
        for (int i = 0; i < _movementQueue.Count; i++)
        {
            if (ExerciseCatalog.AllowsSimultaneousBilateral(_movementQueue[i]))
                return false;
        }
        return ExerciseCatalog.RequiresExclusiveArm(_selectedMovement);
    }

    private void OnRightArmToggled(bool on)
    {
        if (_armToggleSuppress) return;
        if (IsSequentialOn()) return;
        if (on && QueueRequiresExclusiveArm())
        {
            _armToggleSuppress = true;
            _leftToggle.isOn = false;
            _armToggleSuppress = false;
        }
        else if (!on && !_leftToggle.isOn)
        {
            _armToggleSuppress = true;
            _rightToggle.isOn = true;
            _armToggleSuppress = false;
        }
    }

    private void OnLeftArmToggled(bool on)
    {
        if (_armToggleSuppress) return;
        if (IsSequentialOn()) return;
        if (on && QueueRequiresExclusiveArm())
        {
            _armToggleSuppress = true;
            _rightToggle.isOn = false;
            _armToggleSuppress = false;
        }
        else if (!on && !_rightToggle.isOn)
        {
            _armToggleSuppress = true;
            _leftToggle.isOn = true;
            _armToggleSuppress = false;
        }
    }

    private void OnSequentialToggled(bool on)
    {
        if (_armToggleSuppress) return;
        ApplySequentialArmUi();
    }

    private void ApplySequentialArmUi()
    {
        bool allowSeq = QueueAllowsSequential();
        if (_sequentialToggle != null)
            _sequentialToggle.gameObject.SetActive(allowSeq);

        if (IsSequentialOn())
        {
            _armToggleSuppress = true;
            _rightToggle.isOn = true;
            _leftToggle.isOn = true;
            _armToggleSuppress = false;
            return;
        }

        EnforceExclusiveArmIfNeeded();
    }

    private void EnforceExclusiveArmIfNeeded()
    {
        if (!QueueRequiresExclusiveArm()) return;
        if (IsSequentialOn()) return;
        if (_rightToggle == null || _leftToggle == null) return;
        if (!_rightToggle.isOn && !_leftToggle.isOn)
        {
            _armToggleSuppress = true;
            _rightToggle.isOn = true;
            _armToggleSuppress = false;
            return;
        }
        if (_rightToggle.isOn && _leftToggle.isOn)
        {
            _armToggleSuppress = true;
            _leftToggle.isOn = false;
            _armToggleSuppress = false;
        }
    }

    private void LoadExistingProfile()
    {
        if (_dataManager == null) return;
        PatientProfile p = _dataManager.LoadProfile();
        _editOriginalFirstName = p.firstName ?? "";
        _editOriginalLastName = p.lastName ?? "";
        _firstNameField.text = p.firstName ?? "";
        _lastNameField.text = p.lastName ?? "";
        _heightField.text = p.heightCm.ToString("F0");
        if (p.ageYears > 0) _ageField.text = p.ageYears.ToString();
        if (_reasonField != null)
            _reasonField.text = p.reasonForCare ?? "";
        _rightToggle.isOn = p.measureRightArm;
        _leftToggle.isOn = p.measureLeftArm;
        _selectedRegion = ExerciseCatalog.ClampRegion(p.preferredBodyRegionId);
        _selectedMovement = ExerciseCatalog.ClampMovement(p.preferredMovementId);
        if (ExerciseCatalog.TryGet(_selectedMovement, out ExerciseDefinition loadedDef))
            _selectedRegion = loadedDef.RegionId;
        _movementQueue.Clear();
        if (!_profileOnly && p.plannedMovementIds != null)
        {
            for (int i = 0; i < p.plannedMovementIds.Length && _movementQueue.Count < PatientProfile.MaxPlannedMovements; i++)
            {
                MovementId mid = ExerciseCatalog.ClampMovement(p.plannedMovementIds[i]);
                if (!ExerciseCatalog.IsLiveReady(mid)) continue;
                if (IndexInQueue(mid) >= 0) continue;
                _movementQueue.Add(mid);
            }
        }
        if (_movementQueue.Count == 0)
            _movementQueue.Add(_selectedMovement);
        else
            _selectedMovement = _movementQueue[0];
        if (_sequentialToggle != null)
            _sequentialToggle.isOn = p.sequentialBothArms && QueueAllowsSequential();
        if (!IsSequentialOn())
            EnforceExclusiveArmIfNeeded();
        else if (!_rightToggle.isOn && !_leftToggle.isOn)
        {
            _armToggleSuppress = true;
            _rightToggle.isOn = true;
            _armToggleSuppress = false;
        }
        ApplySequentialArmUi();
        bool female = p.gender == PatientProfile.GenderFemale;
        _maleToggle.isOn = !female;
        _femaleToggle.isOn = female;
        if (_consentToggle != null)
            _consentToggle.isOn = p.HasValidConsent;

        RebuildMoveList();
        RefreshExerciseSelectionUi();
    }

    private void OnConfirm()
    {
        if (_consentToggle == null || !_consentToggle.isOn)
        {
            _errorText.text = Loc.T("presession.err.consent");
            return;
        }

        var profile = new PatientProfile();
        PatientProfile existing = _dataManager != null ? _dataManager.LoadProfile() : null;
        if (existing != null && !string.IsNullOrEmpty(existing.patientId))
            profile.patientId = existing.patientId;
        if (_profileOnly && existing != null)
        {
            profile.hasSessionTargets = existing.hasSessionTargets;
            profile.lastSessionTargetAngle = existing.lastSessionTargetAngle;
            profile.lastSessionTargetReps = existing.lastSessionTargetReps;
            profile.plannedMovementIds = existing.plannedMovementIds;
            profile.plannedMovementIndex = existing.plannedMovementIndex;
        }
        profile.firstName = _firstNameField.text != null ? _firstNameField.text.Trim() : "";
        profile.lastName = _lastNameField.text != null ? _lastNameField.text.Trim() : "";

        if (string.IsNullOrWhiteSpace(profile.firstName) || string.IsNullOrWhiteSpace(profile.lastName))
        {
            _errorText.text = Loc.T("presession.err.name");
            return;
        }

        if (!float.TryParse(_heightField.text, out profile.heightCm))
        {
            _errorText.text = Loc.T("presession.err.height");
            return;
        }

        profile.ageYears = 0;
        if (!string.IsNullOrWhiteSpace(_ageField.text))
        {
            if (!int.TryParse(_ageField.text, out profile.ageYears) || profile.ageYears < 1 || profile.ageYears > 120)
            {
                _errorText.text = Loc.T("presession.err.age");
                return;
            }
        }

        profile.measureRightArm = _rightToggle.isOn;
        profile.measureLeftArm = _leftToggle.isOn;
        profile.sequentialBothArms = _sequentialToggle != null && _sequentialToggle.isOn && QueueAllowsSequential();
        if (profile.sequentialBothArms)
        {
            profile.measureRightArm = true;
            profile.measureLeftArm = true;
        }
        else if (QueueRequiresExclusiveArm())
        {
            if (profile.measureRightArm && profile.measureLeftArm)
                profile.measureLeftArm = false;
            if (!profile.measureRightArm && !profile.measureLeftArm)
            {
                _errorText.text = Loc.T("presession.err.armXor");
                return;
            }
        }
        profile.reasonForCare = PatientProfile.NormalizeReasonForCare(
            _reasonField != null ? _reasonField.text : "");
        if (_femaleToggle != null && _femaleToggle.isOn)
            profile.gender = PatientProfile.GenderFemale;
        else
            profile.gender = PatientProfile.GenderMale;

        if (!_maleToggle.isOn && !_femaleToggle.isOn)
        {
            _errorText.text = Loc.T("presession.err.gender");
            return;
        }

        profile.preferredBodyRegionId = (int)_selectedRegion;
        profile.preferredMovementId = (int)_selectedMovement;

        if (!_profileOnly)
        {
            EnsureQueueHasCurrent();
            int[] ids = new int[_movementQueue.Count];
            for (int i = 0; i < _movementQueue.Count; i++)
                ids[i] = (int)_movementQueue[i];
            profile.SetPlannedMovements(ids, 0);
            profile.hasSessionTargets = true;
            profile.lastSessionTargetAngle = _targetAngle;
            profile.lastSessionTargetReps = _targetReps;
        }

        // Tam seans: yalnızca canlı ölçümü olan hareketle başlat
        if (!_profileOnly && !ExerciseCatalog.IsLiveReady(_selectedMovement))
        {
            _errorText.text = Loc.T("presession.err.movement");
            if (_confirmBtn != null) _confirmBtn.interactable = false;
            return;
        }

        if (!_profileOnly)
        {
            EnsureQueueHasCurrent();
            bool anyLive = false;
            for (int i = 0; i < _movementQueue.Count; i++)
            {
                if (ExerciseCatalog.IsLiveReady(_movementQueue[i]))
                {
                    anyLive = true;
                    break;
                }
            }
            if (!anyLive)
            {
                _errorText.text = Loc.T("presession.err.movement");
                return;
            }
        }

        if (!_profileOnly)
        {
            if (_targetAngle < PersonalizedTargetAdvisor.MinAngleDegrees
                || _targetAngle > PersonalizedTargetAdvisor.MaxAngleDegrees)
            {
                _errorText.text = Loc.T("presession.err.angle");
                return;
            }

            if (_targetReps < ManualMinReps || _targetReps > ManualMaxReps)
            {
                _errorText.text = Loc.T("presession.err.reps");
                return;
            }
        }
        else
        {
            _targetAngle = PersonalizedTargetAdvisor.DefaultAngle;
            _targetReps = PersonalizedTargetAdvisor.DefaultReps;
        }

        profile.consentAccepted = true;
        profile.consentVersion = PatientProfile.ConsentTextVersion;
        profile.consentAcceptedAt = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm");

        if (!profile.IsValidForSession())
        {
            _errorText.text = Loc.T("presession.err.session");
            return;
        }

        PatientProfile previousSnapshot = null;
        if (_editMode && _dataManager != null)
        {
            previousSnapshot = new PatientProfile
            {
                firstName = _editOriginalFirstName,
                lastName = _editOriginalLastName
            };
        }

        if (_dataManager != null)
        {
            if (_editMode)
                _dataManager.SaveProfileAndMigrateIdentity(previousSnapshot, profile);
            else
                _dataManager.SaveProfile(profile);
        }

        _onComplete?.Invoke(new Result
        {
            confirmed = true,
            profile = profile,
            applyPersonalizedTargets = false,
            targetAngle = _targetAngle,
            targetReps = _targetReps,
            bodyRegionId = (int)_selectedRegion,
            movementId = (int)_selectedMovement
        });
        Destroy(gameObject);
    }

    private void OnCancel()
    {
        _onComplete?.Invoke(new Result { confirmed = false, applyPersonalizedTargets = false });
        Destroy(gameObject);
    }

    /// <summary>İptal ve onay aynı yatay satırda (landscape).</summary>
    private void PlaceFooterButtons(Transform card, string confirmKey)
    {
        _errorText = CreateLabel(card, "Error", "", 14f, FontStyles.Normal,
            new Vector2(0.5f, 0f), new Vector2(0f, 58f), new Vector2(1200f, 22f), TextAlignmentOptions.Center);
        _errorText.rectTransform.pivot = new Vector2(0.5f, 0f);
        _errorText.color = UiTheme.Danger;

        CreateButton(card, "CancelBtn", Loc.T("presession.cancel"), UiTheme.ButtonNormal,
            new Vector2(0.5f, 0f), new Vector2(-220f, 16f), new Vector2(260f, 52f), OnCancel);
        _confirmBtn = CreateButton(card, "ConfirmBtn", Loc.T(confirmKey), UiTheme.Cta,
            new Vector2(0.5f, 0f), new Vector2(220f, 16f), new Vector2(380f, 52f), OnConfirm);
    }

    private static Transform CreateColumn(Transform parent, string name, float anchorMinX, float anchorMaxX, float padL, float padR)
    {
        GameObject pane = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        pane.transform.SetParent(parent, false);
        var rt = pane.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(anchorMinX, 0f);
        rt.anchorMax = new Vector2(anchorMaxX, 1f);
        rt.offsetMin = new Vector2(padL, 0f);
        rt.offsetMax = new Vector2(-padR, 0f);
        pane.GetComponent<Image>().color = UiTheme.Card;
        pane.GetComponent<Image>().raycastTarget = true;

        GameObject scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(pane.transform, false);
        Stretch(scrollGo.GetComponent<RectTransform>());
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.offsetMin = new Vector2(8f, 8f);
        scrollRt.offsetMax = new Vector2(-8f, -8f);
        scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        viewport.transform.SetParent(scrollGo.transform, false);
        Stretch(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 12, 16);
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRt;
        return content.transform;
    }

    private static void AddSectionTitle(Transform parent, string text)
    {
        AddLayoutLabel(parent, "Section", text, 16f, FontStyles.Bold, 26f);
    }

    private static TextMeshProUGUI AddLayoutLabel(Transform parent, string name, string text, float font, FontStyles style, float height)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        go.GetComponent<LayoutElement>().minHeight = height;
        go.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = font;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.color = UiTheme.TextPrimary;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static TextMeshProUGUI AddBodyText(Transform parent, string name, string text, float font, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 28f;
        le.preferredHeight = 36f;
        le.flexibleWidth = 1f;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = font;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.color = color;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Transform AddHRow(Transform parent, string name, float height)
    {
        GameObject row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        var le = row.GetComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleWidth = 1f;
        return row.transform;
    }

    private static TMP_InputField AddLabeledInput(Transform parent, string name, string label, string placeholder, bool decimalNum = false)
    {
        GameObject block = new GameObject(name + "Block", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        block.transform.SetParent(parent, false);
        var vlg = block.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 4f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var le = block.GetComponent<LayoutElement>();
        le.minHeight = 68f;
        le.preferredHeight = 70f;
        le.flexibleWidth = 1f;

        AddLayoutLabel(block.transform, "Lbl", label, 13f, FontStyles.Normal, 22f);
        TMP_InputField field = decimalNum
            ? CreateDecimalInput(block.transform, name, placeholder, Vector2.zero, Vector2.zero, new Vector2(0f, 40f))
            : CreateInput(block.transform, name, placeholder, Vector2.zero, Vector2.zero, new Vector2(0f, 40f));
        return field;
    }

    private static TMP_InputField AddLabeledMultiline(Transform parent, string name, string label, string placeholder, float bodyH)
    {
        GameObject block = new GameObject(name + "Block", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        block.transform.SetParent(parent, false);
        var vlg = block.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 4f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var le = block.GetComponent<LayoutElement>();
        le.minHeight = 26f + bodyH;
        le.preferredHeight = 28f + bodyH;
        le.flexibleWidth = 1f;

        AddLayoutLabel(block.transform, "Lbl", label, 13f, FontStyles.Normal, 22f);
        return CreateMultilineInput(block.transform, name, placeholder, Vector2.zero, Vector2.zero, new Vector2(0f, bodyH));
    }

    private static Toggle AddFlexToggle(Transform parent, string name, string label, bool on)
    {
        Toggle t = CreateToggle(parent, name, label, Vector2.zero, Vector2.zero, on);
        var le = t.gameObject.GetComponent<LayoutElement>();
        if (le == null) le = t.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 40f;
        le.preferredHeight = 40f;
        le.flexibleWidth = 1f;
        return t;
    }

    /// <summary>
    /// KVKK onay satırı: uzun metin kaydırılan içeriğe sığmalı; kutu kesilmemeli.
    /// </summary>
    private void PlaceConsentToggle(Transform form)
    {
        _consentToggle = AddFlexToggle(form, "ConsentToggle", PrivacyNotice.ConsentLabel, false);
        var consentLe = _consentToggle.GetComponent<LayoutElement>();
        if (consentLe != null)
        {
            consentLe.minHeight = ConsentToggleHeight;
            consentLe.preferredHeight = ConsentToggleHeight;
        }
        var consentRt = _consentToggle.GetComponent<RectTransform>();
        consentRt.sizeDelta = new Vector2(0f, ConsentToggleHeight);
        StyleConsentLabel(_consentToggle);
    }

    private static void StyleConsentLabel(Toggle consentToggle)
    {
        if (consentToggle == null) return;

        var bg = consentToggle.transform.Find("Background");
        if (bg != null)
        {
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 1f);
            bgRt.anchorMax = new Vector2(0f, 1f);
            bgRt.pivot = new Vector2(0.5f, 1f);
            bgRt.anchoredPosition = new Vector2(16f, -6f);
            bgRt.sizeDelta = new Vector2(24f, 24f);
        }

        var consentLbl = consentToggle.transform.Find("Label");
        if (consentLbl == null) return;
        var lblTmp = consentLbl.GetComponent<TextMeshProUGUI>();
        if (lblTmp != null)
        {
            lblTmp.fontSize = 12f;
            lblTmp.enableWordWrapping = true;
            lblTmp.overflowMode = TextOverflowModes.Overflow;
            lblTmp.alignment = TextAlignmentOptions.TopLeft;
        }
        var lblRt = consentLbl.GetComponent<RectTransform>();
        if (lblRt != null)
        {
            lblRt.anchorMin = new Vector2(0f, 0f);
            lblRt.anchorMax = new Vector2(1f, 1f);
            lblRt.offsetMin = new Vector2(40f, 4f);
            lblRt.offsetMax = new Vector2(-8f, -4f);
        }
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, FontStyles style,
        Vector2 anchor, Vector2 pos, Vector2 sizeDelta, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(anchor.x, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = UiTheme.TextPrimary;
        return tmp;
    }

    private static TMP_InputField CreateInput(Transform parent, string name, string placeholder, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = UiTheme.Card;

        GameObject textArea = new GameObject("Text Area", typeof(RectTransform));
        textArea.transform.SetParent(go.transform, false);
        Stretch(textArea.GetComponent<RectTransform>());

        GameObject phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        phGo.transform.SetParent(textArea.transform, false);
        Stretch(phGo.GetComponent<RectTransform>());
        var ph = phGo.GetComponent<TextMeshProUGUI>();
        ph.text = placeholder;
        ph.fontSize = 16f;
        ph.color = UiTheme.TextMuted;
        ph.alignment = TextAlignmentOptions.Left;

        GameObject txtGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        txtGo.transform.SetParent(textArea.transform, false);
        Stretch(txtGo.GetComponent<RectTransform>());
        var txt = txtGo.GetComponent<TextMeshProUGUI>();
        txt.fontSize = 16f;
        txt.color = UiTheme.TextPrimary;
        txt.alignment = TextAlignmentOptions.Left;
        txt.raycastTarget = true;

        var field = go.GetComponent<TMP_InputField>();
        field.textViewport = textArea.GetComponent<RectTransform>();
        field.textComponent = txt;
        field.placeholder = ph;
        field.contentType = TMP_InputField.ContentType.Standard;
        UiTheme.ApplyVisibleCaret(field);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = size.y > 1f ? size.y : 40f;
        le.minHeight = le.preferredHeight;
        le.flexibleWidth = 1f;
        return field;
    }

    private static TMP_InputField CreateDecimalInput(Transform parent, string name, string placeholder, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var field = CreateInput(parent, name, placeholder, anchor, pos, size);
        field.contentType = TMP_InputField.ContentType.DecimalNumber;
        return field;
    }

    private static TMP_InputField CreateMultilineInput(Transform parent, string name, string placeholder, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var field = CreateInput(parent, name, placeholder, anchor, pos, size);
        field.lineType = TMP_InputField.LineType.MultiLineNewline;
        field.contentType = TMP_InputField.ContentType.Standard;
        if (field.textComponent != null)
        {
            field.textComponent.enableWordWrapping = true;
            field.textComponent.alignment = TextAlignmentOptions.TopLeft;
        }
        if (field.placeholder is TextMeshProUGUI ph)
        {
            ph.enableWordWrapping = true;
            ph.alignment = TextAlignmentOptions.TopLeft;
        }
        return field;
    }

    private static Toggle CreateToggle(Transform parent, string name, string label, Vector2 anchor, Vector2 pos, bool defaultOn)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(400f, 32f);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 40f;
        le.preferredHeight = 40f;
        le.flexibleWidth = 1f;

        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bg.transform.SetParent(go.transform, false);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(0f, 0.5f);
        bgRt.sizeDelta = new Vector2(24f, 24f);
        bgRt.anchoredPosition = new Vector2(12f, 0f);
        bg.GetComponent<Image>().color = UiTheme.ButtonNormal;

        GameObject check = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        check.transform.SetParent(bg.transform, false);
        Stretch(check.GetComponent<RectTransform>());
        check.GetComponent<Image>().color = UiTheme.Accent;

        GameObject lbl = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        var lblRt = lbl.GetComponent<RectTransform>();
        lblRt.anchorMin = new Vector2(0f, 0f);
        lblRt.anchorMax = new Vector2(1f, 1f);
        lblRt.offsetMin = new Vector2(36f, 0f);
        lblRt.offsetMax = Vector2.zero;
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16f;
        tmp.color = UiTheme.TextPrimary;
        tmp.alignment = TextAlignmentOptions.Left;

        var toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = bg.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = defaultOn;
        return toggle;
    }

    private static Button CreateButton(Transform parent, string name, string label, Color color, Vector2 anchor, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = color;
        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        GameObject lbl = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.ContrastOn(color);
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
