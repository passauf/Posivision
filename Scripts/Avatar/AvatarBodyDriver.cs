using System.Collections.Concurrent;
using Mediapipe.Tasks.Components.Containers;
using UnityEngine;

/// <summary>
/// MediaPipe pose → Mixamo Humanoid kemik rotasyonları.
/// Omuz fleksiyonu: görsel sürüş omuz+dirsek; klinik açı PhysioAnalyzer'dan da uygulanır.
/// Görünmeyen bölgeler tahmin edilmez. Animator açık kalır (Optimize Game Objects uyumu).
/// </summary>
public class AvatarBodyDriver : MonoBehaviour
{
    private const int LandmarkCount = 33;
    private const int Nose = 0;
    private const int LeftEar = 7;
    private const int RightEar = 8;
    private const int LeftShoulder = 11;
    private const int RightShoulder = 12;
    private const int LeftElbow = 13;
    private const int RightElbow = 14;
    private const int LeftWrist = 15;
    private const int RightWrist = 16;
    private const int LeftHip = 23;
    private const int RightHip = 24;
    private const int LeftKnee = 25;
    private const int RightKnee = 26;
    private const int LeftAnkle = 27;
    private const int RightAnkle = 28;

    private struct LandmarkSample
    {
        public float x, y, z, visibility;
        public bool hasVisibility;
        public bool copied;
    }

    private struct PoseSample
    {
        public float timestampSeconds;
        public bool valid;
        public LandmarkSample nose;
        public LandmarkSample lEar, rEar;
        public LandmarkSample lShoulder, rShoulder, lElbow, rElbow, lWrist, rWrist;
        public LandmarkSample lHip, rHip, lKnee, rKnee, lAnkle, rAnkle;
    }

    private struct BoneCache
    {
        public Transform bone;
        public Transform parent;
        public Quaternion restLocal;
        public Vector3 bindLocalDir;
        public bool ready;
    }

    [Header("Bölgeler (omuz fleksiyonu)")]
    [SerializeField] private PoseRegionMask regionMask = PoseRegionMask.ShoulderFlexion();

    [Header("Filtre")]
    [SerializeField] private float filterMinCutoff = 1.0f;
    [SerializeField] private float filterBeta = 0.007f;
    [SerializeField] private float filterDCutoff = 1f;
    [Tooltip("Eşik altı landmark geçersiz. Görsel kol sürüşü omuz+dirsek ister.")]
    [SerializeField] private float landmarkVisibilityThreshold = 0.5f;
    [Tooltip("Baş (burun/kulak) için daha yumuşak eşik — yan profilde MediaPipe skorları düşük olabilir.")]
    [SerializeField] private float headLandmarkVisibilityThreshold = 0.28f;

    [Header("Ölçek / ayna")]
    [SerializeField] private float horizontalScale = 1.6f;
    [SerializeField] private float verticalScale = 1.6f;
    [SerializeField] private float depthScale = 0.8f;
    [Tooltip("Landmark X aynası (ön kamera görüntüsü). Kemik eşlemesini değiştirmez.")]
    [SerializeField] private bool mirrorX = true;
    [Tooltip("Açık: önce Physio ROM açısı ile sür (slider ile aynı kol). Landmark yalnızca açı yoksa.")]
    [SerializeField] private bool preferFlexionAngles = true;
    [Tooltip("Seans öncesi örnek hareket: MediaPipe pose ve radial yaylar kapalı.")]
    [SerializeField] private bool exampleDemoMode;

    [Header("Omuz kaldırma (dikey düzlem)")]
    [Tooltip("Coronal: yandan kaldırma (kameraya karşı). Sagittal: öne fleksiyon. Açı: 0° aşağı, 90° yatay, 180° yukarı.")]
    [SerializeField] private ArmRaisePlane raisePlane = ArmRaisePlane.Coronal;

    [Header("Gövde lean (kompansasyon görseli)")]
    [Tooltip("Hasta dik durmuyorsa avatar kalça/omurga aynı yöne eğilir — yanlış formu görsün. Kalça yeter; bacak gerekmez.")]
    [SerializeField] private bool driveTorsoLean = true;
    [Tooltip("Açıkken gövde yalnızca PhysioAnalyzer 'dik dur' uyarısı aktifken eğilir.")]
    [SerializeField] private bool torsoLeanOnlyDuringCompensationWarn = true;
    [Tooltip("Uyarı başlayınca bu açıda o tarafa kilitlenir; dik durana kadar sabit kalır (titreme yok).")]
    [SerializeField] private float compensationHoldLeanDegrees = 12f;
    [SerializeField] private float leanReturnDegreesPerSecond = 40f;
    [SerializeField] private float torsoLeanVisualGain = 0.85f;
    [SerializeField] private float hipRollVisualGain = 0.45f;
    [SerializeField] private float maxTorsoLeanVisualDegrees = 18f;
    [Tooltip("Yön örneklemede ölü bant (yalnızca uyarı başlangıcında bir kez).")]
    [SerializeField] private float torsoLeanDeadbandDegrees = 6f;

    /// <summary>PhysioAnalyzer: kompansasyon uyarısı açıkken true.</summary>
    private bool _compensationWarnActive;
    private bool _leanHoldLatched;
    private float _heldLeanSignedDeg;
    private float _appliedLeanDeg;

    public enum ArmRaisePlane
    {
        Coronal = 0,
        Sagittal = 1
    }

    private readonly ConcurrentQueue<PoseSample> _queue = new ConcurrentQueue<PoseSample>();
    private readonly OneEuroFilter2D[] _filtersXy = new OneEuroFilter2D[LandmarkCount];
    private readonly OneEuroFilter1D[] _filtersZ = new OneEuroFilter1D[LandmarkCount];
    private readonly Vector3[] _filtered = new Vector3[LandmarkCount];
    private readonly float[] _norm01X = new float[LandmarkCount];
    private readonly float[] _norm01Y = new float[LandmarkCount];
    private readonly bool[] _valid = new bool[LandmarkCount];

    private Animator _animator;
    private Transform _root;
    private BoneCache _hips, _spine, _chest, _head;
    private BoneCache _lUpper, _lLower, _rUpper, _rLower;
    private BoneCache _lULeg, _lLLeg, _rULeg, _rLLeg;

    private bool _humanoidBound;
    private bool _filtersConfigured;
    private bool _hasPose;
    private bool _loggedFirstDrive;
    private PoseRegionVisibility _visibility;

    // PhysioAnalyzer'dan gelen klinik açı (slider ile aynı)
    private bool _flexRightOk, _flexLeftOk;
    private float _flexRightDeg, _flexLeftDeg;
    private float _flexRightTarget = 160f;
    private float _flexLeftTarget = 160f;
    private bool _hasFlexionAngles;
    private bool _measureRight = true;
    private bool _measureLeft = true;

    private ProceduralMannequin _mannequin;
    private ShoulderFlexionArcIndicator _arcIndicator;
    private HipKneeArcIndicator _hipKneeArcIndicator;
    private JointHingeArcIndicator _elbowArcIndicator;
    private JointHingeArcIndicator _ankleArcIndicator;
    private BodyRegionId _arcRegion = BodyRegionId.Shoulder;

    public bool HasPose => _hasPose;
    public bool HasHumanoid => _humanoidBound;
    public PoseRegionMask RegionMask => regionMask;
    public PoseRegionVisibility RegionVisibility => _visibility;
    public bool HasShoulderFlexionFrame => _visibility.HasShoulderFlexionFrame(regionMask);

    /// <summary>
    /// Seans öncesi konum: her iki omuz görünür mü + gövde merkezi X (0..1, ham MediaPipe).
    /// exampleDemoMode iken kuyruk boşaltıldığı için false döner.
    /// </summary>
    public bool TryGetShoulderCenter01(out float midX01, out bool bothShouldersVisible)
    {
        midX01 = 0.5f;
        bothShouldersVisible = _valid[LeftShoulder] && _valid[RightShoulder];
        if (!bothShouldersVisible || !_hasPose) return false;
        midX01 = 0.5f * (_norm01X[LeftShoulder] + _norm01X[RightShoulder]);
        midX01 = Mathf.Clamp01(midX01);
        return true;
    }

    /// <summary>
    /// Baş kadrajda mı — burun veya kulak (yan profilde burun visibility düşük olabilir).
    /// Mask.head kapalı olsa bile burun/kulak örneklenir (konum kapısı için).
    /// </summary>
    public bool TryGetHeadVisible(out bool headVisible)
    {
        headVisible = false;
        if (!_hasPose) return false;
        headVisible = _valid[Nose] || _valid[LeftEar] || _valid[RightEar];
        return true;
    }

    /// <summary>Ham omuz genişliği 0–1 (yan sapma φ için).</summary>
    public bool TryGetRawShoulderWidth01(out float width01)
    {
        width01 = 0f;
        if (!_hasPose || !_valid[LeftShoulder] || !_valid[RightShoulder]) return false;
        float dx = _norm01X[LeftShoulder] - _norm01X[RightShoulder];
        float dy = _norm01Y[LeftShoulder] - _norm01Y[RightShoulder];
        width01 = Mathf.Sqrt(dx * dx + dy * dy);
        return width01 > 1e-5f;
    }

    /// <summary>Ham gövde boyu 0–1 (orta-omuz → orta-kalça) — yan φ mesafe-bağımsız ref.</summary>
    public bool TryGetRawTorsoLength01(out float length01)
    {
        length01 = 0f;
        if (!_hasPose) return false;
        bool ls = _valid[LeftShoulder];
        bool rs = _valid[RightShoulder];
        bool lh = _valid[LeftHip];
        bool rh = _valid[RightHip];
        if ((!ls && !rs) || (!lh && !rh)) return false;

        Vector2 midS;
        if (ls && rs)
            midS = new Vector2(
                0.5f * (_norm01X[LeftShoulder] + _norm01X[RightShoulder]),
                0.5f * (_norm01Y[LeftShoulder] + _norm01Y[RightShoulder]));
        else if (ls)
            midS = new Vector2(_norm01X[LeftShoulder], _norm01Y[LeftShoulder]);
        else
            midS = new Vector2(_norm01X[RightShoulder], _norm01Y[RightShoulder]);

        Vector2 midH;
        if (lh && rh)
            midH = new Vector2(
                0.5f * (_norm01X[LeftHip] + _norm01X[RightHip]),
                0.5f * (_norm01Y[LeftHip] + _norm01Y[RightHip]));
        else if (lh)
            midH = new Vector2(_norm01X[LeftHip], _norm01Y[LeftHip]);
        else
            midH = new Vector2(_norm01X[RightHip], _norm01Y[RightHip]);

        length01 = Vector2.Distance(midS, midH);
        return length01 > 1e-5f;
    }

    /// <summary>Çalışan vs karşı kol görünürlük (yanlış yan sezgisi).</summary>
    public void GetArmVisibility(out bool rightOk, out bool leftOk)
    {
        rightOk = _visibility.rightArm || _visibility.rightForearm;
        leftOk = _visibility.leftArm || _visibility.leftForearm;
    }

    public void SetRaisePlane(ArmRaisePlane plane)
    {
        raisePlane = plane;
        if (_arcIndicator != null)
            _arcIndicator.SetRaisePlane(raisePlane);
        if (_hipKneeArcIndicator != null)
            _hipKneeArcIndicator.SetRaisePlane(raisePlane);
    }

    public ArmRaisePlane RaisePlane => raisePlane;

    public void SetRegionMask(PoseRegionMask mask)
    {
        regionMask = mask;
        // Ölçüm bayrakları üstün: kapalı kol yeniden açılmasın
        regionMask.rightArm = regionMask.rightArm && _measureRight;
        regionMask.leftArm = regionMask.leftArm && _measureLeft;
    }

    /// <summary>
    /// PhysioAnalyzer ROM açısını modele uygular — slider ile birebir senkron.
    /// SaMD: açı zaten güven kapısından geçmiş örneklerden gelir.
    /// </summary>
    public void ApplyShoulderFlexionAngles(bool rightOk, float rightDegrees, bool leftOk, float leftDegrees)
    {
        ApplyShoulderFlexionAngles(rightOk, rightDegrees, leftOk, leftDegrees, _flexRightTarget, _flexLeftTarget);
    }

    public void ApplyShoulderFlexionAngles(
        bool rightOk, float rightDegrees, bool leftOk, float leftDegrees,
        float rightTargetDegrees, float leftTargetDegrees)
    {
        _flexRightOk = rightOk;
        _flexLeftOk = leftOk;
        _flexRightDeg = rightDegrees;
        _flexLeftDeg = leftDegrees;
        if (rightTargetDegrees > 1f) _flexRightTarget = rightTargetDegrees;
        if (leftTargetDegrees > 1f) _flexLeftTarget = leftTargetDegrees;
        _hasFlexionAngles = rightOk || leftOk;
    }

    /// <summary>
    /// Katalog <see cref="MovementAvatarDriver"/> ile açı uygular.
    /// Yeni driver: buraya case ekle (ElbowHinge vb.).
    /// </summary>
    public void ApplyMeasuredArmAngles(
        MovementAvatarDriver driver,
        bool rightOk, float rightDegrees, bool leftOk, float leftDegrees,
        float rightTargetDegrees, float leftTargetDegrees)
    {
        switch (driver)
        {
            case MovementAvatarDriver.ElbowHinge:
                // Gelecek: dirsek menteşe sürücüsü. Şimdilik elevasyon yoluna düş.
                ApplyShoulderFlexionAngles(
                    rightOk, rightDegrees, leftOk, leftDegrees,
                    rightTargetDegrees, leftTargetDegrees);
                break;
            case MovementAvatarDriver.ShoulderElevation:
            case MovementAvatarDriver.None:
            default:
                ApplyShoulderFlexionAngles(
                    rightOk, rightDegrees, leftOk, leftDegrees,
                    rightTargetDegrees, leftTargetDegrees);
                break;
        }
    }

    /// <summary>Seans hedef açısını yay rengi / track ucu için günceller (ölçüm olmadan).</summary>
    public void SetFlexionTargets(float rightTargetDegrees, float leftTargetDegrees)
    {
        if (rightTargetDegrees > 1f) _flexRightTarget = rightTargetDegrees;
        if (leftTargetDegrees > 1f) _flexLeftTarget = leftTargetDegrees;
    }

    public void SetMeasuredArms(bool measureRight, bool measureLeft)
    {
        _measureRight = measureRight;
        _measureLeft = measureLeft;
        // Ölçülmeyen kol bölgesini kapat — landmark kopya/filtre/sürüş yok
        regionMask.rightArm = measureRight;
        regionMask.leftArm = measureLeft;
        ApplyArcVisibility();
    }

    /// <summary>
    /// 'Dik dur' uyarısı: başlayınca yön bir kez kilitlenir, dik durana kadar sabit eğik kalır.
    /// Uyarı bitince model dikleşir. Kare kare landmark takip edilmez → titreme yok.
    /// </summary>
    public void SetCompensationLeanVisual(bool warnActive)
    {
        if (warnActive && !_compensationWarnActive)
            _leanHoldLatched = false; // yükselen kenar: yönü yeniden örnekle
        if (!warnActive)
        {
            _leanHoldLatched = false;
            _heldLeanSignedDeg = 0f;
        }
        _compensationWarnActive = warnActive;
    }

    /// <summary>Seans öncesi örnek animasyon: pose/yay kapalı, fleksiyon açıları açık.</summary>
    public bool IsExampleDemoMode => exampleDemoMode;

    public void SetExampleDemoMode(bool enabled)
    {
        exampleDemoMode = enabled;
        if (enabled)
        {
            while (_queue.TryDequeue(out _)) { }
            _hasPose = false;
        }
        ApplyArcVisibility();
    }

    /// <summary>Seans bölgesine göre tek eklem yayı (omuz / dirsek / kalça / ayak bileği).</summary>
    public void SetArcRegion(BodyRegionId region)
    {
        _arcRegion = region;
        ApplyArcVisibility();
    }

    public void BindMannequin(ProceduralMannequin mannequin)
    {
        _mannequin = mannequin;
        EnsureFilters();
    }

    public bool BindHumanoid(Animator animator)
    {
        _humanoidBound = false;
        _animator = animator;
        if (_animator == null || !_animator.isHuman)
        {
            Debug.LogWarning("[AvatarBodyDriver] Animator Humanoid değil.");
            return false;
        }

        _root = _animator.transform;
        _animator.enabled = true;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.applyRootMotion = false;
        _animator.runtimeAnimatorController = null;
        _animator.Update(0f);

        CacheBone(ref _hips, _animator.GetBoneTransform(HumanBodyBones.Hips),
            _animator.GetBoneTransform(HumanBodyBones.Spine));
        CacheBone(ref _spine, _animator.GetBoneTransform(HumanBodyBones.Spine), null);
        Transform chest = _animator.GetBoneTransform(HumanBodyBones.Chest)
                          ?? _animator.GetBoneTransform(HumanBodyBones.UpperChest);
        CacheBone(ref _chest, chest, null);
        CacheBone(ref _head, _animator.GetBoneTransform(HumanBodyBones.Head), null);

        CacheBone(ref _lUpper, _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
            _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm));
        CacheBone(ref _lLower, _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
            _animator.GetBoneTransform(HumanBodyBones.LeftHand));
        CacheBone(ref _rUpper, _animator.GetBoneTransform(HumanBodyBones.RightUpperArm),
            _animator.GetBoneTransform(HumanBodyBones.RightLowerArm));
        CacheBone(ref _rLower, _animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
            _animator.GetBoneTransform(HumanBodyBones.RightHand));

        CacheBone(ref _lULeg, _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),
            _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg));
        CacheBone(ref _lLLeg, _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
            _animator.GetBoneTransform(HumanBodyBones.LeftFoot));
        CacheBone(ref _rULeg, _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg),
            _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg));
        CacheBone(ref _rLLeg, _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
            _animator.GetBoneTransform(HumanBodyBones.RightFoot));

        // Animator AÇIK kalır — Optimize Game Objects + skinning için gerekli.
        // Controller yok; LateUpdate kemik yazar.
        _mannequin = null;
        _humanoidBound = _lUpper.ready && _rUpper.ready;
        _loggedFirstDrive = false;
        EnsureFilters();
        ResetDrivenBonesToRest();

        if (!_humanoidBound)
            Debug.LogWarning("[AvatarBodyDriver] Kol kemikleri alınamadı.");
        else
        {
            EnsureArcIndicator();
            EnsureHipKneeArcIndicator();
            EnsureElbowArcIndicator();
            EnsureAnkleArcIndicator();
            ApplyArcVisibility();
            Debug.Log("[AvatarBodyDriver] Bind OK — açı + landmark sürüşü + bölge yayları.");
        }

        return _humanoidBound;
    }

    private void EnsureArcIndicator()
    {
        if (_arcIndicator == null)
            _arcIndicator = GetComponent<ShoulderFlexionArcIndicator>();
        if (_arcIndicator == null)
            _arcIndicator = gameObject.AddComponent<ShoulderFlexionArcIndicator>();

        Transform rArm = _rUpper.ready ? _rUpper.bone : null;
        Transform lArm = _lUpper.ready ? _lUpper.bone : null;
        Transform rElbow = _rLower.ready ? _rLower.bone : null;
        Transform lElbow = _lLower.ready ? _lLower.bone : null;
        // Kalça eklemi = UpperLeg kökü (omuz→kalça vektörü yay başlangıcı)
        Transform rHip = _animator != null ? _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) : null;
        Transform lHip = _animator != null ? _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) : null;
        if (rHip == null && _hips.ready) rHip = _hips.bone;
        if (lHip == null && _hips.ready) lHip = _hips.bone;

        _arcIndicator.Bind(rArm, lArm, rElbow, lElbow, rHip, lHip, _root);
        _arcIndicator.SetRaisePlane(raisePlane);
        ApplyArcVisibility();
    }

    /// <summary>Omuz yayına dokunmadan kalça–diz (uyluk) yayı — koltuk altı bölgesi.</summary>
    private void EnsureHipKneeArcIndicator()
    {
        if (_hipKneeArcIndicator == null)
            _hipKneeArcIndicator = GetComponent<HipKneeArcIndicator>();
        if (_hipKneeArcIndicator == null)
            _hipKneeArcIndicator = gameObject.AddComponent<HipKneeArcIndicator>();

        Transform rHip = _rULeg.ready ? _rULeg.bone : null;
        Transform lHip = _lULeg.ready ? _lULeg.bone : null;
        Transform rKnee = _rLLeg.ready ? _rLLeg.bone : null;
        Transform lKnee = _lLLeg.ready ? _lLLeg.bone : null;
        if (rHip == null && _animator != null)
            rHip = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        if (lHip == null && _animator != null)
            lHip = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        if (rKnee == null && _animator != null)
            rKnee = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        if (lKnee == null && _animator != null)
            lKnee = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);

        _hipKneeArcIndicator.Bind(rHip, lHip, rKnee, lKnee, _root);
        _hipKneeArcIndicator.SetRaisePlane(raisePlane);
        ApplyArcVisibility();
    }

    private void EnsureElbowArcIndicator()
    {
        if (_elbowArcIndicator == null)
            _elbowArcIndicator = gameObject.AddComponent<JointHingeArcIndicator>();
        _elbowArcIndicator.ConfigureName("ElbowArc");

        Transform rProx = _rUpper.ready ? _rUpper.bone : null;
        Transform lProx = _lUpper.ready ? _lUpper.bone : null;
        Transform rHinge = _rLower.ready ? _rLower.bone : null;
        Transform lHinge = _lLower.ready ? _lLower.bone : null;
        Transform rHand = _animator != null ? _animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        Transform lHand = _animator != null ? _animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        _elbowArcIndicator.Bind(rProx, lProx, rHinge, lHinge, rHand, lHand, _root);
        ApplyArcVisibility();
    }

    private void EnsureAnkleArcIndicator()
    {
        if (_ankleArcIndicator == null)
            _ankleArcIndicator = gameObject.AddComponent<JointHingeArcIndicator>();
        _ankleArcIndicator.ConfigureName("AnkleArc");

        Transform rKnee = _rLLeg.ready ? _rLLeg.bone : null;
        Transform lKnee = _lLLeg.ready ? _lLLeg.bone : null;
        Transform rFoot = _animator != null ? _animator.GetBoneTransform(HumanBodyBones.RightFoot) : null;
        Transform lFoot = _animator != null ? _animator.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
        Transform rToes = _animator != null ? _animator.GetBoneTransform(HumanBodyBones.RightToes) : null;
        Transform lToes = _animator != null ? _animator.GetBoneTransform(HumanBodyBones.LeftToes) : null;
        if (rToes == null) rToes = rFoot;
        if (lToes == null) lToes = lFoot;
        _ankleArcIndicator.Bind(rKnee, lKnee, rFoot, lFoot, rToes, lToes, _root);
        ApplyArcVisibility();
    }

    private void ApplyArcVisibility()
    {
        RadialArcKind kind = exampleDemoMode
            ? RadialArcKind.None
            : ExerciseCatalog.GetRadialArcKind(_arcRegion);
        bool r = _measureRight;
        bool l = _measureLeft;
        if (_arcIndicator != null)
            _arcIndicator.SetArmActive(kind == RadialArcKind.Shoulder && r, kind == RadialArcKind.Shoulder && l);
        if (_elbowArcIndicator != null)
            _elbowArcIndicator.SetSideActive(kind == RadialArcKind.Elbow && r, kind == RadialArcKind.Elbow && l);
        if (_hipKneeArcIndicator != null)
            _hipKneeArcIndicator.SetLegActive(kind == RadialArcKind.Hip && r, kind == RadialArcKind.Hip && l);
        if (_ankleArcIndicator != null)
            _ankleArcIndicator.SetSideActive(kind == RadialArcKind.Ankle && r, kind == RadialArcKind.Ankle && l);
    }

    private void CacheBone(ref BoneCache cache, Transform bone, Transform childHint)
    {
        cache = default;
        if (bone == null) return;

        cache.bone = bone;
        cache.parent = bone.parent;
        cache.restLocal = bone.localRotation;

        Vector3 worldDir;
        if (childHint != null)
            worldDir = childHint.position - bone.position;
        else if (bone.childCount > 0)
            worldDir = bone.GetChild(0).position - bone.position;
        else
            worldDir = bone.up;

        if (worldDir.sqrMagnitude < 1e-8f) worldDir = bone.up;
        worldDir.Normalize();

        if (cache.parent != null)
            cache.bindLocalDir = cache.parent.InverseTransformDirection(worldDir);
        else
            cache.bindLocalDir = worldDir;

        if (cache.bindLocalDir.sqrMagnitude < 1e-8f)
            cache.bindLocalDir = Vector3.up;
        else
            cache.bindLocalDir.Normalize();

        cache.ready = true;
    }

    public void ClearBinding()
    {
        if (_arcIndicator != null)
            _arcIndicator.Clear();
        if (_hipKneeArcIndicator != null)
            _hipKneeArcIndicator.Clear();
        if (_elbowArcIndicator != null)
            _elbowArcIndicator.Clear();
        if (_ankleArcIndicator != null)
            _ankleArcIndicator.Clear();
        _humanoidBound = false;
        _animator = null;
        _root = null;
        _mannequin = null;
    }

    public void EnqueuePose(NormalizedLandmarks landmarks, long timestampMs)
    {
        if (landmarks.landmarks == null || landmarks.landmarks.Count < LandmarkCount) return;

        PoseSample sample = default;
        sample.timestampSeconds = timestampMs > 0 ? timestampMs * 0.001f : 0f;
        sample.valid = true;

        // cmd: yalnızca ölçülen kol + gövde — kapalı kol CPU yormasın
        bool needRightArm = regionMask.rightArm && _measureRight;
        bool needLeftArm = regionMask.leftArm && _measureLeft;
        bool needTorso = regionMask.torso;
        bool needArms = needRightArm || needLeftArm || needTorso;
        bool needWrists = regionMask.forearms && (needRightArm || needLeftArm);
        bool needLegs = regionMask.legs;
        // Baş visibility kapısı: mask.head false olsa bile burun+kulak örnekle (yan profil)
        bool needHeadLandmarks = true;

        if (needHeadLandmarks)
        {
            sample.nose = Copy(landmarks.landmarks[Nose]);
            if (landmarks.landmarks.Count > RightEar)
            {
                sample.lEar = Copy(landmarks.landmarks[LeftEar]);
                sample.rEar = Copy(landmarks.landmarks[RightEar]);
            }
        }
        if (needArms || needHeadLandmarks)
        {
            // Omuz genişliği / gövde için her iki omuz; dirsek yalnız ölçülen kol
            sample.lShoulder = Copy(landmarks.landmarks[LeftShoulder]);
            sample.rShoulder = Copy(landmarks.landmarks[RightShoulder]);
        }
        if (needTorso || needRightArm || needLeftArm)
        {
            sample.lHip = Copy(landmarks.landmarks[LeftHip]);
            sample.rHip = Copy(landmarks.landmarks[RightHip]);
        }
        if (needLeftArm)
            sample.lElbow = Copy(landmarks.landmarks[LeftElbow]);
        if (needRightArm)
            sample.rElbow = Copy(landmarks.landmarks[RightElbow]);
        if (needWrists)
        {
            if (needLeftArm) sample.lWrist = Copy(landmarks.landmarks[LeftWrist]);
            if (needRightArm) sample.rWrist = Copy(landmarks.landmarks[RightWrist]);
        }
        if (needLegs)
        {
            sample.lKnee = Copy(landmarks.landmarks[LeftKnee]);
            sample.rKnee = Copy(landmarks.landmarks[RightKnee]);
            sample.lAnkle = Copy(landmarks.landmarks[LeftAnkle]);
            sample.rAnkle = Copy(landmarks.landmarks[RightAnkle]);
            if (!needArms)
            {
                sample.lHip = Copy(landmarks.landmarks[LeftHip]);
                sample.rHip = Copy(landmarks.landmarks[RightHip]);
            }
        }

        _queue.Enqueue(sample);
    }

    private void LateUpdate()
    {
        if (!_humanoidBound && _mannequin == null) return;

        if (exampleDemoMode)
        {
            // MediaPipe kuyruğunu tüket ama uygulama — hasta kopyası yok
            while (_queue.TryDequeue(out _)) { }
            _hasPose = false;
            if (_humanoidBound)
                DriveHumanoid();
            return;
        }

        PoseSample latest = default;
        bool got = false;
        while (_queue.TryDequeue(out var s))
        {
            latest = s;
            got = true;
        }

        if (got && latest.valid)
        {
            float t = latest.timestampSeconds > 0f ? latest.timestampSeconds : Time.time;
            for (int i = 0; i < LandmarkCount; i++)
                _valid[i] = false;

            bool needRightArm = regionMask.rightArm && _measureRight;
            bool needLeftArm = regionMask.leftArm && _measureLeft;
            bool needTorso = regionMask.torso;
            bool needArms = needRightArm || needLeftArm || needTorso;
            bool needWrists = regionMask.forearms && (needRightArm || needLeftArm);
            bool needLegs = regionMask.legs;
            // Konum/seans baş kapısı: her zaman burun+kulak
            bool needHeadLandmarks = true;

            if (needHeadLandmarks)
            {
                if (latest.nose.copied)
                    ApplyLandmark(Nose, latest.nose, t, headLandmarkVisibilityThreshold);
                if (latest.lEar.copied)
                    ApplyLandmark(LeftEar, latest.lEar, t, headLandmarkVisibilityThreshold);
                if (latest.rEar.copied)
                    ApplyLandmark(RightEar, latest.rEar, t, headLandmarkVisibilityThreshold);
            }
            if (needArms || needHeadLandmarks)
            {
                ApplyLandmark(LeftShoulder, latest.lShoulder, t);
                ApplyLandmark(RightShoulder, latest.rShoulder, t);
            }
            if (needTorso || needRightArm || needLeftArm)
            {
                ApplyLandmark(LeftHip, latest.lHip, t);
                ApplyLandmark(RightHip, latest.rHip, t);
            }
            if (needLeftArm)
                ApplyLandmark(LeftElbow, latest.lElbow, t);
            if (needRightArm)
                ApplyLandmark(RightElbow, latest.rElbow, t);
            if (needWrists)
            {
                if (needLeftArm) ApplyLandmark(LeftWrist, latest.lWrist, t);
                if (needRightArm) ApplyLandmark(RightWrist, latest.rWrist, t);
            }
            if (needLegs)
            {
                if (!needArms)
                {
                    ApplyLandmark(LeftHip, latest.lHip, t);
                    ApplyLandmark(RightHip, latest.rHip, t);
                }
                ApplyLandmark(LeftKnee, latest.lKnee, t);
                ApplyLandmark(RightKnee, latest.rKnee, t);
                ApplyLandmark(LeftAnkle, latest.lAnkle, t);
                ApplyLandmark(RightAnkle, latest.rAnkle, t);
            }

            EvaluateRegionVisibility();
            _hasPose = true;
        }

        if (_humanoidBound)
        {
            DriveHumanoid();
            UpdateArcIndicator();
        }
        else if (got)
            DriveMannequin();
    }

    private void UpdateArcIndicator()
    {
        if (exampleDemoMode) return;

        RadialArcKind kind = ExerciseCatalog.GetRadialArcKind(_arcRegion);

        if (kind == RadialArcKind.Shoulder && _arcIndicator != null)
        {
            _arcIndicator.SetRaisePlane(raisePlane);
            _arcIndicator.UpdateArcs(
                _measureRight && _flexRightOk, _flexRightDeg, _flexRightTarget,
                _measureLeft && _flexLeftOk, _flexLeftDeg, _flexLeftTarget);
        }

        if (kind == RadialArcKind.Hip && _hipKneeArcIndicator != null)
        {
            _hipKneeArcIndicator.SetRaisePlane(raisePlane);
            _hipKneeArcIndicator.UpdateArcs(
                _measureRight, _flexRightTarget,
                _measureLeft, _flexLeftTarget);
        }

        if (kind == RadialArcKind.Elbow && _elbowArcIndicator != null)
        {
            _elbowArcIndicator.UpdateArcs(
                _measureRight, _flexRightTarget,
                _measureLeft, _flexLeftTarget);
        }

        if (kind == RadialArcKind.Ankle && _ankleArcIndicator != null)
        {
            _ankleArcIndicator.UpdateArcs(
                _measureRight, _flexRightTarget,
                _measureLeft, _flexLeftTarget);
        }
    }

    private void EvaluateRegionVisibility()
    {
        // Klinik kol: kalça+omuz+dirsek | Görsel kol: omuz+dirsek (hip olmadan da kemiği sür)
        bool rArmClinical = _valid[RightHip] && _valid[RightShoulder] && _valid[RightElbow];
        bool lArmClinical = _valid[LeftHip] && _valid[LeftShoulder] && _valid[LeftElbow];
        bool rArmVisual = _valid[RightShoulder] && _valid[RightElbow];
        bool lArmVisual = _valid[LeftShoulder] && _valid[LeftElbow];

        _visibility.rightArm = rArmClinical;
        _visibility.leftArm = lArmClinical;
        // Görsel bayrakları forearms alanına geçici koyma — ayrı tut
        _visibility.rightForearm = rArmVisual;
        _visibility.leftForearm = lArmVisual;
        _visibility.torso = _valid[LeftHip] && _valid[RightHip]
                            && _valid[LeftShoulder] && _valid[RightShoulder];
        _visibility.legs = false;
        _visibility.head = false;
        if (regionMask.legs)
        {
            _visibility.legs = _valid[LeftHip] && _valid[RightHip]
                               && _valid[LeftKnee] && _valid[RightKnee]
                               && _valid[LeftAnkle] && _valid[RightAnkle];
        }
        if (regionMask.head)
            _visibility.head = (_valid[Nose] || _valid[LeftEar] || _valid[RightEar])
                               && _valid[LeftShoulder] && _valid[RightShoulder];
        else
            _visibility.head = _valid[Nose] || _valid[LeftEar] || _valid[RightEar];
    }

    private void DriveHumanoid()
    {
        ResetDrivenBonesToRest();

        bool drove = false;

        // Gövde: uyarıda sabit yana yatış (kilitli); uyarı yoksa dik
        if (driveTorsoLean && !exampleDemoMode)
            DriveCompensationLeanHeld();

        // Ölçülen kolun açısı anatomik kemikte sürülür (ön kamera MP L/R düzeltmesi PhysioAnalyzer'da).
        // Kapalı kol: ne açı ne landmark — CPU + yanlış hareket yok.
        if (preferFlexionAngles && _hasFlexionAngles)
        {
            if (_flexRightOk)
            {
                drove |= DriveOneArm(
                    true, false, true, _flexRightDeg,
                    RightShoulder, RightElbow, ref _rUpper, boneIsCharacterRight: true);
            }
            if (_flexLeftOk)
            {
                drove |= DriveOneArm(
                    true, false, true, _flexLeftDeg,
                    LeftShoulder, LeftElbow, ref _lUpper, boneIsCharacterRight: false);
            }
        }
        else
        {
            if (_measureRight)
            {
                drove |= DriveOneArm(
                    regionMask.rightArm, _visibility.rightForearm, _flexRightOk, _flexRightDeg,
                    RightShoulder, RightElbow, ref _rUpper, boneIsCharacterRight: true);
            }
            if (_measureLeft)
            {
                drove |= DriveOneArm(
                    regionMask.leftArm, _visibility.leftForearm, _flexLeftOk, _flexLeftDeg,
                    LeftShoulder, LeftElbow, ref _lUpper, boneIsCharacterRight: false);
            }
        }

        if (!_loggedFirstDrive && (_hasPose || _hasFlexionAngles))
        {
            _loggedFirstDrive = true;
            Debug.Log(drove
                ? "[AvatarBodyDriver] Kol sürüşü: anatomik (sağ→sağ, sol→sol)."
                : "[AvatarBodyDriver] Pose/açı var ama kol sürülmedi.");
        }
    }

    /// <summary>
    /// Kompansasyon uyarısı süresince sabit bel eğimi.
    /// Uyarı açılınca yön bir kez alınır ve kilitlenir; dik durunca 0'a döner.
    /// SaMD Class B: görsel geri bildirim; klinik eşik PhysioAnalyzer'da.
    /// </summary>
    private void DriveCompensationLeanHeld()
    {
        if (torsoLeanOnlyDuringCompensationWarn)
        {
            if (_compensationWarnActive)
                TryLatchLeanDirectionOnce();
            else
            {
                _leanHoldLatched = false;
                _heldLeanSignedDeg = 0f;
            }

            float target = _compensationWarnActive && _leanHoldLatched ? _heldLeanSignedDeg : 0f;
            float step = leanReturnDegreesPerSecond * Time.deltaTime;
            // Uyarıya girerken hızlı otur; çıkarken kontrollü dikleş
            if (Mathf.Abs(target) > 0.01f)
                _appliedLeanDeg = target;
            else
                _appliedLeanDeg = Mathf.MoveTowards(_appliedLeanDeg, 0f, step);
        }
        else
        {
            // Eski davranış kapısı kapalıysa: canlı pose (nadiren kullanılır)
            DriveTorsoLeanFromPoseLive();
            return;
        }

        ApplyHeldTorsoLeanDegrees(_appliedLeanDeg);
    }

    private void TryLatchLeanDirectionOnce()
    {
        if (_leanHoldLatched) return;
        if (!TrySampleSignedLeanDegrees(out float signed)) return;

        float sign = Mathf.Sign(signed);
        if (Mathf.Abs(sign) < 0.01f)
            return;

        float hold = Mathf.Clamp(compensationHoldLeanDegrees, 4f, maxTorsoLeanVisualDegrees);
        _heldLeanSignedDeg = sign * hold;
        _leanHoldLatched = true;
    }

    private bool TrySampleSignedLeanDegrees(out float signedLeanDeg)
    {
        signedLeanDeg = 0f;
        if (!regionMask.torso || !_visibility.torso) return false;

        Vector3 midHip = 0.5f * (_filtered[LeftHip] + _filtered[RightHip]);
        Vector3 midShoulder = 0.5f * (_filtered[LeftShoulder] + _filtered[RightShoulder]);
        Vector2 spineXy = new Vector2(midShoulder.x - midHip.x, midShoulder.y - midHip.y);
        if (spineXy.sqrMagnitude < 1e-6f) return false;

        float leanDeg = Mathf.Atan2(spineXy.x, spineXy.y) * Mathf.Rad2Deg;
        if (Mathf.Abs(leanDeg) < torsoLeanDeadbandDegrees)
        {
            // Omurga net değilse kalça roll yönüne bak
            Vector2 hipLine = new Vector2(
                _filtered[RightHip].x - _filtered[LeftHip].x,
                _filtered[RightHip].y - _filtered[LeftHip].y);
            if (hipLine.sqrMagnitude < 1e-6f) return false;
            float hipRollDeg = Mathf.Atan2(hipLine.y, Mathf.Abs(hipLine.x) + 1e-5f) * Mathf.Rad2Deg;
            if (Mathf.Abs(hipRollDeg) < torsoLeanDeadbandDegrees) return false;
            signedLeanDeg = hipRollDeg;
            return true;
        }

        signedLeanDeg = leanDeg;
        return true;
    }

    private void ApplyHeldTorsoLeanDegrees(float leanDeg)
    {
        if (_root == null) return;
        if (Mathf.Abs(leanDeg) < 0.08f) return;

        float hipsDeg = Mathf.Clamp(leanDeg, -maxTorsoLeanVisualDegrees, maxTorsoLeanVisualDegrees);
        float upperDeg = hipsDeg;
        Vector3 leanAxis = _root.forward;
        ApplyWorldAxisLean(ref _hips, leanAxis, -hipsDeg);
        ApplyWorldAxisLean(ref _spine, leanAxis, -upperDeg * 0.85f);
        ApplyWorldAxisLean(ref _chest, leanAxis, -upperDeg);
    }

    /// <summary>Kapı kapalıyken canlı lean (debug / eski davranış).</summary>
    private void DriveTorsoLeanFromPoseLive()
    {
        if (!regionMask.torso || !_visibility.torso) return;
        if (_root == null) return;
        if (!TrySampleSignedLeanDegrees(out float leanDeg)) return;

        Vector2 hipLine = new Vector2(
            _filtered[RightHip].x - _filtered[LeftHip].x,
            _filtered[RightHip].y - _filtered[LeftHip].y);
        float hipRollDeg = 0f;
        if (hipLine.sqrMagnitude > 1e-6f)
            hipRollDeg = Mathf.Atan2(hipLine.y, Mathf.Abs(hipLine.x) + 1e-5f) * Mathf.Rad2Deg;
        if (Mathf.Abs(hipRollDeg) < torsoLeanDeadbandDegrees)
            hipRollDeg = 0f;

        float hipsDeg = Mathf.Clamp(
            (leanDeg * 0.7f + hipRollDeg * hipRollVisualGain) * torsoLeanVisualGain,
            -maxTorsoLeanVisualDegrees, maxTorsoLeanVisualDegrees);
        float upperDeg = Mathf.Clamp(
            leanDeg * torsoLeanVisualGain,
            -maxTorsoLeanVisualDegrees, maxTorsoLeanVisualDegrees);
        ApplyHeldTorsoLeanDegrees(hipsDeg);
        if (Mathf.Abs(upperDeg - hipsDeg) > 0.01f)
        {
            Vector3 leanAxis = _root.forward;
            ApplyWorldAxisLean(ref _spine, leanAxis, -upperDeg * 0.85f);
            ApplyWorldAxisLean(ref _chest, leanAxis, -upperDeg);
        }
    }

    private static void ApplyWorldAxisLean(ref BoneCache cache, Vector3 worldAxis, float degrees)
    {
        if (!cache.ready || cache.bone == null) return;
        if (Mathf.Abs(degrees) < 0.08f) return;
        if (worldAxis.sqrMagnitude < 1e-8f) return;

        Quaternion lean = Quaternion.AngleAxis(degrees, worldAxis.normalized);
        Quaternion restWorld = cache.parent != null
            ? cache.parent.rotation * cache.restLocal
            : cache.restLocal;
        cache.bone.rotation = lean * restWorld;
    }

    private bool DriveOneArm(
        bool regionOn, bool visualOk, bool flexOk, float flexDeg,
        int shoulderIdx, int elbowIdx, ref BoneCache bone, bool boneIsCharacterRight)
    {
        if (!regionOn) return false;

        // Slider ile aynı kaynak önce — yanlış kola landmark aim bağlanmasın
        if (preferFlexionAngles && _hasFlexionAngles && flexOk)
        {
            ApplyElevationToUpperArm(ref bone, flexDeg, isRight: boneIsCharacterRight);
            return true;
        }

        if (visualOk)
        {
            ApplyBoneAimFromLandmarks(ref bone, _filtered[shoulderIdx], _filtered[elbowIdx]);
            return true;
        }

        if (_hasFlexionAngles && flexOk)
        {
            ApplyElevationToUpperArm(ref bone, flexDeg, isRight: boneIsCharacterRight);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Physio açısını dikey kaldırma yönüne çevirir (yere paralel dönme yok).
    /// 0° aşağı, 90° T-pose, 180° yukarı.
    /// </summary>
    private void ApplyElevationToUpperArm(ref BoneCache cache, float degrees, bool isRight)
    {
        if (!cache.ready || cache.bone == null) return;

        float a = Mathf.Clamp(degrees, 0f, 180f) * Mathf.Deg2Rad;
        float sinA = Mathf.Sin(a);
        float cosA = Mathf.Cos(a);

        Vector3 charLocal;
        if (raisePlane == ArmRaisePlane.Sagittal)
        {
            // Öne fleksiyon: aşağı → ileri → yukarı (karakter lokal Z ileri varsayımı)
            float side = isRight ? 0.2f : -0.2f;
            charLocal = new Vector3(side * sinA, -cosA, sinA);
        }
        else
        {
            // Coronal / lateral raise: aşağı → yana → yukarı
            float side = isRight ? 1f : -1f;
            charLocal = new Vector3(side * sinA, -cosA, 0f);
        }

        if (charLocal.sqrMagnitude < 1e-8f) return;
        charLocal.Normalize();

        Transform root = _root != null ? _root : cache.bone;
        Vector3 worldDir = root.TransformDirection(charLocal);
        AimBoneAlongWorldDir(ref cache, worldDir);
    }

    private void ApplyBoneAimFromLandmarks(ref BoneCache cache, Vector3 lmFrom, Vector3 lmTo)
    {
        if (!cache.ready || cache.bone == null) return;

        Vector3 lmDir = lmTo - lmFrom;
        if (lmDir.sqrMagnitude < 1e-8f) return;
        lmDir.Normalize();

        // Landmark: X yan, Y dikey (yukarı+), Z derinlik — karakter lokaline map
        Transform root = _root != null ? _root : cache.bone;
        Vector3 worldDir = root.TransformDirection(lmDir);
        AimBoneAlongWorldDir(ref cache, worldDir);
    }

    private static void AimBoneAlongWorldDir(ref BoneCache cache, Vector3 worldDir)
    {
        if (!cache.ready || cache.bone == null) return;
        if (worldDir.sqrMagnitude < 1e-8f) return;
        worldDir.Normalize();

        if (cache.parent != null)
        {
            Vector3 localDir = cache.parent.InverseTransformDirection(worldDir);
            if (localDir.sqrMagnitude < 1e-8f) return;
            localDir.Normalize();
            cache.bone.localRotation = Quaternion.FromToRotation(cache.bindLocalDir, localDir) * cache.restLocal;
        }
        else
        {
            cache.bone.rotation = Quaternion.FromToRotation(cache.bindLocalDir, worldDir) * cache.restLocal;
        }
    }

    private void ResetDrivenBonesToRest()
    {
        // cmd: kapalı bölgelerde gereksiz localRotation yazma
        if (regionMask.leftArm)
        {
            ResetBone(ref _lUpper);
            ResetBone(ref _lLower);
        }
        if (regionMask.rightArm)
        {
            ResetBone(ref _rUpper);
            ResetBone(ref _rLower);
        }
        if (regionMask.torso)
        {
            ResetBone(ref _hips);
            ResetBone(ref _spine);
            ResetBone(ref _chest);
        }
        if (regionMask.head)
            ResetBone(ref _head);
        if (regionMask.legs)
        {
            ResetBone(ref _lULeg);
            ResetBone(ref _lLLeg);
            ResetBone(ref _rULeg);
            ResetBone(ref _rLLeg);
        }
    }

    private static void ResetBone(ref BoneCache cache)
    {
        if (cache.ready && cache.bone != null)
            cache.bone.localRotation = cache.restLocal;
    }

    private void DriveMannequin()
    {
        if (_mannequin == null) return;

        if (regionMask.leftArm && _visibility.leftForearm)
        {
            _mannequin.LeftShoulder.position = _filtered[LeftShoulder];
            _mannequin.LeftElbow.position = _filtered[LeftElbow];
        }
        if (regionMask.rightArm && _visibility.rightForearm)
        {
            _mannequin.RightShoulder.position = _filtered[RightShoulder];
            _mannequin.RightElbow.position = _filtered[RightElbow];
        }
        if (regionMask.torso && _visibility.torso)
            _mannequin.Hips.position = 0.5f * (_filtered[LeftHip] + _filtered[RightHip]);
    }

    private void ApplyLandmark(int index, LandmarkSample raw, float timestamp)
    {
        ApplyLandmark(index, raw, timestamp, landmarkVisibilityThreshold);
    }

    private void ApplyLandmark(int index, LandmarkSample raw, float timestamp, float visibilityThreshold)
    {
        if (raw.hasVisibility && raw.visibility < visibilityThreshold)
        {
            _valid[index] = false;
            return;
        }

        _norm01X[index] = raw.x;
        _norm01Y[index] = raw.y;

        float x = (raw.x - 0.5f) * horizontalScale;
        if (mirrorX) x = -x;
        float y = (0.5f - raw.y) * verticalScale;
        float z = -raw.z * depthScale;

        Vector2 xy = _filtersXy[index].Filter(x, y, timestamp);
        float zf = _filtersZ[index].Filter(z, timestamp);
        _filtered[index] = new Vector3(xy.x, xy.y, zf);
        _valid[index] = true;
    }

    private void EnsureFilters()
    {
        if (_filtersConfigured) return;
        for (int i = 0; i < LandmarkCount; i++)
        {
            _filtersXy[i].Configure(filterMinCutoff, filterBeta, filterDCutoff);
            _filtersZ[i].Configure(filterMinCutoff, filterBeta, filterDCutoff);
        }
        _filtersConfigured = true;
    }

    private static LandmarkSample Copy(NormalizedLandmark lm)
    {
        LandmarkSample p;
        p.x = lm.x;
        p.y = lm.y;
        p.z = lm.z;
        p.hasVisibility = lm.visibility.HasValue;
        p.visibility = lm.visibility.HasValue ? lm.visibility.Value : 1f;
        p.copied = true;
        return p;
    }
}
