using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Hasta görünen antrenman programı — bölümlü, okunaklı kartlar.
/// Klinisyen notları burada yok. SaMD Class B karar-destek.
/// </summary>
public class CareProgramPanel : MonoBehaviour
{
    private const float CardW = UiSafeLayout.LandscapeOverlayWidth;
    private const float CardH = UiSafeLayout.LandscapeOverlayHeight;

    public static void Show(Transform canvasRoot, DataManager dataManager)
    {
        if (canvasRoot == null || dataManager == null) return;
        var existing = canvasRoot.GetComponentsInChildren<CareProgramPanel>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != null)
                DestroyImmediate(existing[i].gameObject);
        }

        GameObject go = new GameObject("CareProgramPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(Canvas), typeof(GraphicRaycaster), typeof(CareProgramPanel));
        go.transform.SetParent(canvasRoot, false);
        go.transform.SetAsLastSibling();

        var overlay = go.GetComponent<Canvas>();
        overlay.overrideSorting = true;
        overlay.sortingOrder = 260;

        go.GetComponent<CareProgramPanel>().Build(dataManager);
    }

    private void Build(DataManager dataManager)
    {
        var rt = GetComponent<RectTransform>();
        Stretch(rt);
        GetComponent<Image>().color = new Color(UiTheme.Background.r, UiTheme.Background.g, UiTheme.Background.b, 0.92f);
        GetComponent<Image>().raycastTarget = true;

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        UiSafeLayout.BindCenteredCard(rt, cardRt, CardW, CardH);
        card.GetComponent<Image>().color = UiTheme.Panel;

        CreateLabel(card.transform, Loc.T("careplan.title"), 22f, FontStyles.Bold,
            new Vector2(0f, -18f), new Vector2(540f, 32f), TextAlignmentOptions.Center, UiTheme.TextPrimary);

        PatientProfile profile = dataManager.LoadProfile();
        PatientHistory history = dataManager.LoadHistoryForPatient(profile);
        PatientCareState state = dataManager.LoadCareState(history, profile);

        // Scrollable content
        GameObject scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(card.transform, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(24f, 72f);
        scrollRt.offsetMax = new Vector2(-24f, -64f);
        scrollRt.pivot = new Vector2(0.5f, 0.5f);
        scrollRt.anchoredPosition = Vector2.zero;
        scrollRt.sizeDelta = Vector2.zero;
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
        vlg.spacing = 12f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRt;

        BuildContent(content.transform, state, profile);

        CreateBtn(card.transform, Loc.T("detail.btn.back"), new Vector2(0f, 18f), () => Destroy(gameObject));
    }

    private static void BuildContent(Transform root, PatientCareState state, PatientProfile profile)
    {
        if (state == null)
        {
            AddInfoBlock(root, Loc.T("careplan.empty"), UiTheme.TextMuted);
            return;
        }

        if (!string.IsNullOrEmpty(profile != null ? profile.DisplayName : null))
            AddSectionTitle(root, Loc.Format("careplan.forPatient", profile.DisplayName));

        string reason = profile != null
            ? PatientProfile.NormalizeReasonForCare(profile.reasonForCare)
            : "";
        if (!string.IsNullOrEmpty(reason))
            AddInfoBlock(root, Loc.Format("clinician.inapp.reason", reason), UiTheme.TextMuted);

        if (state.phase == CarePhase.Assessment)
        {
            AddBadge(root, Loc.T("careplan.phase.assess"), UiTheme.AccentDim);
            AddInfoBlock(root, Loc.Format("careplan.assess.progress",
                state.assessmentSessionCount, PatientCareState.AssessmentSessionTarget), UiTheme.TextPrimary);
            return;
        }

        CarePlan p = state.plan;
        if (p == null)
        {
            AddInfoBlock(root, Loc.T("careplan.empty"), UiTheme.TextMuted);
            return;
        }

        AddBadge(root, Loc.T("careplan.phase.active"), UiTheme.Accent);

        bool today = CarePlanBuilder.IsTrainingDay(p);
        AddHighlightBlock(root,
            today ? Loc.T("careplan.today.train") : Loc.T("careplan.today.rest"),
            today ? UiTheme.Accent : UiTheme.TextMuted);

        if (!string.IsNullOrEmpty(p.patientSummary))
            AddInfoBlock(root, p.patientSummary, UiTheme.TextMuted);

        // Bugünkü hedef — büyük rakamlar
        AddSectionTitle(root, Loc.T("careplan.section.todayTarget"));
        AddMetricRow(root,
            Loc.T("careplan.metric.angle"), (int)p.dailyTargetAngle + "°",
            Loc.T("careplan.metric.reps"), p.dailyTargetReps.ToString());

        AddSectionTitle(root, Loc.T("careplan.section.week"));
        AddMetricRow(root,
            Loc.T("careplan.metric.sessionsWeek"), p.sessionsPerWeek.ToString(),
            Loc.T("careplan.metric.intensity"), IntensityLabel(p.currentIntensity));

        if (p.monthlyWeeks != null && p.monthlyWeeks.Count > 0)
        {
            AddSectionTitle(root, Loc.T("careplan.section.month"));
            for (int i = 0; i < p.monthlyWeeks.Count; i++)
            {
                CarePlanWeek w = p.monthlyWeeks[i];
                AddWeekRow(root, w);
            }
        }
    }

    private static string IntensityLabel(CareIntensity i)
    {
        switch (i)
        {
            case CareIntensity.Easy: return Loc.T("careplan.intensity.easy");
            case CareIntensity.Deload: return Loc.T("careplan.intensity.deload");
            default: return Loc.T("careplan.intensity.standard");
        }
    }

    private static void AddSectionTitle(Transform parent, string text)
    {
        GameObject go = new GameObject("Sec", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 28f;
        go.GetComponent<LayoutElement>().minHeight = 28f;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 14f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = UiTheme.Accent;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
    }

    private static void AddBadge(Transform parent, string text, Color bg)
    {
        GameObject go = new GameObject("Badge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 36f;
        go.GetComponent<LayoutElement>().minHeight = 36f;
        go.GetComponent<Image>().color = bg;

        GameObject lbl = new GameObject("L", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 14f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
    }

    private static void AddHighlightBlock(Transform parent, string text, Color color)
    {
        GameObject go = new GameObject("Hi", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 44f;
        go.GetComponent<LayoutElement>().minHeight = 44f;
        go.GetComponent<Image>().color = UiTheme.Card;

        GameObject lbl = new GameObject("L", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var lrt = lbl.GetComponent<RectTransform>();
        lrt.offsetMin = new Vector2(12f, 4f);
        lrt.offsetMax = new Vector2(-12f, -4f);
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 16f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.raycastTarget = false;
    }

    private static void AddInfoBlock(Transform parent, string text, Color color)
    {
        GameObject go = new GameObject("Info", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement), typeof(ContentSizeFitter));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 36f;
        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 14f;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
    }

    private static void AddMetricRow(Transform parent, string k1, string v1, string k2, string v2)
    {
        GameObject row = new GameObject("Metrics", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = 72f;
        row.GetComponent<LayoutElement>().minHeight = 72f;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;
        h.childControlWidth = true;
        h.childControlHeight = true;

        AddMetricCard(row.transform, k1, v1);
        AddMetricCard(row.transform, k2, v2);
    }

    private static void AddMetricCard(Transform parent, string key, string value)
    {
        GameObject go = new GameObject("M", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = UiTheme.Card;

        GameObject kGo = new GameObject("K", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        kGo.transform.SetParent(go.transform, false);
        var krt = kGo.GetComponent<RectTransform>();
        krt.anchorMin = new Vector2(0f, 0.55f);
        krt.anchorMax = new Vector2(1f, 1f);
        krt.offsetMin = new Vector2(8f, 0f);
        krt.offsetMax = new Vector2(-8f, -4f);
        var kTmp = kGo.GetComponent<TextMeshProUGUI>();
        kTmp.text = key;
        kTmp.fontSize = 12f;
        kTmp.color = UiTheme.TextMuted;
        kTmp.alignment = TextAlignmentOptions.Center;
        kTmp.raycastTarget = false;

        GameObject vGo = new GameObject("V", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        vGo.transform.SetParent(go.transform, false);
        var vrt = vGo.GetComponent<RectTransform>();
        vrt.anchorMin = new Vector2(0f, 0f);
        vrt.anchorMax = new Vector2(1f, 0.55f);
        vrt.offsetMin = new Vector2(8f, 4f);
        vrt.offsetMax = new Vector2(-8f, 0f);
        var vTmp = vGo.GetComponent<TextMeshProUGUI>();
        vTmp.text = value;
        vTmp.fontSize = 22f;
        vTmp.fontStyle = FontStyles.Bold;
        vTmp.color = UiTheme.TextPrimary;
        vTmp.alignment = TextAlignmentOptions.Center;
        vTmp.raycastTarget = false;
    }

    private static void AddWeekRow(Transform parent, CarePlanWeek w)
    {
        GameObject go = new GameObject("Week", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 40f;
        go.GetComponent<LayoutElement>().minHeight = 40f;
        go.GetComponent<Image>().color = UiTheme.Card;

        string line = Loc.Format("careplan.monthly.weekClean",
            w.weekIndex + 1, (int)w.targetAngle, w.targetReps, IntensityLabel(w.intensity));

        GameObject lbl = new GameObject("L", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var lrt = lbl.GetComponent<RectTransform>();
        lrt.offsetMin = new Vector2(12f, 2f);
        lrt.offsetMax = new Vector2(-12f, -2f);
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = line;
        tmp.fontSize = 14f;
        tmp.color = UiTheme.TextPrimary;
        tmp.alignment = TextAlignmentOptions.Left;
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
