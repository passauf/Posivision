using System.IO;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SessionEntry
{
    public string dateTime;
    // Geriye uyumluluk — etkin kolların en yüksek değerleri
    public float maxROM;
    public float averageROM;
    public int completedReps;
    public int invalidReps;
    public int targetReps;
    public float completionRate;
    public float durationSeconds;
    public int compensationEvents;
    public float targetAngle;

    // Sağ / sol ayrı ölçüm
    public float rightMaxROM;
    public float leftMaxROM;
    public float rightAverageROM;
    public float leftAverageROM;
    public int rightCompletedReps;
    public int leftCompletedReps;
    public int rightInvalidReps;
    public int leftInvalidReps;
    public bool rightArmEnabled;
    public bool leftArmEnabled;

    // Seans anındaki profil (klinik bağlam + rapor kimliği)
    public string firstName;
    public string lastName;
    public float heightCm;
    public int ageYears;
    public int gender;

    // Yüz zorlanma özeti (0..1 skaler; ham yüz verisi saklanmaz — KVKK)
    public float peakStrain;
    public float meanStrain;
    public float angleAtPeakStrainR;
    public float angleAtPeakStrainL;

    // DTW hareket kalitesi (0..100). Negatif = hesaplanamadı / eski kayıt
    public float movementScoreRight = -1f;
    public float movementScoreLeft = -1f;

    /// <summary>
    /// Seans ortalama QualityScore (0..1). Negatif = eski kayıt / hesaplanmadı.
    /// Formül: SessionQualityScorer.FormulaVersion (QS-1.0).
    /// </summary>
    public float qualityScoreMean = -1f;
    /// <summary>Seans içi en düşük kare skoru (0..1). Negatif = yok.</summary>
    public float qualityScoreMin = -1f;
    /// <summary>0=Unknown, 1=Reliable, 2=Caution, 3=Invalid (<see cref="SessionQualityBand"/>).</summary>
    public int qualityBand;
    /// <summary>Örn. QS-1.0 — rapor/CSV dipnotu için.</summary>
    public string qualityFormulaVersion = "";

    /// <summary>
    /// Yardımlı (assisted) başarılı tekrarlar — bağımsız tekrarın alt kümesi değildir;
    /// rightCompletedReps toplam başarılıyı (bağımsız+yardımlı) tutar.
    /// SaMD Class B: istatistikte bağımsız ROM/tekrar ile karıştırılmamalı.
    /// </summary>
    public int rightAssistedReps;
    public int leftAssistedReps;
    public int assistedReps;

    /// <summary>
    /// Kadraj/takip sıçraması olay sayısı (iskelet milisaniyeler içinde teleporte benzeri kayma).
    /// SaMD Class B: seans kalitesi bağlamı; teşhis değildir.
    /// </summary>
    public int trackingJumpEvents;

    /// <summary>
    /// Sahnede 2. kişinin algılandığı olay sayısı (manuel yardım kapalı olsa da).
    /// SaMD Class B: yardım bağlamı; teşhis değildir.
    /// </summary>
    public int secondPersonEvents;

    /// <summary>
    /// Yardımlı sezgi (temas + hız vektörü + süreğenlik) olay sayısı.
    /// </summary>
    public int assistNearEvents;

    /// <summary>
    /// Seans içi ROM zaman serisi (downsample) — menü detay grafiği için.
    /// Boş/null = eski kayıt; HTML raporu açılabilir. Ham yüz/video yok (KVKK).
    /// </summary>
    public float[] seriesTimes;
    public float[] seriesRight;
    public float[] seriesLeft;
    public float[] seriesStrain;
    public float[] seriesCompTimes;
    public bool[] seriesAssistRight;
    public bool[] seriesAssistLeft;

    /// <summary>Seans sırasında yapılan hareket (<see cref="MovementId"/>).</summary>
    public int movementId;
    /// <summary>Seans vücut bölgesi (<see cref="BodyRegionId"/>).</summary>
    public int bodyRegionId;

    /// <summary>
    /// Seans sonrası özbildirim (Likert). false = anket yok / eski kayıt.
    /// KVKK: skor; kimlik yok. Hasta HTML görünümünde gizli — klinisyen butonu.
    /// </summary>
    public bool hasPostSessionSurvey;
    public int surveyDifficulty = -1;
    public int surveyPain = -1;
    public int surveyMotivation = -1;
    public int surveyFatigue = -1;
    public int surveyHomeDays = -1;
    public int surveySleep = -1;
    public int surveyConfidence = -1;
    public int surveyWillingness = -1;
}

[System.Serializable]
public class PatientHistory
{
    public List<SessionEntry> sessions = new List<SessionEntry>();
}

public class DataManager : MonoBehaviour
{
    private string _filePath;
    private string _profilePath;
    private string _careStatePath;
    private string _registryPath;
    private string _activeCareStatePath;

    private void Awake()
    {
        _filePath = Path.Combine(Application.persistentDataPath, "patient_history.json");
        _profilePath = Path.Combine(Application.persistentDataPath, "patient_profile.json");
        _careStatePath = Path.Combine(Application.persistentDataPath, "patient_care_state.json");
        _registryPath = Path.Combine(Application.persistentDataPath, "patient_registry.json");
        _activeCareStatePath = _careStatePath;
    }

    public PatientProfile LoadProfile()
    {
        if (!File.Exists(_profilePath)) return new PatientProfile();

        string json = File.ReadAllText(_profilePath);
        PatientProfile profile = JsonUtility.FromJson<PatientProfile>(json);
        return profile ?? new PatientProfile();
    }

    public void SaveProfile(PatientProfile profile)
    {
        if (profile == null) return;
        // cmd: KVKK — rıza yoksa PII diske yazılmaz
        if (!profile.HasValidConsent)
        {
            Debug.LogWarning("[DataManager] Profil kaydı reddedildi: geçerli KVKK rızası yok.");
            return;
        }

        profile.lastUpdated = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        if (string.IsNullOrEmpty(profile.patientId))
            profile.patientId = System.Guid.NewGuid().ToString("N");

        string json = JsonUtility.ToJson(profile, true);
        File.WriteAllText(_profilePath, json);

        PatientRegistryData registry = LoadRegistry();
        PatientRegistry.UpsertFromProfile(registry, profile);
        SaveRegistry(registry);
    }

    /// <summary>Aktif hastayı ayarlar; seans/rapor bağlamı bu profil olur.</summary>
    public void SetActivePatient(RegisteredPatient patient)
    {
        if (patient == null) return;
        PatientProfile profile = patient.ToProfile();
        // Seçim için rıza yoksa bile aktif bağlam tutulur; seans başında PreSession rızayı yeniler
        if (!profile.HasValidConsent)
        {
            profile.consentAccepted = patient.consentAccepted;
            profile.consentVersion = patient.consentVersion;
            profile.consentAcceptedAt = patient.consentAcceptedAt ?? "";
        }

        profile.lastUpdated = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        if (string.IsNullOrEmpty(profile.patientId))
            profile.patientId = System.Guid.NewGuid().ToString("N");

        // Aktif profil her zaman yazılır (seçim); tam rıza yoksa registry'ye Upsert yapılmaz
        File.WriteAllText(_profilePath, JsonUtility.ToJson(profile, true));

        PatientRegistryData registry = LoadRegistry();
        if (profile.HasValidConsent)
            PatientRegistry.UpsertFromProfile(registry, profile);
        else
            registry.activePatientId = profile.patientId;
        SaveRegistry(registry);
    }

    public void ClearActivePatientForNew()
    {
        var blank = new PatientProfile();
        File.WriteAllText(_profilePath, JsonUtility.ToJson(blank, true));
        PatientRegistryData registry = LoadRegistry();
        registry.activePatientId = "";
        SaveRegistry(registry);
    }

    /// <summary>
    /// Ad/soyad değişince geçmiş seans anlık görüntülerini günceller (geçmiş isimle filtrelenir).
    /// KVKK: yalnızca yerel; kimlik düzeltmesi. SaMD Class B: yanlış hasta bağlamını önler.
    /// </summary>
    public void RewriteSessionPatientNames(string oldFirst, string oldLast, string newFirst, string newLast)
    {
        string oFn = oldFirst != null ? oldFirst.Trim() : "";
        string oLn = oldLast != null ? oldLast.Trim() : "";
        string nFn = newFirst != null ? newFirst.Trim() : "";
        string nLn = newLast != null ? newLast.Trim() : "";
        if (oFn.Length == 0 && oLn.Length == 0) return;
        if (string.Equals(oFn, nFn, System.StringComparison.Ordinal)
            && string.Equals(oLn, nLn, System.StringComparison.Ordinal))
            return;

        PatientHistory history = LoadHistory();
        if (history == null || history.sessions == null) return;

        bool dirty = false;
        for (int i = 0; i < history.sessions.Count; i++)
        {
            SessionEntry s = history.sessions[i];
            if (s == null) continue;
            string sFn = s.firstName != null ? s.firstName.Trim() : "";
            string sLn = s.lastName != null ? s.lastName.Trim() : "";
            if (!string.Equals(sFn, oFn, System.StringComparison.OrdinalIgnoreCase)
                || !string.Equals(sLn, oLn, System.StringComparison.OrdinalIgnoreCase))
                continue;
            s.firstName = nFn;
            s.lastName = nLn;
            dirty = true;
        }

        if (dirty)
        {
            string json = JsonUtility.ToJson(history, true);
            File.WriteAllText(_filePath, json);
        }

        PatientVault.TryRenamePatientFolder(oFn, oLn, nFn, nLn);
    }

    /// <summary>Profil kaydı + ad değiştiyse geçmiş/klasör migrasyonu.</summary>
    public void SaveProfileAndMigrateIdentity(PatientProfile previous, PatientProfile updated)
    {
        if (updated == null) return;
        SaveProfile(updated);
        if (previous == null) return;
        RewriteSessionPatientNames(
            previous.firstName, previous.lastName,
            updated.firstName, updated.lastName);
    }

    public PatientRegistryData LoadRegistry()
    {
        PatientRegistryData data = null;
        if (File.Exists(_registryPath))
        {
            try
            {
                string json = File.ReadAllText(_registryPath);
                data = JsonUtility.FromJson<PatientRegistryData>(json);
            }
            catch { data = null; }
        }

        if (data == null) data = new PatientRegistryData();
        if (data.patients == null) data.patients = new List<RegisteredPatient>();

        PatientHistory history = LoadHistory();
        PatientRegistry.SyncFromHistory(data, history);

        // Mevcut tek profil registry'ye ekle
        PatientProfile active = LoadProfile();
        if (active != null && (!string.IsNullOrWhiteSpace(active.firstName) || !string.IsNullOrWhiteSpace(active.lastName)))
        {
            if (active.HasValidConsent)
                PatientRegistry.UpsertFromProfile(data, active);
            else if (string.IsNullOrEmpty(data.activePatientId) && !string.IsNullOrEmpty(active.patientId))
                data.activePatientId = active.patientId;
        }

        SaveRegistry(data);
        return data;
    }

    public void SaveRegistry(PatientRegistryData data)
    {
        if (data == null) return;
        if (data.patients == null) data.patients = new List<RegisteredPatient>();
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(_registryPath, json);
    }

    public void SaveSession(SessionEntry newEntry)
    {
        PatientHistory history = LoadHistory();
        history.sessions.Add(newEntry);

        string json = JsonUtility.ToJson(history, true);
        File.WriteAllText(_filePath, json);

        PatientRegistryData registry = LoadRegistryRaw();
        PatientRegistry.TouchSession(registry, newEntry);
        SaveRegistry(registry);
        // Hasta tanımlayıcı bilgi loglanmaz (KVKK)
    }

    /// <summary>Seans sonrası anketi son seansa yazar. KVKK: Likert skor; kimlik yok.</summary>
    public void AttachSurveyToLatestSession(PatientProfile profile, SurveyResponse survey)
    {
        if (survey == null) return;
        PatientHistory full = LoadHistory();
        if (full.sessions == null || full.sessions.Count == 0) return;

        PatientHistory filtered = PatientVault.FilterHistoryForPatient(full, profile, fallbackToAll: false);
        SessionEntry last = null;
        if (filtered != null && filtered.sessions != null && filtered.sessions.Count > 0)
            last = filtered.sessions[filtered.sessions.Count - 1];
        if (last == null)
            last = full.sessions[full.sessions.Count - 1];

        survey.CopyTo(last);
        string json = JsonUtility.ToJson(full, true);
        File.WriteAllText(_filePath, json);
    }

    public PatientHistory LoadHistory()
    {
        if (!File.Exists(_filePath)) return new PatientHistory();

        string json = File.ReadAllText(_filePath);
        PatientHistory history = JsonUtility.FromJson<PatientHistory>(json);
        return history ?? new PatientHistory();
    }

    /// <summary>Aktif hastaya filtrelenmiş seans geçmişi (fallback yok).</summary>
    public PatientHistory LoadHistoryForActivePatient()
    {
        return PatientVault.FilterHistoryForPatient(LoadHistory(), LoadProfile(), fallbackToAll: false);
    }

    public PatientHistory LoadHistoryForPatient(PatientProfile profile)
    {
        return PatientVault.FilterHistoryForPatient(LoadHistory(), profile, fallbackToAll: false);
    }

    /// <summary>Hasta bazlı bakım durumu yolu.</summary>
    public string GetCareStatePath(PatientProfile profile)
    {
        if (profile == null || (string.IsNullOrEmpty(profile.patientId) && string.IsNullOrWhiteSpace(profile.FileNameSafe)))
            return _careStatePath;

        string key = !string.IsNullOrEmpty(profile.patientId)
            ? profile.patientId
            : SanitizeCareKey(profile.FileNameSafe);
        return Path.Combine(Application.persistentDataPath, "care_state_" + key + ".json");
    }

    private static string SanitizeCareKey(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "default";
        var sb = new System.Text.StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            else if (c == ' ') sb.Append('_');
        }
        string s = sb.ToString();
        return s.Length > 0 ? s : "default";
    }

    public PatientCareState LoadCareState(PatientHistory history = null)
    {
        return LoadCareState(history, LoadProfile());
    }

    public PatientCareState LoadCareState(PatientHistory history, PatientProfile profile)
    {
        string path = GetCareStatePath(profile);
        _activeCareStatePath = path;

        // Eski tek dosyadan hasta dosyasına bir kez taşı (yalnızca aktif hasta + dosya yoksa)
        if (!File.Exists(path) && path != _careStatePath && File.Exists(_careStatePath)
            && profile != null && !string.IsNullOrWhiteSpace(profile.firstName))
        {
            // Yeni hasta için eski global state kopyalanmaz — boş başlat
        }

        PatientCareState state = null;
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                state = JsonUtility.FromJson<PatientCareState>(json);
            }
            catch { state = null; }
        }

        if (state == null)
            state = new PatientCareState { createdAt = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm") };

        if (state.surveys == null) state.surveys = new System.Collections.Generic.List<SurveyResponse>();
        if (state.clinicianNotes == null) state.clinicianNotes = new System.Collections.Generic.List<ClinicianNote>();
        if (state.plan == null) state.plan = new CarePlan();
        if (state.plan.monthlyWeeks == null) state.plan.monthlyWeeks = new System.Collections.Generic.List<CarePlanWeek>();

        if (history == null)
            history = profile != null ? LoadHistoryForPatient(profile) : LoadHistory();

        int sessionCount = history != null && history.sessions != null ? history.sessions.Count : 0;
        if (string.IsNullOrEmpty(state.createdAt))
            state.createdAt = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm");

        if (state.phase == CarePhase.Assessment
            && state.assessmentSessionCount == 0
            && sessionCount > 0
            && sessionCount < PatientCareState.AssessmentSessionTarget)
        {
            state.assessmentSessionCount = sessionCount;
        }
        else if (state.phase == CarePhase.Assessment
                 && sessionCount >= PatientCareState.AssessmentSessionTarget
                 && state.assessmentSessionCount < PatientCareState.AssessmentSessionTarget
                 && (state.plan.monthlyWeeks == null || state.plan.monthlyWeeks.Count == 0))
        {
            state.assessmentSessionCount = PatientCareState.AssessmentSessionTarget;
            state.plan = CarePlanBuilder.BuildInitial(history, state.surveys);
            state.phase = CarePhase.ActiveProgram;
            state.programVersion = Mathf.Max(1, state.programVersion);
            state.lastAdaptedAt = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            SaveCareState(state, profile);
        }

        return state;
    }

    public void SaveCareState(PatientCareState state)
    {
        SaveCareState(state, LoadProfile());
    }

    public void SaveCareState(PatientCareState state, PatientProfile profile)
    {
        if (state == null) return;
        string path = GetCareStatePath(profile);
        _activeCareStatePath = path;
        string json = JsonUtility.ToJson(state, true);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// KVKK silme hakkı: profil, seans geçmişi, bakım planı, raporlar, yüz kalibrasyonu.
    /// Bulut yok; yalnızca yerel dosyalar. Klinisyen PIN silinmez (cihaz ayarı).
    /// </summary>
    public void DeleteAllLocalPatientData()
    {
        TryDeleteFile(_profilePath);
        TryDeleteFile(_filePath);
        TryDeleteFile(_careStatePath);
        TryDeleteFile(_registryPath);
        // Hasta bazlı care_state_*.json
        try
        {
            string[] careFiles = Directory.GetFiles(Application.persistentDataPath, "care_state_*.json");
            for (int i = 0; i < careFiles.Length; i++)
                TryDeleteFile(careFiles[i]);
        }
        catch { }
        FaceStrainProfile.Delete();
        ReportExporter.DeleteAllReports();
    }

    /// <summary>LoadRegistry her çağrıda Sync+Save yapar; menü açılışında bir kez yeterli.</summary>
    public PatientRegistryData EnsureRegistry()
    {
        return LoadRegistry();
    }

    /// <summary>Sync yapmadan ham registry (SaveSession döngüsünü önlemek için).</summary>
    private PatientRegistryData LoadRegistryRaw()
    {
        if (!File.Exists(_registryPath)) return new PatientRegistryData();
        try
        {
            string json = File.ReadAllText(_registryPath);
            PatientRegistryData data = JsonUtility.FromJson<PatientRegistryData>(json);
            if (data == null) data = new PatientRegistryData();
            if (data.patients == null) data.patients = new List<RegisteredPatient>();
            return data;
        }
        catch
        {
            return new PatientRegistryData();
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (System.Exception)
        {
            // Dosya kilitli olabilir — kimlik loglanmaz
        }
    }
}
