using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Seans geçmişi dönem + kalite + egzersiz (bölge/hareket) filtresi (AND) ve gelişim yüzdesi.
/// SaMD Class B: karar-destek özeti; teşhis değildir.
/// </summary>
public enum HistoryFilterMode
{
    All = 0,
    Last7Days = 1,
    Last30Days = 2,
    Last90Days = 3,
    Last5Sessions = 4,
    Last10Sessions = 5,
    Last20Sessions = 6,
    WithCompensation = 7,
    NoCompensation = 8,
    HighStrain = 9,
    IncompleteTarget = 10,
    RightArmMeasured = 11,
    LeftArmMeasured = 12,

    // Egzersiz / bölge filtresi (dropdown 3)
    RegionShoulder = 20,
    RegionArm = 21,
    RegionElbow = 22,
    RegionNeck = 23,
    RegionLeg = 24,
    RegionAnkle = 25,
    MovementShoulderFlexion = 30,
    MovementShoulderAbduction = 31
    // Yeni hareket filtreleri: MovementFilterBase + (int)MovementId — enum’a satır eklemeye gerek yok
}

public struct ProgressSummary
{
    public int sessionCount;
    public float combinedPct;
    public float rightPct;
    public float leftPct;
    public float firstMax;
    public float lastMax;
    public float firstRightMax;
    public float lastRightMax;
    public float firstLeftMax;
    public float lastLeftMax;
    public bool hasRight;
    public bool hasLeft;
}

public static class SessionHistoryFilter
{
    public const string PrefKeyDate = "history_filter_date";
    public const string PrefKeyQuality = "history_filter_quality";
    public const string PrefKeyExercise = "history_filter_exercise";
    public const string PrefKeyRegion = "history_filter_region";
    public const string PrefKeyMovement = "history_filter_movement";
    [Obsolete("Use PrefKeyDate / PrefKeyQuality")]
    public const string PrefKey = PrefKeyDate;

    public const float HighStrainThreshold = 0.55f;
    public const float IncompleteCompletionRate = 99.5f;

    /// <summary>
    /// Hareket filtresi kodu = base + MovementId.
    /// Legacy enum 30/31 PlayerPrefs yüklemede bu forma migrate edilir.
    /// </summary>
    public const int MovementFilterBase = 1000;

    private static readonly MovementId[] LiveMovementScratch = new MovementId[32];
    private static readonly BodyRegionId[] LiveRegionScratch = new BodyRegionId[16];
    private static HistoryFilterMode[] _exerciseModes;
    private static HistoryFilterMode[] _regionModes;
    private static HistoryFilterMode[] _allMovementModes;

    /// <summary>Dönem / tarih dropdown — sağ üst.</summary>
    public static readonly HistoryFilterMode[] DateModes =
    {
        HistoryFilterMode.All,
        HistoryFilterMode.Last7Days,
        HistoryFilterMode.Last30Days,
        HistoryFilterMode.Last90Days,
        HistoryFilterMode.Last5Sessions,
        HistoryFilterMode.Last10Sessions,
        HistoryFilterMode.Last20Sessions
    };

    /// <summary>Kalite / klinik dropdown — filtre satırı.</summary>
    public static readonly HistoryFilterMode[] QualityModes =
    {
        HistoryFilterMode.All,
        HistoryFilterMode.WithCompensation,
        HistoryFilterMode.NoCompensation,
        HistoryFilterMode.HighStrain,
        HistoryFilterMode.IncompleteTarget,
        HistoryFilterMode.RightArmMeasured,
        HistoryFilterMode.LeftArmMeasured
    };

    /// <summary>Tümü + katalogda Implemented canlı hareketler (otomatik büyür).</summary>
    public static HistoryFilterMode[] ExerciseModes
    {
        get
        {
            if (_exerciseModes == null)
                _exerciseModes = BuildExerciseModes();
            return _exerciseModes;
        }
    }

    /// <summary>Tümü + en az bir canlı hareketi olan bölgeler.</summary>
    public static HistoryFilterMode[] RegionModes
    {
        get
        {
            if (_regionModes == null)
                _regionModes = BuildRegionModes();
            return _regionModes;
        }
    }

    /// <summary>Tümü + tüm canlı hareketler (bölge seçilmeden).</summary>
    public static HistoryFilterMode[] AllMovementModes
    {
        get
        {
            if (_allMovementModes == null)
                _allMovementModes = BuildMovementModes(BodyRegionId.Shoulder, includeAllRegions: true);
            return _allMovementModes;
        }
    }

    /// <summary>Editor / test: katalog değişince yeniden kur.</summary>
    public static void RebuildExerciseModes()
    {
        _exerciseModes = BuildExerciseModes();
        _regionModes = BuildRegionModes();
        _allMovementModes = null;
    }

    public static HistoryFilterMode ForRegion(BodyRegionId region)
    {
        switch (region)
        {
            case BodyRegionId.Shoulder: return HistoryFilterMode.RegionShoulder;
            case BodyRegionId.Arm: return HistoryFilterMode.RegionArm;
            case BodyRegionId.Elbow: return HistoryFilterMode.RegionElbow;
            case BodyRegionId.Neck: return HistoryFilterMode.RegionNeck;
            case BodyRegionId.Leg: return HistoryFilterMode.RegionLeg;
            case BodyRegionId.Ankle: return HistoryFilterMode.RegionAnkle;
            default: return HistoryFilterMode.All;
        }
    }

    public static bool TryResolveRegion(HistoryFilterMode mode, out BodyRegionId region)
    {
        switch (mode)
        {
            case HistoryFilterMode.RegionShoulder: region = BodyRegionId.Shoulder; return true;
            case HistoryFilterMode.RegionArm: region = BodyRegionId.Arm; return true;
            case HistoryFilterMode.RegionElbow: region = BodyRegionId.Elbow; return true;
            case HistoryFilterMode.RegionNeck: region = BodyRegionId.Neck; return true;
            case HistoryFilterMode.RegionLeg: region = BodyRegionId.Leg; return true;
            case HistoryFilterMode.RegionAnkle: region = BodyRegionId.Ankle; return true;
            default: region = default; return false;
        }
    }

    public static bool IsRegionMode(HistoryFilterMode mode)
    {
        return TryResolveRegion(mode, out _);
    }

  /// <summary>Seçili bölgeye göre hareket dropdown seçenekleri (Tümü + canlı hareketler).</summary>
    public static HistoryFilterMode[] GetMovementModes(HistoryFilterMode regionMode)
    {
        if (regionMode == HistoryFilterMode.All || !TryResolveRegion(regionMode, out BodyRegionId region))
            return AllMovementModes;
        return BuildMovementModes(region, includeAllRegions: false);
    }

    private static HistoryFilterMode[] BuildRegionModes()
    {
        int liveCount = ExerciseCatalog.CopyLiveRegions(LiveRegionScratch);
        var modes = new HistoryFilterMode[liveCount + 1];
        modes[0] = HistoryFilterMode.All;
        for (int i = 0; i < liveCount; i++)
            modes[i + 1] = ForRegion(LiveRegionScratch[i]);
        return modes;
    }

    private static HistoryFilterMode[] BuildMovementModes(BodyRegionId region, bool includeAllRegions)
    {
        int liveCount = includeAllRegions
            ? ExerciseCatalog.CopyLiveMovements(LiveMovementScratch)
            : ExerciseCatalog.CopyLiveMovementsForRegion(region, LiveMovementScratch);
        var modes = new HistoryFilterMode[liveCount + 1];
        modes[0] = HistoryFilterMode.All;
        for (int i = 0; i < liveCount; i++)
            modes[i + 1] = ForMovement(LiveMovementScratch[i]);
        return modes;
    }

    private static HistoryFilterMode[] BuildExerciseModes()
    {
        int liveCount = ExerciseCatalog.CopyLiveMovements(LiveMovementScratch);
        var modes = new HistoryFilterMode[liveCount + 1];
        modes[0] = HistoryFilterMode.All;
        for (int i = 0; i < liveCount; i++)
            modes[i + 1] = ForMovement(LiveMovementScratch[i]);
        return modes;
    }

    public static HistoryFilterMode ForMovement(MovementId id)
    {
        return (HistoryFilterMode)(MovementFilterBase + (int)id);
    }

    public static bool TryResolveMovement(HistoryFilterMode mode, out MovementId id)
    {
        if (mode == HistoryFilterMode.MovementShoulderFlexion)
        {
            id = MovementId.ShoulderFlexion;
            return true;
        }
        if (mode == HistoryFilterMode.MovementShoulderAbduction)
        {
            id = MovementId.ShoulderAbduction;
            return true;
        }

        int v = (int)mode;
        if (v >= MovementFilterBase)
        {
            id = (MovementId)(v - MovementFilterBase);
            return ExerciseCatalog.TryGet(id, out _);
        }

        id = default;
        return false;
    }

    public static HistoryFilterMode MigrateExerciseMode(HistoryFilterMode mode)
    {
        if (mode == HistoryFilterMode.MovementShoulderFlexion)
            return ForMovement(MovementId.ShoulderFlexion);
        if (mode == HistoryFilterMode.MovementShoulderAbduction)
            return ForMovement(MovementId.ShoulderAbduction);
        return mode;
    }

    private static readonly string[] DateFormats =
    {
        "dd/MM/yyyy HH:mm",
        "dd.MM.yyyy HH:mm",
        "dd/MM/yyyy HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-ddTHH:mm:ss"
    };

    public static void LoadSaved(out HistoryFilterMode dateMode, out HistoryFilterMode qualityMode)
    {
        dateMode = LoadMode(PrefKeyDate, DateModes);
        qualityMode = LoadMode(PrefKeyQuality, QualityModes);
    }

    public static void LoadSaved(
        out HistoryFilterMode dateMode, out HistoryFilterMode qualityMode, out HistoryFilterMode exerciseMode)
    {
        dateMode = LoadMode(PrefKeyDate, DateModes);
        qualityMode = LoadMode(PrefKeyQuality, QualityModes);
        LoadRegionMovementSaved(out HistoryFilterMode regionMode, out HistoryFilterMode movementMode);
        exerciseMode = CombineRegionMovement(regionMode, movementMode);
    }

    public static void LoadRegionMovementSaved(
        out HistoryFilterMode regionMode, out HistoryFilterMode movementMode)
    {
        regionMode = HistoryFilterMode.All;
        movementMode = HistoryFilterMode.All;

        if (PlayerPrefs.HasKey(PrefKeyRegion) || PlayerPrefs.HasKey(PrefKeyMovement))
        {
            regionMode = LoadMode(PrefKeyRegion, RegionModes);
            movementMode = LoadMode(PrefKeyMovement, AllMovementModes);
            if (movementMode != HistoryFilterMode.All && !MovementAllowedForRegion(regionMode, movementMode))
                movementMode = HistoryFilterMode.All;
            return;
        }

        HistoryFilterMode legacy = LoadMode(PrefKeyExercise, ExerciseModes);
        SplitExerciseFilter(legacy, out regionMode, out movementMode);
    }

    public static void SaveRegion(HistoryFilterMode mode)
    {
        if (IndexOf(mode, RegionModes) < 0) mode = HistoryFilterMode.All;
        PlayerPrefs.SetInt(PrefKeyRegion, (int)mode);
        PlayerPrefs.Save();
    }

    public static void SaveMovement(HistoryFilterMode mode)
    {
        mode = MigrateExerciseMode(mode);
        if (IndexOf(mode, AllMovementModes) < 0) mode = HistoryFilterMode.All;
        PlayerPrefs.SetInt(PrefKeyMovement, (int)mode);
        PlayerPrefs.Save();
    }

    public static void SplitExerciseFilter(
        HistoryFilterMode combined,
        out HistoryFilterMode regionMode,
        out HistoryFilterMode movementMode)
    {
        regionMode = HistoryFilterMode.All;
        movementMode = HistoryFilterMode.All;
        if (combined == HistoryFilterMode.All) return;

        combined = MigrateExerciseMode(combined);
        if (IsRegionMode(combined))
        {
            regionMode = combined;
            return;
        }

        if (TryResolveMovement(combined, out MovementId id))
        {
            movementMode = combined;
            regionMode = ForRegion(ExerciseCatalog.GetOrDefault(id).RegionId);
        }
    }

    public static HistoryFilterMode CombineRegionMovement(
        HistoryFilterMode regionMode, HistoryFilterMode movementMode)
    {
        if (movementMode != HistoryFilterMode.All)
            return movementMode;
        return regionMode;
    }

    public static bool MovementAllowedForRegion(HistoryFilterMode regionMode, HistoryFilterMode movementMode)
    {
        if (movementMode == HistoryFilterMode.All) return true;
        if (!TryResolveMovement(movementMode, out MovementId moveId)) return false;
        if (regionMode == HistoryFilterMode.All) return true;
        if (!TryResolveRegion(regionMode, out BodyRegionId region)) return false;
        return ExerciseCatalog.GetOrDefault(moveId).RegionId == region;
    }

    public static void SaveDate(HistoryFilterMode mode)
    {
        if (IndexOf(mode, DateModes) < 0) mode = HistoryFilterMode.All;
        PlayerPrefs.SetInt(PrefKeyDate, (int)mode);
        PlayerPrefs.Save();
    }

    public static void SaveQuality(HistoryFilterMode mode)
    {
        if (IndexOf(mode, QualityModes) < 0) mode = HistoryFilterMode.All;
        PlayerPrefs.SetInt(PrefKeyQuality, (int)mode);
        PlayerPrefs.Save();
    }

    public static void SaveExercise(HistoryFilterMode mode)
    {
        mode = MigrateExerciseMode(mode);
        if (IndexOf(mode, ExerciseModes) < 0) mode = HistoryFilterMode.All;
        PlayerPrefs.SetInt(PrefKeyExercise, (int)mode);
        PlayerPrefs.Save();
    }

    private static HistoryFilterMode LoadMode(string key, HistoryFilterMode[] allowed)
    {
        int v = PlayerPrefs.GetInt(key, (int)HistoryFilterMode.All);
        HistoryFilterMode mode = MigrateExerciseMode((HistoryFilterMode)v);
        if (IndexOf(mode, allowed) < 0) return HistoryFilterMode.All;
        return mode;
    }

    public static int IndexOf(HistoryFilterMode mode, HistoryFilterMode[] list)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i] == mode) return i;
        return -1;
    }

    public static HistoryFilterMode FromIndex(int index, HistoryFilterMode[] list)
    {
        if (list == null || index < 0 || index >= list.Length) return HistoryFilterMode.All;
        return list[index];
    }

    public static string ModeLabel(HistoryFilterMode mode)
    {
        return ModeLabel(mode, LanguageSettings.Current);
    }

    public static string ModeLabel(HistoryFilterMode mode, AppLanguage lang)
    {
        switch (mode)
        {
            case HistoryFilterMode.All: return Loc.T("filter.all", lang);
            case HistoryFilterMode.Last7Days: return Loc.T("filter.week", lang);
            case HistoryFilterMode.Last30Days: return Loc.T("filter.month", lang);
            case HistoryFilterMode.Last90Days: return Loc.T("filter.quarter", lang);
            case HistoryFilterMode.Last5Sessions: return Loc.T("filter.last5", lang);
            case HistoryFilterMode.Last10Sessions: return Loc.T("filter.last10", lang);
            case HistoryFilterMode.Last20Sessions: return Loc.T("filter.last20", lang);
            case HistoryFilterMode.WithCompensation: return Loc.T("filter.withComp", lang);
            case HistoryFilterMode.NoCompensation: return Loc.T("filter.noComp", lang);
            case HistoryFilterMode.HighStrain: return Loc.T("filter.highStrain", lang);
            case HistoryFilterMode.IncompleteTarget: return Loc.T("filter.incomplete", lang);
            case HistoryFilterMode.RightArmMeasured: return Loc.T("filter.rightArm", lang);
            case HistoryFilterMode.LeftArmMeasured: return Loc.T("filter.leftArm", lang);
            case HistoryFilterMode.RegionShoulder: return Loc.T("filter.region.shoulder", lang);
            case HistoryFilterMode.RegionArm: return Loc.T("filter.region.arm", lang);
            case HistoryFilterMode.RegionElbow: return Loc.T("filter.region.elbow", lang);
            case HistoryFilterMode.RegionNeck: return Loc.T("filter.region.neck", lang);
            case HistoryFilterMode.RegionLeg: return Loc.T("filter.region.leg", lang);
            case HistoryFilterMode.RegionAnkle: return Loc.T("filter.region.ankle", lang);
            default:
                if (TryResolveMovement(mode, out MovementId moveId))
                {
                    ExerciseDefinition def = ExerciseCatalog.GetOrDefault(moveId);
                    return Loc.T(def.LocKey, lang);
                }
                return Loc.T("filter.all", lang);
        }
    }

    public static string ModeJsId(HistoryFilterMode mode)
    {
        switch (mode)
        {
            case HistoryFilterMode.Last7Days: return "week";
            case HistoryFilterMode.Last30Days: return "month";
            case HistoryFilterMode.Last90Days: return "quarter";
            case HistoryFilterMode.Last5Sessions: return "last5";
            case HistoryFilterMode.Last10Sessions: return "last10";
            case HistoryFilterMode.Last20Sessions: return "last20";
            case HistoryFilterMode.WithCompensation: return "withComp";
            case HistoryFilterMode.NoCompensation: return "noComp";
            case HistoryFilterMode.HighStrain: return "highStrain";
            case HistoryFilterMode.IncompleteTarget: return "incomplete";
            case HistoryFilterMode.RightArmMeasured: return "rightArm";
            case HistoryFilterMode.LeftArmMeasured: return "leftArm";
            case HistoryFilterMode.RegionShoulder: return "regionShoulder";
            case HistoryFilterMode.RegionArm: return "regionArm";
            case HistoryFilterMode.RegionElbow: return "regionElbow";
            case HistoryFilterMode.RegionNeck: return "regionNeck";
            case HistoryFilterMode.RegionLeg: return "regionLeg";
            case HistoryFilterMode.RegionAnkle: return "regionAnkle";
            default:
                if (TryResolveMovement(mode, out MovementId moveId))
                    return "move" + ((int)moveId).ToString();
                return "all";
        }
    }

    public static bool TryParseSessionDate(string dateTime, out DateTime result)
    {
        result = default;
        if (string.IsNullOrEmpty(dateTime)) return false;
        for (int i = 0; i < DateFormats.Length; i++)
        {
            if (DateTime.TryParseExact(dateTime, DateFormats[i], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out result))
                return true;
        }
        return DateTime.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)
               || DateTime.TryParse(dateTime, new CultureInfo("tr-TR"), DateTimeStyles.None, out result);
    }

    /// <summary>Dönem ∩ kalite; kronolojik sıra korunur.</summary>
    public static List<SessionEntry> Filter(
        PatientHistory history, HistoryFilterMode dateMode, HistoryFilterMode qualityMode)
    {
        return Filter(history, dateMode, qualityMode, HistoryFilterMode.All);
    }

    /// <summary>Dönem ∩ kalite ∩ egzersiz (bölge/hareket).</summary>
    public static List<SessionEntry> Filter(
        PatientHistory history,
        HistoryFilterMode dateMode,
        HistoryFilterMode qualityMode,
        HistoryFilterMode exerciseMode)
    {
        SplitExerciseFilter(exerciseMode, out HistoryFilterMode regionMode, out HistoryFilterMode movementMode);
        return Filter(history, dateMode, qualityMode, regionMode, movementMode);
    }

    /// <summary>Dönem ∩ kalite ∩ bölge ∩ hareket.</summary>
    public static List<SessionEntry> Filter(
        PatientHistory history,
        HistoryFilterMode dateMode,
        HistoryFilterMode qualityMode,
        HistoryFilterMode regionMode,
        HistoryFilterMode movementMode)
    {
        List<SessionEntry> byDate = ApplySingle(history != null ? history.sessions : null, dateMode, isDatePass: true);
        var result = new List<SessionEntry>(byDate.Count);
        for (int i = 0; i < byDate.Count; i++)
        {
            SessionEntry s = byDate[i];
            if (qualityMode != HistoryFilterMode.All && !PassesQuality(s, qualityMode))
                continue;
            if (!PassesRegionAndMovement(s, regionMode, movementMode))
                continue;
            result.Add(s);
        }
        return result;
    }

    /// <summary>Geriye uyumluluk — tek mod (tarih veya kalite).</summary>
    public static List<SessionEntry> Filter(PatientHistory history, HistoryFilterMode mode)
    {
        bool isDate = IndexOf(mode, DateModes) >= 0;
        if (isDate)
            return Filter(history, mode, HistoryFilterMode.All, HistoryFilterMode.All);
        if (IndexOf(mode, ExerciseModes) >= 0)
            return Filter(history, HistoryFilterMode.All, HistoryFilterMode.All, mode);
        return Filter(history, HistoryFilterMode.All, mode, HistoryFilterMode.All);
    }

    private static List<SessionEntry> ApplySingle(List<SessionEntry> src, HistoryFilterMode mode, bool isDatePass)
    {
        var result = new List<SessionEntry>(32);
        if (src == null || src.Count == 0) return result;
        int n = src.Count;

        if (mode == HistoryFilterMode.All)
        {
            for (int i = 0; i < n; i++) result.Add(src[i]);
            return result;
        }

        switch (mode)
        {
            case HistoryFilterMode.Last5Sessions: return TakeLast(src, 5);
            case HistoryFilterMode.Last10Sessions: return TakeLast(src, 10);
            case HistoryFilterMode.Last20Sessions: return TakeLast(src, 20);
            case HistoryFilterMode.Last7Days: return FilterByDays(src, 7);
            case HistoryFilterMode.Last30Days: return FilterByDays(src, 30);
            case HistoryFilterMode.Last90Days: return FilterByDays(src, 90);
            default:
                if (!isDatePass)
                {
                    for (int i = 0; i < n; i++)
                        if (PassesQuality(src[i], mode)) result.Add(src[i]);
                    return result;
                }
                for (int i = 0; i < n; i++) result.Add(src[i]);
                return result;
        }
    }

    private static bool PassesQuality(SessionEntry s, HistoryFilterMode mode)
    {
        if (s == null) return false;
        switch (mode)
        {
            case HistoryFilterMode.All: return true;
            case HistoryFilterMode.WithCompensation: return s.compensationEvents > 0;
            case HistoryFilterMode.NoCompensation: return s.compensationEvents <= 0;
            case HistoryFilterMode.HighStrain: return s.peakStrain >= HighStrainThreshold;
            case HistoryFilterMode.IncompleteTarget:
                if (s.targetReps > 0 && s.completedReps < s.targetReps) return true;
                return s.targetReps > 0 && s.completionRate < IncompleteCompletionRate;
            case HistoryFilterMode.RightArmMeasured:
                return ShowRight(s) || s.rightCompletedReps > 0 || s.rightMaxROM > 0f;
            case HistoryFilterMode.LeftArmMeasured:
                return ShowLeft(s) || s.leftCompletedReps > 0 || s.leftMaxROM > 0f;
            default: return true;
        }
    }

    private static bool PassesRegionAndMovement(
        SessionEntry s, HistoryFilterMode regionMode, HistoryFilterMode movementMode)
    {
        if (regionMode == HistoryFilterMode.All && movementMode == HistoryFilterMode.All)
            return true;
        if (regionMode != HistoryFilterMode.All && !PassesExercise(s, regionMode))
            return false;
        if (movementMode != HistoryFilterMode.All && !PassesExercise(s, movementMode))
            return false;
        return true;
    }

    private static bool PassesExercise(SessionEntry s, HistoryFilterMode mode)
    {
        if (s == null) return false;
        int region = s.bodyRegionId;
        int movement = s.movementId;
        bool legacyUnset = ExerciseCatalog.IsLegacyUnsetAbduction(region, movement);
        MovementId resolvedMove = ExerciseCatalog.ResolveStoredMovementId(region, movement);

        switch (mode)
        {
            case HistoryFilterMode.All:
                return true;
            case HistoryFilterMode.RegionShoulder:
                return region == (int)BodyRegionId.Shoulder || legacyUnset;
            case HistoryFilterMode.RegionArm:
                return region == (int)BodyRegionId.Arm;
            case HistoryFilterMode.RegionElbow:
                return region == (int)BodyRegionId.Elbow;
            case HistoryFilterMode.RegionNeck:
                return region == (int)BodyRegionId.Neck;
            case HistoryFilterMode.RegionLeg:
                return region == (int)BodyRegionId.Leg;
            case HistoryFilterMode.RegionAnkle:
                return region == (int)BodyRegionId.Ankle;
            default:
                if (TryResolveMovement(mode, out MovementId filterMove))
                    return resolvedMove == filterMove;
                return true;
        }
    }

    private static List<SessionEntry> TakeLast(List<SessionEntry> src, int count)
    {
        var result = new List<SessionEntry>(count);
        int n = src.Count;
        int start = Mathf.Max(0, n - count);
        for (int i = start; i < n; i++) result.Add(src[i]);
        return result;
    }

    private static List<SessionEntry> FilterByDays(List<SessionEntry> src, int days)
    {
        var result = new List<SessionEntry>(src.Count);
        DateTime cutoff = DateTime.Now.Date.AddDays(-days);
        for (int i = 0; i < src.Count; i++)
        {
            SessionEntry s = src[i];
            if (!TryParseSessionDate(s.dateTime, out DateTime dt))
            {
                result.Add(s);
                continue;
            }
            if (dt.Date >= cutoff) result.Add(s);
        }
        return result;
    }

    public static float EffectiveMax(SessionEntry s)
    {
        if (s == null) return 0f;
        float split = Mathf.Max(s.rightMaxROM, s.leftMaxROM);
        if (split > 1f) return split;
        return s.maxROM;
    }

    public static float EffectiveRightMax(SessionEntry s)
    {
        if (s == null) return 0f;
        if (s.rightMaxROM > 1f) return s.rightMaxROM;
        if (s.rightArmEnabled && s.maxROM > 1f && !s.leftArmEnabled) return s.maxROM;
        return s.rightMaxROM;
    }

    public static float EffectiveLeftMax(SessionEntry s)
    {
        if (s == null) return 0f;
        if (s.leftMaxROM > 1f) return s.leftMaxROM;
        if (s.leftArmEnabled && s.maxROM > 1f && !s.rightArmEnabled) return s.maxROM;
        return s.leftMaxROM;
    }

    public static bool ShowRight(SessionEntry s)
    {
        if (s == null) return false;
        return s.rightArmEnabled || s.rightMaxROM > 0f || s.rightCompletedReps > 0;
    }

    public static bool ShowLeft(SessionEntry s)
    {
        if (s == null) return false;
        return s.leftArmEnabled || s.leftMaxROM > 0f || s.leftCompletedReps > 0;
    }

    public static ProgressSummary ComputeProgress(List<SessionEntry> filtered)
    {
        ProgressSummary p = default;
        if (filtered == null || filtered.Count == 0) return p;

        p.sessionCount = filtered.Count;
        SessionEntry first = filtered[0];
        SessionEntry last = filtered[filtered.Count - 1];

        p.firstMax = EffectiveMax(first);
        p.lastMax = EffectiveMax(last);
        p.combinedPct = PctChange(p.firstMax, p.lastMax);

        p.firstRightMax = EffectiveRightMax(first);
        p.lastRightMax = EffectiveRightMax(last);
        p.firstLeftMax = EffectiveLeftMax(first);
        p.lastLeftMax = EffectiveLeftMax(last);

        for (int i = 0; i < filtered.Count; i++)
        {
            if (EffectiveRightMax(filtered[i]) > 1f || filtered[i].rightArmEnabled) p.hasRight = true;
            if (EffectiveLeftMax(filtered[i]) > 1f || filtered[i].leftArmEnabled) p.hasLeft = true;
        }

        if (p.hasRight)
            p.rightPct = PctChange(p.firstRightMax > 1f ? p.firstRightMax : p.firstMax,
                p.lastRightMax > 1f ? p.lastRightMax : p.lastMax);
        if (p.hasLeft)
            p.leftPct = PctChange(p.firstLeftMax > 1f ? p.firstLeftMax : p.firstMax,
                p.lastLeftMax > 1f ? p.lastLeftMax : p.lastMax);

        return p;
    }

    private static float PctChange(float first, float last)
    {
        if (first < 1f) return 0f;
        return ((last - first) / first) * 100f;
    }

    public static string FormatProgressCard(ProgressSummary p)
    {
        if (p.sessionCount < 2)
            return Loc.T("progress.need2");

        if (p.hasRight && p.hasLeft)
        {
            return Loc.Format("progress.split",
                FormatSignedPct(p.rightPct),
                FormatSignedPct(p.leftPct));
        }
        if (p.hasRight)
            return Loc.Format("progress.rightOnly", FormatSignedPct(p.rightPct));
        if (p.hasLeft)
            return Loc.Format("progress.leftOnly", FormatSignedPct(p.leftPct));
        return Loc.Format("progress.combined", FormatSignedPct(p.combinedPct));
    }

    public static string FormatSignedPct(float pct)
    {
        string sign = pct >= 0f ? "+" : "";
        return sign + pct.ToString("F0") + "%";
    }
}
