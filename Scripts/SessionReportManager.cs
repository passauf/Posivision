using UnityEngine;
using TMPro;
using System.Text;

/// <summary>
/// Seans boyunca ROM zaman serisi, tekrar ve kompansasyon olaylarını toplar;
/// seans bitiminde notlar + parametre özeti + grafik gösterir.
/// SaMD Class B: seans özeti klinik karar destek bilgisidir; teşhis yerine geçmez.
/// </summary>
public class SessionReportManager : MonoBehaviour
{
    // 10 Hz × 60 dk; dolunca yerinde yarıya sıkıştırılır (tüm seans kapsanır, Hz düşer)
    private const int MaxAngleSamples = 36000;
    private const int MaxCompensationEvents = 256;
    private const int MaxTrackingJumpEvents = 128;
    private const int MaxSecondPersonEvents = 128;
    private const int MaxAssistNearEvents = 128;
    private const float SampleIntervalSeconds = 0.1f; // 10 FPS downsample (DTW kuralı ile uyumlu)

    [Header("Rapor Paneli UI")]
    public GameObject reportPanel;
    public TextMeshProUGUI maxAngleText;
    public TextMeshProUGUI avgAngleText;
    public TextMeshProUGUI durationText;
    public TextMeshProUGUI totalRepsText;

    [Header("Seans Notları ve Parametreler")]
    [SerializeField] private TextMeshProUGUI notesText;
    [SerializeField] private TextMeshProUGUI parametersText;
    [SerializeField] private SessionGraphRenderer graphRenderer;

    [Header("Grafik Ölçek")]
    [SerializeField] private float graphMaxAngleScale = 180f;
    [Tooltip("Canlı seans grafiğinde gösterilen son pencere (sn). 0 = tüm süre.")]
    [SerializeField] private float liveGraphWindowSeconds = 300f;

    // Önceden tahsis edilmiş örnek tamponları (zero-allocation hot path)
    private readonly float[] _sampleTimes = new float[MaxAngleSamples];
    private readonly float[] _rightAngles = new float[MaxAngleSamples];
    private readonly float[] _leftAngles = new float[MaxAngleSamples];
    private readonly bool[] _assistRight = new bool[MaxAngleSamples];
    private readonly bool[] _assistLeft = new bool[MaxAngleSamples];
    private int _sampleCount;

    private readonly float[] _compensationTimes = new float[MaxCompensationEvents];
    private int _compensationCount;

    private readonly float[] _trackingJumpTimes = new float[MaxTrackingJumpEvents];
    private int _trackingJumpCount;

    private readonly float[] _secondPersonTimes = new float[MaxSecondPersonEvents];
    private int _secondPersonCount;

    private readonly float[] _assistNearTimes = new float[MaxAssistNearEvents];
    private int _assistNearCount;

    private float _startTime;
    private float _endTime;
    private float _lastSampleTime = -10f;
    private float _maxAngle;
    private float _angleSum;
    private int _angleSampleTotal;
    private int _completedReps;
    private int _invalidReps;

    // Sağ / sol ayrı istatistik
    private float _maxAngleR;
    private float _maxAngleL;
    private float _angleSumR;
    private float _angleSumL;
    private int _sampleCountR;
    private int _sampleCountL;
    private int _completedRepsR;
    private int _completedRepsL;
    private int _invalidRepsR;
    private int _invalidRepsL;
    private int _assistedRepsR;
    private int _assistedRepsL;
    private bool _trackRight = true;
    private bool _trackLeft = true;

    private int _targetReps;
    private float _targetAngle;
    private bool _sessionActive;
    private bool _reportShown;

    // Hedef hareket ile DTW benzerlik skoru (0..100). Negatif = hesaplanmadı/geçersiz.
    private float _movementScoreRight = -1f;
    private float _movementScoreLeft = -1f;
    private readonly StringBuilder _notesBuilder = new StringBuilder(512);
    private readonly StringBuilder _paramsBuilder = new StringBuilder(512);

    // Yüz zorlanma × açı korelasyonu (10 Hz, SaMD Class B karar-destek)
    private readonly float[] _strainSamples = new float[MaxAngleSamples];
    private float _peakStrain;
    private float _strainSum;
    private int _strainSampleCount;
    private float _angleAtPeakStrainR;
    private float _angleAtPeakStrainL;
    private float _prevSessionPeakStrain = -1f;
    private float _pendingStrainEffort = -1f;
    private float _lastGraphDrawTime = -10f;
    private const float GraphRedrawIntervalSeconds = 1f;

    private float _qualitySum;
    private float _qualityMin = 1f;
    private int _qualitySampleCount;

    // Tampon dolunca yarıya sıkıştırma (uzun seans sürekliliği)
    private int _compactGenerations;
    private int _compensationOverflow;
    private int _trackingJumpOverflow;
    private int _secondPersonOverflow;
    private int _assistNearOverflow;

    private void Awake()
    {
        EnsureReportUi();
    }

    /// <summary>
    /// Inspector'da notes/parameters/graph bağlı değilse reportPanel altında oluşturur.
    /// Düzen: üst parametreler | orta grafik | alt notlar — çakışmasız.
    /// </summary>
    private void EnsureReportUi()
    {
        if (reportPanel == null) return;

        var panelImage = reportPanel.GetComponent<UnityEngine.UI.Image>();
        if (panelImage != null)
            panelImage.color = UiTheme.Panel;

        var panelRt = reportPanel.GetComponent<RectTransform>();
        if (panelRt != null)
        {
            // Rapor panelini ekranın ortasında daha okunaklı bir karta çek
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(720f, 640f);
            panelRt.anchoredPosition = Vector2.zero;
        }

        maxAngleText = EnsureTmp(reportPanel.transform, "MaxAngleText", maxAngleText,
            new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(660f, 28f), UiTheme.Accent, 18f);
        avgAngleText = EnsureTmp(reportPanel.transform, "AvgAngleText", avgAngleText,
            new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(660f, 28f), UiTheme.TextPrimary, 16f);
        durationText = EnsureTmp(reportPanel.transform, "DurationText", durationText,
            new Vector2(0.5f, 1f), new Vector2(0f, -74f), new Vector2(660f, 24f), UiTheme.TextMuted, 15f);
        totalRepsText = EnsureTmp(reportPanel.transform, "TotalRepsText", totalRepsText,
            new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(660f, 28f), UiTheme.TextPrimary, 15f);

        parametersText = EnsureTmp(reportPanel.transform, "SessionParameters", parametersText,
            new Vector2(0.5f, 1f), new Vector2(0f, -132f), new Vector2(660f, 120f), UiTheme.TextPrimary, 14f);

        if (graphRenderer == null)
        {
            Transform existing = reportPanel.transform.Find("SessionGraph");
            GameObject graphGo = existing != null
                ? existing.gameObject
                : new GameObject("SessionGraph", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.RawImage));

            if (existing == null)
                graphGo.transform.SetParent(reportPanel.transform, false);

            var rt = graphGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -40f);
            rt.sizeDelta = new Vector2(640f, 260f);

            graphRenderer = graphGo.GetComponent<SessionGraphRenderer>();
            if (graphRenderer == null) graphRenderer = graphGo.AddComponent<SessionGraphRenderer>();

            var raw = graphGo.GetComponent<UnityEngine.UI.RawImage>();
            raw.color = Color.white;
            graphRenderer.SetGraphImage(raw);
        }

        // Grafik altında eksen açıklaması
        EnsureTmp(reportPanel.transform, "GraphAxisHint", null,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -180f), new Vector2(640f, 20f), UiTheme.TextMuted, 12f)
            .text = "Yatay eksen: zaman (saniye)   ·   Dikey eksen: açı (°)";

        notesText = EnsureTmp(reportPanel.transform, "SessionNotes", notesText,
            new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(660f, 120f), UiTheme.TextMuted, 13f);
    }

    private static TextMeshProUGUI EnsureTmp(
        Transform parent, string name, TextMeshProUGUI existing,
        Vector2 anchor, Vector2 anchoredPos, Vector2 size, Color color, float fontSize)
    {
        TextMeshProUGUI tmp = existing;
        if (tmp == null)
        {
            Transform found = parent.Find(name);
            GameObject go;
            if (found != null)
            {
                go = found.gameObject;
            }
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                go.transform.SetParent(parent, false);
            }
            tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp == null) tmp = go.AddComponent<TextMeshProUGUI>();
        }

        var rt = tmp.rectTransform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, anchor.y > 0.5f ? 1f : (anchor.y < 0.5f ? 0f : 0.5f));
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.color = color;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Truncate;
        if (string.IsNullOrEmpty(tmp.text) || tmp.text == "New Text")
            tmp.text = "";
        return tmp;
    }

    private static TextMeshProUGUI FindOrCreateTmp(Transform parent, string name, Vector2 anchoredPos, Vector2 size, Color color)
    {
        return EnsureTmp(parent, name, null, new Vector2(0.5f, 1f), anchoredPos, size, color, UiTheme.BodyFontSize);
    }

    public bool HasData => _angleSampleTotal > 0 || _completedReps > 0 || _completedRepsR > 0 || _completedRepsL > 0;
    public bool IsSessionActive => _sessionActive;
    public float MaxAngle => _maxAngle;
    public float AverageAngle => _angleSampleTotal > 0 ? _angleSum / _angleSampleTotal : 0f;
    public int CompletedReps => _completedReps;
    public int InvalidReps => _invalidReps;

    public float RightMaxAngle => _maxAngleR;
    public float LeftMaxAngle => _maxAngleL;
    public float RightAverageAngle => _sampleCountR > 0 ? _angleSumR / _sampleCountR : 0f;
    public float LeftAverageAngle => _sampleCountL > 0 ? _angleSumL / _sampleCountL : 0f;
    public int RightCompletedReps => _completedRepsR;
    public int LeftCompletedReps => _completedRepsL;
    public int RightAssistedReps => _assistedRepsR;
    public int LeftAssistedReps => _assistedRepsL;
    public int AssistedReps => _assistedRepsR + _assistedRepsL;
    public int RightIndependentReps => Mathf.Max(0, _completedRepsR - _assistedRepsR);
    public int LeftIndependentReps => Mathf.Max(0, _completedRepsL - _assistedRepsL);
    public int RightInvalidReps => _invalidRepsR;
    public int LeftInvalidReps => _invalidRepsL;
    public int CompensationEventCount => _compensationCount;
    public int TrackingJumpEventCount => _trackingJumpCount;
    public int SecondPersonEventCount => _secondPersonCount;
    public int AssistNearEventCount => _assistNearCount;
    public float SessionDurationSeconds => (_sessionActive ? Time.time : _endTime) - _startTime;

    // Dışa aktarım (HTML/CSV rapor) için salt-okunur veri erişimi
    public float[] SampleTimes => _sampleTimes;
    public float[] RightAngles => _rightAngles;
    public float[] LeftAngles => _leftAngles;
    /// <summary>Örnek başına anatomik sağ kol yardımlı mı (grafik kırmızı segment).</summary>
    public bool[] AssistRightFlags => _assistRight;
    /// <summary>Örnek başına anatomik sol kol yardımlı mı (grafik kırmızı segment).</summary>
    public bool[] AssistLeftFlags => _assistLeft;
    public float[] StrainSamples => _strainSamples;
    public int SampleCount => _sampleCount;
    public float[] CompensationTimes => _compensationTimes;
    public float TargetAngle => _targetAngle;
    public int TargetReps => _targetReps;

    /// <summary>Tampon kaç kez yarıya sıkıştırıldı (0 = ham 10 Hz).</summary>
    public int GraphCompactGenerations => _compactGenerations;
    /// <summary>Grafik zaman serisinin kapsadığı süre (son örnek zamanı).</summary>
    public float GraphSpanSeconds =>
        _sampleCount > 0 ? Mathf.Max(0f, _sampleTimes[_sampleCount - 1]) : 0f;
    /// <summary>Yaklaşık örnekleme hızı (Hz); sıkıştırmadan sonra düşer.</summary>
    public float EffectiveSampleHz =>
        SampleIntervalSeconds > 0f
            ? (1f / SampleIntervalSeconds) / Mathf.Pow(2f, Mathf.Max(0, _compactGenerations))
            : 0f;
    public int CompensationOverflowCount => _compensationOverflow;
    public int TrackingJumpOverflowCount => _trackingJumpOverflow;
    public int SecondPersonOverflowCount => _secondPersonOverflow;
    public int AssistNearOverflowCount => _assistNearOverflow;

    public float PeakStrain => _peakStrain;
    /// <summary>DTW benzerlik 0..100; negatif = yok.</summary>
    public float MovementScoreRight => _movementScoreRight;
    /// <summary>DTW benzerlik 0..100; negatif = yok.</summary>
    public float MovementScoreLeft => _movementScoreLeft;
    public float MeanStrain => _strainSampleCount > 0 ? _strainSum / _strainSampleCount : 0f;
    public float AngleAtPeakStrainRight => _angleAtPeakStrainR;
    public float AngleAtPeakStrainLeft => _angleAtPeakStrainL;

    public float MeanQualityScore => _qualitySampleCount > 0 ? _qualitySum / _qualitySampleCount : -1f;
    public float MinQualityScore => _qualitySampleCount > 0 ? _qualityMin : -1f;
    public int QualitySampleCount => _qualitySampleCount;
    /// <summary>QS-1.0 varsayılan eşikleriyle bant (Inspector eşikleri PhysioAnalyzer’da).</summary>
    public SessionQualityBand QualityBand =>
        SessionQualityScorer.BandFromMeanDefaults(MeanQualityScore);
    public int StrainSampleCount => _strainSampleCount;

    /// <summary>Önceki seansın peakStrain değeri (geçmiş karşılaştırma için; &lt;0 = yok).</summary>
    public void SetPreviousSessionPeakStrain(float previousPeak)
    {
        _prevSessionPeakStrain = previousPeak;
    }

    public void StartSession(int targetReps, float targetAngle, bool trackRight, bool trackLeft)
    {
        _startTime = Time.time;
        _endTime = _startTime;
        _lastSampleTime = -10f;
        _sampleCount = 0;
        _compensationCount = 0;
        _trackingJumpCount = 0;
        _secondPersonCount = 0;
        _assistNearCount = 0;
        _maxAngle = 0f;
        _angleSum = 0f;
        _angleSampleTotal = 0;
        _completedReps = 0;
        _invalidReps = 0;
        _maxAngleR = 0f;
        _maxAngleL = 0f;
        _angleSumR = 0f;
        _angleSumL = 0f;
        _sampleCountR = 0;
        _sampleCountL = 0;
        _completedRepsR = 0;
        _completedRepsL = 0;
        _invalidRepsR = 0;
        _invalidRepsL = 0;
        _assistedRepsR = 0;
        _assistedRepsL = 0;
        _trackRight = trackRight;
        _trackLeft = trackLeft;
        _movementScoreRight = -1f;
        _movementScoreLeft = -1f;
        _peakStrain = 0f;
        _strainSum = 0f;
        _strainSampleCount = 0;
        _angleAtPeakStrainR = 0f;
        _angleAtPeakStrainL = 0f;
        _pendingStrainEffort = -1f;
        _targetReps = targetReps;
        _targetAngle = targetAngle;
        _sessionActive = true;
        _reportShown = false;
        _qualitySum = 0f;
        _qualityMin = 1f;
        _qualitySampleCount = 0;
        _compactGenerations = 0;
        _compensationOverflow = 0;
        _trackingJumpOverflow = 0;
        _secondPersonOverflow = 0;
        _assistNearOverflow = 0;
        _lastGraphDrawTime = -10f;
        _qualityMin = 1f;
        _qualitySampleCount = 0;

        if (reportPanel != null) reportPanel.SetActive(false);
    }

    /// <summary>Geriye uyumluluk — her iki kol aktif.</summary>
    public void StartSession(int targetReps, float targetAngle)
    {
        StartSession(targetReps, targetAngle, true, true);
    }

    /// <summary>Geriye uyumluluk — hedef bilgisi olmadan başlatma.</summary>
    public void StartSession()
    {
        StartSession(0, 160f);
    }

    public void RegisterAngleSample(float rightAngle, float leftAngle, bool trackRight, bool trackLeft)
    {
        RegisterAngleSample(rightAngle, leftAngle, trackRight, trackLeft, _pendingStrainEffort,
            allowPeakUpdate: true, assistRight: false, assistLeft: false);
    }

    /// <summary>
    /// 10 Hz açı örneği; effort01 &gt;= 0 ise aynı karede zorlanma da kaydedilir.
    /// allowPeakUpdate=false iken max ROM şişmez (düşük QualityScore kapısı).
    /// </summary>
    public void RegisterAngleSample(
        float rightAngle, float leftAngle, bool trackRight, bool trackLeft, float effort01)
    {
        RegisterAngleSample(rightAngle, leftAngle, trackRight, trackLeft, effort01,
            allowPeakUpdate: true, assistRight: false, assistLeft: false);
    }

    public void RegisterAngleSample(
        float rightAngle, float leftAngle, bool trackRight, bool trackLeft, bool allowPeakUpdate)
    {
        RegisterAngleSample(rightAngle, leftAngle, trackRight, trackLeft, _pendingStrainEffort,
            allowPeakUpdate, assistRight: false, assistLeft: false);
    }

    public void RegisterAngleSample(
        float rightAngle, float leftAngle, bool trackRight, bool trackLeft,
        bool allowPeakUpdate, bool assistRight, bool assistLeft)
    {
        RegisterAngleSample(rightAngle, leftAngle, trackRight, trackLeft, _pendingStrainEffort,
            allowPeakUpdate, assistRight, assistLeft);
    }

    public void RegisterAngleSample(
        float rightAngle, float leftAngle, bool trackRight, bool trackLeft, float effort01, bool allowPeakUpdate)
    {
        RegisterAngleSample(rightAngle, leftAngle, trackRight, trackLeft, effort01,
            allowPeakUpdate, assistRight: false, assistLeft: false);
    }

    public void RegisterAngleSample(
        float rightAngle, float leftAngle, bool trackRight, bool trackLeft,
        float effort01, bool allowPeakUpdate, bool assistRight, bool assistLeft)
    {
        if (!_sessionActive) return;

        float now = Time.time;
        if (now - _lastSampleTime < SampleIntervalSeconds) return;
        _lastSampleTime = now;
        _pendingStrainEffort = -1f;

        float peak = 0f;
        bool anySample = false;

        if (trackRight && !float.IsNaN(rightAngle))
        {
            // Yardımlıyken o kolun max ROM'u şişmez (Class B)
            if (allowPeakUpdate && !assistRight && rightAngle > _maxAngleR) _maxAngleR = rightAngle;
            _angleSumR += rightAngle;
            _sampleCountR++;
            peak = Mathf.Max(peak, rightAngle);
            anySample = true;
        }

        if (trackLeft && !float.IsNaN(leftAngle))
        {
            if (allowPeakUpdate && !assistLeft && leftAngle > _maxAngleL) _maxAngleL = leftAngle;
            _angleSumL += leftAngle;
            _sampleCountL++;
            peak = Mathf.Max(peak, leftAngle);
            anySample = true;
        }

        if (!anySample) return;

        // Global max: yalnızca yardımlı olmayan kolların peak'inden
        float indepPeak = 0f;
        bool anyIndep = false;
        if (trackRight && !float.IsNaN(rightAngle) && !assistRight)
        {
            indepPeak = Mathf.Max(indepPeak, rightAngle);
            anyIndep = true;
        }
        if (trackLeft && !float.IsNaN(leftAngle) && !assistLeft)
        {
            indepPeak = Mathf.Max(indepPeak, leftAngle);
            anyIndep = true;
        }
        if (anyIndep && allowPeakUpdate && indepPeak > _maxAngle) _maxAngle = indepPeak;

        _angleSum += peak;
        _angleSampleTotal++;

        // Tampon doluysa yerinde yarıya sıkıştır — seans grafiği kesilmesin (Hz düşer)
        if (_sampleCount >= MaxAngleSamples)
            CompactAngleSamplesByHalf();

        _sampleTimes[_sampleCount] = now - _startTime;
        _rightAngles[_sampleCount] = trackRight ? rightAngle : float.NaN;
        _leftAngles[_sampleCount] = trackLeft ? leftAngle : float.NaN;
        _assistRight[_sampleCount] = trackRight && assistRight;
        _assistLeft[_sampleCount] = trackLeft && assistLeft;

        if (!float.IsNaN(effort01) && effort01 >= 0f)
        {
            float effort = Mathf.Clamp01(effort01);
            _strainSamples[_sampleCount] = effort;
            _strainSum += effort;
            _strainSampleCount++;
            if (effort >= _peakStrain)
            {
                _peakStrain = effort;
                if (trackRight && !float.IsNaN(rightAngle))
                    _angleAtPeakStrainR = rightAngle;
                if (trackLeft && !float.IsNaN(leftAngle))
                    _angleAtPeakStrainL = leftAngle;
            }
        }
        else
        {
            _strainSamples[_sampleCount] = _sampleCount > 0 ? _strainSamples[_sampleCount - 1] : 0f;
        }

        _sampleCount++;

        if (now - _lastGraphDrawTime >= GraphRedrawIntervalSeconds && _sampleCount >= 2)
        {
            _lastGraphDrawTime = now;
            DrawGraph();
        }
    }

    /// <summary>
    /// Tampon dolduğunda çift indeksleri atarak yarıya indirger.
    /// Tüm zaman aralığı korunur; efektif örnekleme hızı yarıya iner.
    /// SaMD: grafik kapsaması sürekliliği için; teşhis iddiası yoktur.
    /// </summary>
    private void CompactAngleSamplesByHalf()
    {
        if (_sampleCount < 2) return;
        int newCount = _sampleCount / 2;
        for (int i = 0; i < newCount; i++)
        {
            int src = i * 2;
            int srcNext = src + 1;
            _sampleTimes[i] = _sampleTimes[src];
            _rightAngles[i] = _rightAngles[src];
            _leftAngles[i] = _leftAngles[src];
            _assistRight[i] = _assistRight[src] || (srcNext < _sampleCount && _assistRight[srcNext]);
            _assistLeft[i] = _assistLeft[src] || (srcNext < _sampleCount && _assistLeft[srcNext]);
            float s0 = _strainSamples[src];
            float s1 = srcNext < _sampleCount ? _strainSamples[srcNext] : s0;
            if (float.IsNaN(s0)) s0 = 0f;
            if (float.IsNaN(s1)) s1 = s0;
            _strainSamples[i] = Mathf.Max(s0, s1);
        }
        _sampleCount = newCount;
        _compactGenerations++;
    }

    public void RegisterAngleSample(float rightAngle, float leftAngle)
    {
        RegisterAngleSample(rightAngle, leftAngle, _trackRight, _trackLeft);
    }

    /// <summary>Kalite skoru örneği (0..1). Hot path tahsissiz.</summary>
    public void RegisterQualitySample(float quality01)
    {
        if (!_sessionActive) return;
        float q = Mathf.Clamp01(quality01);
        _qualitySum += q;
        _qualitySampleCount++;
        if (q < _qualityMin) _qualityMin = q;
    }

    /// <summary>
    /// Bir sonraki RegisterAngleSample çağrısına eklenecek zorlanma (0..1).
    /// SaMD Class B: karar-destek; ham yüz verisi saklanmaz.
    /// </summary>
    public void RegisterStrainSample(float effort01, float rightAngle, float leftAngle)
    {
        if (!_sessionActive) return;
        if (float.IsNaN(effort01)) return;
        _pendingStrainEffort = Mathf.Clamp01(effort01);
        // Açı parametreleri RegisterAngleSample'dan gelir; burada yalnızca effort bekletilir.
    }

    /// <summary>
    /// Hedef hareket ile DTW benzerlik skorunu kaydeder (0..100). Negatif = geçersiz.
    /// Rapor gösterilmeden önce (EndSessionAndShowReport öncesi) çağrılmalıdır.
    /// </summary>
    public void SetMovementScore(float rightScore, float leftScore)
    {
        _movementScoreRight = rightScore;
        _movementScoreLeft = leftScore;
    }

    /// <summary>Tekrar tamamlandığında çağrılır (peak açı kaydı için).</summary>
    public void RegisterAngle(float angle, bool isRight)
    {
        if (!_sessionActive) return;
        if (isRight)
        {
            if (angle > _maxAngleR) _maxAngleR = angle;
        }
        else
        {
            if (angle > _maxAngleL) _maxAngleL = angle;
        }
        if (angle > _maxAngle) _maxAngle = angle;
    }

    public void RegisterAngle(float angle)
    {
        RegisterAngle(angle, true);
    }

    public void IncrementRep(bool isRight)
    {
        IncrementRep(isRight, assisted: false);
    }

    /// <summary>
    /// Başarılı tekrar. assisted=true ise yardımcılı tekrar olarak da sayılır (Class B: bağımsız istatistikten ayrılır).
    /// </summary>
    public void IncrementRep(bool isRight, bool assisted)
    {
        if (!_sessionActive) return;
        if (isRight)
        {
            _completedRepsR++;
            _completedReps = Mathf.Max(_completedReps, _completedRepsR);
            if (assisted) _assistedRepsR++;
        }
        else
        {
            _completedRepsL++;
            _completedReps = Mathf.Max(_completedReps, _completedRepsL);
            if (assisted) _assistedRepsL++;
        }
    }

    public void IncrementRep()
    {
        IncrementRep(true, assisted: false);
    }

    /// <summary>Kompansasyon nedeniyle geçersiz sayılan tekrar.</summary>
    public void RegisterInvalidRep(bool isRight)
    {
        if (!_sessionActive) return;
        if (isRight)
        {
            _invalidRepsR++;
            _invalidReps = Mathf.Max(_invalidReps, _invalidRepsR);
        }
        else
        {
            _invalidRepsL++;
            _invalidReps = Mathf.Max(_invalidReps, _invalidRepsL);
        }
    }

    public void RegisterInvalidRep()
    {
        RegisterInvalidRep(true);
    }

    public void RegisterCompensationEvent()
    {
        if (!_sessionActive) return;
        if (_compensationCount >= MaxCompensationEvents)
        {
            _compensationOverflow++;
            return;
        }
        _compensationTimes[_compensationCount] = Time.time - _startTime;
        _compensationCount++;
    }

    /// <summary>
    /// Kadraj dışı / takip sıçraması — seans notu için zaman damgası.
    /// SaMD Class B: kalite bağlamı; teşhis değildir.
    /// </summary>
    public void RegisterTrackingJumpEvent()
    {
        if (!_sessionActive) return;
        if (_trackingJumpCount >= MaxTrackingJumpEvents)
        {
            _trackingJumpOverflow++;
            return;
        }
        _trackingJumpTimes[_trackingJumpCount] = Time.time - _startTime;
        _trackingJumpCount++;
    }

    /// <summary>
    /// Sahnede 2. kişi algılandı (manuel yardım kapalı olsa da kaydedilir).
    /// SaMD Class B: yardım bağlamı; teşhis değildir.
    /// </summary>
    public void RegisterSecondPersonEvent()
    {
        if (!_sessionActive) return;
        if (_secondPersonCount >= MaxSecondPersonEvents)
        {
            _secondPersonOverflow++;
            return;
        }
        _secondPersonTimes[_secondPersonCount] = Time.time - _startTime;
        _secondPersonCount++;
    }

    /// <summary>
    /// Yardımlı sezgi (temas + hız vektörü + süreğenlik) ile otomatik yardım.
    /// SaMD Class B: yardım bağlamı; teşhis değildir.
    /// </summary>
    public void RegisterAssistNearEvent()
    {
        if (!_sessionActive) return;
        if (_assistNearCount >= MaxAssistNearEvents)
        {
            _assistNearOverflow++;
            return;
        }
        _assistNearTimes[_assistNearCount] = Time.time - _startTime;
        _assistNearCount++;
    }

    public void EndSessionAndShowReport()
    {
        if (!_sessionActive && _reportShown) return;

        EndSessionCore();
        ShowReport();
        _reportShown = true;
    }

    /// <summary>
    /// UI göstermeden seansı kapatır (ani çıkış / HTML export). Süre dondurulur.
    /// </summary>
    public void EndSessionSilent()
    {
        EndSessionCore();
    }

    private void EndSessionCore()
    {
        if (!_sessionActive) return;
        _endTime = Time.time;
        _sessionActive = false;
    }

    public void ShowReport()
    {
        if (reportPanel != null) reportPanel.SetActive(true);

        float duration = Mathf.Max(0f, _endTime - _startTime);
        float average = AverageAngle;
        float completion = _targetReps > 0
            ? Mathf.Clamp01((float)_completedReps / _targetReps) * 100f
            : 0f;

        if (maxAngleText != null)
        {
            maxAngleText.text = Loc.Format("report.ui.maxSplit",
                _maxAngleR.ToString("F0"), _maxAngleL.ToString("F0"));
        }
        if (avgAngleText != null)
        {
            avgAngleText.text = Loc.Format("report.ui.avgSplit",
                RightAverageAngle.ToString("F1"), LeftAverageAngle.ToString("F1"));
        }
        if (durationText != null)
        {
            durationText.text = Loc.Format("report.ui.duration",
                Mathf.FloorToInt(duration / 60).ToString("00"),
                Mathf.FloorToInt(duration % 60).ToString("00"));
        }
        if (totalRepsText != null)
        {
            totalRepsText.text = _targetReps > 0
                ? Loc.Format("report.ui.repsSplit",
                    _completedRepsR, _targetReps, _invalidRepsR,
                    _completedRepsL, _targetReps, _invalidRepsL)
                : Loc.Format("report.ui.repsOnly", _completedRepsR, _completedRepsL);
        }

        WriteParameters(duration, average, completion);
        WriteClinicalNotes(duration, average, completion);
        DrawGraph();
    }

    private void WriteParameters(float duration, float average, float completion)
    {
        if (parametersText == null) return;

        _paramsBuilder.Length = 0;
        _paramsBuilder.AppendLine("--- PARAMETRE ÖLÇÜMÜ ---");
        if (_trackRight)
        {
            _paramsBuilder.Append("Sağ maks ROM: ").Append(_maxAngleR.ToString("F1")).Append("°\n");
            _paramsBuilder.Append("Sağ ort ROM: ").Append(RightAverageAngle.ToString("F1")).Append("°\n");
            _paramsBuilder.Append("Sağ tekrar: ").Append(_completedRepsR).Append(" / ").Append(_targetReps);
            _paramsBuilder.Append(" (geçersiz ").Append(_invalidRepsR).Append(")\n");
        }
        if (_trackLeft)
        {
            _paramsBuilder.Append("Sol maks ROM: ").Append(_maxAngleL.ToString("F1")).Append("°\n");
            _paramsBuilder.Append("Sol ort ROM: ").Append(LeftAverageAngle.ToString("F1")).Append("°\n");
            _paramsBuilder.Append("Sol tekrar: ").Append(_completedRepsL).Append(" / ").Append(_targetReps);
            _paramsBuilder.Append(" (geçersiz ").Append(_invalidRepsL).Append(")\n");
        }
        _paramsBuilder.Append("Hedef Açı: ").Append(_targetAngle.ToString("F0")).Append("°\n");
        _paramsBuilder.Append("Tamamlanma: %").Append(completion.ToString("F0")).Append('\n');
        _paramsBuilder.Append("Süre (sn): ").Append(duration.ToString("F1")).Append('\n');
        _paramsBuilder.Append("Kompansasyon: ").Append(_compensationCount).Append(" olay\n");
        _paramsBuilder.Append("Takip sıçraması: ").Append(_trackingJumpCount).Append(" olay\n");
        _paramsBuilder.Append("2. kişi sahnede: ").Append(_secondPersonCount).Append(" olay\n");
        _paramsBuilder.Append("Yardımlı sezgi: ").Append(_assistNearCount).Append(" olay\n");
        _paramsBuilder.Append("Örnek sayısı: ").Append(_sampleCount);
        if (_compactGenerations > 0)
        {
            _paramsBuilder.Append(" (sıkıştırma×").Append(_compactGenerations)
                .Append(", ~").Append(EffectiveSampleHz.ToString("F1")).Append(" Hz)");
        }
        if (_compensationOverflow + _trackingJumpOverflow + _secondPersonOverflow + _assistNearOverflow > 0)
        {
            _paramsBuilder.Append("\nOlay taşması: komp+")
                .Append(_compensationOverflow)
                .Append(" sıçrama+").Append(_trackingJumpOverflow)
                .Append(" 2.kişi+").Append(_secondPersonOverflow)
                .Append(" yardım+").Append(_assistNearOverflow);
        }
        if (_strainSampleCount > 0)
        {
            _paramsBuilder.Append("\nZorlanma pik: %").Append((_peakStrain * 100f).ToString("F0"));
            _paramsBuilder.Append("  ort: %").Append((MeanStrain * 100f).ToString("F0"));
            if (_trackRight)
                _paramsBuilder.Append("\nPik zorlanma açısı (sağ): ").Append(_angleAtPeakStrainR.ToString("F0")).Append('°');
            if (_trackLeft)
                _paramsBuilder.Append("\nPik zorlanma açısı (sol): ").Append(_angleAtPeakStrainL.ToString("F0")).Append('°');
        }

        parametersText.text = _paramsBuilder.ToString();
    }

    private void WriteClinicalNotes(float duration, float average, float completion)
    {
        if (notesText == null) return;

        _notesBuilder.Length = 0;
        _notesBuilder.AppendLine("--- SEANS NOTLARI ---");

        if (_trackRight && !_trackLeft)
            _notesBuilder.AppendLine("Yalnızca sağ kol ölçüldü.");
        else if (!_trackRight && _trackLeft)
            _notesBuilder.AppendLine("Yalnızca sol kol ölçüldü.");
        else if (_trackRight && _trackLeft)
            _notesBuilder.AppendLine("Her iki kol ayrı ayrı ölçüldü.");

        if (_compactGenerations > 0)
        {
            _notesBuilder.Append("Uzun seans: grafik tamponu ")
                .Append(_compactGenerations)
                .Append(" kez sıkıştırıldı (~")
                .Append(EffectiveSampleHz.ToString("F1"))
                .AppendLine(" Hz). Tüm süre kapsanır; ince ayrıntı azalmış olabilir.");
        }

        float span = GraphSpanSeconds;
        if (duration > 30f && span > 0f && span < duration * 0.9f)
        {
            _notesBuilder.Append("Grafik zaman serisi ")
                .Append(span.ToString("F0"))
                .Append(" sn; seans süresi ")
                .Append(duration.ToString("F0"))
                .AppendLine(" sn — kapsama farkı not edilmeli.");
        }

        if (_compensationOverflow > 0 || _trackingJumpOverflow > 0)
        {
            _notesBuilder.AppendLine("Bazı olay zaman damgaları tampon limitine takıldı (sayaçlar tam; grafik noktaları eksik olabilir).");
        }

        if (_completedReps == 0)
        {
            _notesBuilder.AppendLine("Seans boyunca tamamlanan tekrar yok.");
        }
        else if (_targetReps > 0 && _completedReps >= _targetReps)
        {
            _notesBuilder.AppendLine("Hedef tekrar sayısına ulaşıldı.");
        }
        else if (_targetReps > 0)
        {
            _notesBuilder.Append("Hedefin %")
                .Append(completion.ToString("F0"))
                .AppendLine(" kadarı tamamlandı.");
        }

        if (_maxAngle >= _targetAngle)
        {
            _notesBuilder.AppendLine("Hedef açıya ulaşıldı veya aşıldı.");
        }
        else
        {
            float deficit = _targetAngle - _maxAngle;
            _notesBuilder.Append("Maks ROM hedefin ")
                .Append(deficit.ToString("F0"))
                .AppendLine("° altında kaldı.");
        }

        if (_compensationCount == 0)
        {
            _notesBuilder.AppendLine("Belirgin gövde kompansasyonu kaydedilmedi.");
        }
        else if (_compensationCount <= 3)
        {
            _notesBuilder.Append(_compensationCount)
                .AppendLine(" kez hafif gövde kompansasyonu tespit edildi.");
        }
        else
        {
            _notesBuilder.Append(_compensationCount)
                .AppendLine(" kez gövde kompansasyonu tespit edildi — form kontrolü önerilir.");
        }

        if (_invalidReps > 0)
        {
            _notesBuilder.Append(_invalidReps)
                .AppendLine(" tekrar kompansasyon nedeniyle geçersiz sayıldı.");
        }

        if (_trackingJumpCount > 0)
        {
            _notesBuilder.Append(_trackingJumpCount)
                .AppendLine(" kez takip/kadraj sıçraması (iskelet ani kayma) kaydedildi — ROM güvenilirliği düşmüş olabilir.");
        }

        if (_secondPersonCount > 0)
        {
            _notesBuilder.Append(_secondPersonCount)
                .AppendLine(" kez sahnede 2. kişi algılandı (manuel yardım kapalı olsa da not edilir).");
        }

        if (_assistNearCount > 0)
        {
            _notesBuilder.Append(_assistNearCount)
                .AppendLine(" kez yardımlı sezgi (temas + hız vektörü + süreğenlik) kaydedildi.");
        }

        if (_assistedRepsR + _assistedRepsL > 0)
        {
            _notesBuilder.Append("Yardımlı tekrar: sağ ")
                .Append(_assistedRepsR)
                .Append(" / sol ")
                .Append(_assistedRepsL)
                .AppendLine(".");
        }

        if (duration < 30f && _completedReps > 0)
        {
            _notesBuilder.AppendLine("Seans süresi kısa; tempo değerlendirilebilir.");
        }

        AppendMovementScoreNote();
        AppendStrainNote();

        _notesBuilder.AppendLine("(Bu özet teşhis değildir; klinik değerlendirme klinikisyen tarafından yapılmalıdır.)");
        notesText.text = _notesBuilder.ToString();
    }

    // Hedef hareketle DTW benzerlik skorunu (varsa) nota ekler.
    private void AppendMovementScoreNote()
    {
        bool hasRight = _trackRight && _movementScoreRight >= 0f;
        bool hasLeft = _trackLeft && _movementScoreLeft >= 0f;
        if (!hasRight && !hasLeft) return;

        _notesBuilder.Append(Loc.T("report.ui.dtwPrefix"));
        if (hasRight)
            _notesBuilder.Append(Loc.T("side.right.abbr")).Append(" %")
              .Append(_movementScoreRight.ToString("F0"));
        if (hasRight && hasLeft) _notesBuilder.Append(", ");
        if (hasLeft)
            _notesBuilder.Append(Loc.T("side.left.abbr")).Append(" %")
              .Append(_movementScoreLeft.ToString("F0"));
        _notesBuilder.AppendLine(Loc.T("report.ui.dtwSuffix"));
    }

    /// <summary>
    /// Yüz zorlanması özeti: pik açı + geçmiş seans farkı.
    /// SaMD Class B: karar-destek göstergesi; teşhis değildir.
    /// </summary>
    private void AppendStrainNote()
    {
        if (_strainSampleCount <= 0) return;

        _notesBuilder.Append("Yüz zorlanması: pik %")
            .Append((_peakStrain * 100f).ToString("F0"))
            .Append(", ort %")
            .Append((MeanStrain * 100f).ToString("F0"))
            .AppendLine(".");

        if (_trackRight)
        {
            _notesBuilder.Append("En yüksek zorlanma sağ ")
                .Append(_angleAtPeakStrainR.ToString("F0"))
                .AppendLine("° civarında.");
        }
        if (_trackLeft)
        {
            _notesBuilder.Append("En yüksek zorlanma sol ")
                .Append(_angleAtPeakStrainL.ToString("F0"))
                .AppendLine("° civarında.");
        }

        if (_prevSessionPeakStrain >= 0f)
        {
            float deltaPct = (_peakStrain - _prevSessionPeakStrain) * 100f;
            if (deltaPct > 1f)
                _notesBuilder.Append("Önceki seanstan +").Append(deltaPct.ToString("F0")).AppendLine("% daha yüksek pik zorlanma.");
            else if (deltaPct < -1f)
                _notesBuilder.Append("Önceki seanstan ").Append((-deltaPct).ToString("F0")).AppendLine("% daha düşük pik zorlanma.");
            else
                _notesBuilder.AppendLine("Pik zorlanma önceki seansa yakın.");
        }
    }

    private void DrawGraph()
    {
        if (graphRenderer == null) return;

        // Yardım kırmızısı yalnızca seans sonrası rapor grafiğinde.
        // Canlı seansta uyarı zaten var; çizgi rengi değişmesin.
        bool showAssistColor = !_sessionActive;
        float viewStart = 0f;
        float viewEnd = -1f;
        if (_sessionActive && liveGraphWindowSeconds > 1f && _sampleCount >= 2)
        {
            float tLast = _sampleTimes[_sampleCount - 1];
            viewStart = Mathf.Max(0f, tLast - liveGraphWindowSeconds);
            viewEnd = tLast;
        }

        graphRenderer.Draw(
            _sampleTimes,
            _rightAngles,
            _leftAngles,
            _strainSamples,
            _sampleCount,
            _compensationTimes,
            _compensationCount,
            _targetAngle,
            graphMaxAngleScale,
            showAssistColor ? _assistRight : null,
            showAssistColor ? _assistLeft : null,
            viewStart,
            viewEnd);
    }

    public void SetGraphSeriesVisibility(bool showRight, bool showLeft, bool showStrain)
    {
        if (graphRenderer == null) return;
        graphRenderer.ShowRight = showRight;
        graphRenderer.ShowLeft = showLeft;
        graphRenderer.ShowStrain = showStrain;
        if (_sampleCount >= 2) DrawGraph();
    }

    /// <summary>
    /// Geçmiş kaydı / menü detay için zaman serisini kopyalar (downsample, tahsis yalnızca kayıt anında).
    /// MaxPoints: uzun seanslarda JSON boyutunu sınırlar (~1 Hz @ 15 dk ≈ 900).
    /// </summary>
    public void CopySeriesToEntry(SessionEntry entry, int maxPoints = 900)
    {
        if (entry == null || _sampleCount < 2)
        {
            if (entry != null)
            {
                entry.seriesTimes = null;
                entry.seriesRight = null;
                entry.seriesLeft = null;
                entry.seriesStrain = null;
                entry.seriesCompTimes = null;
                entry.seriesAssistRight = null;
                entry.seriesAssistLeft = null;
            }
            return;
        }

        int n = _sampleCount;
        int step = 1;
        if (n > maxPoints)
            step = Mathf.CeilToInt(n / (float)maxPoints);
        int outCount = (n + step - 1) / step;

        entry.seriesTimes = new float[outCount];
        entry.seriesRight = new float[outCount];
        entry.seriesLeft = new float[outCount];
        entry.seriesStrain = new float[outCount];
        entry.seriesAssistRight = new bool[outCount];
        entry.seriesAssistLeft = new bool[outCount];

        int o = 0;
        for (int i = 0; i < n && o < outCount; i += step, o++)
        {
            entry.seriesTimes[o] = _sampleTimes[i];
            entry.seriesRight[o] = _rightAngles[i];
            entry.seriesLeft[o] = _leftAngles[i];
            entry.seriesStrain[o] = _strainSamples[i];
            entry.seriesAssistRight[o] = _assistRight[i];
            entry.seriesAssistLeft[o] = _assistLeft[i];
        }

        // Son örneği her zaman dahil et (adım atladıysa)
        if (o > 0 && (n - 1) % step != 0)
        {
            int last = o < outCount ? o : outCount - 1;
            if (last >= 0)
            {
                entry.seriesTimes[last] = _sampleTimes[n - 1];
                entry.seriesRight[last] = _rightAngles[n - 1];
                entry.seriesLeft[last] = _leftAngles[n - 1];
                entry.seriesStrain[last] = _strainSamples[n - 1];
                entry.seriesAssistRight[last] = _assistRight[n - 1];
                entry.seriesAssistLeft[last] = _assistLeft[n - 1];
            }
        }

        if (_compensationCount > 0)
        {
            entry.seriesCompTimes = new float[_compensationCount];
            for (int i = 0; i < _compensationCount; i++)
                entry.seriesCompTimes[i] = _compensationTimes[i];
        }
        else
        {
            entry.seriesCompTimes = System.Array.Empty<float>();
        }
    }
}
