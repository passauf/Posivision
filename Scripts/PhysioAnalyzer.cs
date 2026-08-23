using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using Mediapipe.Tasks.Components.Containers;

/// <summary>
/// Pose Landmarker tabanlı omuz fleksiyon ROM analizi.
/// LIVE_STREAM callback thread-safe: landmark kopyası ConcurrentQueue ile ana thread'e aktarılır.
/// SaMD Class B: ROM ve kompansasyon çıktıları klinik karar destek bilgisidir.
/// </summary>
public class PhysioAnalyzer : MonoBehaviour
{
    private const int PoseLandmarkCount = PoseLandmarkIndices.Count;
    private const int IdxNose = PoseLandmarkIndices.Nose;
    private const int IdxLeftShoulder = PoseLandmarkIndices.LeftShoulder;
    private const int IdxRightShoulder = PoseLandmarkIndices.RightShoulder;
    private const int IdxLeftElbow = PoseLandmarkIndices.LeftElbow;
    private const int IdxRightElbow = PoseLandmarkIndices.RightElbow;
    private const int IdxLeftWrist = PoseLandmarkIndices.LeftWrist;
    private const int IdxRightWrist = PoseLandmarkIndices.RightWrist;
    private const int IdxLeftIndex = PoseLandmarkIndices.LeftIndex;
    private const int IdxRightIndex = PoseLandmarkIndices.RightIndex;
    private const int IdxLeftHip = PoseLandmarkIndices.LeftHip;
    private const int IdxRightHip = PoseLandmarkIndices.RightHip;

    [Header("Bileşenler")]
    public WarningManager warningManager;
    public DataManager dataManager;
    public SessionReportManager reportManager;
    public float lerpSpeed = 30f;

    [Tooltip("Yüz zorlanma analizi — yoksa runtime'da bulunur.")]
    [SerializeField] private FaceStrainAnalyzer faceStrainAnalyzer;

    [Header("SAĞ KOL UI")]
    public Slider rightSlider;
    public SliderColorController rightColorCtrl;
    public TextMeshProUGUI rightAngleText, rightRepText;

    [Header("SOL KOL UI")]
    public Slider leftSlider;
    public TextMeshProUGUI leftAngleText, leftRepText;
    public SliderColorController leftColorCtrl;

    [Header("Egzersiz Ayarları")]
    [Tooltip("Eski ayna UI swap — kullanılmıyor. Klinik sağ/sol anatomiktir.")]
    public bool isMirrored = false;
    [Tooltip("Ön kamera görüntüsü MediaPipe'a çevrilince L/R yer değişir. Açıkken UI/rapor/avatar anatomik sağ-sola map edilir.")]
    [SerializeField] private bool autoSwapArmsForMirroredCamera = true;
    [FormerlySerializedAs("hedefAci")]
    public float targetAngleDegrees = 160f;
    [FormerlySerializedAs("baslangicAci")]
    public float repLowerLimitDegrees = 40f;
    [FormerlySerializedAs("hedefToplamTekrar")]
    public int targetReps = 10;

    [Header("Tekrar sayımı")]
    [Tooltip("Hedef açı üstünde bu süre kalınca tekrar sayılır (sn).")]
    [SerializeField] private float repTargetHoldSeconds = 0.5f;
    [Tooltip("Hedefe bu kadar kala 'hedefte' say (gürültü payı, derece).")]
    [SerializeField] private float repTargetEnterSlackDegrees = 4f;
    [Tooltip("Alt eşik = hedef × oran. Sonraki tekrar için bu açının altına inilmeli.")]
    [SerializeField] private float repReturnRatio = 0.35f;
    [Tooltip("Alt eşik mutlak tabanı (çok düşük ROM).")]
    [SerializeField] private float repLowerMinDegrees = 4f;
    [Tooltip("Alt eşik tavanı (yüksek ROM; aşırı aşağı zorlamaz).")]
    [SerializeField] private float repLowerMaxDegrees = 45f;
    [Tooltip("Hedef ile alt eşik arasında asgari açı farkı (derece).")]
    [SerializeField] private float repMinTravelDegrees = 8f;

    [Header("Bölgeler (omuz fleksiyonu)")]
    [Tooltip("Kapalı bölgeler hesaplanmaz. Görünmeyen açık bölgeler tahmin edilmez — o karede atlanır.")]
    [SerializeField] private PoseRegionMask regionMask = PoseRegionMask.ShoulderFlexion();
    private MovementId _selectedMovementId = MovementId.ShoulderFlexion;
    private BodyRegionId _selectedBodyRegionId = BodyRegionId.Shoulder;

    public MovementId SelectedMovementId => _selectedMovementId;
    public BodyRegionId SelectedBodyRegionId => _selectedBodyRegionId;

    /// <summary>Yan kamera protokolü (Inspector / seans seçimi). Kapalıyken önden tam görünüm + yaw kapısı.</summary>
    public bool PatientSideView
    {
        get => patientSideView;
        set => patientSideView = value;
    }

    /// <summary>Son kare klinik sağ kol açısı (derece). Ölçülmüyorsa 0.</summary>
    public float CurrentRightAngleDegrees => measureRightArm ? _physicRight : 0f;

    /// <summary>Son kare klinik sol kol açısı (derece). Ölçülmüyorsa 0.</summary>
    public float CurrentLeftAngleDegrees => measureLeftArm ? _physicLeft : 0f;

    /// <summary>Hedefe kalan derece (0 = hedefe ulaşıldı veya aşıldı).</summary>
    public float DegreesRemainingToTarget(bool anatomicalRight)
    {
        float cur = anatomicalRight ? CurrentRightAngleDegrees : CurrentLeftAngleDegrees;
        return Mathf.Max(0f, targetAngleDegrees - cur);
    }

    /// <summary>0 = başlangıç, 1 = hedef açıya ulaşıldı.</summary>
    public float ProgressToTarget01(bool anatomicalRight)
    {
        float t = Mathf.Max(1f, targetAngleDegrees);
        float cur = anatomicalRight ? CurrentRightAngleDegrees : CurrentLeftAngleDegrees;
        return Mathf.Clamp01(cur / t);
    }

    /// <summary>Son kare gövde dikey sapması (derece). HUD için.</summary>
    public float CurrentSpineLeanDegrees => _lastSpineLeanDegrees;

    /// <summary>Ön protokolde gövde yaw tahmini (derece). Yan protokolde 0.</summary>
    public float CurrentBodyYawDegrees => patientSideView ? 0f : _frontalFacingGate.LastBodyYawDegrees;

    /// <summary>Ön protokolde hasta kameraya yeterince dönük mü. Yaw kapısı kapalıysa her zaman true.</summary>
    public bool IsFrontalFacingOk => _frontalFacingGate.IsFacingOk(patientSideView);

    /// <summary>65+ için artırılmış dik duruş uyarı eşiği.</summary>
    public float EffectiveMaxSpineLeanDegrees =>
        maxSpineLeanDegrees * _spineCompensationGate.ElderToleranceMultiplier(patientAgeYears);

    /// <summary>Ön protokol yaw eşiği (derece).</summary>
    public float EffectiveMaxBodyYawDegrees => Mathf.Max(1f, maxBodyYawDegrees);

    /// <summary>1 = eşikten uzak (iyi/yeşil), 0 = eşik aşımı (kırmızı). Dik duruş.</summary>
    public float UprightHealth01
    {
        get
        {
            float lim = EffectiveMaxSpineLeanDegrees;
            if (lim < 0.5f) return 1f;
            return Mathf.Clamp01(1f - (_lastSpineLeanDegrees / lim));
        }
    }

    /// <summary>Seans içi yaw kapısı kapalı — her zaman 1 (rehber seans öncesi UI).</summary>
    public float FacingHealth01 => 1f;

    [Header("Güven / Filtre")]
    [Tooltip("Açıkken düşük visibility'li landmark'lar elenir (tahmin yok). Klinik için açık tut.")]
    [SerializeField] private bool enableConfidenceGate = true;
    [SerializeField] private float landmarkVisibilityThreshold = 0.5f;
    [Tooltip("Yan profilde baş (burun) için yumuşak eşik — MediaPipe skorları düşük olabilir.")]
    [SerializeField] private float headLandmarkVisibilityThreshold = 0.28f;
    [Tooltip("Presence skoru birçok pozda düşük çıkar; klinik kalite için visibility ile birlikte kontrol edilir.")]
    [SerializeField] private bool requirePresenceScore = true;
    [SerializeField] private float filterMinCutoff = 1.0f;
    [SerializeField] private float filterBeta = 0.007f;
    [SerializeField] private float filterDCutoff = 1.0f;

    [Header("Kamera görünümü (ön / yan)")]
    [Tooltip("Açık: yan profil protokolü.")]
    [SerializeField] private bool patientSideView;
    [Tooltip("Kapalı (önerilen): seans içi yaw/dönüş kapısı yok — yalnızca seans öncesi UI rehberi.")]
    [SerializeField] private bool enableSessionYawGate;
    [Tooltip("Ön protokolde gövde yaw üst sınırı (yalnızca enableSessionYawGate açıkken).")]
    [SerializeField] private float maxBodyYawDegrees = 15f;
    [SerializeField] private float bodyYawHysteresisDegrees = 3f;
    [SerializeField] private bool requireFullFrontalTorso = true;
    [Tooltip("65+ yaş dik duruş tolerans artışı (örn. 0.18 = +%18).")]
    [SerializeField] private float elderUprightToleranceBoost = 0.18f;
    [SerializeField] private int elderAgeThresholdYears = 65;

    [Header("Kompansasyon (Omurga)")]
    [Tooltip("Bu eşiğin üstünde 'DİK DUR!' soft uyarısı (derece). Kompansasyon sayılmaz.")]
    [SerializeField] private float maxSpineLeanDegrees = 8f;
    [Tooltip("Bu eşiğin üstünde kompansasyon olayı kaydedilir (derece).")]
    [SerializeField] private float spineCompensationDegrees = 10f;
    [Tooltip("Tekrarı geçersiz saymak için eşik (derece). Genelde kompansasyon ile aynı.")]
    [SerializeField] private float invalidateLeanDegrees = 10f;
    [Tooltip("Uyarı kapanması için eşiğin altına inme payı — sınırda titreyen uyarı/avatar kilidini önler.")]
    [SerializeField] private float spineWarnHysteresisDegrees = 2f;
    [SerializeField] private float warningCooldownSeconds = 4f;

    [Header("Yan profil (omuz fleksiyonu)")]
    [SerializeField] private float sideSkewWarnDegrees = SideProfileGate.DefaultWarnDegrees;
    [SerializeField] private float sideSkewInvalidDegrees = SideProfileGate.DefaultInvalidDegrees;
    [SerializeField] private float sideFrontalShoulderWidth01 = SideProfileGate.DefaultFrontalShoulderWidth01;
    [SerializeField] private float sideSkewWarningCooldownSeconds = 3.5f;

    [Header("Takip sıçraması (kadraj)")]
    [Tooltip("Açıkken iskelet noktalarının ani teleporte benzeri kaymasını tespit eder (Class B kalite uyarısı).")]
    [SerializeField] private bool detectTrackingJumps = true;
    [Tooltip("Ölçek biriminde eklem teleport eşiği. Ön=omuz genişliği, yan=gövde boyu. Varsayılan 0.45.")]
    [SerializeField] private float trackingJumpDeltaShoulderWidths = TrackingJumpDetector.DefaultDeltaScaleUnits;
    [Tooltip("Aynı karede bu kadar eklem eşiği aşarsa sıçrama (varsayılan 3).")]
    [SerializeField] private int trackingJumpMinJoints = TrackingJumpDetector.DefaultMinJoints;
    [Tooltip("Protokol ölçeğinin kareler arası oranı bu çarpanı aşarsa sıçrama (varsayılan 1.55). Yan: gövde boyu.")]
    [SerializeField] private float trackingJumpShoulderWidthRatio = TrackingJumpDetector.DefaultScaleRatio;
    [Tooltip("Aynı uyarıyı tekrar gösterme süresi (sn).")]
    [SerializeField] private float trackingJumpWarningCooldownSeconds = 2.5f;

    [Header("Seans kalite skoru (QualityScore QS-1.0)")]
    [Tooltip("SaMD Class B: düşük kalitede zirve ROM güncellenmez. Teşhis değildir.")]
    [SerializeField] private float qualityWeightVisibility = 0.45f;
    [SerializeField] private float qualityWeightStability = 0.30f;
    [SerializeField] private float qualityWeightLean = 0.25f;
    [Tooltip("Protokol ölçeği CV bu değerde kararlılık bileşeni 0 olur (ön: omuz w, yan: gövde L).")]
    [SerializeField] private float qualityMaxShoulderWidthCv = 0.18f;
    [SerializeField] private float qualityReliableThreshold = 0.75f;
    [SerializeField] private float qualityCautionThreshold = 0.50f;
    [Tooltip("Bu skorun altında karede max ROM güncellenmez.")]
    [SerializeField] private float qualityPeakGateThreshold = 0.50f;

    [Header("Yardımlı tekrar (assisted) — 4 katman")]
    [Tooltip("HUD manuel: açıkken tekrar yardımlı sayılır (2. kişi olmasa da).")]
    [SerializeField] private bool assistHelpActive;
    [Tooltip("Temas + vektörel hız + süreğenlik ile otomatik yardımlı (Class B sezgi; teşhis değil).")]
    [SerializeField] private bool autoAssistFromMultiPerson = true;
    [Tooltip("Terapist eli – hasta çalışan eklem temas yarıçapı (omuz genişliği). ~0.5 ≈ 15–20 cm.")]
    [SerializeField] private float assistProximityShoulderWidths = 0.50f;
    [Tooltip("Temas debounce: ardışık yakın kare.")]
    [SerializeField] private int assistContactMinFrames = 2;
    [Tooltip("Hasta çalışan eklem min hızı (omuz-normalize birim/sn) — aktif hareket karesi.")]
    [SerializeField] private float assistMinJointSpeedShoulderWidthsPerSec = 0.35f;
    [Tooltip("Tekrar aktif süresinde temas+eş-yön oranı eşiği (0.5–0.6).")]
    [SerializeField] [Range(0.35f, 0.85f)] private float assistMinRepFraction = 0.55f;
    [Tooltip("Yardımlı karar için asgari aktif hareket karesi.")]
    [SerializeField] private int assistMinActiveMotionFrames = 6;
    [Tooltip("Sahnede 2. kişi algılanınca ekran uyarısı göster (Class B bilgilendirme).")]
    [SerializeField] private bool warnOnSecondPerson = true;
    [Tooltip("2. kişi uyarısı için ardışık kare eşiği.")]
    [SerializeField] private int secondPersonMinFrames = 3;
    [Tooltip("Aynı 2. kişi varlığı süresince uyarıyı yeniden gösterme aralığı (sn).")]
    [SerializeField] private float secondPersonWarningCooldownSeconds = 5f;

    private int _latestDetectedPoseCount = 1;
    private readonly AssistedRepDetector _assistedRepDetector = new AssistedRepDetector();
    private readonly AssistPresenceTracker _assistPresenceTracker = new AssistPresenceTracker();

    // Sırayla iki kol (omuz fleksiyonu)
    private bool _sequentialBothArms;
    private int _sequentialPhase;
    private bool _plannedMeasureRight = true;
    private bool _plannedMeasureLeft = true;

    // Kapı servisleri (SRP composition)
    private readonly TrackingJumpDetector _trackingJumpDetector = new TrackingJumpDetector();
    private readonly SpineCompensationGate _spineCompensationGate = new SpineCompensationGate();
    private readonly FrontalFacingGate _frontalFacingGate = new FrontalFacingGate();
    private readonly SideProfileSessionGate _sideProfileSessionGate = new SideProfileSessionGate();
    private readonly QualityFramePublisher _qualityFramePublisher = new QualityFramePublisher();
    private readonly SessionCloseoutService _sessionCloseoutService = new SessionCloseoutService();
    private readonly PhysioArmUiPresenter _armUiPresenter = new PhysioArmUiPresenter();
    private System.Action _onTrackingJumpDetected;

    // Eski sahneler: contact min frames buradan taşınır (ConfigureAssistedRepDetector).
    [SerializeField, HideInInspector] private int assistMultiPersonMinFrames = 3;

    [Header("Yüz Zorlanması (SaMD Class B)")]
    [Tooltip("Yüksek zorlanmada soft uyarı. Tekrar geçersiz kılma FaceStrainAnalyzer.invalidateOnHighStrain ile.")]
    [SerializeField] private float strainWarningCooldownSeconds = 5f;
    private float _lastStrainWarningTime = -100f;

    [Header("Seans")]
    [Tooltip("Açıkken sahne yüklenince seans otomatik başlar. Kapalıyken kullanıcı 'Seansı Başlat' demelidir.")]
    [SerializeField] private bool autoStartSessionOnEnable = false;

    [Header("Kol Ölçümü (seans öncesi profilden ayarlanır)")]
    [SerializeField] private bool measureRightArm = true;
    [SerializeField] private bool measureLeftArm = true;
    [SerializeField] private float patientHeightCm = 170f;
    [SerializeField] private int patientAgeYears;
    private string patientFirstName = "";
    private string patientLastName = "";
    private int patientGender;

    [Header("Kişisel hedef / Sesli koç")]
    [Tooltip("Geçmiş seansa göre hedef açı ve tekrar önerisini uygular (karar-destek).")]
    [SerializeField] private bool applyPersonalizedTargets = true;
    [SerializeField] private bool enableVoiceCoach = true;
    [Tooltip("Bu hızın üstünde (derece/sn) 'daha yavaş' uyarısı.")]
    [SerializeField] private float maxRaiseDegreesPerSecond = 110f;

    [Header("Önkol dönüşü filtresi")]
    [Tooltip("Dirsek açısı hızlı değişip omuz fleksiyonu sabitse (avuç çevirme) açıyı dondur.")]
    [SerializeField] private bool suppressForearmRotationArtifact = true;
    [SerializeField] private float forearmRotationElbowDeltaDegrees = 12f;
    [SerializeField] private float forearmRotationFlexionDeltaDegrees = 4f;

    [Header("Derinlik foreshortening (kameraya doğru düz kol)")]
    [Tooltip("Üst kol 2D/referans: ~sin(20°)≈0.34 — yalnızca kameraya neredeyse dik bakınca. 45° öne tetiklemez.")]
    [SerializeField] private float foreshorteningMinArmRatio = 0.34f;
    [Tooltip("Omuz-el toplam uzunluğu / (2×üst kol ref); düz kol kameraya bakınca çöker.")]
    [SerializeField] private float foreshorteningMinChainRatio = 0.34f;
    [Tooltip("Dirsek açısı bu değerin altındaysa (bükük kol, ele-ağız) alarm YOK — omuz-dirsek-el düz çizgi şart.")]
    [SerializeField] private float foreshorteningMinElbowExtensionDegrees = 155f;
    [Tooltip("Referans uzunluğu yalnızca bu oran aralığında güncelle.")]
    [SerializeField] private float foreshorteningRefUpdateMinRatio = 0.75f;
    [SerializeField] private float foreshorteningRefUpdateMaxRatio = 1.30f;
    [SerializeField] private float foreshorteningWarningCooldownSeconds = 3.5f;

    [Header("Teorik 2D projeksiyon düzeltmesi")]
    [Tooltip("Gonyometre yokken geometrik formül: θ' = atan2(sin θ, cos θ · cos α). SaMD Class B.")]
    [SerializeField] private bool enableTheoreticalRomCorrection = true;
    [SerializeField] private bool correctForeshorteningProjection = true;
    [SerializeField] private bool correctBodyYawProjection = true;
    [SerializeField] private bool correctDistanceProxy = true;
    [Tooltip("Foreshortening düzeltme kazancı (0–1). 1 = tam teorik.")]
    [SerializeField] private float theoreticalForeshortenGain = 1f;
    [Tooltip("Yaw düzeltme kazancı (0–1).")]
    [SerializeField] private float theoreticalYawGain = 1f;
    [SerializeField] private float theoreticalMinYawDegrees = 3f;
    [SerializeField] private float theoreticalMaxYawCorrectionDegrees = 25f;
    [Tooltip("Normalize öncesi ideal omuz genişliği (görüntü 0–1). ~2 m mesafede tipik.")]
    [SerializeField] private float idealShoulderWidth01 = 0.22f;
    [Tooltip("Uzaktayken teorik düzeltmeyi yumuşatma (0–1).")]
    [SerializeField] private float distanceBlendStrength = 0.2f;

    private VoiceCoach _voiceCoach;
    private float _prevAngleR, _prevAngleL;
    private float _prevAngleTimeR = -1f, _prevAngleTimeL = -1f;
    private bool _almostDoneSpoken;
    private bool _pendingApplyPersonalized = true;

    private readonly ConcurrentQueue<PoseLandmarkSample> _poseQueue = new ConcurrentQueue<PoseLandmarkSample>();
    private readonly Vector2[] _filteredXy = new Vector2[PoseLandmarkCount];
    private readonly OneEuroFilter2D[] _filters = new OneEuroFilter2D[PoseLandmarkCount];

    private float _physicRight, _physicLeft;
    private float _visualRight, _visualLeft;
    private int _countR, _countL;
    private int _invalidR, _invalidL;
    private bool _isUpR, _isUpL, _hasData;
    private bool _repInvalidR, _repInvalidL;
    private float _targetHoldR, _targetHoldL;
    private bool _repCountedAtPeakR, _repCountedAtPeakL;
    private bool _inTargetZoneR, _inTargetZoneL;
    private float _lastRepGateRight, _lastRepGateLeft;
    private bool _repGateRightValid, _repGateLeftValid;
    private bool _sessionEnded;
    private bool _sessionStarted;
    private bool _filtersConfigured;

    // İlk seans ROM analizi → dinamik slider / hedef (SaMD Class B motivasyon)
    private const float SliderStartFullDegrees = 15f;
    private const float SliderMotivationalRatio = 2f;
    private const float SliderMotivationalSlackDegrees = 5f;
    private const float SliderMinFullDegrees = 10f;
    private const float AssessmentSettleSeconds = 6f;
    private const float AssessmentMinPeakDegrees = 3f;
    private const int AssessmentDefaultReps = 8;
    private bool _romAssessmentAnalyzing;
    private float _sessionPeakRom;
    private float _sliderFullDegrees = 180f;
    private float _peakLastImprovedAt;
    private float _assessmentPhaseStartedAt;
    private float _shoulderWidth = 1f;
    private float _rawShoulderWidthForQuality;
    private bool _rawShoulderWidthValid;
    /// <summary>Normalize / jump ölçeği (ön: omuz w, yan: torso L). Görüntü 0–1.</summary>
    private float _rawPoseScaleLength;
    private bool _rawPoseScaleValid;
    private PoseScaleBasis _activeScaleBasis = PoseScaleBasis.ShoulderWidth;
    private float _lastSpineLeanDegrees;
    private bool _foreshortenMpRight;
    private bool _foreshortenMpLeft;
    private float _lastForeshortenWarnTime = -100f;
    private bool _torsoRegionActive;
    private PoseRegionVisibility _regionVisibility;
    private AvatarBodyDriver _avatarBodyDriver;
    private bool _avatarLookupAttempted;

    // Hareket stratejisi (Faz 1: omuz fleksiyonu). Refactor only — klinik eşik değişikliği yok.
    private IMovementAnalyzer _movementAnalyzer;
    private ShoulderFlexionAnalyzer _shoulderFlexionAnalyzer;
    private IRepPolicy _repPolicy;
    private ArmRepState _armRepR;
    private ArmRepState _armRepL;

    // cmd: masaüstü 60 FPS (Windows/Editor); telefonda 30 FPS (ısı/pil)
    private const int TargetFrameRateDesktop = 60;
    private const int TargetFrameRateMobile = 30;

    public PoseRegionMask RegionMask => regionMask;
    public PoseRegionVisibility RegionVisibility => _regionVisibility;
    public bool IsTorsoTrackingActive => regionMask.torso && _regionVisibility.torso;
    public bool IsRightArmTrackingActive => regionMask.rightArm && _regionVisibility.rightArm;
    public bool IsLeftArmTrackingActive => regionMask.leftArm && _regionVisibility.leftArm;

    /// <summary>Son kare QualityScore (0..1).</summary>
    public float CurrentQualityScore01 => _qualityFramePublisher.CurrentQualityScore01;
    /// <summary>Seans ortalaması; örnek yoksa -1.</summary>
    public float SessionMeanQualityScore01 => _qualityFramePublisher.SessionMeanQualityScore01;
    public SessionQualityBand CurrentQualityBand => _qualityFramePublisher.CurrentQualityBand;
    public SessionQualityBand SessionQualityBandMean => _qualityFramePublisher.SessionQualityBandMean;
    public bool QualityAllowsPeakRom => _qualityFramePublisher.QualityAllowsPeakRom;

    /// <summary>HUD manuel yardım. Class B — yardımcı destekliyorsa açılır.</summary>
    public bool AssistHelpActive
    {
        get => assistHelpActive;
        set => assistHelpActive = value;
    }

    /// <summary>Son karede MediaPipe’ın ürettiği pose sayısı.</summary>
    public int DetectedPoseCount => _latestDetectedPoseCount;

    /// <summary>Sahnede hasta dışında en az bir kişi (pose ≥ 2) stabil algılandı mı.</summary>
    public bool IsSecondPersonOnStage => _assistPresenceTracker.IsSecondPersonOnStage();

    /// <summary>Manuel veya otomatik (yakınlık+eş hareket+kaldırma latch) — efektif yardımlı durum.</summary>
    public bool IsAssistEffective => IsAssistEffectiveRight || IsAssistEffectiveLeft;

    /// <summary>Anatomik sağ kol için yardımlı durum (grafik / tekrar).</summary>
    public bool IsAssistEffectiveRight =>
        assistHelpActive
        || (autoAssistFromMultiPerson && _assistedRepDetector.IsAutoAssistEffective(true));

    /// <summary>Anatomik sol kol için yardımlı durum (grafik / tekrar).</summary>
    public bool IsAssistEffectiveLeft =>
        assistHelpActive
        || (autoAssistFromMultiPerson && _assistedRepDetector.IsAutoAssistEffective(false));

    /// <summary>Otomatik eş-hareket yardımlı sezgisi aktif mi (manuel değil).</summary>
    public bool IsAssistFromMultiPerson =>
        !assistHelpActive
        && autoAssistFromMultiPerson
        && (_assistedRepDetector.IsAutoAssistEffective(true)
            || _assistedRepDetector.IsAutoAssistEffective(false));

    public void SetRegionMask(PoseRegionMask mask)
    {
        regionMask = mask;
        regionMask.rightArm = mask.rightArm && measureRightArm;
        regionMask.leftArm = mask.leftArm && measureLeftArm;
        if (_avatarBodyDriver != null)
        {
            _avatarBodyDriver.SetRegionMask(regionMask);
            _avatarBodyDriver.SetMeasuredArms(measureRightArm, measureLeftArm);
        }
        else
        {
            var driver = FindObjectOfType<AvatarBodyDriver>(true);
            if (driver != null)
            {
                driver.SetMeasuredArms(measureRightArm, measureLeftArm);
                driver.SetRegionMask(regionMask);
            }
        }
    }

    /// <summary>
    /// Hasta/seans egzersiz seçimini uygular. SaMD Class B: bağlam seçimi; teşhis değildir.
    /// Canlı ROM: omuz fleksiyonu (yan) + abdüksiyon (ön).
    /// </summary>
    public void ApplyExerciseSelection(int bodyRegionId, int movementId)
    {
        _selectedBodyRegionId = ExerciseCatalog.ClampRegion(bodyRegionId);
        _selectedMovementId = ExerciseCatalog.ClampMovement(movementId);
        ExerciseDefinition def = ExerciseCatalog.GetOrDefault(_selectedMovementId);
        _selectedBodyRegionId = def.RegionId;
        patientSideView = ExerciseCatalog.UsesSideProfile(_selectedMovementId);
        SetRegionMask(def.BuildMask());
        EnsureMovementStrategy();
        ApplyRaisePlaneForMovement();
    }

    private void ApplyRaisePlaneForMovement()
    {
        if (_avatarBodyDriver == null && !_avatarLookupAttempted)
        {
            _avatarBodyDriver = FindObjectOfType<AvatarBodyDriver>(true);
            _avatarLookupAttempted = true;
        }
        if (_avatarBodyDriver == null) return;

        _avatarBodyDriver.SetRaisePlane(ToAvatarRaisePlane(ExerciseCatalog.GetRaisePlane(_selectedMovementId)));
        _avatarBodyDriver.SetArcRegion(_selectedBodyRegionId);
    }

    private static AvatarBodyDriver.ArmRaisePlane ToAvatarRaisePlane(MovementRaisePlane plane)
    {
        switch (plane)
        {
            case MovementRaisePlane.Coronal:
                return AvatarBodyDriver.ArmRaisePlane.Coronal;
            case MovementRaisePlane.Sagittal:
                return AvatarBodyDriver.ArmRaisePlane.Sagittal;
            default:
                return AvatarBodyDriver.ArmRaisePlane.Sagittal;
        }
    }

    public void ApplyExerciseSelectionFromProfile(PatientProfile profile)
    {
        if (profile == null)
        {
            ApplyExerciseSelection((int)ExerciseCatalog.DefaultRegionId, (int)ExerciseCatalog.DefaultMovementId);
            return;
        }

        int region = profile.preferredBodyRegionId;
        int move = profile.preferredMovementId;
        if (profile.TryGetCurrentPlannedMovement(out MovementId planned))
        {
            move = (int)planned;
            if (ExerciseCatalog.TryGet(planned, out ExerciseDefinition def))
                region = (int)def.RegionId;
        }
        ApplyExerciseSelection(region, move);
    }

    [Header("Hareket Uyumu (DTW)")]
    [Tooltip("Seans sonunda hasta hareketini hedef şablonla DTW ile karşılaştırıp rapora skor ekler.")]
    [SerializeField] private bool enableMovementScoring = true;
    [Tooltip("İdeal tekrar şablonundaki nokta sayısı (0 → hedef açı → 0 yarım-sinüs).")]
    [SerializeField] private int movementTemplatePoints = 60;

    // Burst/Jobs — Awake'te tahsis, OnDestroy'da Dispose (hot path allocation yok)
    private const int ArmJobCount = 2;
    private const int LandmarkTripletCount = 6; // 2 kol * (hip,shoulder,elbow)
    private NativeArray<float2> _jobLandmarks;
    private NativeArray<float> _jobAngles;
    private NativeArray<bool> _jobEnabled;
    private NativeArray<float> _jobRefArmLengths;
    private NativeArray<float> _jobLeanOut;
    private bool _nativeReady;

    private string _cachedRightAngle = "";
    private string _cachedLeftAngle = "";
    private string _cachedRightRep = "";
    private string _cachedLeftRep = "";
    private int _lastShownRightAngle = int.MinValue;
    private int _lastShownLeftAngle = int.MinValue;
    private int _lastShownCountR = int.MinValue;
    private int _lastShownCountL = int.MinValue;
    private int _lastShownTargetReps = int.MinValue;

    private void Awake()
    {
        ClampForeshorteningSettings();
        Application.targetFrameRate = IsMobileRuntime()
            ? TargetFrameRateMobile
            : TargetFrameRateDesktop;

        ConfigureFilters();
        AllocateNative();
        EnsureMovementStrategy();
        ConfigureGateServices();
        ConfigureAssistedRepDetector();
        ConfigureAssistPresenceTracker();
        _onTrackingJumpDetected = OnTrackingJumpDetected;
        if (faceStrainAnalyzer == null)
            faceStrainAnalyzer = FindObjectOfType<FaceStrainAnalyzer>();
        // Avatar sahnesini erken kur (PiP Screen.Initialize sonrası runner'da uygulanır)
        if (FindObjectOfType<AvatarStageController>(true) == null)
        {
            var go = new GameObject("AvatarStageController");
            go.AddComponent<AvatarStageController>();
        }
    }

    private void OnValidate()
    {
        ClampForeshorteningSettings();
        if (_movementAnalyzer != null)
            ConfigureMovementStrategy();
        ConfigureGateServices();
        ConfigureAssistedRepDetector();
        ConfigureAssistPresenceTracker();
    }

    private void ConfigureAssistPresenceTracker()
    {
        _assistPresenceTracker.Configure(new AssistPresenceTracker.Config
        {
            warnOnSecondPerson = warnOnSecondPerson,
            secondPersonMinFrames = secondPersonMinFrames,
            secondPersonWarningCooldownSeconds = secondPersonWarningCooldownSeconds,
            enableConfidenceGate = enableConfidenceGate,
            landmarkVisibilityThreshold = landmarkVisibilityThreshold,
            requirePresenceScore = requirePresenceScore
        });
    }

    private void ConfigureGateServices()
    {
        _trackingJumpDetector.Configure(new TrackingJumpDetector.Config
        {
            enabled = detectTrackingJumps,
            deltaScaleUnits = trackingJumpDeltaShoulderWidths,
            minJoints = trackingJumpMinJoints,
            scaleRatio = trackingJumpShoulderWidthRatio,
            warningCooldownSeconds = trackingJumpWarningCooldownSeconds
        });

        _spineCompensationGate.Configure(new SpineCompensationGate.Config
        {
            maxSpineLeanDegrees = maxSpineLeanDegrees,
            spineCompensationDegrees = spineCompensationDegrees,
            invalidateLeanDegrees = invalidateLeanDegrees,
            spineWarnHysteresisDegrees = spineWarnHysteresisDegrees,
            warningCooldownSeconds = warningCooldownSeconds,
            elderUprightToleranceBoost = elderUprightToleranceBoost,
            elderAgeThresholdYears = elderAgeThresholdYears
        });

        _frontalFacingGate.Configure(new FrontalFacingGate.Config
        {
            enableSessionYawGate = enableSessionYawGate,
            maxBodyYawDegrees = maxBodyYawDegrees,
            bodyYawHysteresisDegrees = bodyYawHysteresisDegrees,
            requireFullFrontalTorso = requireFullFrontalTorso,
            warningCooldownSeconds = warningCooldownSeconds
        });

        _sideProfileSessionGate.Configure(new SideProfileSessionGate.Config
        {
            warnDegrees = sideSkewWarnDegrees,
            invalidDegrees = sideSkewInvalidDegrees,
            frontalShoulderWidth01 = sideFrontalShoulderWidth01,
            warningCooldownSeconds = sideSkewWarningCooldownSeconds,
            softWarnHysteresisDegrees = SideProfileGate.SoftWarnHysteresisDegrees
        });
    }

    /// <summary>
    /// Katalog hareketine göre analizör/tekrar politikası.
    /// Fleksiyon + abdüksiyon ortak elevasyon pipeline kullanır.
    /// SaMD Class B; teşhis değildir.
    /// </summary>
    private void EnsureMovementStrategy()
    {
        MovementId id = _selectedMovementId;
        if (!ExerciseCatalog.IsLiveReady(id))
            id = MovementId.ShoulderFlexion;

        bool needNew = _movementAnalyzer == null;
        if (!needNew)
        {
            MovementAnalysisFamily family = ExerciseCatalog.GetAnalysisFamily(id);
            if (family == MovementAnalysisFamily.ShoulderElevation)
                needNew = !(_movementAnalyzer is ShoulderFlexionAnalyzer) || _movementAnalyzer.Id != id;
            else
                needNew = _movementAnalyzer.Id != id
                    || ExerciseCatalog.GetAnalysisFamily(_movementAnalyzer.Id) != family;
        }

        if (needNew)
        {
            _movementAnalyzer = MovementAnalyzerFactory.CreateAnalyzer(id);
            _shoulderFlexionAnalyzer = _movementAnalyzer as ShoulderFlexionAnalyzer;
            _repPolicy = MovementAnalyzerFactory.CreateRepPolicy(id);
        }

        ConfigureMovementStrategy();
    }

    private void ConfigureMovementStrategy()
    {
        if (_shoulderFlexionAnalyzer != null)
        {
            var analyzerCfg = new ShoulderFlexionAnalyzerConfig
            {
                suppressForearmRotationArtifact = suppressForearmRotationArtifact,
                forearmRotationElbowDeltaDegrees = forearmRotationElbowDeltaDegrees,
                forearmRotationFlexionDeltaDegrees = forearmRotationFlexionDeltaDegrees,
                foreshorteningMinArmRatio = foreshorteningMinArmRatio,
                foreshorteningMinChainRatio = foreshorteningMinChainRatio,
                foreshorteningMinElbowExtensionDegrees = foreshorteningMinElbowExtensionDegrees,
                foreshorteningRefUpdateMinRatio = foreshorteningRefUpdateMinRatio,
                foreshorteningRefUpdateMaxRatio = foreshorteningRefUpdateMaxRatio
            };
            _shoulderFlexionAnalyzer.Configure(analyzerCfg);
            var romCfg = new TheoreticalRomCorrectionConfig
            {
                enabled = enableTheoreticalRomCorrection,
                correctForeshortening = correctForeshorteningProjection,
                correctYaw = correctBodyYawProjection,
                correctDistanceProxy = correctDistanceProxy,
                foreshortenGain = theoreticalForeshortenGain,
                yawGain = theoreticalYawGain,
                minYawDegrees = theoreticalMinYawDegrees,
                maxYawCorrectionDegrees = theoreticalMaxYawCorrectionDegrees,
                idealShoulderWidth01 = idealShoulderWidth01,
                distanceBlendStrength = distanceBlendStrength
            };
            _shoulderFlexionAnalyzer.ConfigureRomCorrection(romCfg);
        }

        if (_repPolicy != null)
        {
            var repCfg = new RepPolicyHostConfig
            {
                holdSeconds = repTargetHoldSeconds,
                enterSlackDegrees = repTargetEnterSlackDegrees,
                returnRatio = repReturnRatio,
                lowerMinDegrees = repLowerMinDegrees,
                lowerMaxDegrees = repLowerMaxDegrees,
                minTravelDegrees = repMinTravelDegrees
            };
            _repPolicy.Configure(repCfg);
            _repPolicy.SetTargetDegrees(targetAngleDegrees);
            repLowerLimitDegrees = _repPolicy.LowerLimitDegrees;
        }
    }

    /// <summary>
    /// Eski Inspector değerleri (örn. 0.55) 45° öne kaldırmada yanlış alarm verirdi — sıkı banda çek.
    /// Yan φ: eski 15°/25° gövde-refsiz formülle sürekli "daha yana dön" üretir → yeni taban.
    /// </summary>
    private void ClampForeshorteningSettings()
    {
        foreshorteningMinArmRatio = Mathf.Clamp(foreshorteningMinArmRatio, 0.20f, 0.40f);
        foreshorteningMinChainRatio = Mathf.Clamp(foreshorteningMinChainRatio, 0.20f, 0.40f);
        foreshorteningMinElbowExtensionDegrees = Mathf.Clamp(foreshorteningMinElbowExtensionDegrees, 145f, 175f);

        // Eski sahne serileştirmesi (15/25) → gövde-ref ile uyumlu eşikler
        if (sideSkewWarnDegrees < 20f)
            sideSkewWarnDegrees = SideProfileGate.DefaultWarnDegrees;
        if (sideSkewInvalidDegrees < 30f)
            sideSkewInvalidDegrees = SideProfileGate.DefaultInvalidDegrees;
        if (sideSkewInvalidDegrees <= sideSkewWarnDegrees + 1f)
            sideSkewInvalidDegrees = sideSkewWarnDegrees + 12f;
    }

    private static bool IsMobileRuntime()
    {
        // Editor / Windows / Mac → 60; yalnızca gerçek cihaz player'ında 30
        RuntimePlatform p = Application.platform;
        return p == RuntimePlatform.Android || p == RuntimePlatform.IPhonePlayer;
    }

    private void AllocateNative()
    {
        if (_nativeReady) return;
        _jobLandmarks = new NativeArray<float2>(LandmarkTripletCount, Allocator.Persistent);
        _jobAngles = new NativeArray<float>(ArmJobCount, Allocator.Persistent);
        _jobEnabled = new NativeArray<bool>(ArmJobCount, Allocator.Persistent);
        _jobRefArmLengths = new NativeArray<float>(ArmJobCount, Allocator.Persistent);
        _jobLeanOut = new NativeArray<float>(1, Allocator.Persistent);
        _nativeReady = true;
    }

    private void DisposeNative()
    {
        if (!_nativeReady) return;
        if (_jobLandmarks.IsCreated) _jobLandmarks.Dispose();
        if (_jobAngles.IsCreated) _jobAngles.Dispose();
        if (_jobEnabled.IsCreated) _jobEnabled.Dispose();
        if (_jobRefArmLengths.IsCreated) _jobRefArmLengths.Dispose();
        if (_jobLeanOut.IsCreated) _jobLeanOut.Dispose();
        _nativeReady = false;
    }

    private void OnDestroy()
    {
        DisposeNative();
    }

    private void OnEnable()
    {
        if (autoStartSessionOnEnable)
        {
            BeginSession();
        }
    }

    private void Start()
    {
        // TMP varsayılan "New Text" yerine başlangıç değerlerini yaz
        RefreshUiTexts(force: true);
        EnsureExerciseHud();
        if (dataManager != null)
        {
            PatientProfile p = dataManager.LoadProfile();
            measureRightArm = p.measureRightArm;
            measureLeftArm = p.measureLeftArm;
            patientHeightCm = p.heightCm;
            patientAgeYears = p.ageYears;
            patientFirstName = p.firstName ?? "";
            patientLastName = p.lastName ?? "";
            patientGender = p.gender;
            ApplyExerciseSelectionFromProfile(p);
            patientSideView = ExerciseCatalog.UsesSideProfile(_selectedMovementId);
            if (patientSideView)
            {
                if (p.sequentialBothArms
                    && ExerciseCatalog.AllowsBilateralSequential(_selectedMovementId))
                {
                    // Demo / hazırlık: sırayla protokolün ilk fazı
                    measureRightArm = true;
                    measureLeftArm = false;
                }
                else if (ExerciseCatalog.RequiresExclusiveArm(_selectedMovementId)
                    && measureRightArm && measureLeftArm)
                {
                    measureLeftArm = false;
                }
            }
            ApplyArmUiVisibility();
        }

        SyncArmMeasurementPipeline();
    }

    /// <summary>
    /// Ölçülmeyen kol: açı job / filtre / avatar sürüşü kapalı — CPU tasarrufu.
    /// regionMask kolları measure bayraklarıyla hizalanır; gövde lean ayrı kalır.
    /// </summary>
    private void SyncArmMeasurementPipeline()
    {
        if (!measureRightArm && !measureLeftArm)
        {
            measureRightArm = true;
            measureLeftArm = ExerciseCatalog.AllowsSimultaneousBilateral(_selectedMovementId);
        }

        // Egzersiz maskesi + yan seçimi
        ExerciseDefinition def = ExerciseCatalog.GetOrDefault(_selectedMovementId);
        PoseRegionMask baseMask = def.BuildMask();
        regionMask = baseMask;
        regionMask.rightArm = baseMask.rightArm && measureRightArm;
        regionMask.leftArm = baseMask.leftArm && measureLeftArm;

        if (_avatarBodyDriver == null && !_avatarLookupAttempted)
        {
            _avatarBodyDriver = FindObjectOfType<AvatarBodyDriver>(true);
            _avatarLookupAttempted = true;
        }
        if (_avatarBodyDriver != null)
        {
            _avatarBodyDriver.SetRegionMask(regionMask);
            _avatarBodyDriver.SetMeasuredArms(measureRightArm, measureLeftArm);
        }
        else
        {
            var driver = FindObjectOfType<AvatarBodyDriver>(true);
            if (driver != null)
            {
                driver.SetRegionMask(regionMask);
                driver.SetMeasuredArms(measureRightArm, measureLeftArm);
            }
        }

        ApplyArmUiVisibility();
    }

    private void EnsureExerciseHud()
    {
        if (GetComponent<ExerciseHudController>() == null)
        {
            gameObject.AddComponent<ExerciseHudController>();
        }
    }

    private void RefreshUiTexts(bool force)
    {
        if (force)
        {
            _lastShownRightAngle = int.MinValue;
            _lastShownLeftAngle = int.MinValue;
            _lastShownCountR = int.MinValue;
            _lastShownCountL = int.MinValue;
            _lastShownTargetReps = int.MinValue;
        }

        if (rightAngleText != null) rightAngleText.text = "0°";
        if (leftAngleText != null) leftAngleText.text = "0°";
        if (rightRepText != null) rightRepText.text = Loc.T("hud.rep.right") + " 0 / " + targetReps.ToString();
        if (leftRepText != null) leftRepText.text = Loc.T("hud.rep.left") + " 0 / " + targetReps.ToString();
    }

    private void ConfigureFilters()
    {
        for (int i = 0; i < PoseLandmarkCount; i++)
        {
            var filter = _filters[i];
            filter.Configure(filterMinCutoff, filterBeta, filterDCutoff);
            filter.Reset();
            _filters[i] = filter;
        }
        _filtersConfigured = true;
    }

    /// <summary>Seans öncesi panelden: kişisel hedef uygulansın mı.</summary>
    public void SetApplyPersonalizedTargets(bool apply)
    {
        _pendingApplyPersonalized = apply;
    }

    /// <summary>
    /// Seans öncesi panelden seçilen hedef açı / tekrar.
    /// SaMD Class B: klinisyen/kullanıcı seçimi; otomatik öneriyi ezer.
    /// </summary>
    public void SetSessionTargets(float angleDegrees, int reps)
    {
        targetAngleDegrees = Mathf.Clamp(angleDegrees,
            PersonalizedTargetAdvisor.MinAngleDegrees,
            PersonalizedTargetAdvisor.MaxAngleDegrees);
        targetReps = Mathf.Clamp(reps, 1, 30);
        RefreshRepLowerLimitFromTarget();
        _pendingApplyPersonalized = false;
        _sliderFullDegrees = Mathf.Clamp(
            Mathf.Max(targetAngleDegrees * SliderMotivationalRatio, targetAngleDegrees + SliderMotivationalSlackDegrees),
            SliderMinFullDegrees, 180f);
        _lastShownTargetReps = int.MinValue;
        SyncFlexionTargetsToAvatar();
        RefreshUiTexts(force: true);
    }

    /// <summary>Radial yay rengi/track: kişisel hedef açı (0→hedef = kırmızı→yeşil).</summary>
    private void SyncFlexionTargetsToAvatar()
    {
        if (_avatarBodyDriver == null && !_avatarLookupAttempted)
        {
            _avatarBodyDriver = FindObjectOfType<AvatarBodyDriver>(true);
            _avatarLookupAttempted = true;
        }
        if (_avatarBodyDriver != null)
            _avatarBodyDriver.SetFlexionTargets(targetAngleDegrees, targetAngleDegrees);
    }

    /// <summary>
    /// Tekrar alt eşiği (repLowerLimitDegrees). Sonraki tekrar için bu açının altına inilmeli,
    /// sonra hedefe çıkılmalı. SaMD Class B tekrar tanıma eşiği; teşhis değildir.
    /// Formül: clamp(hedef × repReturnRatio, min, max), sonra hedef − minTravel üstünde kalmasın.
    /// </summary>
    private void RefreshRepLowerLimitFromTarget()
    {
        EnsureMovementStrategy();
        if (_repPolicy != null)
        {
            _repPolicy.SetTargetDegrees(targetAngleDegrees);
            repLowerLimitDegrees = _repPolicy.LowerLimitDegrees;
        }
    }

    public PersonalizedTargetAdvisor.Suggestion PreviewPersonalizedTargets()
    {
        PatientHistory history = dataManager != null ? dataManager.LoadHistory() : null;
        return PersonalizedTargetAdvisor.Suggest(history, targetAngleDegrees, targetReps);
    }

    public void BeginSession()
    {
        PatientProfile profile = null;
        if (dataManager != null) profile = dataManager.LoadProfile();
        BeginSession(profile);
    }

    public void BeginSession(PatientProfile profile)
    {
        // cmd: KVKK — rızasız profil ile seans/PII işleme
        if (profile != null && !profile.HasValidConsent)
        {
            Debug.LogWarning("[PhysioAnalyzer] Seans başlamadı: geçerli KVKK rızası yok.");
            return;
        }

        if (profile != null)
        {
            measureRightArm = profile.measureRightArm;
            measureLeftArm = profile.measureLeftArm;
            patientHeightCm = profile.heightCm;
            patientAgeYears = profile.ageYears;
            patientFirstName = profile.firstName ?? "";
            patientLastName = profile.lastName ?? "";
            patientGender = profile.gender;

            ApplyExerciseSelectionFromProfile(profile);
            if (!ExerciseCatalog.IsLiveReady(_selectedMovementId))
            {
                ApplyExerciseSelection((int)BodyRegionId.Shoulder, (int)MovementId.ShoulderFlexion);
            }

            // Kamera protokolü katalog meta’sından
            patientSideView = ExerciseCatalog.UsesSideProfile(_selectedMovementId);

            _sequentialBothArms = profile.sequentialBothArms
                && ExerciseCatalog.AllowsBilateralSequential(_selectedMovementId);
            _plannedMeasureRight = profile.measureRightArm || _sequentialBothArms;
            _plannedMeasureLeft = profile.measureLeftArm || _sequentialBothArms;
            _sequentialPhase = 0;
            if (_sequentialBothArms)
            {
                // Önce sağ, sonra sol
                measureRightArm = true;
                measureLeftArm = false;
            }
            else if (ExerciseCatalog.RequiresExclusiveArm(_selectedMovementId))
            {
                // XOR güvenlik ağı (fleksiyon / yan profil)
                if (measureRightArm && measureLeftArm)
                    measureLeftArm = false;
            }

            var stage = FindObjectOfType<AvatarStageController>();
            if (stage != null)
            {
                stage.ApplyGenderFromProfile(profile);
                stage.ApplySideOrbitForMeasuredArm(measureRightArm, measureLeftArm, patientSideView);
            }
        }
        else
        {
            ApplyExerciseSelection((int)ExerciseCatalog.DefaultRegionId, (int)ExerciseCatalog.DefaultMovementId);
            patientSideView = ExerciseCatalog.UsesSideProfile(_selectedMovementId);
            _sequentialBothArms = false;
            _plannedMeasureRight = measureRightArm;
            _plannedMeasureLeft = measureLeftArm;
        }

        if (!measureRightArm && !measureLeftArm)
        {
            measureRightArm = true;
            measureLeftArm = ExerciseCatalog.AllowsSimultaneousBilateral(_selectedMovementId);
        }

        if (!_sequentialBothArms)
        {
            _plannedMeasureRight = measureRightArm;
            _plannedMeasureLeft = measureLeftArm;
        }

        SyncArmMeasurementPipeline();

        _voiceCoach = VoiceCoach.Ensure();
        if (_voiceCoach != null)
            _voiceCoach.SetEnabled(enableVoiceCoach);

        // Panelden SetSessionTargets gelmediyse isteğe bağlı kişisel öneri (hasta filtreli)
        PatientHistory patientHistory = null;
        if (dataManager != null)
        {
            patientHistory = PatientVault.FilterHistoryForPatient(dataManager.LoadHistory(), profile, fallbackToAll: false);
        }

        int priorSessions = patientHistory != null && patientHistory.sessions != null
            ? patientHistory.sessions.Count
            : 0;
        bool firstSessionForPatient = priorSessions == 0;

        bool useSavedTargets = profile != null && profile.hasSessionTargets
            && profile.lastSessionTargetAngle >= PersonalizedTargetAdvisor.MinAngleDegrees;

        _romAssessmentAnalyzing = firstSessionForPatient && !useSavedTargets;
        _sessionPeakRom = 0f;
        _peakLastImprovedAt = Time.time;
        _assessmentPhaseStartedAt = Time.time;
        _sliderFullDegrees = _romAssessmentAnalyzing ? SliderStartFullDegrees : 180f;

        if (useSavedTargets)
        {
            targetAngleDegrees = Mathf.Clamp(profile.lastSessionTargetAngle,
                PersonalizedTargetAdvisor.MinAngleDegrees,
                PersonalizedTargetAdvisor.MaxAngleDegrees);
            int savedReps = profile.lastSessionTargetReps > 0 ? profile.lastSessionTargetReps : targetReps;
            targetReps = Mathf.Clamp(savedReps, 1, 30);
            RefreshRepLowerLimitFromTarget();
            _pendingApplyPersonalized = false;
            _sliderFullDegrees = Mathf.Clamp(
                Mathf.Max(targetAngleDegrees * SliderMotivationalRatio, targetAngleDegrees + SliderMotivationalSlackDegrees),
                SliderMinFullDegrees, 180f);
        }

        bool usePersonal = applyPersonalizedTargets && _pendingApplyPersonalized && !firstSessionForPatient && !useSavedTargets;
        if (usePersonal && patientHistory != null)
        {
            var suggestion = PersonalizedTargetAdvisor.Suggest(
                patientHistory, targetAngleDegrees, targetReps);
            targetAngleDegrees = suggestion.targetAngle;
            targetReps = suggestion.targetReps;
            RefreshRepLowerLimitFromTarget();
            _sliderFullDegrees = Mathf.Clamp(
                Mathf.Max(targetAngleDegrees * SliderMotivationalRatio, targetAngleDegrees + SliderMotivationalSlackDegrees),
                SliderMinFullDegrees, 180f);
        }
        else if (firstSessionForPatient && !useSavedTargets)
        {
            targetAngleDegrees = PersonalizedTargetAdvisor.MaxAngleDegrees;
            targetReps = AssessmentDefaultReps;
            if (warningManager != null)
                warningManager.TriggerWarning(Loc.T("assess.live.start"));
        }

        RefreshRepLowerLimitFromTarget();
        SyncFlexionTargetsToAvatar();

        while (_poseQueue.TryDequeue(out _)) { }

        _countR = 0;
        _countL = 0;
        _invalidR = 0;
        _invalidL = 0;
        _isUpR = false;
        _isUpL = false;
        _repInvalidR = false;
        _repInvalidL = false;
        _armRepR = default;
        _armRepL = default;
        ResetRepHoldState();
        _visualRight = 0f;
        _visualLeft = 0f;
        _physicRight = 0f;
        _physicLeft = 0f;
        _sessionEnded = false;
        _sessionStarted = true;
        _hasData = false;
        _almostDoneSpoken = false;
        _prevAngleTimeR = -1f;
        _prevAngleTimeL = -1f;
        _lastShownRightAngle = int.MinValue;
        _lastShownLeftAngle = int.MinValue;
        _lastShownCountR = int.MinValue;
        _lastShownCountL = int.MinValue;
        EnsureMovementStrategy();
        _movementAnalyzer?.ResetSession();
        _repPolicy?.Reset();
        ConfigureGateServices();
        _spineCompensationGate.Reset();
        _frontalFacingGate.Reset();
        _sideProfileSessionGate.Reset();
        _trackingJumpDetector.Reset();

        ConfigureQualityScorer();
        _qualityFramePublisher.Reset();
        _rawShoulderWidthValid = false;
        _rawPoseScaleValid = false;
        assistHelpActive = false;
        _latestDetectedPoseCount = 1;
        ConfigureAssistedRepDetector();
        ConfigureAssistPresenceTracker();
        _assistedRepDetector.Reset();
        _assistPresenceTracker.Reset();

        if (!_filtersConfigured) ConfigureFilters();
        for (int i = 0; i < PoseLandmarkCount; i++)
        {
            var filter = _filters[i];
            filter.Reset();
            _filters[i] = filter;
        }

        if (reportManager != null)
        {
            // Sequential: rapor her iki kolu baştan izler; örnekleme yine faz bayraklarıyla sınırlı.
            reportManager.StartSession(
                targetReps, targetAngleDegrees, _plannedMeasureRight, _plannedMeasureLeft);
            // Önceki seans pik zorlanması — geçmiş karşılaştırma
            if (dataManager != null)
            {
                PatientHistory history = dataManager.LoadHistory();
                if (history != null && history.sessions != null && history.sessions.Count > 0)
                {
                    SessionEntry prev = history.sessions[history.sessions.Count - 1];
                    reportManager.SetPreviousSessionPeakStrain(prev.peakStrain);
                }
            }
        }

        SessionStatus.MarkActive();
        var hologram = FindObjectOfType<ExampleMovementHologram>(true);
        if (hologram != null)
            hologram.NotifySessionStarted();
        ApplyArmUiVisibility();
        RefreshUiTexts(force: true);

        if (enableVoiceCoach && _voiceCoach != null)
        {
            if (usePersonal)
                _voiceCoach.SpeakTargets(targetAngleDegrees, targetReps);
            else
                _voiceCoach.Speak(CoachCue.SessionStart);
        }
    }

    /// <summary>HUD seans durumu değişince slider/açı/tekrar görünürlüğünü yeniler.</summary>
    public void RefreshArmUiForSessionState()
    {
        ApplyArmUiVisibility();
    }

    private void ApplyArmUiVisibility()
    {
        _armUiPresenter.ApplyArmUiVisibility(
            IsSessionRunning,
            measureLeftArm,
            measureRightArm,
            leftSlider,
            rightSlider,
            leftAngleText,
            rightAngleText,
            leftRepText,
            rightRepText,
            leftColorCtrl,
            rightColorCtrl);
    }

    private bool IsSessionGoalReached()
    {
        if (_romAssessmentAnalyzing) return false;
        if (targetReps <= 0) return false;

        if (_sequentialBothArms)
        {
            if (_sequentialPhase == 0)
            {
                if (measureRightArm && _countR >= targetReps)
                    AdvanceSequentialPhase();
                return false;
            }

            bool leftDone = !measureLeftArm || _countL >= targetReps;
            bool rightDone = !measureRightArm || _countR >= targetReps;
            return leftDone && rightDone;
        }

        bool rDone = !measureRightArm || _countR >= targetReps;
        bool lDone = !measureLeftArm || _countL >= targetReps;
        return rDone && lDone;
    }

    private void AdvanceSequentialPhase()
    {
        if (!_sequentialBothArms || _sequentialPhase != 0) return;
        _sequentialPhase = 1;
        measureRightArm = false;
        measureLeftArm = true;
        SyncArmMeasurementPipeline();
        var stage = FindObjectOfType<AvatarStageController>();
        if (stage != null)
            stage.ApplySideOrbitForMeasuredArm(measureRightArm, measureLeftArm, patientSideView);
        if (warningManager != null)
            warningManager.TriggerWarning(Loc.T("hud.phase.left"));
#if UNITY_EDITOR
        Debug.Log("[SaMD_Safety] Sequential phase → left arm");
#endif
    }

    /// <summary>Seans çalışıyor mu? (BeginSession sonrası, bitmeden önce).</summary>
    public bool IsSessionRunning => _sessionStarted && !_sessionEnded;
    /// <summary>Hareket sırası segmenti bitti: (biten hareket, indeks, toplam). Daha hareket varsa HUD devam kartı açar.</summary>
    public event System.Action<MovementId, int, int> VisitSegmentCompleted;
    public bool IsMeasuringRightArm => measureRightArm;
    public bool IsMeasuringLeftArm => measureLeftArm;
    public bool IsSequentialBothArms => _sequentialBothArms;
    public int SequentialPhaseIndex => _sequentialPhase;
    public int SequentialPhaseCount => _sequentialBothArms ? 2 : 1;
    public float CurrentSideSkewDegrees => _sideProfileSessionGate.LastSkewDegrees;
    public bool IsSideMeasurementValid => _sideProfileSessionGate.MeasurementValid;

    public void EndSessionManually()
    {
        FinishSession(showReport: true);
    }

    /// <summary>
    /// MediaPipe LIVE_STREAM callback'inden çağrılır (arka plan thread).
    /// Unity API kullanılmaz; yalnızca değer kopyalanıp kuyruğa alınır.
    /// detectedPoseCount: bu karede üretilen pose sayısı.
    /// helperLandmarks: 2. kişi (yardımcı) — yakınlık + eş hareket sezgisi; null liste ise yok sayılır.
    /// </summary>
    public void AnalyzeBothArms(
        NormalizedLandmarks landmarks,
        long timestampMs,
        int detectedPoseCount,
        NormalizedLandmarks helperLandmarks)
    {
        if (!_sessionStarted || _sessionEnded) return;
        // Pose model 33 nokta üretir; en az sağ kalça indeksine (24) ihtiyaç var
        if (landmarks.landmarks == null || landmarks.landmarks.Count <= IdxRightHip) return;

        PoseLandmarkSample sample = default;
        sample.timestampSeconds = timestampMs > 0 ? timestampMs * 0.001f : 0f;
        sample.detectedPoseCount = detectedPoseCount < 1 ? 1 : detectedPoseCount;
        sample.leftShoulder = CopyPoint(landmarks.landmarks[IdxLeftShoulder]);
        sample.rightShoulder = CopyPoint(landmarks.landmarks[IdxRightShoulder]);
        sample.leftElbow = CopyPoint(landmarks.landmarks[IdxLeftElbow]);
        sample.rightElbow = CopyPoint(landmarks.landmarks[IdxRightElbow]);
        if (landmarks.landmarks.Count > IdxRightWrist)
        {
            sample.leftWrist = CopyPoint(landmarks.landmarks[IdxLeftWrist]);
            sample.rightWrist = CopyPoint(landmarks.landmarks[IdxRightWrist]);
        }
        sample.leftHip = CopyPoint(landmarks.landmarks[IdxLeftHip]);
        sample.rightHip = CopyPoint(landmarks.landmarks[IdxRightHip]);
        if (landmarks.landmarks.Count > IdxNose)
            sample.nose = CopyPoint(landmarks.landmarks[IdxNose]);

        if (helperLandmarks.landmarks != null
            && helperLandmarks.landmarks.Count > IdxRightElbow
            && sample.detectedPoseCount >= 2)
        {
            sample.hasHelperPose = true;
            if (helperLandmarks.landmarks.Count > IdxRightShoulder)
            {
                sample.helperLeftShoulder = CopyPoint(helperLandmarks.landmarks[IdxLeftShoulder]);
                sample.helperRightShoulder = CopyPoint(helperLandmarks.landmarks[IdxRightShoulder]);
            }
            sample.helperLeftElbow = CopyPoint(helperLandmarks.landmarks[IdxLeftElbow]);
            sample.helperRightElbow = CopyPoint(helperLandmarks.landmarks[IdxRightElbow]);
            if (helperLandmarks.landmarks.Count > IdxRightWrist)
            {
                sample.helperLeftWrist = CopyPoint(helperLandmarks.landmarks[IdxLeftWrist]);
                sample.helperRightWrist = CopyPoint(helperLandmarks.landmarks[IdxRightWrist]);
            }
            if (helperLandmarks.landmarks.Count > IdxRightIndex)
            {
                sample.helperLeftIndex = CopyPoint(helperLandmarks.landmarks[IdxLeftIndex]);
                sample.helperRightIndex = CopyPoint(helperLandmarks.landmarks[IdxRightIndex]);
            }
            if (helperLandmarks.landmarks.Count > IdxRightHip)
            {
                sample.helperLeftHip = CopyPoint(helperLandmarks.landmarks[IdxLeftHip]);
                sample.helperRightHip = CopyPoint(helperLandmarks.landmarks[IdxRightHip]);
            }
        }

        _poseQueue.Enqueue(sample);
    }

    /// <summary>Geriye uyumluluk — yardımcı pose yok.</summary>
    public void AnalyzeBothArms(NormalizedLandmarks landmarks, long timestampMs, int detectedPoseCount)
    {
        AnalyzeBothArms(landmarks, timestampMs, detectedPoseCount, default);
    }

    /// <summary>Geriye uyumluluk — tek pose varsayılır.</summary>
    public void AnalyzeBothArms(NormalizedLandmarks landmarks, long timestampMs)
    {
        AnalyzeBothArms(landmarks, timestampMs, 1, default);
    }

    /// <summary>Geriye uyumluluk — timestamp MediaPipe'dan gelmezse ana thread'de tamamlanır.</summary>
    public void AnalyzeBothArms(NormalizedLandmarks landmarks)
    {
        AnalyzeBothArms(landmarks, -1L, 1, default);
    }

    private static LandmarkPoint CopyPoint(NormalizedLandmark lm)
    {
        LandmarkPoint p;
        p.x = lm.x;
        p.y = lm.y;
        p.hasVisibility = lm.visibility.HasValue;
        p.visibility = lm.visibility.HasValue ? lm.visibility.Value : 1f;
        p.hasPresence = lm.presence.HasValue;
        p.presence = lm.presence.HasValue ? lm.presence.Value : 1f;
        return p;
    }

    private void ProcessSampleOnMainThread(PoseLandmarkSample sample)
    {
        _latestDetectedPoseCount = sample.detectedPoseCount < 1 ? 1 : sample.detectedPoseCount;
        _assistPresenceTracker.UpdateSecondPersonPresence(
            _latestDetectedPoseCount, warningManager, reportManager);

        float timestamp = sample.timestampSeconds;
        if (timestamp <= 0f)
        {
            timestamp = Time.realtimeSinceStartup;
        }

        // MediaPipe landmark görünürlüğü (indeks tarafı — henüz anatomik değil)
        bool mpRightVis = IsPointConfident(sample.rightShoulder)
                          && IsPointConfident(sample.rightElbow)
                          && IsPointConfident(sample.rightHip);
        bool mpLeftVis = IsPointConfident(sample.leftShoulder)
                         && IsPointConfident(sample.leftElbow)
                         && IsPointConfident(sample.leftHip);
        bool torsoVis = IsPointConfident(sample.leftShoulder)
                        && IsPointConfident(sample.rightShoulder)
                        && IsPointConfident(sample.leftHip)
                        && IsPointConfident(sample.rightHip);

        // cmd: ön kamera flip → MediaPipe L/R anatomik ters; UI/rapor anatomik, avatar MP-native
        bool swap = ShouldSwapArmLaterality();
        bool anatRightVis = swap ? mpLeftVis : mpRightVis;
        bool anatLeftVis = swap ? mpRightVis : mpLeftVis;

        bool mpRightWristOk = mpRightVis && sample.rightWrist.hasVisibility && IsPointConfident(sample.rightWrist);
        bool mpLeftWristOk = mpLeftVis && sample.leftWrist.hasVisibility && IsPointConfident(sample.leftWrist);

        _regionVisibility.rightArm = anatRightVis;
        _regionVisibility.leftArm = anatLeftVis;
        _regionVisibility.torso = torsoVis;
        _regionVisibility.rightForearm = swap ? mpLeftWristOk : mpRightWristOk;
        _regionVisibility.leftForearm = swap ? mpRightWristOk : mpLeftWristOk;
        _regionVisibility.legs = false;
        _regionVisibility.head = false;

        bool wantAnatRight = regionMask.rightArm && measureRightArm;
        bool wantAnatLeft = regionMask.leftArm && measureLeftArm;
        bool clinicalRightOk = wantAnatRight && anatRightVis;
        bool clinicalLeftOk = wantAnatLeft && anatLeftVis;

        // Job'lar MediaPipe indeksleriyle çalışır
        bool mpRightOk = mpRightVis && (swap ? wantAnatLeft : wantAnatRight);
        bool mpLeftOk = mpLeftVis && (swap ? wantAnatRight : wantAnatLeft);
        _torsoRegionActive = regionMask.torso && torsoVis;

        if (!mpRightOk && !mpLeftOk && !_torsoRegionActive)
        {
            _lastSpineLeanDegrees = 0f;
            _spineCompensationGate.ClearSticky();
            _frontalFacingGate.Reset();
            _rawShoulderWidthValid = false;
            _rawPoseScaleValid = false;
            _qualityFramePublisher.SetVisibilityFraction(
                _qualityFramePublisher.ComputeVisibilityFraction(
                    measureRightArm, measureLeftArm,
                    regionMask.rightArm, regionMask.leftArm, regionMask.torso,
                    false, false, false));
            _assistedRepDetector.ClearTransientStreaks();
            _assistPresenceTracker.ClearHelperCache();
            _trackingJumpDetector.Reset();
            PushQualityFrame();
            return;
        }

        // Omuzlar: her protokolde; yan ölçek için kalçalar erken filtrelenir
        bool needLeftShoulder = measureLeftArm || _torsoRegionActive || measureRightArm;
        bool needRightShoulder = measureRightArm || _torsoRegionActive || measureLeftArm;
        if (needLeftShoulder && IsPointConfident(sample.leftShoulder))
        {
            _filteredXy[IdxLeftShoulder] = FilterPoint(IdxLeftShoulder, sample.leftShoulder.x, sample.leftShoulder.y, timestamp);
        }
        if (needRightShoulder && IsPointConfident(sample.rightShoulder))
        {
            _filteredXy[IdxRightShoulder] = FilterPoint(IdxRightShoulder, sample.rightShoulder.x, sample.rightShoulder.y, timestamp);
        }
        bool noseOk = IsPointConfident(
            sample.nose,
            patientSideView ? headLandmarkVisibilityThreshold : landmarkVisibilityThreshold);
        if (noseOk)
            _filteredXy[IdxNose] = FilterPoint(IdxNose, sample.nose.x, sample.nose.y, timestamp);

        bool leftShoulderOk = needLeftShoulder && IsPointConfident(sample.leftShoulder);
        bool rightShoulderOk = needRightShoulder && IsPointConfident(sample.rightShoulder);
        bool leftHipOk = IsPointConfident(sample.leftHip);
        bool rightHipOk = IsPointConfident(sample.rightHip);

        // Yan / gövde: kalçalar ölçek (torso L) için omuzlarla aynı anda lazım
        _activeScaleBasis = PoseScaleResolver.FromSideView(patientSideView);
        bool needHipsForScale = _activeScaleBasis == PoseScaleBasis.TorsoLength
            || _torsoRegionActive
            || mpRightOk
            || mpLeftOk;
        if (needHipsForScale)
        {
            if (leftHipOk)
                _filteredXy[IdxLeftHip] = FilterPoint(IdxLeftHip, sample.leftHip.x, sample.leftHip.y, timestamp);
            if (rightHipOk)
                _filteredXy[IdxRightHip] = FilterPoint(IdxRightHip, sample.rightHip.x, sample.rightHip.y, timestamp);
        }

        // Ham omuz genişliği: yan φ kapısı + (ön) kalite; normalize için değil
        float rawWidth = PoseScaleResolver.ComputeShoulderWidth(
            _filteredXy[IdxLeftShoulder], _filteredXy[IdxRightShoulder],
            leftShoulderOk, rightShoulderOk, out bool shoulderWOk);
        _rawShoulderWidthValid = shoulderWOk;
        _rawShoulderWidthForQuality = shoulderWOk ? rawWidth : 0f;

        float scaleLen = PoseScaleResolver.Compute(
            _activeScaleBasis,
            _filteredXy[IdxLeftShoulder], _filteredXy[IdxRightShoulder],
            _filteredXy[IdxLeftHip], _filteredXy[IdxRightHip],
            leftShoulderOk, rightShoulderOk, leftHipOk, rightHipOk,
            out bool scaleOk);
        _rawPoseScaleValid = scaleOk;
        _rawPoseScaleLength = scaleOk ? scaleLen : 0f;

        // Normalize divisor: protokol ölçeği (ön=omuz w, yan=gövde L)
        _shoulderWidth = scaleOk ? scaleLen : 1f;
        if (_shoulderWidth < PoseScaleResolver.MinScale)
            _shoulderWidth = 1f;

        _sideProfileSessionGate.Evaluate(
            patientSideView,
            rawWidth,
            shoulderWOk,
            _rawPoseScaleValid ? _rawPoseScaleLength : 0f,
            noseOk,
            measureRightArm,
            measureLeftArm,
            anatRightVis,
            anatLeftVis,
            warningManager);

        float inv = 1f / _shoulderWidth;

        if (mpRightOk)
        {
            _filteredXy[IdxRightElbow] = FilterPoint(IdxRightElbow, sample.rightElbow.x, sample.rightElbow.y, timestamp);
            if (!needHipsForScale)
                _filteredXy[IdxRightHip] = FilterPoint(IdxRightHip, sample.rightHip.x, sample.rightHip.y, timestamp);
            if (mpRightWristOk)
                _filteredXy[IdxRightWrist] = FilterPoint(IdxRightWrist, sample.rightWrist.x, sample.rightWrist.y, timestamp);
        }

        if (mpLeftOk)
        {
            _filteredXy[IdxLeftElbow] = FilterPoint(IdxLeftElbow, sample.leftElbow.x, sample.leftElbow.y, timestamp);
            if (!needHipsForScale)
                _filteredXy[IdxLeftHip] = FilterPoint(IdxLeftHip, sample.leftHip.x, sample.leftHip.y, timestamp);
            if (mpLeftWristOk)
                _filteredXy[IdxLeftWrist] = FilterPoint(IdxLeftWrist, sample.leftWrist.x, sample.leftWrist.y, timestamp);
        }

        // Gövde lean: kalçalar henüz yoksa (yalnız gövde, scale shoulder ise)
        if (_torsoRegionActive)
        {
            if (!mpRightOk && !needHipsForScale)
                _filteredXy[IdxRightHip] = FilterPoint(IdxRightHip, sample.rightHip.x, sample.rightHip.y, timestamp);
            if (!mpLeftOk && !needHipsForScale)
                _filteredXy[IdxLeftHip] = FilterPoint(IdxLeftHip, sample.leftHip.x, sample.leftHip.y, timestamp);
        }

        // Normalize in-place (protokol ölçeği)
        if (leftShoulderOk) _filteredXy[IdxLeftShoulder] *= inv;
        if (rightShoulderOk) _filteredXy[IdxRightShoulder] *= inv;
        if (noseOk) _filteredXy[IdxNose] *= inv;
        if (mpLeftOk || _torsoRegionActive || needHipsForScale)
        {
            if (mpLeftOk) _filteredXy[IdxLeftElbow] *= inv;
            if (mpLeftOk && mpLeftWristOk) _filteredXy[IdxLeftWrist] *= inv;
            if (mpLeftOk || _torsoRegionActive || (needHipsForScale && leftHipOk))
                _filteredXy[IdxLeftHip] *= inv;
        }
        if (mpRightOk || _torsoRegionActive || needHipsForScale)
        {
            if (mpRightOk) _filteredXy[IdxRightElbow] *= inv;
            if (mpRightOk && mpRightWristOk) _filteredXy[IdxRightWrist] *= inv;
            if (mpRightOk || _torsoRegionActive || (needHipsForScale && rightHipOk))
                _filteredXy[IdxRightHip] *= inv;
        }

        // SaMD Class B: yardımcı pose önbelleği — açı sonrası üçlü koşul (yakınlık+eş hareket+kaldırma)
        _assistPresenceTracker.CacheHelperPose(in sample, inv);

        // Kadraj/takip sıçraması: ölçek birimi protokole göre (yan: gövde boyu — omuz w değil)
        bool trackingJump = _trackingJumpDetector.Evaluate(
            timestamp, _rawPoseScaleValid ? _rawPoseScaleLength : 0f, _filteredXy,
            mpRightOk, mpLeftOk, mpRightWristOk, mpLeftWristOk,
            leftShoulderOk,
            rightShoulderOk,
            _torsoRegionActive,
            _onTrackingJumpDetected,
            warningManager);

        // Yaw önce — teorik ROM düzeltmesi aynı karede güncel φ kullanır
        _frontalFacingGate.Update(
            torsoVis, noseOk, patientSideView,
            _filteredXy[IdxLeftShoulder], _filteredXy[IdxRightShoulder], _filteredXy[IdxNose]);

        if (!trackingJump)
        {
            ScheduleAngleAndLeanJobs(mpRightOk, mpLeftOk, mpRightWristOk, mpLeftWristOk,
                _torsoRegionActive, swap, clinicalRightOk, clinicalLeftOk);
        }

        _qualityFramePublisher.SetVisibilityFraction(
            _qualityFramePublisher.ComputeVisibilityFraction(
                measureRightArm, measureLeftArm,
                regionMask.rightArm, regionMask.leftArm, regionMask.torso,
                clinicalRightOk, clinicalLeftOk, _torsoRegionActive));
        PushQualityFrame();
    }

    private void ConfigureAssistedRepDetector()
    {
        // Eski sahneler: contact min frames taşınmamışsa legacy değerden doldur
        if (assistContactMinFrames < 1 && assistMultiPersonMinFrames > 0)
            assistContactMinFrames = assistMultiPersonMinFrames;

        var cfg = new AssistedRepDetectorConfig
        {
            proximityShoulderWidths = assistProximityShoulderWidths,
            minContactFrames = assistContactMinFrames > 0 ? assistContactMinFrames : 2,
            minJointSpeedShoulderWidthsPerSec = assistMinJointSpeedShoulderWidthsPerSec,
            minAssistRepFraction = assistMinRepFraction,
            minActiveMotionFrames = assistMinActiveMotionFrames
        };
        _assistedRepDetector.Configure(in cfg);
    }

    /// <summary>
    /// Klinik açı sonrası: Katman 2–4 (temas + hız vektörü + süreğenlik).
    /// SaMD Class B: yardım bağlamı; teşhis değildir.
    /// </summary>
    private void UpdateAssistedRepAfterAngles(
        bool swap,
        bool mpRightOk,
        bool mpLeftOk,
        bool mpRightWristOk,
        bool mpLeftWristOk)
    {
        if (!autoAssistFromMultiPerson)
        {
            _assistedRepDetector.Reset();
            _assistPresenceTracker.ClearProximityWarnLatch();
            return;
        }

        ConfigureAssistedRepDetector();
        float dt = Time.unscaledDeltaTime;
        float inv = _assistPresenceTracker.CachedInvShoulderWidth;
        int poseCount = _latestDetectedPoseCount;
        bool hasHelper = _assistPresenceTracker.HasHelperPose;
        ref AssistedHelperPose helper = ref _assistPresenceTracker.HelperPose;

        bool anatRightTrack = swap ? mpLeftOk : mpRightOk;
        bool anatLeftTrack = swap ? mpRightOk : mpLeftOk;
        bool anatRightWrist = swap ? mpLeftWristOk : mpRightWristOk;
        bool anatLeftWrist = swap ? mpRightWristOk : mpLeftWristOk;
        Vector2 elbowR = swap ? _filteredXy[IdxLeftElbow] : _filteredXy[IdxRightElbow];
        Vector2 elbowL = swap ? _filteredXy[IdxRightElbow] : _filteredXy[IdxLeftElbow];
        Vector2 wristR = swap ? _filteredXy[IdxLeftWrist] : _filteredXy[IdxRightWrist];
        Vector2 wristL = swap ? _filteredXy[IdxRightWrist] : _filteredXy[IdxLeftWrist];

        _assistedRepDetector.UpdateArm(
            anatomicalRight: true,
            armTrackingOk: anatRightTrack && measureRightArm,
            wristOk: anatRightWrist,
            patientElbowNorm: elbowR,
            patientWristNorm: wristR,
            patientAngleDegrees: anatRightTrack ? _physicRight : float.NaN,
            deltaTime: dt,
            lowerLimitDegrees: repLowerLimitDegrees,
            hasHelperPose: hasHelper,
            detectedPoseCount: poseCount,
            helper: in helper,
            invShoulderWidth: inv);

        _assistedRepDetector.UpdateArm(
            anatomicalRight: false,
            armTrackingOk: anatLeftTrack && measureLeftArm,
            wristOk: anatLeftWrist,
            patientElbowNorm: elbowL,
            patientWristNorm: wristL,
            patientAngleDegrees: anatLeftTrack ? _physicLeft : float.NaN,
            deltaTime: dt,
            lowerLimitDegrees: repLowerLimitDegrees,
            hasHelperPose: hasHelper,
            detectedPoseCount: poseCount,
            helper: in helper,
            invShoulderWidth: inv);

        _assistPresenceTracker.MaybeWarnProximity(
            IsAssistFromMultiPerson, warningManager, reportManager);
    }

    private void OnTrackingJumpDetected()
    {
        // One Euro geçmişi bozuk kareye yapışmasın; uyarı TrackingJumpDetector içinde
        for (int i = 0; i < PoseLandmarkCount; i++)
        {
            var filter = _filters[i];
            filter.Reset();
            _filters[i] = filter;
        }

        if (reportManager != null)
            reportManager.RegisterTrackingJumpEvent();
    }

    private void ConfigureQualityScorer()
    {
        float ageMul = _spineCompensationGate.ElderToleranceMultiplier(patientAgeYears);
        _qualityFramePublisher.Configure(
            qualityWeightVisibility,
            qualityWeightStability,
            qualityWeightLean,
            qualityMaxShoulderWidthCv,
            qualityReliableThreshold,
            qualityCautionThreshold,
            qualityPeakGateThreshold,
            maxSpineLeanDegrees * ageMul,
            invalidateLeanDegrees * ageMul);
    }

    private void PushQualityFrame()
    {
        _qualityFramePublisher.PushFrame(
            _torsoRegionActive,
            _lastSpineLeanDegrees,
            _rawPoseScaleValid,
            _rawPoseScaleLength,
            reportManager);
    }

    /// <summary>
    /// Ön kamera yatay çevirince MediaPipe L/R hasta anatomisine göre terslenir.
    /// </summary>
    private bool ShouldSwapArmLaterality()
    {
        if (!autoSwapArmsForMirroredCamera) return false;
        try
        {
            var src = Mediapipe.Unity.Sample.ImageSourceProvider.ImageSource;
            if (src == null) return true;
            return src.GetTransformationOptions().flipHorizontally;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Burst job: sağ/sol omuz fleksiyon + omurga lean (yalnızca XY).
    /// NativeArray'ler önceden tahsisli — hot path'te allocation yok.
    /// swap=true: job MP indeksi; _physic* ve avatar anatomik sağ/sol.
    /// </summary>
    private void ScheduleAngleAndLeanJobs(
        bool mpRightOk, bool mpLeftOk, bool mpRightWristOk, bool mpLeftWristOk,
        bool torsoOk, bool swap, bool clinicalRightOk, bool clinicalLeftOk)
    {
        if (!_nativeReady) AllocateNative();
        EnsureMovementStrategy();

        // index 0 = MP sağ: hip, shoulder, elbow
        // Omuz fleksiyonu: kalça+omuz+dirsek yeterli; bacaklar hiç hesaplanmaz / tahmin edilmez.
        _jobEnabled[0] = mpRightOk;
        if (mpRightOk)
        {
            _jobLandmarks[0] = ToFloat2(_filteredXy[IdxRightHip]);
            _jobLandmarks[1] = ToFloat2(_filteredXy[IdxRightShoulder]);
            _jobLandmarks[2] = ToFloat2(_filteredXy[IdxRightElbow]);
            if (_shoulderFlexionAnalyzer != null)
            {
                _shoulderFlexionAnalyzer.UpdateReferenceArmLength(
                    0, _filteredXy[IdxRightShoulder], _filteredXy[IdxRightElbow]);
                _jobRefArmLengths[0] = _shoulderFlexionAnalyzer.GetReferenceArmLength(0);
            }
        }

        // index 1 = MP sol
        _jobEnabled[1] = mpLeftOk;
        if (mpLeftOk)
        {
            _jobLandmarks[3] = ToFloat2(_filteredXy[IdxLeftHip]);
            _jobLandmarks[4] = ToFloat2(_filteredXy[IdxLeftShoulder]);
            _jobLandmarks[5] = ToFloat2(_filteredXy[IdxLeftElbow]);
            if (_shoulderFlexionAnalyzer != null)
            {
                _shoulderFlexionAnalyzer.UpdateReferenceArmLength(
                    1, _filteredXy[IdxLeftShoulder], _filteredXy[IdxLeftElbow]);
                _jobRefArmLengths[1] = _shoulderFlexionAnalyzer.GetReferenceArmLength(1);
            }
        }

        var angleJob = new JointAngleJob
        {
            landmarks = _jobLandmarks,
            referenceArmLengths = _jobRefArmLengths,
            anglesOut = _jobAngles,
            enabled = _jobEnabled
        };

        JobHandle angleHandle = angleJob.Schedule(ArmJobCount, 1);

        // Kompansasyon lean: kalça görünürse her zaman aktif (oturarak dahil). Bacak gerekmez.
        if (torsoOk)
        {
            var leanJob = new SpineLeanJob
            {
                leftShoulder = ToFloat2(_filteredXy[IdxLeftShoulder]),
                rightShoulder = ToFloat2(_filteredXy[IdxRightShoulder]),
                leftHip = ToFloat2(_filteredXy[IdxLeftHip]),
                rightHip = ToFloat2(_filteredXy[IdxRightHip]),
                leanDegreesOut = _jobLeanOut
            };
            JobHandle leanHandle = leanJob.Schedule();
            JobHandle.CombineDependencies(angleHandle, leanHandle).Complete();
            _lastSpineLeanDegrees = _jobLeanOut[0];
        }
        else
        {
            angleHandle.Complete();
            _lastSpineLeanDegrees = 0f;
            _spineCompensationGate.ClearSticky();
        }

        // Omuz fleksiyon yorumu stratejide (SoftFollow / foreshorten / forearm guard)
        var frameCtx = new MovementFrameContext
        {
            deltaTime = Time.unscaledDeltaTime,
            swapArms = swap,
            mpRightOk = mpRightOk,
            mpLeftOk = mpLeftOk,
            mpRightWristOk = mpRightWristOk,
            mpLeftWristOk = mpLeftWristOk,
            clinicalRightOk = clinicalRightOk,
            clinicalLeftOk = clinicalLeftOk,
            jobAngleMpRight = mpRightOk ? _jobAngles[0] : float.NaN,
            jobAngleMpLeft = mpLeftOk ? _jobAngles[1] : float.NaN,
            mpRightShoulder = _filteredXy[IdxRightShoulder],
            mpRightElbow = _filteredXy[IdxRightElbow],
            mpRightWrist = _filteredXy[IdxRightWrist],
            mpLeftShoulder = _filteredXy[IdxLeftShoulder],
            mpLeftElbow = _filteredXy[IdxLeftElbow],
            mpLeftWrist = _filteredXy[IdxLeftWrist],
            bodyYawDegrees = CurrentBodyYawDegrees,
            patientSideView = patientSideView,
            // Teorik mesafe/yaw proxy: ham omuz genişliği (yan φ ayrı; normalize ölçeği değil)
            rawShoulderWidth01 = _rawShoulderWidthValid ? _rawShoulderWidthForQuality : 0f
        };
        MovementFrameResult frameResult = default;
        if (_movementAnalyzer != null)
            _movementAnalyzer.ProcessFrame(in frameCtx, ref frameResult);

        _physicRight = frameResult.clinicalRightAngle;
        _physicLeft = frameResult.clinicalLeftAngle;
        if (frameResult.hasClinicalData)
            _hasData = true;

        _foreshortenMpRight = frameResult.foreshortenMpRight;
        _foreshortenMpLeft = frameResult.foreshortenMpLeft;
        if (frameResult.notifyForeshorten)
            NotifyForeshorteningFeedback();

        _repGateRightValid = frameResult.repGateRightValid;
        _repGateLeftValid = frameResult.repGateLeftValid;
        if (_repGateRightValid) _lastRepGateRight = frameResult.repGateRight;
        if (_repGateLeftValid) _lastRepGateLeft = frameResult.repGateLeft;

        // Yardımlı sezgi: klinik açı + yardımcı elevasyon (yakınlık ∧ eş hareket ∧ kaldırma)
        UpdateAssistedRepAfterAngles(swap, mpRightOk, mpLeftOk, mpRightWristOk, mpLeftWristOk);

        // Avatar: anatomik sağ/sol (ön kamerada MP etiketi terslenebilir)
        PushAnglesToAvatar(
            swap,
            frameResult.avatarMpRightOk, frameResult.avatarMpRightAngle,
            frameResult.avatarMpLeftOk, frameResult.avatarMpLeftAngle);
    }

    private void PushAnglesToAvatar(
        bool swap,
        bool mpRightOk, float mpRightAngle, bool mpLeftOk, float mpLeftAngle)
    {
        // cmd: FindObjectOfType her kare yasak — bir kez dene, yoksa bırak
        if (_avatarBodyDriver == null && !_avatarLookupAttempted)
        {
            _avatarBodyDriver = FindObjectOfType<AvatarBodyDriver>(true);
            _avatarLookupAttempted = true;
        }
        if (_avatarBodyDriver == null) return;
        // Seans öncesi örnek demo MediaPipe açısını ezmesin
        if (_avatarBodyDriver.IsExampleDemoMode) return;

        _avatarBodyDriver.SetMeasuredArms(measureRightArm, measureLeftArm);
        MovementAvatarDriver driver = ExerciseCatalog.GetAvatarDriver(_selectedMovementId);
        // Ön kamera: MP L/R görüntü tarafıdır; model hasta anatomisini izlemeli (sağ→sağ).
        if (swap)
        {
            _avatarBodyDriver.ApplyMeasuredArmAngles(
                driver,
                mpLeftOk, mpLeftAngle,
                mpRightOk, mpRightAngle,
                targetAngleDegrees, targetAngleDegrees);
            return;
        }

        _avatarBodyDriver.ApplyMeasuredArmAngles(
            driver,
            mpRightOk, mpRightAngle,
            mpLeftOk, mpLeftAngle,
            targetAngleDegrees, targetAngleDegrees);
    }

    private void NotifyForeshorteningFeedback()
    {
        if (Time.time <= _lastForeshortenWarnTime + foreshorteningWarningCooldownSeconds)
            return;
        _lastForeshortenWarnTime = Time.time;
        // Speak zaten altyazı gösterir — çift uyarı olmasın
        if (_voiceCoach != null && enableVoiceCoach)
            _voiceCoach.Speak(CoachCue.DepthCollapse);
        else if (warningManager != null)
            warningManager.TriggerWarning(Loc.T("warn.depthCollapse"));
    }

    private static float2 ToFloat2(Vector2 v)
    {
        return new float2(v.x, v.y);
    }

    private Vector2 FilterPoint(int index, float x, float y, float timestamp)
    {
        var filter = _filters[index];
        Vector2 result = filter.Filter(x, y, timestamp);
        _filters[index] = filter;
        return result;
    }

    private bool IsPointConfident(LandmarkPoint p)
    {
        return IsPointConfident(p, landmarkVisibilityThreshold);
    }

    private bool IsPointConfident(LandmarkPoint p, float visibilityThreshold)
    {
        if (!enableConfidenceGate) return true;
        float thr = Mathf.Clamp01(visibilityThreshold);
        if (p.hasVisibility && p.visibility < thr) return false;
        if (requirePresenceScore && p.hasPresence && p.presence < thr) return false;
        return true;
    }

    private static float Angle2D(Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 v1 = a - b;
        Vector2 v2 = c - b;
        if (v1.sqrMagnitude < 1e-12f || v2.sqrMagnitude < 1e-12f) return float.NaN;
        return Vector2.Angle(v1, v2);
    }

    void Update()
    {
        if (!_sessionStarted || _sessionEnded) return;

        // Kuyruktan yalnızca en güncel kareyi al (gecikme birikmesin)
        PoseLandmarkSample latest = default;
        bool gotSample = false;
        while (_poseQueue.TryDequeue(out var sample))
        {
            latest = sample;
            gotSample = true;
        }

        if (gotSample)
        {
            ProcessSampleOnMainThread(latest);
        }

        float repDt = Time.unscaledDeltaTime;
        bool warnLeanRep = false;
        bool invalidateLeanEarly = _torsoRegionActive && _spineCompensationGate.Evaluate(
            _lastSpineLeanDegrees, patientAgeYears, out warnLeanRep,
            warningManager, _voiceCoach, enableVoiceCoach, reportManager);
        bool invalidateFacingEarly = _frontalFacingGate.CheckWarnings(
            patientSideView, warningManager, _voiceCoach, enableVoiceCoach);
        bool invalidateStrainEarly = CheckFaceStrainWarning();
        bool invalidateSideEarly = patientSideView && !_sideProfileSessionGate.MeasurementValid;
        if (invalidateSideEarly)
            _sideProfileSessionGate.MaybeWarnInvalid(warningManager);
        else if (patientSideView)
            _sideProfileSessionGate.MaybeWarnSoft(warningManager);
        bool invalidatePoseEarly = invalidateLeanEarly || invalidateFacingEarly || invalidateStrainEarly || invalidateSideEarly;
        bool swapArmsEarly = ShouldSwapArmLaterality();
        bool fsClinicalRightEarly = swapArmsEarly ? _foreshortenMpLeft : _foreshortenMpRight;
        bool fsClinicalLeftEarly = swapArmsEarly ? _foreshortenMpRight : _foreshortenMpLeft;

        if (!_romAssessmentAnalyzing)
        {
            if (measureRightArm)
                TickRepViaPolicy(
                    _lastRepGateRight, _repGateRightValid, repDt,
                    rightRepText, Loc.T("hud.rep.right"),
                    ref _armRepR, ref _lastShownCountR, ref _cachedRightRep,
                    invalidatePoseEarly || fsClinicalRightEarly, true);
            if (measureLeftArm)
                TickRepViaPolicy(
                    _lastRepGateLeft, _repGateLeftValid, repDt,
                    leftRepText, Loc.T("hud.rep.left"),
                    ref _armRepL, ref _lastShownCountL, ref _cachedLeftRep,
                    invalidatePoseEarly || fsClinicalLeftEarly, false);
        }
        else if (_romAssessmentAnalyzing)
        {
            string analyzeLabel = Loc.T("assess.live.measuring");
            if (measureRightArm && rightRepText != null && _cachedRightRep != analyzeLabel)
            {
                _cachedRightRep = analyzeLabel;
                rightRepText.text = analyzeLabel;
            }
            if (measureLeftArm && leftRepText != null && _cachedLeftRep != analyzeLabel)
            {
                _cachedLeftRep = analyzeLabel;
                leftRepText.text = analyzeLabel;
            }
        }

        if (!_hasData)
        {
            PushCompensationLeanVisual(false);
            return;
        }

        // Kompansasyon + ön görünüm kapısı
        bool warnLean = warnLeanRep;
        PushCompensationLeanVisual(warnLean);

        bool foreshortenClinicalRight = fsClinicalRightEarly;
        bool foreshortenClinicalLeft = fsClinicalLeftEarly;

        if (measureRightArm)
            UpdateArm(true, _physicRight, rightSlider, rightColorCtrl, rightAngleText,
                ref _cachedRightAngle, ref _lastShownRightAngle);
        if (measureLeftArm)
            UpdateArm(false, _physicLeft, leftSlider, leftColorCtrl, leftAngleText,
                ref _cachedLeftAngle, ref _lastShownLeftAngle);

        if (reportManager != null)
        {
            if (faceStrainAnalyzer != null && faceStrainAnalyzer.HasFace)
                reportManager.RegisterStrainSample(faceStrainAnalyzer.CurrentEffort01, _physicRight, _physicLeft);

            bool allowPeak = _qualityFramePublisher.QualityAllowsPeakRom
                && IsFrontalFacingOk
                && !(measureRightArm && foreshortenClinicalRight)
                && !(measureLeftArm && foreshortenClinicalLeft);
            reportManager.RegisterAngleSample(
                _physicRight, _physicLeft, measureRightArm, measureLeftArm,
                allowPeakUpdate: allowPeak,
                assistRight: IsAssistEffectiveRight,
                assistLeft: IsAssistEffectiveLeft);
        }

        if (!_sessionEnded && IsSessionGoalReached())
        {
            TryFinishReachedGoal();
        }

        _hasData = false;
    }

    /// <summary>
    /// Tekrar sayımı politikaya delege; UI/rapor yan etkileri host'ta kalır.
    /// Refactor only — klinik eşik değişikliği yok. SaMD Class B; teşhis değildir.
    /// </summary>
    private void TickRepViaPolicy(
        float gateAngle,
        bool gateValid,
        float dt,
        TextMeshProUGUI rText,
        string pref,
        ref ArmRepState state,
        ref int lastShownCount,
        ref string cachedRep,
        bool invalidatePose,
        bool anatomicalRight)
    {
        if (_repPolicy == null) EnsureMovementStrategy();
        if (_repPolicy == null) return;

        var ctx = new RepTickContext
        {
            gateAngle = gateAngle,
            gateValid = gateValid,
            deltaTime = dt,
            targetDegrees = targetAngleDegrees,
            lowerLimitDegrees = repLowerLimitDegrees,
            holdSeconds = repTargetHoldSeconds,
            enterSlackDegrees = repTargetEnterSlackDegrees,
            minTravelDegrees = repMinTravelDegrees,
            invalidatePose = invalidatePose,
            anatomicalRight = anatomicalRight
        };
        RepTickResult result = default;
        _repPolicy.Tick(in ctx, ref state, ref result);

        if (anatomicalRight)
        {
            _countR = state.count;
            _invalidR = state.invalidCount;
            _isUpR = state.isUp;
            _repInvalidR = state.repInvalid;
            _targetHoldR = state.targetHoldStreak;
            _repCountedAtPeakR = state.repCountedAtPeak;
            _inTargetZoneR = state.inTargetZone;
        }
        else
        {
            _countL = state.count;
            _invalidL = state.invalidCount;
            _isUpL = state.isUp;
            _repInvalidL = state.repInvalid;
            _targetHoldL = state.targetHoldStreak;
            _repCountedAtPeakL = state.repCountedAtPeak;
            _inTargetZoneL = state.inTargetZone;
        }

        if (result.countedInvalid)
        {
            if (reportManager != null) reportManager.RegisterInvalidRep(anatomicalRight);
            if (warningManager != null) warningManager.TriggerWarning(Loc.T("warn.repInvalid"));
            if (_voiceCoach != null) _voiceCoach.Speak(CoachCue.RepInvalid);
        }
        else if (result.countedValid)
        {
            bool assisted = anatomicalRight ? IsAssistEffectiveRight : IsAssistEffectiveLeft;
            if (reportManager != null)
            {
                reportManager.IncrementRep(anatomicalRight, assisted);
                if (!assisted && _qualityFramePublisher.QualityAllowsPeakRom)
                    reportManager.RegisterAngle(result.gateAngleAtCount, anatomicalRight);
            }
            MaybeSpeakAlmostDone();
        }

        int count = anatomicalRight ? _countR : _countL;
        if (rText != null && (count != lastShownCount || targetReps != _lastShownTargetReps))
        {
            cachedRep = pref + " " + count.ToString() + " / " + targetReps.ToString();
            rText.text = cachedRep;
            lastShownCount = count;
            _lastShownTargetReps = targetReps;
        }
    }

    private void UpdateArm(
        bool isRight,
        float rawAngle,
        Slider slider,
        SliderColorController color,
        TextMeshProUGUI aText,
        ref string cachedAngle,
        ref int lastShownAngle)
    {
        CheckRaiseTempo(isRight, rawAngle);
        UpdateRomAssessmentAndSliderScale(rawAngle);

        if (isRight)
        {
            _armUiPresenter.UpdateArmVisual(
                ref _visualRight, rawAngle, lerpSpeed, Time.deltaTime,
                slider, color, aText, ref cachedAngle, ref lastShownAngle);
        }
        else
        {
            _armUiPresenter.UpdateArmVisual(
                ref _visualLeft, rawAngle, lerpSpeed, Time.deltaTime,
                slider, color, aText, ref cachedAngle, ref lastShownAngle);
        }
    }

    /// <summary>
    /// İlk seans: zirve ROM ölç → slider doluluk ölçeğini yapabileceğin ×2 yap → eğitim hedefine geç.
    /// Örn. 5° kaldırabiliyorsa slider full ≈ 10°.
    /// </summary>
    private void UpdateRomAssessmentAndSliderScale(float rawAngle)
    {
        if (rawAngle > _sessionPeakRom + 0.5f)
        {
            _sessionPeakRom = rawAngle;
            _peakLastImprovedAt = Time.time;
        }

        if (_romAssessmentAnalyzing || _sessionPeakRom >= AssessmentMinPeakDegrees)
        {
            float desiredFull = Mathf.Max(
                _sessionPeakRom * SliderMotivationalRatio,
                _sessionPeakRom + SliderMotivationalSlackDegrees,
                SliderStartFullDegrees);
            desiredFull = Mathf.Clamp(desiredFull, SliderMinFullDegrees, 180f);
            if (desiredFull > _sliderFullDegrees)
                _sliderFullDegrees = desiredFull;
        }

        if (!_romAssessmentAnalyzing) return;
        if (_sessionPeakRom < AssessmentMinPeakDegrees) return;
        if (Time.time - _peakLastImprovedAt < AssessmentSettleSeconds) return;
        if (Time.time - _assessmentPhaseStartedAt < AssessmentSettleSeconds) return;

        // Analiz bitti → yapabileceği açıyı hedef yap; slider motivasyon ölçeğinde kalsın
        float trainable = Mathf.Clamp(
            Mathf.Round(_sessionPeakRom / PersonalizedTargetAdvisor.AngleStepDegrees)
                * PersonalizedTargetAdvisor.AngleStepDegrees,
            PersonalizedTargetAdvisor.MinAngleDegrees,
            PersonalizedTargetAdvisor.MaxAngleDegrees);
        if (trainable < AssessmentMinPeakDegrees)
            trainable = Mathf.Max(AssessmentMinPeakDegrees, _sessionPeakRom);

        targetAngleDegrees = trainable;
        targetReps = AssessmentDefaultReps;
        RefreshRepLowerLimitFromTarget();
        _sliderFullDegrees = Mathf.Clamp(
            Mathf.Max(targetAngleDegrees * SliderMotivationalRatio, targetAngleDegrees + SliderMotivationalSlackDegrees),
            SliderMinFullDegrees, 180f);
        _romAssessmentAnalyzing = false;
        SyncFlexionTargetsToAvatar();
        _countR = 0;
        _countL = 0;
        _isUpR = false;
        _isUpL = false;
        _repInvalidR = false;
        _repInvalidL = false;
        _armRepR = default;
        _armRepL = default;
        ResetRepHoldState();
        _lastShownCountR = int.MinValue;
        _lastShownCountL = int.MinValue;

        if (reportManager != null && reportManager.IsSessionActive)
            reportManager.StartSession(targetReps, targetAngleDegrees, _plannedMeasureRight, _plannedMeasureLeft);

        if (warningManager != null)
            warningManager.TriggerWarning(Loc.Format("assess.live.ready", (int)targetAngleDegrees, (int)_sliderFullDegrees));
        if (_voiceCoach != null && enableVoiceCoach)
            _voiceCoach.SpeakTargets(targetAngleDegrees, targetReps);
    }

    private float CalculateAngle2D(int p1, int p2, int p3)
    {
        return Angle2D(_filteredXy[p1], _filteredXy[p2], _filteredXy[p3]);
    }

    /// <summary>
    /// Yüz zorlanması soft uyarısı. Tekrar geçersiz kılma yalnızca FaceStrainAnalyzer.invalidateOnHighStrain açıksa.
    /// SaMD Class B: karar-destek göstergesi; teşhis değildir.
    /// </summary>
    private bool CheckFaceStrainWarning()
    {
        if (faceStrainAnalyzer == null || !faceStrainAnalyzer.HasFace) return false;

        if (faceStrainAnalyzer.IsAboveWarnThreshold
            && Time.time > _lastStrainWarningTime + strainWarningCooldownSeconds)
        {
            _lastStrainWarningTime = Time.time;
            if (warningManager != null)
                warningManager.TriggerWarning(Loc.T("warn.strain"));
            if (_voiceCoach != null)
                _voiceCoach.Speak(CoachCue.HighStrain);
        }

        return faceStrainAnalyzer.IsAboveInvalidateThreshold;
    }

    private void PushCompensationLeanVisual(bool warnLean)
    {
        if (_avatarBodyDriver == null && !_avatarLookupAttempted)
        {
            _avatarBodyDriver = FindObjectOfType<AvatarBodyDriver>(true);
            _avatarLookupAttempted = true;
        }
        if (_avatarBodyDriver != null)
            _avatarBodyDriver.SetCompensationLeanVisual(warnLean);
    }

    private void CheckRaiseTempo(bool isRight, float rawAngle)
    {
        if (_voiceCoach == null || !enableVoiceCoach) return;

        float prev = isRight ? _prevAngleR : _prevAngleL;
        float prevT = isRight ? _prevAngleTimeR : _prevAngleTimeL;
        float now = Time.time;

        if (prevT > 0f)
        {
            float dt = now - prevT;
            if (dt > 0.04f && dt < 0.45f && rawAngle > prev + 1f)
            {
                float rate = (rawAngle - prev) / dt;
                if (rate > maxRaiseDegreesPerSecond)
                    _voiceCoach.Speak(CoachCue.SlowDown);
            }
        }

        if (isRight)
        {
            _prevAngleR = rawAngle;
            _prevAngleTimeR = now;
        }
        else
        {
            _prevAngleL = rawAngle;
            _prevAngleTimeL = now;
        }
    }

    private void ResetRepHoldState()
    {
        _targetHoldR = 0f;
        _targetHoldL = 0f;
        _repCountedAtPeakR = false;
        _repCountedAtPeakL = false;
        _inTargetZoneR = false;
        _inTargetZoneL = false;
        _repGateRightValid = false;
        _repGateLeftValid = false;
        _lastRepGateRight = 0f;
        _lastRepGateLeft = 0f;
        _armRepR.targetHoldStreak = 0f;
        _armRepL.targetHoldStreak = 0f;
        _armRepR.repCountedAtPeak = false;
        _armRepL.repCountedAtPeak = false;
        _armRepR.inTargetZone = false;
        _armRepL.inTargetZone = false;
    }

    private void MaybeSpeakAlmostDone()
    {
        if (_almostDoneSpoken || _voiceCoach == null || !enableVoiceCoach) return;
        if (targetReps <= 0) return;

        int need = 0;
        int done = 0;
        if (_plannedMeasureRight) { need += targetReps; done += _countR; }
        if (_plannedMeasureLeft) { need += targetReps; done += _countL; }
        if (need <= 0) return;

        // Son %20'ye girince bir kez
        if (done * 5 >= need * 4)
        {
            _almostDoneSpoken = true;
            _voiceCoach.Speak(CoachCue.AlmostDone);
        }
    }

    public void SetTargetReps(int newGoal)
    {
        targetReps = newGoal;
        _countR = 0;
        _countL = 0;
        _armRepR = default;
        _armRepL = default;
        ResetRepHoldState();
        _lastShownCountR = int.MinValue;
        _lastShownCountL = int.MinValue;
        _lastShownTargetReps = int.MinValue;

        if (reportManager != null && reportManager.IsSessionActive)
        {
            reportManager.StartSession(targetReps, targetAngleDegrees, _plannedMeasureRight, _plannedMeasureLeft);
        }
    }

    public void SaveCurrentSession()
    {
        var exercise = new SessionCloseoutService.ExerciseContext
        {
            targetReps = targetReps,
            targetAngleDegrees = targetAngleDegrees,
            plannedMeasureRight = _plannedMeasureRight,
            plannedMeasureLeft = _plannedMeasureLeft,
            measureRightArm = measureRightArm,
            measureLeftArm = measureLeftArm,
            selectedMovementId = _selectedMovementId,
            selectedBodyRegionId = _selectedBodyRegionId
        };
        var counts = new SessionCloseoutService.SessionCounts
        {
            countR = _countR,
            countL = _countL,
            invalidR = _invalidR,
            invalidL = _invalidL,
            visualRight = _visualRight,
            visualLeft = _visualLeft
        };
        var identity = new SessionCloseoutService.PatientIdentity
        {
            firstName = patientFirstName,
            lastName = patientLastName,
            heightCm = patientHeightCm,
            ageYears = patientAgeYears,
            gender = patientGender
        };
        _sessionCloseoutService.SaveCurrentSession(
            in exercise, in counts, in identity,
            _qualityFramePublisher.Scorer, dataManager, reportManager);
    }

    private void TryFinishReachedGoal()
    {
        PatientProfile profile = dataManager != null ? dataManager.LoadProfile() : null;
        if (profile != null && profile.HasRemainingVisitMovements())
        {
            MovementId done = _selectedMovementId;
            int idx = profile.plannedMovementIndex;
            int total = profile.PlannedMovementCount;
            FinishSession(showReport: false, visitComplete: false);
            profile.AdvancePlannedMovement();
            dataManager.SaveProfile(profile);
            VisitSegmentCompleted?.Invoke(done, idx, total);
            return;
        }

        FinishSession(showReport: true, visitComplete: true);
    }

    private void FinishSession(bool showReport)
    {
        FinishSession(showReport, visitComplete: showReport);
    }

    private void FinishSession(bool showReport, bool visitComplete)
    {
        if (!_sessionStarted || _sessionEnded) return;
        _sessionEnded = true;

        _sessionCloseoutService.ComputeMovementScore(
            enableMovementScoring, targetAngleDegrees, movementTemplatePoints,
            measureRightArm, measureLeftArm, reportManager);

        // Ani çıkışta süreyi önce dondur; normal bitişte UI EndSessionAndShowReport dondurur.
        if (!showReport && reportManager != null)
            reportManager.EndSessionSilent();

        SaveCurrentSession();
        if (visitComplete)
            SessionStatus.MarkCompleted();
        else
            SessionStatus.MarkIdle();
        _sessionCloseoutService.ExportSessionFiles(
            dataManager, reportManager,
            new SessionCloseoutService.PatientIdentity
            {
                firstName = patientFirstName,
                lastName = patientLastName,
                heightCm = patientHeightCm,
                ageYears = patientAgeYears,
                gender = patientGender
            },
            measureRightArm, measureLeftArm,
            _selectedMovementId);
        ApplyArmUiVisibility();

        Transform canvasRoot = ResolveHudCanvas();
        if (showReport)
        {
            AssessmentFlow.OnSessionFinished(dataManager, canvasRoot, true, () =>
            {
                ReexportSessionHtmlWithSurvey();
                if (reportManager != null)
                    reportManager.EndSessionAndShowReport();
            });
        }
        else if (visitComplete)
        {
            AssessmentFlow.OnSessionFinished(dataManager, null, false, null);
        }
    }

    private void ReexportSessionHtmlWithSurvey()
    {
        if (dataManager == null || reportManager == null || !reportManager.HasData) return;
        PatientProfile profile = dataManager.LoadProfile();
        PatientHistory history = dataManager.LoadHistoryForPatient(profile);
        SurveyResponse survey = null;
        if (history != null && history.sessions != null && history.sessions.Count > 0)
            survey = SurveyResponse.FromSessionEntry(history.sessions[history.sessions.Count - 1]);
        if (survey == null) return;

        _sessionCloseoutService.ExportSessionFiles(
            dataManager, reportManager,
            new SessionCloseoutService.PatientIdentity
            {
                firstName = patientFirstName,
                lastName = patientLastName,
                heightCm = patientHeightCm,
                ageYears = patientAgeYears,
                gender = patientGender
            },
            measureRightArm, measureLeftArm,
            _selectedMovementId,
            survey);
    }

    private static Transform ResolveHudCanvas()
    {
        Canvas c = Object.FindObjectOfType<Canvas>();
        return c != null ? c.transform : null;
    }

    private void OnDisable()
    {
        // Sahne değişimi / kilitlenme: DTW + JSON + HTML/CSV (UI raporu yok)
        TryEmergencyCloseout();
    }

    private void OnApplicationQuit()
    {
        TryEmergencyCloseout();
    }

    /// <summary>
    /// Ani çıkışta tam klinik kapanış: DTW, geçmiş kaydı, yerel HTML/CSV.
    /// showReport=false — UI paneli açılmaz (sahne zaten kapanıyor olabilir).
    /// </summary>
    private void TryEmergencyCloseout()
    {
        if (!_sessionStarted || _sessionEnded) return;
        FinishSession(showReport: false);
    }
}
