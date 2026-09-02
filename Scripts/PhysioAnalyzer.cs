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
/// Pose Landmarker tabanlı ROM orkestratörü (composition root).
/// Parçalar: PhysioAnalyzer.Session, .PosePipeline, .RepCoordinator.
/// Hareket mantığı IMovementAnalyzer / IRepPolicy / aile pipeline'larında; bu sınıf hareket adına dallanmaz.
/// LIVE_STREAM callback thread-safe: landmark kopyası ConcurrentQueue ile ana thread'e aktarılır.
/// SaMD Class B: ROM ve kompansasyon çıktıları klinik karar destek bilgisidir.
/// </summary>
public partial class PhysioAnalyzer : MonoBehaviour
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

    // Hareket stratejisi — factory + protokol profili; somut analyzer tipi host'ta yok.
    private IMovementAnalyzer _movementAnalyzer;
    private IRepPolicy _repPolicy;
    private MovementProtocolProfile _movementProtocolProfile;
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
        EnsureMovementStrategy();
        SetRegionMask(_movementAnalyzer != null ? _movementAnalyzer.RequiredMask : def.BuildMask());
        ApplyRaisePlaneForMovement();
        ConfigureGateServices();
        ConfigureAssistPresenceTracker();
        if (!_sessionStarted || _sessionEnded)
            ApplyPreSessionAvatarOrbit();
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
            enableSessionYawGate = _movementProtocolProfile.enableYawGate && enableSessionYawGate,
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

    private MovementHostSettings BuildMovementHostSettings()
    {
        var settings = new MovementHostSettings
        {
            reference = new ShoulderElevationReferenceConfig
            {
                refUpdateMinRatio = foreshorteningRefUpdateMinRatio,
                refUpdateMaxRatio = foreshorteningRefUpdateMaxRatio
            },
            abduction = new ShoulderAbductionAnalyzerConfig
            {
                reference = new ShoulderElevationReferenceConfig
                {
                    refUpdateMinRatio = foreshorteningRefUpdateMinRatio,
                    refUpdateMaxRatio = foreshorteningRefUpdateMaxRatio
                },
                suppressForearmRotationArtifact = suppressForearmRotationArtifact,
                forearmRotationElbowDeltaDegrees = forearmRotationElbowDeltaDegrees,
                forearmRotationFlexionDeltaDegrees = forearmRotationFlexionDeltaDegrees,
                foreshorteningMinArmRatio = foreshorteningMinArmRatio,
                foreshorteningMinChainRatio = foreshorteningMinChainRatio,
                foreshorteningMinElbowExtensionDegrees = foreshorteningMinElbowExtensionDegrees
            },
            romCorrection = new TheoreticalRomCorrectionConfig
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
            },
            rep = new RepPolicyHostConfig
            {
                holdSeconds = repTargetHoldSeconds,
                enterSlackDegrees = repTargetEnterSlackDegrees,
                returnRatio = repReturnRatio,
                lowerMinDegrees = repLowerMinDegrees,
                lowerMaxDegrees = repLowerMaxDegrees,
                minTravelDegrees = repMinTravelDegrees
            },
            foreshorteningWarningCooldownSeconds = foreshorteningWarningCooldownSeconds
        };
        return settings;
    }

    /// <summary>
    /// Katalog hareketine göre analizör/tekrar politikası.
    /// SaMD Class B; teşhis değildir.
    /// </summary>
    private void EnsureMovementStrategy()
    {
        MovementId id = _selectedMovementId;
        if (!ExerciseCatalog.IsLiveReady(id))
            id = MovementId.ShoulderFlexion;

        bool needNew = _movementAnalyzer == null;
        if (!needNew)
            needNew = _movementAnalyzer.Id != id;

        if (needNew)
        {
            _movementAnalyzer = MovementAnalyzerFactory.CreateAnalyzer(id);
            _repPolicy = MovementAnalyzerFactory.CreateRepPolicy(id);
            _movementProtocolProfile = MovementProtocolProfile.ForMovement(id);
        }

        ApplyMovementHostSettings();
    }

    private void ApplyMovementHostSettings()
    {
        MovementHostSettings settings = BuildMovementHostSettings();

        if (_movementAnalyzer is IMovementConfigurable configurable)
            configurable.ApplyHostSettings(in settings);

        if (_repPolicy != null)
        {
            _repPolicy.Configure(settings.rep);
            _repPolicy.SetTargetDegrees(targetAngleDegrees);
            repLowerLimitDegrees = _repPolicy.LowerLimitDegrees;
        }
    }

    private void ConfigureMovementStrategy()
    {
        ApplyMovementHostSettings();
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
        _jobLandmarks = new NativeArray<float2>(ShoulderElevationAnglePipeline.LandmarkTripletCount, Allocator.Persistent);
        _jobAngles = new NativeArray<float>(ShoulderElevationAnglePipeline.ArmJobCount, Allocator.Persistent);
        _jobEnabled = new NativeArray<bool>(ShoulderElevationAnglePipeline.ArmJobCount, Allocator.Persistent);
        _jobRefArmLengths = new NativeArray<float>(ShoulderElevationAnglePipeline.ArmJobCount, Allocator.Persistent);
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
            ApplyPreSessionAvatarOrbit();
        }

        SyncArmMeasurementPipeline();
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

}
