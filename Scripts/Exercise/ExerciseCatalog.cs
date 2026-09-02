using System;

/// <summary>
/// Kamera / sahne protokolü. Yeni hareket eklerken <see cref="ExerciseDefinition"/> üzerinde seçilir.
/// </summary>
public enum CameraProtocol : byte
{
    Frontal = 0,
    SideProfile = 1
}

/// <summary>Avatar kol kaldırma düzlemi (MovementId if’lerinden bağımsız).</summary>
public enum MovementRaisePlane : byte
{
    None = 0,
    Sagittal = 1,
    Coronal = 2
}

/// <summary>
/// Analiz/factory ailesi. Aynı pipeline’ı paylaşan hareketler aynı ailede toplanır.
/// Yeni canlı hareket: aileye ekle veya yeni aile + analyzer/rep policy yaz.
/// </summary>
public enum MovementAnalysisFamily : byte
{
    None = 0,
    /// <summary>Omuz fleksiyonu + abdüksiyon (ortak elevasyon job/analyzer).</summary>
    ShoulderElevation = 1,
    /// <summary>Dirsek menteşe (gelecek — IMovementAnalyzer impl gerekli).</summary>
    ElbowHinge = 2,
    Neck = 3,
    LowerLimb = 4
}

/// <summary>Avatar açı uygulama yolu.</summary>
public enum MovementAvatarDriver : byte
{
    None = 0,
    ShoulderElevation = 1,
    ElbowHinge = 2
}

/// <summary>Avatar radial yay kanalı — seans vücut bölgesine göre tek eklem.</summary>
public enum RadialArcKind : byte
{
    None = 0,
    Shoulder = 1,
    Elbow = 2,
    Hip = 3,
    Ankle = 4
}

/// <summary>
/// Yerel egzersiz kataloğu: bölge → hareket + protokol meta.
/// SaMD Class B: seçim karar-destek bağlamıdır; teşhis değildir. KVKK: yalnızca yerel ID saklanır.
///
/// Yeni hareket ekleme (check list):
/// 1) <see cref="MovementId"/> değeri ekle (çakışmayan int)
/// 2) <see cref="ExerciseCatalog"/> All[] satırı: LocKey, Implemented, Camera, RaisePlane, Family, Avatar, Sequential, Simultaneous, ProtocolLocKey
/// 3) Family yeni ise: IMovementAnalyzer + IRepPolicy + <see cref="MovementAnalyzerFactory"/> case
/// 4) Loc.cs’e exercise.move.* (+ isteğe bağlı report.protocol.*)
/// 5) Implemented=true yap; geçmiş filtresi otomatik (CopyLiveMovements)
/// </summary>
public enum BodyRegionId
{
    Shoulder = 0,
    Arm = 1,
    Elbow = 2,
    Neck = 3,
    Leg = 4,
    Ankle = 5
}

public enum MovementId
{
    /// <summary>Legacy varsayılan (eski kayıtlar movementId=0). Klinik geçmiş: omuz abdüksiyonu.</summary>
    ShoulderAbduction = 0,
    /// <summary>Yan profil protokolü.</summary>
    ShoulderFlexion = 1,
    ShoulderExternalRotation = 2,
    ShoulderInternalRotation = 3,
    ArmElevation = 10,
    ArmHorizontalAdduction = 11,
    ElbowFlexion = 20,
    ElbowExtension = 21,
    NeckFlexion = 30,
    NeckRotation = 31,
    NeckLateralFlexion = 32,
    HipFlexion = 40,
    HipAbduction = 41,
    AnkleDorsiflexion = 50,
    AnklePlantarflexion = 51
}

public readonly struct ExerciseDefinition
{
    public readonly MovementId MovementId;
    public readonly BodyRegionId RegionId;
    public readonly string LocKey;
    public readonly bool Implemented;
    public readonly CameraProtocol Camera;
    public readonly MovementRaisePlane RaisePlane;
    public readonly MovementAnalysisFamily AnalysisFamily;
    public readonly MovementAvatarDriver AvatarDriver;
    public readonly bool AllowsBilateralSequential;
    /// <summary>İki kol aynı karede ölçülür (örn. önden abdüksiyon). Fleksiyon yan profilde false.</summary>
    public readonly bool AllowsSimultaneousBilateral;
    /// <summary>Rapor protokol satırı Loc key (örn. report.protocol.side). Boşsa kamera varsayılanı.</summary>
    public readonly string ProtocolLocKey;

    public ExerciseDefinition(
        MovementId movementId,
        BodyRegionId regionId,
        string locKey,
        bool implemented,
        CameraProtocol camera = CameraProtocol.Frontal,
        MovementRaisePlane raisePlane = MovementRaisePlane.None,
        MovementAnalysisFamily analysisFamily = MovementAnalysisFamily.None,
        MovementAvatarDriver avatarDriver = MovementAvatarDriver.None,
        bool allowsBilateralSequential = false,
        bool allowsSimultaneousBilateral = false,
        string protocolLocKey = null)
    {
        MovementId = movementId;
        RegionId = regionId;
        LocKey = locKey;
        Implemented = implemented;
        Camera = camera;
        RaisePlane = raisePlane;
        AnalysisFamily = analysisFamily;
        AvatarDriver = avatarDriver;
        AllowsBilateralSequential = allowsBilateralSequential;
        AllowsSimultaneousBilateral = allowsSimultaneousBilateral;
        ProtocolLocKey = protocolLocKey;
    }

    public bool UsesSideProfile => Camera == CameraProtocol.SideProfile;

    public string ResolveProtocolLocKey()
    {
        if (!string.IsNullOrEmpty(ProtocolLocKey))
            return ProtocolLocKey;
        return Camera == CameraProtocol.SideProfile
            ? "report.protocol.side"
            : "report.protocol.front";
    }

    public PoseRegionMask BuildMask()
    {
        switch (RegionId)
        {
            case BodyRegionId.Shoulder:
            case BodyRegionId.Arm:
                return PoseRegionMask.ShoulderFlexion();
            case BodyRegionId.Elbow:
                return new PoseRegionMask
                {
                    rightArm = true,
                    leftArm = true,
                    torso = true,
                    forearms = true,
                    legs = false,
                    head = false
                };
            case BodyRegionId.Neck:
                return new PoseRegionMask
                {
                    rightArm = false,
                    leftArm = false,
                    torso = true,
                    forearms = false,
                    legs = false,
                    head = true
                };
            case BodyRegionId.Leg:
            case BodyRegionId.Ankle:
                return new PoseRegionMask
                {
                    rightArm = false,
                    leftArm = false,
                    torso = true,
                    forearms = false,
                    legs = true,
                    head = false
                };
            default:
                return PoseRegionMask.ShoulderFlexion();
        }
    }
}

/// <summary>Statik katalog — heap allocation yalnızca UI yolunda (ForRegion / Live kopya).</summary>
public static class ExerciseCatalog
{
    public static readonly MovementId DefaultMovementId = MovementId.ShoulderFlexion;
    public static readonly BodyRegionId DefaultRegionId = BodyRegionId.Shoulder;

    private static readonly ExerciseDefinition[] All =
    {
        new ExerciseDefinition(
            MovementId.ShoulderFlexion, BodyRegionId.Shoulder, "exercise.move.shoulder.flexion", true,
            CameraProtocol.SideProfile, MovementRaisePlane.Sagittal,
            MovementAnalysisFamily.ShoulderElevation, MovementAvatarDriver.ShoulderElevation,
            allowsBilateralSequential: true, protocolLocKey: "report.protocol.side"),
        new ExerciseDefinition(
            MovementId.ShoulderAbduction, BodyRegionId.Shoulder, "exercise.move.shoulder.abduction", true,
            CameraProtocol.Frontal, MovementRaisePlane.Coronal,
            MovementAnalysisFamily.ShoulderElevation, MovementAvatarDriver.ShoulderElevation,
            allowsBilateralSequential: false, allowsSimultaneousBilateral: true,
            protocolLocKey: "report.protocol.front"),
        new ExerciseDefinition(
            MovementId.ShoulderExternalRotation, BodyRegionId.Shoulder, "exercise.move.shoulder.er", false,
            CameraProtocol.Frontal, MovementRaisePlane.None, MovementAnalysisFamily.None),
        new ExerciseDefinition(
            MovementId.ShoulderInternalRotation, BodyRegionId.Shoulder, "exercise.move.shoulder.ir", false,
            CameraProtocol.Frontal, MovementRaisePlane.None, MovementAnalysisFamily.None),

        new ExerciseDefinition(
            MovementId.ArmElevation, BodyRegionId.Arm, "exercise.move.arm.elevation", false,
            CameraProtocol.Frontal, MovementRaisePlane.Coronal, MovementAnalysisFamily.ShoulderElevation,
            MovementAvatarDriver.ShoulderElevation),
        new ExerciseDefinition(
            MovementId.ArmHorizontalAdduction, BodyRegionId.Arm, "exercise.move.arm.hadduction", false,
            CameraProtocol.Frontal, MovementRaisePlane.None, MovementAnalysisFamily.None),

        // Implemented=false: analyzer/factory hazır olunca true yap; filtre/UI otomatik açılır.
        new ExerciseDefinition(
            MovementId.ElbowFlexion, BodyRegionId.Elbow, "exercise.move.elbow.flexion", false,
            CameraProtocol.SideProfile, MovementRaisePlane.Sagittal,
            MovementAnalysisFamily.ElbowHinge, MovementAvatarDriver.ElbowHinge,
            allowsBilateralSequential: true, protocolLocKey: "report.protocol.side"),
        new ExerciseDefinition(
            MovementId.ElbowExtension, BodyRegionId.Elbow, "exercise.move.elbow.extension", false,
            CameraProtocol.SideProfile, MovementRaisePlane.Sagittal,
            MovementAnalysisFamily.ElbowHinge, MovementAvatarDriver.ElbowHinge,
            allowsBilateralSequential: true, protocolLocKey: "report.protocol.side"),

        new ExerciseDefinition(
            MovementId.NeckFlexion, BodyRegionId.Neck, "exercise.move.neck.flexion", false,
            CameraProtocol.SideProfile, MovementRaisePlane.None, MovementAnalysisFamily.Neck),
        new ExerciseDefinition(
            MovementId.NeckRotation, BodyRegionId.Neck, "exercise.move.neck.rotation", false,
            CameraProtocol.Frontal, MovementRaisePlane.None, MovementAnalysisFamily.Neck),
        new ExerciseDefinition(
            MovementId.NeckLateralFlexion, BodyRegionId.Neck, "exercise.move.neck.lateral", false,
            CameraProtocol.Frontal, MovementRaisePlane.None, MovementAnalysisFamily.Neck),

        new ExerciseDefinition(
            MovementId.HipFlexion, BodyRegionId.Leg, "exercise.move.leg.hipFlexion", false,
            CameraProtocol.SideProfile, MovementRaisePlane.None, MovementAnalysisFamily.LowerLimb),
        new ExerciseDefinition(
            MovementId.HipAbduction, BodyRegionId.Leg, "exercise.move.leg.hipAbduction", false,
            CameraProtocol.Frontal, MovementRaisePlane.None, MovementAnalysisFamily.LowerLimb),

        new ExerciseDefinition(
            MovementId.AnkleDorsiflexion, BodyRegionId.Ankle, "exercise.move.ankle.dorsi", false,
            CameraProtocol.SideProfile, MovementRaisePlane.None, MovementAnalysisFamily.LowerLimb),
        new ExerciseDefinition(
            MovementId.AnklePlantarflexion, BodyRegionId.Ankle, "exercise.move.ankle.plantar", false,
            CameraProtocol.SideProfile, MovementRaisePlane.None, MovementAnalysisFamily.LowerLimb),
    };

    public static string RegionLocKey(BodyRegionId region)
    {
        switch (region)
        {
            case BodyRegionId.Shoulder: return "exercise.region.shoulder";
            case BodyRegionId.Arm: return "exercise.region.arm";
            case BodyRegionId.Elbow: return "exercise.region.elbow";
            case BodyRegionId.Neck: return "exercise.region.neck";
            case BodyRegionId.Leg: return "exercise.region.leg";
            case BodyRegionId.Ankle: return "exercise.region.ankle";
            default: return "exercise.region.shoulder";
        }
    }

    public static bool TryGet(MovementId id, out ExerciseDefinition def)
    {
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].MovementId == id)
            {
                def = All[i];
                return true;
            }
        }
        def = default;
        return false;
    }

    public static ExerciseDefinition GetOrDefault(MovementId id)
    {
        if (TryGet(id, out ExerciseDefinition def)) return def;
        TryGet(DefaultMovementId, out def);
        return def;
    }

    public static bool IsLiveReady(MovementId id)
    {
        return TryGet(id, out ExerciseDefinition def) && def.Implemented;
    }

    /// <summary>Yan kamera protokolü — tanım meta’sından.</summary>
    public static bool UsesSideProfile(MovementId id)
    {
        return GetOrDefault(id).UsesSideProfile;
    }

    /// <summary>Ön: omuz genişliği; yan: gövde boyu (omuz–kalça).</summary>
    public static PoseScaleBasis GetScaleBasis(MovementId id)
    {
        return PoseScaleResolver.FromCameraProtocol(GetOrDefault(id).Camera);
    }

    public static bool AllowsBilateralSequential(MovementId id)
    {
        return GetOrDefault(id).AllowsBilateralSequential;
    }

    /// <summary>Önden abdüksiyon: iki kol aynı anda. Yan fleksiyon: XOR veya sırayla.</summary>
    public static bool AllowsSimultaneousBilateral(MovementId id)
    {
        return GetOrDefault(id).AllowsSimultaneousBilateral;
    }

    /// <summary>Aynı anda iki kol yok ve sırayla protokol kapalıysa tek kol zorunlu.</summary>
    public static bool RequiresExclusiveArm(MovementId id)
    {
        return !AllowsSimultaneousBilateral(id);
    }

    /// <summary>Yan profil + tek kol (omuz fleksiyonu).</summary>
    public static bool IsExclusiveSideProfile(MovementId id)
    {
        return UsesSideProfile(id) && RequiresExclusiveArm(id);
    }

    public static MovementRaisePlane GetRaisePlane(MovementId id)
    {
        return GetOrDefault(id).RaisePlane;
    }

    public static MovementAnalysisFamily GetAnalysisFamily(MovementId id)
    {
        return GetOrDefault(id).AnalysisFamily;
    }

    public static MovementAvatarDriver GetAvatarDriver(MovementId id)
    {
        return GetOrDefault(id).AvatarDriver;
    }

    /// <summary>Seans bölgesine göre hangi radial yay çizilir (omuz / dirsek / kalça / ayak bileği).</summary>
    public static RadialArcKind GetRadialArcKind(BodyRegionId region)
    {
        switch (region)
        {
            case BodyRegionId.Shoulder:
            case BodyRegionId.Arm:
                return RadialArcKind.Shoulder;
            case BodyRegionId.Elbow:
                return RadialArcKind.Elbow;
            case BodyRegionId.Leg:
                return RadialArcKind.Hip;
            case BodyRegionId.Ankle:
                return RadialArcKind.Ankle;
            default:
                return RadialArcKind.None;
        }
    }

    /// <summary>Omuz elevasyon ailesi (fleksiyon / abdüksiyon ortak pipeline).</summary>
    public static bool IsShoulderElevationFamily(MovementId id)
    {
        return GetAnalysisFamily(id) == MovementAnalysisFamily.ShoulderElevation;
    }

    /// <summary>Geriye uyumluluk — <see cref="IsShoulderElevationFamily"/>.</summary>
    public static bool IsShoulderElevationLive(MovementId id)
    {
        return IsLiveReady(id) && IsShoulderElevationFamily(id);
    }

    public static string ProtocolLocKey(MovementId id)
    {
        return GetOrDefault(id).ResolveProtocolLocKey();
    }

    /// <summary>Rapor klasör etiketi (TR). Dosya sistemi için PatientVault sanitize eder.</summary>
    public static string ReportFolderLabel(MovementId id)
    {
        ExerciseDefinition def = GetOrDefault(id);
        return Loc.T(def.LocKey, AppLanguage.Turkish);
    }

    /// <summary>Bölgeye göre hareketleri buffer'a yazar; dönüş = adet.</summary>
    public static int CopyForRegion(BodyRegionId region, ExerciseDefinition[] buffer)
    {
        if (buffer == null) return 0;
        int n = 0;
        for (int i = 0; i < All.Length && n < buffer.Length; i++)
        {
            if (All[i].RegionId == region)
                buffer[n++] = All[i];
        }
        return n;
    }

    /// <summary>Implemented=true hareketleri buffer'a yazar (geçmiş filtresi / menü).</summary>
    public static int CopyLiveMovements(MovementId[] buffer)
    {
        if (buffer == null) return 0;
        int n = 0;
        for (int i = 0; i < All.Length && n < buffer.Length; i++)
        {
            if (All[i].Implemented)
                buffer[n++] = All[i].MovementId;
        }
        return n;
    }

    /// <summary>Implemented=true ve bölgeye ait hareketleri buffer'a yazar.</summary>
    public static int CopyLiveMovementsForRegion(BodyRegionId region, MovementId[] buffer)
    {
        if (buffer == null) return 0;
        int n = 0;
        for (int i = 0; i < All.Length && n < buffer.Length; i++)
        {
            if (All[i].Implemented && All[i].RegionId == region)
                buffer[n++] = All[i].MovementId;
        }
        return n;
    }

    /// <summary>En az bir canlı hareketi olan bölgeleri buffer'a yazar.</summary>
    public static int CopyLiveRegions(BodyRegionId[] buffer)
    {
        if (buffer == null) return 0;
        int n = 0;
        for (int r = 0; r <= (int)BodyRegionId.Ankle && n < buffer.Length; r++)
        {
            var region = (BodyRegionId)r;
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Implemented && All[i].RegionId == region)
                {
                    buffer[n++] = region;
                    break;
                }
            }
        }
        return n;
    }

    /// <summary>Implemented tanım kopyası.</summary>
    public static int CopyLiveDefinitions(ExerciseDefinition[] buffer)
    {
        if (buffer == null) return 0;
        int n = 0;
        for (int i = 0; i < All.Length && n < buffer.Length; i++)
        {
            if (All[i].Implemented)
                buffer[n++] = All[i];
        }
        return n;
    }

    public static BodyRegionId ClampRegion(int raw)
    {
        if (raw < 0 || raw > (int)BodyRegionId.Ankle) return DefaultRegionId;
        return (BodyRegionId)raw;
    }

    public static MovementId ClampMovement(int raw)
    {
        if (TryGet((MovementId)raw, out _)) return (MovementId)raw;
        return DefaultMovementId;
    }

    /// <summary>
    /// Eski kayıt (region=0, movement=0) klinik olarak omuz abdüksiyonu sayılır.
    /// SaMD Class B geçmiş eşlemesi; teşhis değildir.
    /// </summary>
    public static bool IsLegacyUnsetAbduction(int bodyRegionId, int movementId)
    {
        return bodyRegionId == 0 && movementId == 0;
    }

    public static MovementId ResolveStoredMovementId(int bodyRegionId, int movementId)
    {
        if (IsLegacyUnsetAbduction(bodyRegionId, movementId))
            return MovementId.ShoulderAbduction;
        return ClampMovement(movementId);
    }
}
