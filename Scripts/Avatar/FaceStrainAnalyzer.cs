using System.Collections.Concurrent;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using UnityEngine;

/// <summary>
/// Yüz zorlanması: kalibre edilmiş rest/strain ifade şablonuna benzerlik.
/// 1) Kısa rahat yüz (rest)  2) ~1 dk zorlanma ifadeleri (strain)  3) Canlı cosine/L2 benzerlik → effort 0..1
/// SaMD Class B: karar-destek; teşhis değildir. Ham yüz videosu saklanmaz.
/// </summary>
public class FaceStrainAnalyzer : MonoBehaviour
{
    public enum Phase
    {
        Idle,
        CalibratingRest,
        CalibratingStrain,
        Ready,
        /// <summary>Profil yokken eski ağırlıklı blendshape tahmini.</summary>
        HeuristicFallback
    }

    private struct StrainSample
    {
        public bool hasFace;
        public bool hasFeatures;
        public FaceFeatureVector features;
        public float heuristicEffort;
    }

    [Header("Kalibrasyon süreleri")]
    [SerializeField] private float restCalibrationSeconds = 15f;
    [SerializeField] private float strainCalibrationSeconds = 60f;
    [SerializeField] private float calibrationSampleHz = 10f;

    [Header("Benzerlik")]
    [Tooltip("0 = yalnızca L2 mesafe, 1 = yalnızca cosine. Karışım effort hesabında kullanılır.")]
    [SerializeField] [Range(0f, 1f)] private float cosineBlend = 0.65f;
    [SerializeField] private float effortSmoothing = 10f;

    [Header("Eşikler (klinik)")]
    [SerializeField] private float strainWarnThreshold = 0.55f;
    [SerializeField] private bool invalidateOnHighStrain = false;
    [SerializeField] private float strainInvalidateThreshold = 0.85f;

    [Header("Heuristic yedek (profil yokken)")]
    [SerializeField] private float weightBrowDown = 0.28f;
    [SerializeField] private float weightEyeSquint = 0.28f;
    [SerializeField] private float weightMouthFrown = 0.22f;
    [SerializeField] private float weightJawOpen = 0.12f;
    [SerializeField] private float weightMouthPress = 0.1f;

    [Header("Görsel")]
    [SerializeField] private float faceColorLerpSpeed = 8f;

    private readonly ConcurrentQueue<StrainSample> _queue = new ConcurrentQueue<StrainSample>();
    private readonly Color _calmFace = new Color(0.92f, 0.78f, 0.68f, 1f);
    private readonly Color _strainFace = new Color(0.85f, 0.35f, 0.28f, 1f);

    private Phase _phase = Phase.Idle;
    private float _phaseStartTime;
    private float _lastCalibSampleTime = -10f;

    private FaceFeatureVector _restSum;
    private FaceFeatureVector _strainSum;
    private int _restCount;
    private int _strainCount;

    private FaceFeatureVector _restMean;
    private FaceFeatureVector _strainMean;
    private bool _hasProfile;

    private float _effort01;
    private float _rawEffort01;
    private bool _hasFace;
    private FaceFeatureVector _latestFeatures;
    private Renderer _headRenderer;
    private Material _headMatInstance;
    private Color _lastHeadColor;
    private bool _hasLastHeadColor;
    private const float HeadColorEpsilon = 0.004f;

    public Phase CurrentPhase => _phase;
    public bool IsCalibrating =>
        _phase == Phase.CalibratingRest || _phase == Phase.CalibratingStrain;
    public float CurrentEffort01 => _effort01;
    public bool HasFace => _hasFace;
    public bool HasCalibrationProfile => _hasProfile;
    public float StrainWarnThreshold => strainWarnThreshold;
    public bool InvalidateOnHighStrain => invalidateOnHighStrain;
    public float StrainInvalidateThreshold => strainInvalidateThreshold;
    public bool IsAboveWarnThreshold => _hasFace && _effort01 >= strainWarnThreshold;
    public bool IsAboveInvalidateThreshold =>
        invalidateOnHighStrain && _hasFace && _effort01 >= strainInvalidateThreshold;

    public float RestCalibrationSeconds => restCalibrationSeconds;
    public float StrainCalibrationSeconds => strainCalibrationSeconds;

    /// <summary>0..1 kalibrasyon fazı ilerlemesi.</summary>
    public float CalibrationProgress01
    {
        get
        {
            if (_phase == Phase.CalibratingRest)
                return Mathf.Clamp01((Time.time - _phaseStartTime) / Mathf.Max(0.01f, restCalibrationSeconds));
            if (_phase == Phase.CalibratingStrain)
                return Mathf.Clamp01((Time.time - _phaseStartTime) / Mathf.Max(0.01f, strainCalibrationSeconds));
            return _hasProfile ? 1f : 0f;
        }
    }

    public int CalibrationRestSamples => _restCount;
    public int CalibrationStrainSamples => _strainCount;

    public void BindHeadRenderer(Renderer headRenderer)
    {
        if (_headMatInstance != null)
        {
            Destroy(_headMatInstance);
            _headMatInstance = null;
        }

        _headRenderer = headRenderer;
        if (_headRenderer == null) return;

        // Prefab asset üzerinde Renderer.material yasak — yalnızca sahnedeki instance
        if (!_headRenderer.gameObject.scene.IsValid() || !_headRenderer.gameObject.scene.isLoaded)
        {
            _headRenderer = null;
            return;
        }

        // sharedMaterial'dan kopya; sharedMaterial'ı doğrudan boyama (tüm instance'ları bozar)
        Material shared = _headRenderer.sharedMaterial;
        if (shared == null) return;
        _headMatInstance = new Material(shared);
        _headRenderer.sharedMaterial = _headMatInstance;
    }

    private void Awake()
    {
        TryLoadProfile();
    }

    public bool TryLoadProfile()
    {
        FaceStrainProfile p = FaceStrainProfile.Load();
        if (p == null || !p.IsValid)
        {
            _hasProfile = false;
            _phase = Phase.HeuristicFallback;
            return false;
        }

        _restMean = p.RestVector;
        _strainMean = p.StrainVector;
        _hasProfile = true;
        _phase = Phase.Ready;
        return true;
    }

    /// <summary>Rest → Strain kalibrasyonunu başlatır.</summary>
    public void StartCalibration()
    {
        _restSum = default;
        _strainSum = default;
        _restCount = 0;
        _strainCount = 0;
        _lastCalibSampleTime = -10f;
        _phaseStartTime = Time.time;
        _phase = Phase.CalibratingRest;
    }

    public void CancelCalibration()
    {
        if (_hasProfile) _phase = Phase.Ready;
        else _phase = Phase.HeuristicFallback;
    }

    /// <summary>
    /// Face result — ana thread (VIDEO) veya callback.
    /// Unity API yok; kuyruğa yazar.
    /// </summary>
    public void OnFaceResult(FaceLandmarkerResult result)
    {
        StrainSample sample;
        sample.hasFace = false;
        sample.hasFeatures = false;
        sample.features = default;
        sample.heuristicEffort = 0f;

        if (result.faceBlendshapes != null && result.faceBlendshapes.Count > 0)
        {
            Classifications cls = result.faceBlendshapes[0];
            if (cls.categories != null && cls.categories.Count > 0)
            {
                sample.features = FaceFeatureVector.FromBlendshapes(cls.categories);
                sample.hasFeatures = true;
                sample.hasFace = true;
                sample.heuristicEffort = HeuristicEffort(sample.features);
            }
        }
        else if (result.faceLandmarks != null && result.faceLandmarks.Count > 0)
        {
            sample.hasFace = true;
        }

        _queue.Enqueue(sample);
    }

    private void Update()
    {
        StrainSample latest = default;
        bool got = false;
        while (_queue.TryDequeue(out var s))
        {
            latest = s;
            got = true;
        }

        if (got)
        {
            _hasFace = latest.hasFace;
            if (latest.hasFeatures)
                _latestFeatures = latest.features;

            if (_phase == Phase.CalibratingRest || _phase == Phase.CalibratingStrain)
                ProcessCalibrationSample(latest);
            else
                ProcessLiveEffort(latest);
        }

        AdvanceCalibrationPhases();
        UpdateHeadVisual();
    }

    private void ProcessCalibrationSample(in StrainSample sample)
    {
        if (!sample.hasFeatures) return;

        float interval = 1f / Mathf.Max(1f, calibrationSampleHz);
        if (Time.time - _lastCalibSampleTime < interval) return;
        _lastCalibSampleTime = Time.time;

        if (_phase == Phase.CalibratingRest)
        {
            _restSum.Add(sample.features);
            _restCount++;
        }
        else if (_phase == Phase.CalibratingStrain)
        {
            _strainSum.Add(sample.features);
            _strainCount++;
        }
    }

    private void AdvanceCalibrationPhases()
    {
        if (_phase == Phase.CalibratingRest)
        {
            bool timeUp = Time.time - _phaseStartTime >= restCalibrationSeconds;
            if (!timeUp) return;
            if (_restCount < 5) return; // süre doldu ama örnek yetersiz — yüz gelene kadar bekle

            FaceFeatureVector mean = _restSum;
            mean.Scale(1f / _restCount);
            _restMean = mean;
            _phase = Phase.CalibratingStrain;
            _phaseStartTime = Time.time;
            _lastCalibSampleTime = -10f;
        }
        else if (_phase == Phase.CalibratingStrain)
        {
            bool timeUp = Time.time - _phaseStartTime >= strainCalibrationSeconds;
            if (!timeUp) return;
            if (_strainCount < 10) return;

            FaceFeatureVector mean = _strainSum;
            mean.Scale(1f / _strainCount);
            _strainMean = mean;

            FaceStrainProfile profile = FaceStrainProfile.FromMeans(
                _restMean, _restCount, _strainMean, _strainCount);
            profile.Save();
            _hasProfile = true;
            _phase = Phase.Ready;
        }
    }

    private void ProcessLiveEffort(in StrainSample sample)
    {
        if (!sample.hasFace)
        {
            _rawEffort01 = 0f;
            _effort01 = Mathf.Lerp(_effort01, 0f, Time.deltaTime * effortSmoothing);
            return;
        }

        float target;
        if (_hasProfile && sample.hasFeatures && _phase == Phase.Ready)
            target = SimilarityEffort(sample.features);
        else
            target = sample.heuristicEffort;

        _rawEffort01 = Mathf.Clamp01(target);
        _effort01 = Mathf.Lerp(_effort01, _rawEffort01, Time.deltaTime * effortSmoothing);
    }

    /// <summary>
    /// Rest'e uzak / Strain'e yakın → yüksek effort.
    /// Cosine + L2 karışımı (SerializeField cosineBlend).
    /// </summary>
    private float SimilarityEffort(in FaceFeatureVector current)
    {
        float cosStrain = FaceFeatureVector.CosineSimilarity(current, _strainMean);
        float cosRest = FaceFeatureVector.CosineSimilarity(current, _restMean);
        // Cosine -1..1 → rest'ten strain'e göreli
        float cosScore = Mathf.Clamp01(0.5f + 0.5f * (cosStrain - cosRest));

        float dStrain = FaceFeatureVector.L2Distance(current, _strainMean);
        float dRest = FaceFeatureVector.L2Distance(current, _restMean);
        float l2Score = dRest / Mathf.Max(1e-4f, dRest + dStrain);

        return Mathf.Lerp(l2Score, cosScore, cosineBlend);
    }

    private float HeuristicEffort(in FaceFeatureVector f)
    {
        float sumW = weightBrowDown + weightEyeSquint + weightMouthFrown + weightJawOpen + weightMouthPress;
        if (sumW < 1e-5f) return 0f;
        return (f.browDown * weightBrowDown
                + f.eyeSquint * weightEyeSquint
                + f.mouthFrown * weightMouthFrown
                + f.jawOpen * weightJawOpen
                + f.mouthPress * weightMouthPress) / sumW;
    }

    private void UpdateHeadVisual()
    {
        if (_headMatInstance == null) return;
        Color target = _hasFace
            ? Color.Lerp(_calmFace, _strainFace, _effort01)
            : _calmFace;
        Color next = Color.Lerp(_headMatInstance.color, target, Time.deltaTime * faceColorLerpSpeed);

        // cmd: her kare material dirty etme — fark yoksa yazma
        if (_hasLastHeadColor
            && Mathf.Abs(next.r - _lastHeadColor.r) < HeadColorEpsilon
            && Mathf.Abs(next.g - _lastHeadColor.g) < HeadColorEpsilon
            && Mathf.Abs(next.b - _lastHeadColor.b) < HeadColorEpsilon)
            return;

        _headMatInstance.color = next;
        _lastHeadColor = next;
        _hasLastHeadColor = true;
    }

    private void OnDestroy()
    {
        if (_headMatInstance != null)
            Destroy(_headMatInstance);
    }
}
