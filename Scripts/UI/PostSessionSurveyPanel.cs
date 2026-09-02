using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Seans sonu anket (tanıma zorunlu / program check-in).
/// SaMD Class B: özbildirim; teşhis değildir. KVKK: yerel.
/// </summary>
public class PostSessionSurveyPanel : MonoBehaviour
{
    private struct Question
    {
        public string locKey;
        public System.Action<int> setter;
        public System.Func<int> getter;
    }

    private SurveyResponse _response;
    private System.Action<SurveyResponse> _onComplete;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI[] _valueLabels;
    private Question[] _questions;
    private int _qIndex;

    public static void Show(
        Transform canvasRoot,
        PatientCareState state,
        PatientHistory history,
        SessionEntry last,
        System.Action<SurveyResponse> onComplete)
    {
        if (canvasRoot == null)
        {
            onComplete?.Invoke(null);
            return;
        }

        var existing = canvasRoot.GetComponentInChildren<PostSessionSurveyPanel>(true);
        if (existing != null) Destroy(existing.gameObject);

        GameObject go = new GameObject("PostSessionSurveyPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(PostSessionSurveyPanel));
        go.transform.SetParent(canvasRoot, false);
        var panel = go.GetComponent<PostSessionSurveyPanel>();
        panel._onComplete = onComplete;
        panel._response = new SurveyResponse
        {
            sessionIndex = history != null && history.sessions != null ? history.sessions.Count : 0,
            dateTime = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
            perceivedDifficulty = -1,
            painVas = -1,
            motivation = -1,
            fatigue = -1,
            homeExerciseDays = -1,
            sleepQuality = -1,
            confidence = -1,
            willingness = -1
        };
        panel.BuildUi(state);
    }

    private void BuildUi(PatientCareState state)
    {
        var rt = GetComponent<RectTransform>();
        Stretch(rt);
        GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
        GetComponent<Image>().raycastTarget = true;

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        UiSafeLayout.BindLandscapeCard(rt, cardRt, 1100f, 520f);
        card.GetComponent<Image>().color = UiTheme.Panel;

        bool assess = state != null && state.phase == CarePhase.Assessment;
        string title = assess
            ? Loc.Format("survey.title.assess",
                Mathf.Min(state.assessmentSessionCount + 1, PatientCareState.AssessmentSessionTarget),
                PatientCareState.AssessmentSessionTarget)
            : Loc.T("survey.title.checkin");

        _title = CreateLabel(card.transform, "Title", title, 20f, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(480f, 36f));

        CreateLabel(card.transform, "Hint", Loc.T("survey.hint"), 13f, FontStyles.Normal,
            new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(460f, 40f)).color = UiTheme.TextMuted;

        _questions = new Question[]
        {
            new Question { locKey = "survey.q.difficulty", getter = () => _response.perceivedDifficulty, setter = v => _response.perceivedDifficulty = v },
            new Question { locKey = "survey.q.pain", getter = () => _response.painVas, setter = v => _response.painVas = v },
            new Question { locKey = "survey.q.motivation", getter = () => _response.motivation, setter = v => _response.motivation = v },
            new Question { locKey = "survey.q.fatigue", getter = () => _response.fatigue, setter = v => _response.fatigue = v },
            new Question { locKey = "survey.q.homeDays", getter = () => _response.homeExerciseDays, setter = v => _response.homeExerciseDays = v },
            new Question { locKey = "survey.q.sleep", getter = () => _response.sleepQuality, setter = v => _response.sleepQuality = v },
            new Question { locKey = "survey.q.confidence", getter = () => _response.confidence, setter = v => _response.confidence = v },
            new Question { locKey = "survey.q.willingness", getter = () => _response.willingness, setter = v => _response.willingness = v },
        };

        // Tek soru görünümü + ilerleme
        _valueLabels = new TextMeshProUGUI[1];
        CreateLabel(card.transform, "QLabel", "", 16f, FontStyles.Normal,
            new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(460f, 48f));

        _valueLabels[0] = CreateStepper(card.transform, "Stepper", -200f);

        CreateButton(card.transform, "UnknownBtn", Loc.T("survey.unknown"), UiTheme.ButtonNormal,
            new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(220f, 40f), () =>
            {
                _questions[_qIndex].setter(-1);
                NextOrFinish();
            });

        CreateButton(card.transform, "NextBtn", Loc.T("survey.next"), UiTheme.Cta,
            new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(280f, 48f), () =>
            {
                // Değer stepper'dan zaten yazılı; varsayılan 5 ise kullanıcı dokunmamış olabilir — mevcut getter
                if (_questions[_qIndex].getter() < 0)
                    _questions[_qIndex].setter(5);
                NextOrFinish();
            });

        _qIndex = 0;
        RefreshQuestion(card.transform);
    }

    private void NextOrFinish()
    {
        _qIndex++;
        if (_qIndex >= _questions.Length)
        {
            var cb = _onComplete;
            _onComplete = null;
            Destroy(gameObject);
            cb?.Invoke(_response);
            return;
        }
        RefreshQuestion(transform.Find("Card"));
    }

    private void RefreshQuestion(Transform card)
    {
        if (card == null) return;
        var qLbl = card.Find("QLabel");
        if (qLbl != null)
        {
            var tmp = qLbl.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
                tmp.text = Loc.Format("survey.progress", _qIndex + 1, _questions.Length)
                           + "\n" + Loc.T(_questions[_qIndex].locKey);
        }

        int cur = _questions[_qIndex].getter();
        if (cur < 0) cur = 5;
        _questions[_qIndex].setter(cur);
        if (_valueLabels[0] != null)
            _valueLabels[0].text = cur.ToString();
    }

    private TextMeshProUGUI CreateStepper(Transform parent, string name, float y)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 1f);
        rootRt.anchorMax = new Vector2(0.5f, 1f);
        rootRt.pivot = new Vector2(0.5f, 1f);
        rootRt.anchoredPosition = new Vector2(0f, y);
        rootRt.sizeDelta = new Vector2(280f, 48f);

        CreateStepButton(root.transform, "-", new Vector2(-100f, 0f), () =>
        {
            int v = Mathf.Max(0, _questions[_qIndex].getter() < 0 ? 5 : _questions[_qIndex].getter() - 1);
            _questions[_qIndex].setter(v);
            if (_valueLabels[0] != null) _valueLabels[0].text = v.ToString();
        });
        CreateStepButton(root.transform, "+", new Vector2(100f, 0f), () =>
        {
            int max = _questions[_qIndex].locKey == "survey.q.homeDays" ? 7 : 10;
            int v = Mathf.Min(max, _questions[_qIndex].getter() < 0 ? 5 : _questions[_qIndex].getter() + 1);
            _questions[_qIndex].setter(v);
            if (_valueLabels[0] != null) _valueLabels[0].text = v.ToString();
        });

        GameObject valGo = new GameObject("Value", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        valGo.transform.SetParent(root.transform, false);
        var valRt = valGo.GetComponent<RectTransform>();
        valRt.anchorMin = new Vector2(0.5f, 0.5f);
        valRt.anchorMax = new Vector2(0.5f, 0.5f);
        valRt.sizeDelta = new Vector2(100f, 48f);
        valGo.GetComponent<Image>().color = UiTheme.Card;

        GameObject txtGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        txtGo.transform.SetParent(valGo.transform, false);
        Stretch(txtGo.GetComponent<RectTransform>());
        var tmp = txtGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 24f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
        return tmp;
    }

    private static void CreateStepButton(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn" + label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(52f, 48f);
        go.GetComponent<Image>().color = UiTheme.ButtonNormal;
        go.GetComponent<Button>().onClick.AddListener(onClick);

        GameObject lbl = new GameObject("L", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 22f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
    }

    private TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, FontStyles style,
        Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
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

    private void CreateButton(Transform parent, string name, string label, Color color,
        Vector2 anchor, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
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
        go.GetComponent<Button>().onClick.AddListener(onClick);

        GameObject lbl = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.ContrastOn(color);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
