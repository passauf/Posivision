using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Ana menü: filtreli seans geçmişi, R/L gelişim, tikli ilerleme grafiği, raporlar.
/// </summary>
public class MenuController : MonoBehaviour
{
    [SerializeField] private string exerciseSceneName = UiTheme.ExerciseSceneName;
    [SerializeField] private DataManager dataManager;
    [Header("Menü UI — MenuRoot / MenuCanvas objelerine ata")]
    [SerializeField] private MenuUiBuilder.BuiltUi ui = new MenuUiBuilder.BuiltUi();

    private const float PrefsFlushDelaySeconds = 0.75f;

    private MenuUiBuilder.BuiltUi _ui;
    private bool _built;
    private HistoryFilterMode _dateFilter = HistoryFilterMode.All;
    private HistoryFilterMode _qualityFilter = HistoryFilterMode.All;
    private HistoryFilterMode _exerciseFilter = HistoryFilterMode.All;
    private List<SessionEntry> _filtered = new List<SessionEntry>(32);
    private PatientHistory _lastHistory;
    private readonly List<SessionHistoryRow> _rowPool = new List<SessionHistoryRow>(32);
    private int _activeRowCount;
    private SessionHistoryRow _historyRowTemplate;
    private Coroutine _prefsFlushCo;
    private bool _tmpGlyphsWarmed;

    private void Awake()
    {
        if (dataManager == null)
        {
            dataManager = FindObjectOfType<DataManager>();
            if (dataManager == null)
            {
                var go = new GameObject("DataManager");
                dataManager = go.AddComponent<DataManager>();
            }
        }

        if (ui == null)
            ui = new MenuUiBuilder.BuiltUi();
        if (ui.canvas == null || ui.startButton == null)
            MenuUiBuilder.TryBindFromHierarchy(transform, ui);
        MenuUiBuilder.EnsureEventSystem();
        _ui = ui;
        _built = _ui != null && _ui.canvas != null && _ui.startButton != null;
        if (!_built)
        {
            Debug.LogError("[Menu] MenuRoot UI bağlanamadı. Inspector'da canvas/buton atayın.");
            return;
        }

        UiSafeLayout.ApplyScaler(_ui.canvas);
        ApplyMenuThemeColors();

        SessionHistoryFilter.LoadSaved(out _dateFilter, out _qualityFilter, out _exerciseFilter);

        _ui.startButton.onClick.AddListener(StartExercise);
        if (_ui.openHtmlButton != null)
            _ui.openHtmlButton.onClick.AddListener(OpenHtmlReport);
        if (_ui.compareSessionsButton != null)
            _ui.compareSessionsButton.onClick.AddListener(OpenSessionCompare);
        if (_ui.openFolderButton != null)
            _ui.openFolderButton.onClick.AddListener(OpenReportsFolder);
        if (_ui.refreshButton != null)
            _ui.refreshButton.onClick.AddListener(Refresh);
        if (_ui.deleteDataButton != null)
            _ui.deleteDataButton.onClick.AddListener(OnDeleteDataClicked);
        if (_ui.languageButton != null)
            _ui.languageButton.onClick.AddListener(OnLanguageClicked);
        if (_ui.programButton != null)
            _ui.programButton.onClick.AddListener(OnProgramClicked);
        if (_ui.clinicianButton != null)
            _ui.clinicianButton.onClick.AddListener(OnClinicianClicked);
        if (_ui.notesButton != null)
            _ui.notesButton.onClick.AddListener(OnNotesClicked);
        if (_ui.selectPatientButton != null)
            _ui.selectPatientButton.onClick.AddListener(OnSelectPatientClicked);
        if (_ui.editPatientButton != null)
            _ui.editPatientButton.onClick.AddListener(OnEditPatientClicked);

        // Eski Reports kökündeki HTML/CSV → Patients/{Ad}/ (şifreli) + Html/Csv/Excel düzeni
        PatientProfile migrateProfile = dataManager != null ? dataManager.LoadProfile() : null;
        int migrated = PatientVault.MigrateLegacyReports(migrateProfile);
        migrated += PatientVault.MigratePatientSubfolders();
        migrated += PatientVault.EncryptPlainFilesInPatientFolders();
        migrated += PatientVault.MigrateClinicianFilesOutOfPatientFolders();
        migrated += PatientVault.MigrateCompareFilesOutOfPatientFolders();
        if (migrated > 0)
            Debug.Log(Loc.Format("vault.migrated", migrated));

        if (_ui.dateFilterDropdown != null)
        {
            _ui.dateFilterDropdown.SetValueWithoutNotify(
                Mathf.Max(0, SessionHistoryFilter.IndexOf(_dateFilter, SessionHistoryFilter.DateModes)));
            _ui.dateFilterDropdown.onValueChanged.AddListener(OnDateFilterChanged);
        }
        if (_ui.filterDropdown != null)
        {
            _ui.filterDropdown.SetValueWithoutNotify(
                Mathf.Max(0, SessionHistoryFilter.IndexOf(_qualityFilter, SessionHistoryFilter.QualityModes)));
            _ui.filterDropdown.onValueChanged.AddListener(OnQualityFilterChanged);
        }
        if (_ui.exerciseFilterDropdown != null)
        {
            _ui.exerciseFilterDropdown.SetValueWithoutNotify(
                Mathf.Max(0, SessionHistoryFilter.IndexOf(_exerciseFilter, SessionHistoryFilter.ExerciseModes)));
            _ui.exerciseFilterDropdown.onValueChanged.AddListener(OnExerciseFilterChanged);
        }

        if (_ui.toggleRight != null) _ui.toggleRight.onValueChanged.AddListener(_ => OnGraphToggle());
        if (_ui.toggleLeft != null) _ui.toggleLeft.onValueChanged.AddListener(_ => OnGraphToggle());
        if (_ui.toggleAvg != null) _ui.toggleAvg.onValueChanged.AddListener(_ => OnGraphToggle());
        if (_ui.toggleStrain != null) _ui.toggleStrain.onValueChanged.AddListener(_ => OnGraphToggle());
    }

    [ContextMenu("Bind Menu UI From Hierarchy")]
    private void BindMenuUiFromHierarchy()
    {
        if (ui == null)
            ui = new MenuUiBuilder.BuiltUi();
        MenuUiBuilder.TryBindFromHierarchy(transform, ui);
        _ui = ui;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (ui == null)
            ui = new MenuUiBuilder.BuiltUi();
        if (ui.canvas == null)
            MenuUiBuilder.TryBindFromHierarchy(transform, ui);
    }

    private void Reset()
    {
        BindMenuUiFromHierarchy();
    }
#endif

    private void OnDateFilterChanged(int index)
    {
        _dateFilter = SessionHistoryFilter.FromIndex(index, SessionHistoryFilter.DateModes);
        SessionHistoryFilter.SaveDate(_dateFilter);
        Refresh();
    }

    private void OnQualityFilterChanged(int index)
    {
        _qualityFilter = SessionHistoryFilter.FromIndex(index, SessionHistoryFilter.QualityModes);
        SessionHistoryFilter.SaveQuality(_qualityFilter);
        Refresh();
    }

    private void OnExerciseFilterChanged(int index)
    {
        _exerciseFilter = SessionHistoryFilter.FromIndex(index, SessionHistoryFilter.ExerciseModes);
        SessionHistoryFilter.SaveExercise(_exerciseFilter);
        Refresh();
    }

    private void Start()
    {
        if (SessionStatus.Current == SessionStatus.Phase.Active)
            SessionStatus.MarkIdle();

        WarmupTmpGlyphs();
        ApplyStaticLanguage();
        Refresh();
    }

    private void OnEnable()
    {
        SessionStatus.Changed += UpdateSessionBadge;
        LanguageSettings.LanguageChanged += OnLanguageChanged;
    }

    private void OnDisable()
    {
        SessionStatus.Changed -= UpdateSessionBadge;
        LanguageSettings.LanguageChanged -= OnLanguageChanged;
        LanguageSettings.FlushPrefs();
    }

    private void OnApplicationQuit()
    {
        LanguageSettings.FlushPrefs();
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause)
            LanguageSettings.FlushPrefs();
    }

    private void OnLanguageClicked()
    {
        LanguageSettings.Toggle();
    }

    private void OnLanguageChanged()
    {
        ApplyStaticLanguage();
        if (_lastHistory != null)
            UpdateCards(_lastHistory, _filtered);
        UpdateSessionBadge();
        RelocalizeHistoryRows();
        SchedulePrefsFlush();
    }

    private void SchedulePrefsFlush()
    {
        if (_prefsFlushCo != null)
            StopCoroutine(_prefsFlushCo);
        _prefsFlushCo = StartCoroutine(FlushPrefsDelayed());
    }

    private System.Collections.IEnumerator FlushPrefsDelayed()
    {
        yield return new WaitForSecondsRealtime(PrefsFlushDelaySeconds);
        LanguageSettings.FlushPrefs();
        _prefsFlushCo = null;
    }

    private void ApplyMenuThemeColors()
    {
        if (_ui == null) return;

        StyleButton(_ui.startButton, _ui.startButtonLabel, UiTheme.Cta);
        StyleButton(_ui.deleteDataButton, _ui.deleteDataButtonLabel, UiTheme.Danger);
        StyleButton(_ui.selectPatientButton, _ui.selectPatientButtonLabel, UiTheme.Primary);
        StyleButton(_ui.programButton, _ui.programButtonLabel, UiTheme.Secondary);
        StyleButton(_ui.compareSessionsButton, _ui.compareSessionsButtonLabel, UiTheme.Secondary);
        StyleButton(_ui.openHtmlButton, _ui.openHtmlButtonLabel, UiTheme.ButtonNormal);
        StyleButton(_ui.openFolderButton, _ui.openFolderButtonLabel, UiTheme.ButtonNormal);
        StyleButton(_ui.refreshButton, _ui.refreshButtonLabel, UiTheme.ButtonNormal);
        StyleButton(_ui.languageButton, _ui.languageButtonLabel, UiTheme.ButtonNormal);
        StyleButton(_ui.clinicianButton, _ui.clinicianButtonLabel, UiTheme.ButtonNormal);
        StyleButton(_ui.notesButton, _ui.notesButtonLabel, UiTheme.ButtonNormal);
        StyleButton(_ui.editPatientButton, _ui.editPatientButtonLabel, UiTheme.ButtonNormal);

        if (_ui.titleText != null) _ui.titleText.color = UiTheme.TextPrimary;
        if (_ui.subtitleText != null) _ui.subtitleText.color = UiTheme.TextMuted;
        if (_ui.activePatientText != null) _ui.activePatientText.color = UiTheme.TextPrimary;
        if (_ui.cardLastRomLabel != null) _ui.cardLastRomLabel.color = UiTheme.TextMuted;
        if (_ui.cardCompletionLabel != null) _ui.cardCompletionLabel.color = UiTheme.TextMuted;
        if (_ui.cardCompensationLabel != null) _ui.cardCompensationLabel.color = UiTheme.TextMuted;
        if (_ui.cardLastRom != null) _ui.cardLastRom.color = UiTheme.Success;
        if (_ui.cardCompletion != null) _ui.cardCompletion.color = UiTheme.Primary;
        if (_ui.cardCompensation != null) _ui.cardCompensation.color = UiTheme.Warning;
        if (_ui.graphTitle != null) _ui.graphTitle.color = UiTheme.TextPrimary;
        if (_ui.graphLegend != null) _ui.graphLegend.color = UiTheme.TextMuted;
        if (_ui.historyTitle != null) _ui.historyTitle.color = UiTheme.TextPrimary;
    }

    private static void StyleButton(Button btn, TextMeshProUGUI label, Color fill)
    {
        if (btn == null) return;
        Image img = btn.targetGraphic as Image;
        if (img == null) img = btn.GetComponent<Image>();
        if (img != null) img.color = fill;
        if (label != null) label.color = UiTheme.ContrastOn(fill);
    }

    private const string TmpWarmupGlyphs =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789" +
        "şŞıİğĞüÜöÖçÇ" +
        " .,;:!?+-_/%°·—–…()'\"[]{}";

    private void WarmupTmpGlyphs()
    {
        if (_tmpGlyphsWarmed) return;
        _tmpGlyphsWarmed = true;

        TextMeshProUGUI probe = _ui != null
            ? (_ui.titleText != null ? _ui.titleText : _ui.subtitleText)
            : null;
        if (probe == null && _ui != null)
            probe = _ui.startButtonLabel;

        TMP_FontAsset primary = probe != null ? probe.font : TMP_Settings.defaultFontAsset;
        TryAddGlyphsIfDynamic(primary);

        if (primary != null)
            TryAddGlyphsOnFallbacks(primary.fallbackFontAssetTable);
        TryAddGlyphsOnFallbacks(TMP_Settings.fallbackFontAssets);
    }

    private static void TryAddGlyphsOnFallbacks(List<TMP_FontAsset> fallbacks)
    {
        if (fallbacks == null) return;
        for (int i = 0; i < fallbacks.Count; i++)
            TryAddGlyphsIfDynamic(fallbacks[i]);
    }

    private static void TryAddGlyphsIfDynamic(TMP_FontAsset font)
    {
        if (font == null) return;
        if (font.atlasPopulationMode != AtlasPopulationMode.Dynamic) return;
        font.TryAddCharacters(TmpWarmupGlyphs);
    }

    private void RelocalizeHistoryRows()
    {
        for (int i = 0; i < _activeRowCount; i++)
        {
            if (_rowPool[i] != null)
                _rowPool[i].RefreshLanguage();
        }
    }

    private void ApplyStaticLanguage()
    {
        if (!_built) return;
        SetText(_ui.subtitleText, Loc.T("menu.subtitle"));
        RefreshActivePatientChip();
        SetText(_ui.cardLastRomLabel, Loc.T("menu.card.rom"));
        SetText(_ui.cardCompletionLabel, Loc.T("menu.card.completion"));
        SetText(_ui.cardCompensationLabel, Loc.T("menu.card.compensation"));
        SetText(_ui.graphTitle, Loc.T("menu.graph.title"));
        SetText(_ui.graphLegend, Loc.T("menu.graph.legend"));
        SetText(_ui.historyTitle, Loc.T("menu.history.title"));
        if (_ui.historyText != null && _ui.historyText.gameObject.activeSelf)
            SetText(_ui.historyText, Loc.T("menu.history.empty"));
        SetText(_ui.startButtonLabel, Loc.T("menu.btn.start"));
        SetText(_ui.openHtmlButtonLabel, Loc.T("menu.btn.html"));
        SetText(_ui.compareSessionsButtonLabel, Loc.T("menu.btn.compare"));
        SetText(_ui.openFolderButtonLabel, Loc.T("menu.btn.folder"));
        SetText(_ui.refreshButtonLabel, Loc.T("menu.btn.refresh"));
        SetText(_ui.deleteDataButtonLabel, Loc.T("menu.btn.delete"));
        SetText(_ui.languageButtonLabel, Loc.T("menu.btn.lang"));
        SetText(_ui.programButtonLabel, Loc.T("menu.btn.program"));
        SetText(_ui.notesButtonLabel, Loc.T("menu.btn.notes"));
        SetText(_ui.clinicianButtonLabel, Loc.T("menu.btn.clinician"));
        SetText(_ui.selectPatientButtonLabel, Loc.T("picker.change"));
        SetText(_ui.editPatientButtonLabel, Loc.T("picker.edit"));
        SetText(_ui.dateFilterLabel, Loc.T("filter.date"));
        SetText(_ui.filterLabel, Loc.T("filter.quality"));
        SetText(_ui.exerciseFilterLabel, Loc.T("filter.exercise"));
        RefreshFilterDropdownOptions();
        SetText(_ui.toggleRightLbl, Loc.T("menu.toggle.right"));
        SetText(_ui.toggleLeftLbl, Loc.T("menu.toggle.left"));
        SetText(_ui.toggleAvgLbl, Loc.T("menu.toggle.avg"));
        SetText(_ui.toggleStrainLbl, Loc.T("menu.toggle.strain"));
    }

    private void RefreshFilterDropdownOptions()
    {
        FillDropdown(_ui.dateFilterDropdown, SessionHistoryFilter.DateModes, _dateFilter);
        FillDropdown(_ui.filterDropdown, SessionHistoryFilter.QualityModes, _qualityFilter);
        FillDropdown(_ui.exerciseFilterDropdown, SessionHistoryFilter.ExerciseModes, _exerciseFilter);
    }

    private static void FillDropdown(TMP_Dropdown dropdown, HistoryFilterMode[] modes, HistoryFilterMode current)
    {
        if (dropdown == null || modes == null) return;
        int keep = Mathf.Max(0, SessionHistoryFilter.IndexOf(current, modes));

        if (dropdown.options.Count == modes.Length)
        {
            for (int i = 0; i < modes.Length; i++)
                dropdown.options[i].text = SessionHistoryFilter.ModeLabel(modes[i]);
            if (dropdown.value != keep)
                dropdown.SetValueWithoutNotify(keep);
            dropdown.RefreshShownValue();
            return;
        }

        dropdown.ClearOptions();
        var opts = new List<TMP_Dropdown.OptionData>(modes.Length);
        for (int i = 0; i < modes.Length; i++)
            opts.Add(new TMP_Dropdown.OptionData(SessionHistoryFilter.ModeLabel(modes[i])));
        dropdown.AddOptions(opts);
        dropdown.SetValueWithoutNotify(keep);
        dropdown.RefreshShownValue();
    }

    private static void SetText(TextMeshProUGUI tmp, string value)
    {
        if (tmp != null) tmp.text = value;
    }

    public void Refresh()
    {
        if (!_built || dataManager == null) return;

        PatientHistory raw = dataManager.LoadHistory();
        PatientProfile profile = dataManager.LoadProfile();
        bool hasPatient = profile != null && !string.IsNullOrWhiteSpace(profile.firstName);
        _lastHistory = PatientVault.FilterHistoryForPatient(raw, profile, fallbackToAll: !hasPatient);
        _filtered = SessionHistoryFilter.Filter(_lastHistory, _dateFilter, _qualityFilter, _exerciseFilter);
        SyncFilterDropdowns();
        UpdateCards(_lastHistory, _filtered);
        UpdateHistoryList(_filtered);
        UpdateSessionBadge();
        ApplyGraphTogglesAndDraw();
        RefreshActivePatientChip();
    }

    private void SyncFilterDropdowns()
    {
        SyncOne(_ui.dateFilterDropdown, SessionHistoryFilter.DateModes, _dateFilter);
        SyncOne(_ui.filterDropdown, SessionHistoryFilter.QualityModes, _qualityFilter);
        SyncOne(_ui.exerciseFilterDropdown, SessionHistoryFilter.ExerciseModes, _exerciseFilter);
    }

    private static void SyncOne(TMP_Dropdown dropdown, HistoryFilterMode[] modes, HistoryFilterMode current)
    {
        if (dropdown == null) return;
        int idx = Mathf.Max(0, SessionHistoryFilter.IndexOf(current, modes));
        if (dropdown.value != idx)
            dropdown.SetValueWithoutNotify(idx);
        dropdown.RefreshShownValue();
    }

    private void OnGraphToggle()
    {
        ApplyGraphTogglesAndDraw();
    }

    private void ApplyGraphTogglesAndDraw()
    {
        if (_ui.progressGraph == null) return;
        _ui.progressGraph.ShowRightMax = _ui.toggleRight == null || _ui.toggleRight.isOn;
        _ui.progressGraph.ShowLeftMax = _ui.toggleLeft == null || _ui.toggleLeft.isOn;
        _ui.progressGraph.ShowAvg = _ui.toggleAvg != null && _ui.toggleAvg.isOn;
        _ui.progressGraph.ShowStrain = _ui.toggleStrain == null || _ui.toggleStrain.isOn;
        _ui.progressGraph.Draw(_filtered);
    }

    private void UpdateSessionBadge()
    {
        if (_ui.sessionStatusText == null) return;

        switch (SessionStatus.Current)
        {
            case SessionStatus.Phase.Completed:
                _ui.sessionStatusText.text = Loc.T("menu.session.done");
                if (_ui.sessionStatusBadge != null)
                    _ui.sessionStatusBadge.color = UiTheme.AccentDim;
                _ui.sessionStatusText.color = UiTheme.TextPrimary;
                break;
            case SessionStatus.Phase.Active:
                _ui.sessionStatusText.text = Loc.T("menu.session.active");
                if (_ui.sessionStatusBadge != null)
                    _ui.sessionStatusBadge.color = UiTheme.Accent;
                _ui.sessionStatusText.color = UiTheme.Background;
                break;
            default:
                _ui.sessionStatusText.text = Loc.T("menu.session.idle");
                if (_ui.sessionStatusBadge != null)
                    _ui.sessionStatusBadge.color = UiTheme.ButtonNormal;
                _ui.sessionStatusText.color = UiTheme.TextMuted;
                break;
        }
    }

    private void UpdateCards(PatientHistory fullHistory, List<SessionEntry> filtered)
    {
        if (fullHistory == null || fullHistory.sessions == null || fullHistory.sessions.Count == 0)
        {
            SetCard(_ui.cardLastRom, "—");
            SetCard(_ui.cardCompletion, "—");
            SetCard(_ui.cardCompensation, "—");
            return;
        }

        SessionEntry last = fullHistory.sessions[fullHistory.sessions.Count - 1];
        bool hasSplit = last.rightMaxROM > 0f || last.leftMaxROM > 0f
                        || last.rightCompletedReps > 0 || last.leftCompletedReps > 0;
        if (hasSplit)
        {
            SetCard(_ui.cardLastRom,
                Loc.T("menu.hist.rightRom") + " " + last.rightMaxROM.ToString("F0") + "°  "
                + Loc.T("menu.hist.leftRom") + " " + last.leftMaxROM.ToString("F0") + "°");
        }
        else
        {
            SetCard(_ui.cardLastRom, last.maxROM.ToString("F0") + "°");
        }

        ProgressSummary progress = SessionHistoryFilter.ComputeProgress(filtered);
        SetCard(_ui.cardCompletion, SessionHistoryFilter.FormatProgressCard(progress));
        SetCard(_ui.cardCompensation, last.compensationEvents.ToString());
    }

    private static void SetCard(TextMeshProUGUI tmp, string value)
    {
        if (tmp != null) tmp.text = value;
    }

    private void UpdateHistoryList(List<SessionEntry> filtered)
    {
        if (_ui.historyListRoot == null && _ui.historyText == null) return;

        EnsureHistoryPool();

        int needed = filtered == null ? 0 : filtered.Count;
        if (needed == 0)
        {
            HidePooledRows();
            if (_ui.historyText != null)
            {
                _ui.historyText.gameObject.SetActive(true);
                _ui.historyText.text = Loc.T("menu.history.empty");
                _ui.historyText.color = UiTheme.TextMuted;
            }
            return;
        }

        if (_ui.historyText != null)
            _ui.historyText.gameObject.SetActive(false);

        if (_historyRowTemplate == null)
        {
            Debug.LogWarning("[Menu] SessionRow şablonu yok — geçmiş listesi atlandı.");
            return;
        }

        while (_rowPool.Count < needed)
        {
            SessionHistoryRow clone = Instantiate(_historyRowTemplate, _ui.historyListRoot);
            clone.gameObject.SetActive(false);
            _rowPool.Add(clone);
        }

        int write = 0;
        for (int i = filtered.Count - 1; i >= 0; i--)
        {
            SessionHistoryRow row = _rowPool[write];
            if (!row.gameObject.activeSelf)
                row.gameObject.SetActive(true);
            row.Bind(filtered[i], ResolveSessionNumber(filtered[i]), OpenSessionDetail);
            write++;
        }

        for (int i = write; i < _rowPool.Count; i++)
        {
            if (_rowPool[i].gameObject.activeSelf)
                _rowPool[i].gameObject.SetActive(false);
        }

        _activeRowCount = write;
    }

    private void EnsureHistoryPool()
    {
        if (_ui.historyListRoot == null) return;
        if (_historyRowTemplate != null) return;

        SessionHistoryRow[] existing = _ui.historyListRoot.GetComponentsInChildren<SessionHistoryRow>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            SessionHistoryRow row = existing[i];
            if (row == null || row.transform.parent != _ui.historyListRoot) continue;
            if (_historyRowTemplate == null)
            {
                _historyRowTemplate = row;
                row.gameObject.SetActive(false);
                continue;
            }
            if (!_rowPool.Contains(row))
            {
                row.gameObject.SetActive(false);
                _rowPool.Add(row);
            }
        }
    }

    private void HidePooledRows()
    {
        for (int i = 0; i < _rowPool.Count; i++)
        {
            if (_rowPool[i] != null && _rowPool[i].gameObject.activeSelf)
                _rowPool[i].gameObject.SetActive(false);
        }
        _activeRowCount = 0;
    }

    private int ResolveSessionNumber(SessionEntry s)
    {
        if (_lastHistory == null || _lastHistory.sessions == null || s == null) return 0;
        int idx = _lastHistory.sessions.IndexOf(s);
        if (idx >= 0) return idx + 1;
        for (int i = 0; i < _lastHistory.sessions.Count; i++)
        {
            SessionEntry e = _lastHistory.sessions[i];
            if (e != null && e.dateTime == s.dateTime)
                return i + 1;
        }
        return 0;
    }

    private void OpenSessionDetail(SessionEntry entry)
    {
        if (_ui.canvas == null || entry == null) return;
        SessionEntry previous = FindPreviousSession(_lastHistory, entry);
        int sessionNumber = ResolveSessionNumber(entry);
        SessionDetailPanel.Show(_ui.canvas.transform, entry, previous, sessionNumber);
    }

    private static SessionEntry FindPreviousSession(PatientHistory history, SessionEntry current)
    {
        if (history == null || history.sessions == null || current == null) return null;
        int idx = history.sessions.IndexOf(current);
        if (idx < 0)
        {
            for (int i = 0; i < history.sessions.Count; i++)
            {
                if (history.sessions[i] != null && history.sessions[i].dateTime == current.dateTime)
                {
                    idx = i;
                    break;
                }
            }
        }
        if (idx <= 0) return null;

        // Aynı hareket/bölge tercih; yoksa kronolojik önceki
        SessionEntry chronological = history.sessions[idx - 1];
        for (int i = idx - 1; i >= 0; i--)
        {
            SessionEntry s = history.sessions[i];
            if (s != null && SameExerciseFamily(s, current))
                return s;
        }
        return chronological;
    }

    private static bool SameExerciseFamily(SessionEntry a, SessionEntry b)
    {
        if (a == null || b == null) return false;
        MovementId moveA = ExerciseCatalog.ResolveStoredMovementId(a.bodyRegionId, a.movementId);
        MovementId moveB = ExerciseCatalog.ResolveStoredMovementId(b.bodyRegionId, b.movementId);
        return moveA == moveB;
    }

    public void StartExercise()
    {
        if (!_built || dataManager == null) return;
        PatientProfile profile = dataManager.LoadProfile();
        Transform canvasRoot = _ui.canvas != null ? _ui.canvas.transform : transform;
        if (profile == null || string.IsNullOrWhiteSpace(profile.DisplayName) || !profile.HasValidConsent)
        {
            PatientPickerPanel.Show(canvasRoot, dataManager, () =>
            {
                Refresh();
            });
            return;
        }

        SessionStatus.MarkIdle();
        PreSessionSetupPanel.Show(canvasRoot, dataManager, result =>
        {
            if (!result.confirmed) return;
            SessionLaunchIntent.MarkPrepared();
            SessionStatus.MarkIdle();
            SceneManager.LoadScene(exerciseSceneName);
        });
    }

    private void OnSelectPatientClicked()
    {
        if (!_built || dataManager == null) return;
        Transform root = _ui.canvas != null ? _ui.canvas.transform : transform;
        PatientPickerPanel.Show(root, dataManager, () => Refresh());
    }

    private void OnEditPatientClicked()
    {
        if (!_built || dataManager == null) return;
        PatientProfile p = dataManager.LoadProfile();
        if (p == null || string.IsNullOrEmpty(p.DisplayName))
        {
            OnSelectPatientClicked();
            return;
        }

        Transform root = _ui.canvas != null ? _ui.canvas.transform : transform;
        PreSessionSetupPanel.ShowEditProfile(root, dataManager, result =>
        {
            if (result.confirmed)
                Refresh();
        });
    }

    private void RefreshActivePatientChip()
    {
        if (!_built || _ui.activePatientText == null || dataManager == null) return;
        PatientProfile p = dataManager.LoadProfile();
        string name = p != null ? p.DisplayName : "";
        if (string.IsNullOrEmpty(name))
            _ui.activePatientText.text = Loc.T("picker.activeNone");
        else
            _ui.activePatientText.text = Loc.Format("picker.active", name);

        if (_ui.editPatientButton != null)
            _ui.editPatientButton.interactable = !string.IsNullOrEmpty(name);
    }

    private void OnProgramClicked()
    {
        if (!_built || dataManager == null) return;
        Transform root = _ui.canvas != null ? _ui.canvas.transform : transform;
        CareProgramPanel.Show(root, dataManager);
    }

    private void OnClinicianClicked()
    {
        if (!_built || dataManager == null) return;
        Transform root = _ui.canvas != null ? _ui.canvas.transform : transform;
        ClinicianAccessPanel.Show(root, dataManager);
    }

    private void OnNotesClicked()
    {
        if (!_built || dataManager == null) return;
        Transform root = _ui.canvas != null ? _ui.canvas.transform : transform;
        // Klinik not — PIN kapısı (hasta klasörüne erişim)
        ClinicianAccessPanel.Show(root, dataManager, () =>
        {
            PatientNotesPanel.Show(root, dataManager);
        }, reportOpenMode: true);
    }

    public void OpenSessionCompare()
    {
        if (dataManager == null || _ui.canvas == null) return;
        PatientHistory history = _lastHistory;
        if (history == null)
        {
            PatientHistory raw = dataManager.LoadHistory();
            PatientProfile profile = dataManager.LoadProfile();
            bool hasPatient = profile != null && !string.IsNullOrWhiteSpace(profile.firstName);
            history = PatientVault.FilterHistoryForPatient(raw, profile, fallbackToAll: !hasPatient);
        }
        SessionComparePickerPanel.Show(_ui.canvas.transform, dataManager, history, null);
    }

    public void OpenHtmlReport()
    {
        if (dataManager == null) return;
        Transform root = _ui.canvas != null ? _ui.canvas.transform : transform;
        // Her HTML açılışı uygulama PIN kapısından geçer (şifreli .enc doğrudan tarayıcıya gitmez)
        ClinicianAccessPanel.Show(root, dataManager, () =>
        {
            PatientHistory history = dataManager.LoadHistory();
            PatientProfile profile = dataManager.LoadProfile();
            int planned = ReportExporter.ResolvePlannedSessionsPerWeek(dataManager, profile, history);
            string path = ReportExporter.ExportProgress(
                history, profile, _dateFilter, _qualityFilter, _exerciseFilter, planned);
            if (string.IsNullOrEmpty(path) || !ReportExporter.TryOpenProgressReportWithSessions(path, profile))
                Debug.LogWarning(Loc.T("vault.openFailed"));
        }, reportOpenMode: true);
    }

    public void OpenReportsFolder()
    {
        if (dataManager == null) return;
        Transform root = _ui.canvas != null ? _ui.canvas.transform : transform;
        // PIN zorunlu — şifreli .enc klasörünü açma; çözülmüş rapor klasörünü aç
        ClinicianAccessPanel.Show(root, dataManager, () =>
        {
            PatientProfile profile = dataManager.LoadProfile();
            string patientDir = PatientVault.GetPatientDirectory(profile);
            string unlocked = PatientVault.UnlockPatientFolderToTemp(patientDir);
            if (string.IsNullOrEmpty(unlocked) || !Directory.Exists(unlocked))
            {
                Debug.LogWarning(Loc.T("vault.openFailed"));
                return;
            }
            if (!ReportExporter.TryOpenFolder(unlocked))
                Debug.LogWarning(Loc.T("vault.openFailed"));
        }, reportOpenMode: true);
    }

    private void OnDeleteDataClicked()
    {
        if (_ui.canvas == null || dataManager == null) return;

        ConfirmDialog.Show(
            _ui.canvas.transform,
            PrivacyNotice.DeleteConfirmTitle,
            PrivacyNotice.DeleteConfirmBody,
            Loc.T("privacy.delete.yes"),
            Loc.T("privacy.delete.no"),
            confirmed =>
            {
                if (!confirmed) return;
                dataManager.DeleteAllLocalPatientData();
                SessionStatus.MarkIdle();
                Refresh();
            });
    }
}
