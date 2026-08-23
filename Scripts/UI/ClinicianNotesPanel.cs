using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Klinisyen-only notlar ve özet — uygulama içi (tarayıcıya çıkılmaz).
/// Hasta seans HTML/CSV klasörüne yazılmaz. SaMD Class B / KVKK yerel.
/// </summary>
public class ClinicianNotesPanel : MonoBehaviour
{
    private const float CardW = UiSafeLayout.LandscapeOverlayWidth;
    private const float CardH = UiSafeLayout.LandscapeOverlayHeight;

    public static void Show(Transform canvasRoot, DataManager dataManager)
    {
        if (canvasRoot == null || dataManager == null) return;

        var existing = canvasRoot.GetComponentsInChildren<ClinicianNotesPanel>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != null)
                DestroyImmediate(existing[i].gameObject);
        }

        GameObject go = new GameObject("ClinicianNotesPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(Canvas), typeof(GraphicRaycaster), typeof(ClinicianNotesPanel));
        go.transform.SetParent(canvasRoot, false);
        go.transform.SetAsLastSibling();

        var overlay = go.GetComponent<Canvas>();
        overlay.overrideSorting = true;
        overlay.sortingOrder = 280;

        go.GetComponent<ClinicianNotesPanel>().Build(dataManager);
    }

    private void Build(DataManager dataManager)
    {
        var rt = GetComponent<RectTransform>();
        Stretch(rt);
        GetComponent<Image>().color = new Color(UiTheme.Background.r, UiTheme.Background.g, UiTheme.Background.b, 0.94f);
        GetComponent<Image>().raycastTarget = true;

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        UiSafeLayout.BindCenteredCard(rt, cardRt, CardW, CardH);
        card.GetComponent<Image>().color = UiTheme.Panel;

        CreateLabel(card.transform, Loc.T("clinician.report.title"), 20f, FontStyles.Bold,
            new Vector2(0f, -16f), new Vector2(560f, 30f), TextAlignmentOptions.Center, UiTheme.TextPrimary);

        PatientProfile profile = dataManager.LoadProfile();
        PatientHistory history = dataManager.LoadHistoryForPatient(profile);
        history = PatientVault.FilterHistoryForPatient(history, profile, fallbackToAll: false);
        PatientCareState state = dataManager.LoadCareState(history, profile);

        string patientLine = profile != null && !string.IsNullOrEmpty(profile.DisplayName)
            ? Loc.Format("clinician.inapp.patient", profile.DisplayName)
            : Loc.T("clinician.inapp.noPatient");
        CreateLabel(card.transform, patientLine, 13f, FontStyles.Normal,
            new Vector2(0f, -46f), new Vector2(560f, 22f), TextAlignmentOptions.Center, UiTheme.TextMuted);

        string reason = profile != null
            ? PatientProfile.NormalizeReasonForCare(profile.reasonForCare)
            : "";
        float scrollTopPad = -110f;
        if (!string.IsNullOrEmpty(reason))
        {
            CreateLabel(card.transform, Loc.Format("clinician.inapp.reason", reason), 12f, FontStyles.Normal,
                new Vector2(0f, -68f), new Vector2(560f, 36f), TextAlignmentOptions.Center, UiTheme.Accent);
            scrollTopPad = -130f;
        }

        GameObject scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(card.transform, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0.5f, 0f);
        scrollRt.anchorMax = new Vector2(0.5f, 1f);
        scrollRt.pivot = new Vector2(0.5f, 0.5f);
        scrollRt.anchoredPosition = new Vector2(0f, 18f);
        scrollRt.sizeDelta = new Vector2(560f, scrollTopPad);
        scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        viewport.transform.SetParent(scrollGo.transform, false);
        Stretch(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
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
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRt;

        BuildBody(content.transform, state, history);

        CreateBtn(card.transform, Loc.T("detail.btn.back"), new Vector2(0f, 16f), () => Destroy(gameObject));
    }

    private static void BuildBody(Transform root, PatientCareState state, PatientHistory history)
    {
        if (state == null) state = new PatientCareState();

        AddSection(root, Loc.T("clinician.report.phase"));
        string phase = state.phase == CarePhase.Assessment
            ? Loc.Format("clinician.inapp.phase.assess", state.assessmentSessionCount, PatientCareState.AssessmentSessionTarget)
            : Loc.Format("clinician.inapp.phase.active", state.programVersion);
        AddBlock(root, phase, UiTheme.TextPrimary);

        if (state.plan != null && state.phase == CarePhase.ActiveProgram)
        {
            AddSection(root, Loc.T("clinician.report.plan"));
            AddBlock(root, Loc.Format("clinician.inapp.plan",
                (int)state.plan.dailyTargetAngle,
                state.plan.dailyTargetReps,
                state.plan.sessionsPerWeek,
                string.IsNullOrEmpty(state.lastAdaptedAt) ? "—" : state.lastAdaptedAt), UiTheme.TextPrimary);
        }

        int sessions = history != null && history.sessions != null ? history.sessions.Count : 0;
        AddSection(root, Loc.T("clinician.report.sessions"));
        AddBlock(root, Loc.Format("clinician.inapp.sessions", sessions), UiTheme.TextMuted);

        AddSection(root, Loc.T("clinician.report.notes"));
        int noteCount = state.clinicianNotes != null ? state.clinicianNotes.Count : 0;
        if (noteCount == 0)
        {
            AddBlock(root, Loc.T("clinician.report.noNotes"), UiTheme.TextMuted);
        }
        else
        {
            // En yeni üstte
            for (int i = noteCount - 1; i >= 0; i--)
            {
                ClinicianNote n = state.clinicianNotes[i];
                AddNoteCard(root, n);
            }
        }

        AddBlock(root, Loc.T("clinician.report.disclaimer"), UiTheme.TextMuted);
    }

    private static void AddNoteCard(Transform parent, ClinicianNote n)
    {
        if (n == null) return;

        GameObject go = new GameObject("Note", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = UiTheme.Card;
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 88f;
        var v = go.GetComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(12, 12, 10, 10);
        v.spacing = 4f;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        string header = Loc.Format("clinician.inapp.note.header",
            n.sessionIndex,
            string.IsNullOrEmpty(n.createdAt) ? "—" : n.createdAt,
            string.IsNullOrEmpty(n.reasonCode) ? "—" : n.reasonCode);
        AddInnerText(go.transform, header, 13f, FontStyles.Bold, UiTheme.Accent);

        AddInnerText(go.transform,
            Loc.Format("clinician.inapp.note.claim", string.IsNullOrEmpty(n.patientClaim) ? "—" : n.patientClaim),
            13f, FontStyles.Normal, UiTheme.TextPrimary);

        AddInnerText(go.transform,
            Loc.Format("clinician.inapp.note.obs", string.IsNullOrEmpty(n.observedSummary) ? "—" : n.observedSummary),
            13f, FontStyles.Normal, UiTheme.TextMuted);
    }

    private static void AddSection(Transform parent, string text)
    {
        GameObject go = new GameObject("Sec", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 26f;
        go.GetComponent<LayoutElement>().minHeight = 26f;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 14f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = UiTheme.Accent;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
    }

    private static void AddBlock(Transform parent, string text, Color color)
    {
        GameObject go = new GameObject("Blk", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement), typeof(ContentSizeFitter));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().minHeight = 28f;
        go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 13f;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
    }

    private static void AddInnerText(Transform parent, string text, float size, FontStyles style, Color color)
    {
        GameObject go = new GameObject("T", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement), typeof(ContentSizeFitter));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().minHeight = 18f;
        go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string text, float size, FontStyles style,
        Vector2 pos, Vector2 sizeDelta, TextAlignmentOptions align, Color color)
    {
        GameObject go = new GameObject("Lbl", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void CreateBtn(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Back", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(200f, 44f);
        go.GetComponent<Image>().color = UiTheme.ButtonNormal;
        go.GetComponent<Button>().onClick.AddListener(onClick);
        GameObject lbl = new GameObject("L", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
    }
}
