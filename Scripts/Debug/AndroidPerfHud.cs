using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sol üst performans HUD: Unity FPS, Pose FPS/latency, Face ms/Hz, GC.
/// Optimizasyon fazı için ölçüm aracı. Production'da kapatılabilir.
/// KVKK: hasta tanımlayıcı loglanmaz / gösterilmez.
/// </summary>
public class AndroidPerfHud : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 0.35f;
    private const float WarnFpsThreshold = 20f;
    private const float GoodFpsThreshold = 28f;

    [SerializeField] private bool visible = false;
    [Tooltip("Release build'de de göster (yoksa yalnızca Editor / Development). Varsayılan kapalı.")]
    [SerializeField] private bool showInRelease = false;
    [Tooltip("Açıkken Ensure() HUD oluşturur. Kapalıyken ekranda FPS sayacı yok.")]
    [SerializeField] private bool enableHud = false;

    private TextMeshProUGUI _label;
    private readonly StringBuilder _sb = new StringBuilder(192);
    private float _nextRefresh;
    private float _fpsEma = 30f;
    private int _lastGc0 = -1;
    private int _gcSpikeCount;
    private int _lastColorBand = -1;

    public static AndroidPerfHud Ensure()
    {
        var existing = FindObjectOfType<AndroidPerfHud>(true);
        if (existing != null)
        {
            if (!existing.enableHud)
                existing.SetVisible(false);
            return existing;
        }

        // cmd: varsayılan kapalı — ekranda yer kaplamasın
        var go = new GameObject("AndroidPerfHud");
        var hud = go.AddComponent<AndroidPerfHud>();
        hud.enableHud = false;
        hud.visible = false;
        hud.enabled = false;
        return hud;
    }

    private void Awake()
    {
        if (!enableHud)
        {
            visible = false;
            enabled = false;
            return;
        }

        // showInRelease her derlemede okunur (CS0414 önlemi); Editor/Dev'de yok sayılır.
        bool allowReleaseHud = showInRelease;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        allowReleaseHud = true;
#endif
        if (!allowReleaseHud)
        {
            visible = false;
            enabled = false;
            return;
        }

        BuildUi();
    }

    private void BuildUi()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            var canvasGo = new GameObject("PerfHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            UiSafeLayout.ApplyScaler(canvas);
        }
        else
        {
            UiSafeLayout.ApplyScaler(canvas);
        }

        Transform existing = canvas.transform.Find("PerfHudLabel");
        if (existing != null)
        {
            _label = existing.GetComponent<TextMeshProUGUI>();
            return;
        }

        GameObject go = new GameObject("PerfHudLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(canvas.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(12f, -12f);
        rt.sizeDelta = new Vector2(420f, 140f);

        _label = go.GetComponent<TextMeshProUGUI>();
        _label.fontSize = 15f;
        _label.alignment = TextAlignmentOptions.TopLeft;
        _label.color = UiTheme.Accent;
        _label.raycastTarget = false;
        _label.text = "Perf…";
        _label.enabled = visible;
    }

    private void Update()
    {
        if (!visible || _label == null) return;

        float dt = Time.unscaledDeltaTime;
        if (dt > 1e-4f)
        {
            float instant = 1f / dt;
            _fpsEma += 0.12f * (instant - _fpsEma);
        }

        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + RefreshIntervalSeconds;

        PerfStats.SampleMemory();
        int gc0 = PerfStats.GcCount0;
        if (_lastGc0 >= 0 && gc0 > _lastGc0)
            _gcSpikeCount += gc0 - _lastGc0;
        _lastGc0 = gc0;

        float unityFps = _fpsEma;
        // cmd: ToString("F0") her refresh GC üretir — tam sayı Append yeterli
        int fpsI = (int)(unityFps + 0.5f);
        int poseFpsI = (int)(PerfStats.PoseFpsEma + 0.5);
        int poseMsI = (int)(PerfStats.PoseLatencyMsEma + 0.5);
        int faceHzI10 = (int)(PerfStats.FaceHzEma * 10.0 + 0.5);
        int faceMsI = (int)(PerfStats.FaceMsEma + 0.5);
        int allocCenti = (int)(PerfStats.AllocDeltaMb * 100.0 + 0.5);

        _sb.Length = 0;
        _sb.Append("FPS ").Append(fpsI);
        _sb.Append("  |  Pose ").Append(poseFpsI).Append(" Hz ");
        _sb.Append(poseMsI).Append(" ms");
        _sb.Append('\n');
        _sb.Append("Face ").Append(faceHzI10 / 10).Append('.').Append(faceHzI10 % 10).Append(" Hz ");
        _sb.Append(faceMsI).Append(" ms");
        _sb.Append('\n');
        _sb.Append("GC ").Append(_gcSpikeCount);
        _sb.Append("  Δalloc ").Append(allocCenti / 100).Append('.');
        int allocFrac = allocCenti % 100;
        if (allocFrac < 10) _sb.Append('0');
        _sb.Append(allocFrac).Append(" MB");
        _sb.Append('\n');
        _sb.Append("nP ").Append(PerfStats.PoseResultCount);
        _sb.Append("  nF ").Append(PerfStats.FaceResultCount);

        _label.text = _sb.ToString();

        int colorBand = unityFps < WarnFpsThreshold ? 2
            : (unityFps < GoodFpsThreshold ? 1 : 0);
        if (colorBand != _lastColorBand)
        {
            _lastColorBand = colorBand;
            if (colorBand == 2) _label.color = UiTheme.Warning;
            else if (colorBand == 1) _label.color = UiTheme.Warning;
            else _label.color = UiTheme.Success;
        }
    }

    public void SetVisible(bool on)
    {
        visible = on;
        if (_label != null) _label.enabled = on;
        enabled = on;
    }
}
