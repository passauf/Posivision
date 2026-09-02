using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Menü UI referansları — sahne/prefab üzerindeki objelere Inspector ataması.
/// Yeni UI üretmez; MenuController bu alanları bağlar.
/// </summary>
public static class MenuUiBuilder
{
    [Serializable]
    public class BuiltUi
    {
        public Canvas canvas;
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI subtitleText;
        public TextMeshProUGUI activePatientText;
        public TextMeshProUGUI sessionStatusText;
        public Image sessionStatusBadge;
        public TextMeshProUGUI cardLastRom;
        public TextMeshProUGUI cardLastRomLabel;
        public TextMeshProUGUI cardCompletion;
        public TextMeshProUGUI cardCompletionLabel;
        public TextMeshProUGUI cardCompensation;
        public TextMeshProUGUI cardCompensationLabel;
        public TextMeshProUGUI graphTitle;
        public TextMeshProUGUI graphLegend;
        public TextMeshProUGUI historyTitle;
        public TextMeshProUGUI historyText;
        public Transform historyListRoot;
        public ProgressGraphRenderer progressGraph;
        public Button startButton;
        public Button openHtmlButton;
        public Button compareSessionsButton;
        public Button openFolderButton;
        public Button refreshButton;
        public Button deleteDataButton;
        public Button languageButton;
        public Button programButton;
        public Button clinicianButton;
        public Button notesButton;
        public Button selectPatientButton;
        public Button editPatientButton;
        public TextMeshProUGUI startButtonLabel;
        public TextMeshProUGUI openHtmlButtonLabel;
        public TextMeshProUGUI compareSessionsButtonLabel;
        public TextMeshProUGUI openFolderButtonLabel;
        public TextMeshProUGUI refreshButtonLabel;
        public TextMeshProUGUI deleteDataButtonLabel;
        public TextMeshProUGUI languageButtonLabel;
        public TextMeshProUGUI programButtonLabel;
        public TextMeshProUGUI clinicianButtonLabel;
        public TextMeshProUGUI notesButtonLabel;
        public TextMeshProUGUI selectPatientButtonLabel;
        public TextMeshProUGUI editPatientButtonLabel;
        public TMP_Dropdown dateFilterDropdown;
        public TextMeshProUGUI dateFilterLabel;
        public TMP_Dropdown filterDropdown;
        public TextMeshProUGUI filterLabel;
        public TMP_Dropdown regionFilterDropdown;
        public TextMeshProUGUI regionFilterLabel;
        public TMP_Dropdown exerciseFilterDropdown;
        public TextMeshProUGUI exerciseFilterLabel;
        public TMP_Dropdown movementFilterDropdown;
        public TextMeshProUGUI movementFilterLabel;
        public Toggle toggleRight;
        public Toggle toggleLeft;
        public Toggle toggleAvg;
        public Toggle toggleStrain;
        public TextMeshProUGUI toggleRightLbl;
        public TextMeshProUGUI toggleLeftLbl;
        public TextMeshProUGUI toggleAvgLbl;
        public TextMeshProUGUI toggleStrainLbl;
    }

    /// <summary>Sahne hiyerarşisinden (MenuRoot/MenuCanvas) isimle bağlar. UI oluşturmaz.</summary>
    public static bool TryBindFromHierarchy(Transform root, BuiltUi ui)
    {
        if (root == null || ui == null) return false;

        Transform canvasTf = FindChild(root, "MenuCanvas");
        if (canvasTf == null) canvasTf = root.Find("MenuCanvas");
        if (canvasTf == null) return false;

        ui.canvas = canvasTf.GetComponent<Canvas>();
        ui.titleText = FindTmp(canvasTf, "Content/Header/Title");
        ui.subtitleText = FindTmp(canvasTf, "Content/Header/Subtitle");
        ui.activePatientText = FindTmp(canvasTf, "Content/Header/ActivePatient");
        ui.dateFilterLabel = FindTmp(canvasTf, "Content/Header/DateFilterLabel");
        ui.dateFilterDropdown = FindDropdown(canvasTf, "Content/Header/DateFilterDropdown");
        ui.languageButton = FindButton(canvasTf, "Content/Header/LanguageButton");
        ui.languageButtonLabel = FindTmp(canvasTf, "Content/Header/LanguageButton/Label");
        ui.sessionStatusBadge = FindImage(canvasTf, "Content/Header/SessionStatusBadge");
        ui.sessionStatusText = FindTmp(canvasTf, "Content/Header/SessionStatusBadge/StatusLabel");

        ui.cardLastRomLabel = FindTmp(canvasTf, "Content/CardsRow/CardLastRom/Label");
        ui.cardLastRom = FindTmp(canvasTf, "Content/CardsRow/CardLastRom/Value");
        ui.cardCompletionLabel = FindTmp(canvasTf, "Content/CardsRow/CardCompletion/Label");
        ui.cardCompletion = FindTmp(canvasTf, "Content/CardsRow/CardCompletion/Value");
        ui.cardCompensationLabel = FindTmp(canvasTf, "Content/CardsRow/CardCompensation/Label");
        ui.cardCompensation = FindTmp(canvasTf, "Content/CardsRow/CardCompensation/Value");

        ui.filterLabel = FindTmp(canvasTf, "Content/FilterRow/FilterLabel");
        ui.filterDropdown = FindDropdown(canvasTf, "Content/FilterRow/QualityFilterDropdown");
        ui.regionFilterLabel = FindTmp(canvasTf, "Content/FilterRow/RegionFilterLabel");
        ui.regionFilterDropdown = FindDropdown(canvasTf, "Content/FilterRow/RegionFilterDropdown");
        ui.exerciseFilterLabel = FindTmp(canvasTf, "Content/FilterRow/ExerciseFilterLabel");
        ui.exerciseFilterDropdown = FindDropdown(canvasTf, "Content/FilterRow/ExerciseFilterDropdown");
        ui.movementFilterLabel = FindTmp(canvasTf, "Content/FilterRow/MovementFilterLabel");
        ui.movementFilterDropdown = FindDropdown(canvasTf, "Content/FilterRow/MovementFilterDropdown");
        if (ui.movementFilterDropdown == null)
            ui.movementFilterDropdown = ui.exerciseFilterDropdown;
        if (ui.movementFilterLabel == null)
            ui.movementFilterLabel = ui.exerciseFilterLabel;

        ui.graphTitle = FindTmp(canvasTf, "Content/Middle/GraphPanel/GraphTitle");
        ui.graphLegend = FindTmp(canvasTf, "Content/Middle/GraphPanel/Legend");
        ui.progressGraph = FindComp<ProgressGraphRenderer>(canvasTf, "Content/Middle/GraphPanel/ProgressGraph");
        ui.toggleRight = FindToggle(canvasTf, "Content/Middle/GraphPanel/ToggleRow/TogRight");
        ui.toggleLeft = FindToggle(canvasTf, "Content/Middle/GraphPanel/ToggleRow/TogLeft");
        ui.toggleAvg = FindToggle(canvasTf, "Content/Middle/GraphPanel/ToggleRow/TogAvg");
        ui.toggleStrain = FindToggle(canvasTf, "Content/Middle/GraphPanel/ToggleRow/TogStrain");
        ui.toggleRightLbl = FindTmp(canvasTf, "Content/Middle/GraphPanel/ToggleRow/TogRight/Label");
        ui.toggleLeftLbl = FindTmp(canvasTf, "Content/Middle/GraphPanel/ToggleRow/TogLeft/Label");
        ui.toggleAvgLbl = FindTmp(canvasTf, "Content/Middle/GraphPanel/ToggleRow/TogAvg/Label");
        ui.toggleStrainLbl = FindTmp(canvasTf, "Content/Middle/GraphPanel/ToggleRow/TogStrain/Label");

        ui.historyTitle = FindTmp(canvasTf, "Content/Middle/HistoryPanel/HistoryTitle");
        Transform histContent = FindChild(canvasTf, "Content/Middle/HistoryPanel/HistoryScroll/Viewport/Content");
        ui.historyListRoot = histContent;
        ui.historyText = histContent != null ? FindTmp(histContent, "HistoryEmpty") : null;

        ui.startButton = FindButton(canvasTf, "Content/ButtonRow/StartButton");
        ui.openHtmlButton = FindButton(canvasTf, "Content/ButtonRow/OpenHtmlButton");
        ui.compareSessionsButton = FindButton(canvasTf, "Content/ButtonRow/CompareSessionsButton");
        ui.openFolderButton = FindButton(canvasTf, "Content/ButtonRow/OpenFolderButton");
        ui.refreshButton = FindButton(canvasTf, "Content/ButtonRow/RefreshButton");
        ui.deleteDataButton = FindButton(canvasTf, "Content/ButtonRow/DeleteDataButton");
        ui.selectPatientButton = FindButton(canvasTf, "Content/ButtonRow/SelectPatientButton");
        ui.editPatientButton = FindButton(canvasTf, "Content/ButtonRow/EditPatientButton");
        ui.programButton = FindButton(canvasTf, "Content/ButtonRow/ProgramButton");
        ui.notesButton = FindButton(canvasTf, "Content/ButtonRow/NotesButton");
        ui.clinicianButton = FindButton(canvasTf, "Content/ButtonRow/ClinicianButton");

        FillButtonLabel(ui.startButton, ref ui.startButtonLabel);
        FillButtonLabel(ui.openHtmlButton, ref ui.openHtmlButtonLabel);
        FillButtonLabel(ui.compareSessionsButton, ref ui.compareSessionsButtonLabel);
        FillButtonLabel(ui.openFolderButton, ref ui.openFolderButtonLabel);
        FillButtonLabel(ui.refreshButton, ref ui.refreshButtonLabel);
        FillButtonLabel(ui.deleteDataButton, ref ui.deleteDataButtonLabel);
        FillButtonLabel(ui.languageButton, ref ui.languageButtonLabel);
        FillButtonLabel(ui.programButton, ref ui.programButtonLabel);
        FillButtonLabel(ui.clinicianButton, ref ui.clinicianButtonLabel);
        FillButtonLabel(ui.notesButton, ref ui.notesButtonLabel);
        FillButtonLabel(ui.selectPatientButton, ref ui.selectPatientButtonLabel);
        FillButtonLabel(ui.editPatientButton, ref ui.editPatientButtonLabel);

        if (ui.progressGraph != null)
        {
            var raw = ui.progressGraph.GetComponent<RawImage>();
            if (raw != null)
                ui.progressGraph.SetGraphImage(raw);
        }

        return ui.canvas != null && ui.startButton != null;
    }

    private static void FillButtonLabel(Button button, ref TextMeshProUGUI label)
    {
        if (label != null || button == null) return;
        label = button.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private static Transform FindChild(Transform root, string path)
    {
        if (root == null || string.IsNullOrEmpty(path)) return null;
        Transform t = root.Find(path);
        if (t != null) return t;
        t = root.Find(path.StartsWith("MenuCanvas/") ? path.Substring("MenuCanvas/".Length) : path);
        return t;
    }

    private static TextMeshProUGUI FindTmp(Transform root, string path)
    {
        Transform t = FindChild(root, path);
        return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
    }

    private static Button FindButton(Transform root, string path)
    {
        Transform t = FindChild(root, path);
        return t != null ? t.GetComponent<Button>() : null;
    }

    private static Toggle FindToggle(Transform root, string path)
    {
        Transform t = FindChild(root, path);
        return t != null ? t.GetComponent<Toggle>() : null;
    }

    private static TMP_Dropdown FindDropdown(Transform root, string path)
    {
        Transform t = FindChild(root, path);
        return t != null ? t.GetComponent<TMP_Dropdown>() : null;
    }

    private static Image FindImage(Transform root, string path)
    {
        Transform t = FindChild(root, path);
        return t != null ? t.GetComponent<Image>() : null;
    }

    private static T FindComp<T>(Transform root, string path) where T : Component
    {
        Transform t = FindChild(root, path);
        return t != null ? t.GetComponent<T>() : null;
    }

    public static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }
}
