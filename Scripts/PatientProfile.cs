using System;

/// <summary>
/// Yerel hasta profili. Ad/soyad seans öncesi girilir ve değişene kadar saklanır.
/// KVKK: veriler yalnızca cihazda (persistentDataPath); buluta gönderilmez.
/// SaMD: boy/cinsiyet avatar ve bağlam; ad yalnızca rapor kimliği içindir.
/// </summary>
[Serializable]
public class PatientProfile
{
    public const int GenderMale = 0;
    public const int GenderFemale = 1;

    /// <summary>Aydınlatma metni değişince artır — yeniden onay gerekir.</summary>
    public const int ConsentTextVersion = 1;

    /// <summary>Neden FT alanı üst karakter sınırı (KVKK asgari veri).</summary>
    public const int MaxReasonForCareLength = 400;

    public string firstName = "";
    public string lastName = "";
    public float heightCm = 170f;
    public int ageYears;
    public bool measureRightArm = true;
    public bool measureLeftArm = true;

    /// <summary>
    /// Omuz fleksiyonu: önce bir kol, hedef tekrar bitince diğer kol (tek seans / tek rapor).
    /// Abdüksiyonda kullanılmaz — iki kol aynı anda ölçülür.
    /// </summary>
    public bool sequentialBothArms;

    /// <summary>0 = Erkek (Y Bot), 1 = Kadın (X Bot).</summary>
    public int gender = GenderMale;

    /// <summary>
    /// Hasta bildirimi / klinisyen girişi: neden fizyoterapi (serbest metin).
    /// Teşhis alanı değildir; otomatik tanı üretmez. KVKK: yalnızca yerel saklanır.
    /// </summary>
    public string reasonForCare = "";

    public string lastUpdated = "";

    /// <summary>Yerel hasta kayıt kimliği (PatientRegistry).</summary>
    public string patientId = "";

    // KVKK açık rıza (yerel kayıt)
    public bool consentAccepted;
    public int consentVersion;
    public string consentAcceptedAt = "";

    /// <summary>
    /// Seçili vücut bölgesi (<see cref="BodyRegionId"/>). Teşhis değildir; egzersiz tercihi.
    /// </summary>
    public int preferredBodyRegionId = (int)BodyRegionId.Shoulder;

    /// <summary>
    /// Seçili hareket (<see cref="MovementId"/>). Yerel kayıt; canlı seans yalnızca implemented hareketlerde.
    /// </summary>
    public int preferredMovementId = (int)MovementId.ShoulderFlexion;

    /// <summary>Menüde onaylanan hedef açı. Seans sahnesi bunu yeniden sormaz.</summary>
    public float lastSessionTargetAngle;

    /// <summary>Menüde onaylanan hedef tekrar.</summary>
    public int lastSessionTargetReps;

    /// <summary>Hedef açı/tekrar menüde kaydedildi (SaMD Class B protokol; teşhis değil).</summary>
    public bool hasSessionTargets;

    /// <summary>Bugünkü hareket sırası (MovementId). Boşsa preferredMovementId.</summary>
    public int[] plannedMovementIds;

    /// <summary>Sıradaki hareket indeksi (0 tabanlı).</summary>
    public int plannedMovementIndex;

    public const int MaxPlannedMovements = 8;

    /// <summary>Trim + uzunluk sınırı; boşsa "".</summary>
    public static string NormalizeReasonForCare(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = raw.Trim();
        if (s.Length > MaxReasonForCareLength)
            s = s.Substring(0, MaxReasonForCareLength);
        return s;
    }

    public bool IsFemale => gender == GenderFemale;

    public bool HasValidConsent =>
        consentAccepted && consentVersion == ConsentTextVersion;

    public string DisplayName
    {
        get
        {
            string a = firstName != null ? firstName.Trim() : "";
            string b = lastName != null ? lastName.Trim() : "";
            if (a.Length == 0 && b.Length == 0) return "";
            if (b.Length == 0) return a;
            if (a.Length == 0) return b;
            return a + " " + b;
        }
    }

    public string FileNameSafe
    {
        get
        {
            string a = firstName != null ? firstName.Trim() : "";
            string b = lastName != null ? lastName.Trim() : "";
            if (a.Length == 0 && b.Length == 0) return "Hasta";
            if (b.Length == 0) return a;
            if (a.Length == 0) return b;
            return a + "_" + b;
        }
    }

    public bool IsValidForSession()
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName)) return false;
        if (heightCm < 100f || heightCm > 250f) return false;
        if (!HasValidConsent) return false;
        return measureRightArm || measureLeftArm;
    }

    public int PlannedMovementCount =>
        plannedMovementIds != null ? plannedMovementIds.Length : 0;

    public bool TryGetPlannedMovementAt(int index, out MovementId id)
    {
        id = ExerciseCatalog.DefaultMovementId;
        if (plannedMovementIds == null || index < 0 || index >= plannedMovementIds.Length)
            return false;
        id = ExerciseCatalog.ClampMovement(plannedMovementIds[index]);
        return ExerciseCatalog.IsLiveReady(id);
    }

    public bool TryGetCurrentPlannedMovement(out MovementId id)
    {
        if (PlannedMovementCount <= 0)
        {
            id = ExerciseCatalog.ClampMovement(preferredMovementId);
            return false;
        }

        int i = plannedMovementIndex;
        if (i < 0) i = 0;
        if (i >= plannedMovementIds.Length) i = plannedMovementIds.Length - 1;
        return TryGetPlannedMovementAt(i, out id);
    }

    public bool HasRemainingVisitMovements()
    {
        return PlannedMovementCount > 1 && plannedMovementIndex < PlannedMovementCount - 1;
    }

    public void AdvancePlannedMovement()
    {
        if (HasRemainingVisitMovements())
            plannedMovementIndex++;
    }

    /// <summary>Yalnızca canlı hareketler; sıra korunur; en fazla MaxPlannedMovements.</summary>
    public void SetPlannedMovements(int[] source, int startIndex)
    {
        if (source == null || source.Length == 0)
        {
            plannedMovementIds = new[] { preferredMovementId };
            plannedMovementIndex = 0;
            return;
        }

        int cap = source.Length < MaxPlannedMovements ? source.Length : MaxPlannedMovements;
        int[] buf = new int[cap];
        int n = 0;
        for (int i = 0; i < source.Length && n < cap; i++)
        {
            MovementId id = ExerciseCatalog.ClampMovement(source[i]);
            if (!ExerciseCatalog.IsLiveReady(id)) continue;
            bool dup = false;
            for (int j = 0; j < n; j++)
            {
                if (buf[j] == (int)id)
                {
                    dup = true;
                    break;
                }
            }
            if (dup) continue;
            buf[n++] = (int)id;
        }

        if (n <= 0)
        {
            plannedMovementIds = new[] { preferredMovementId };
            plannedMovementIndex = 0;
            return;
        }

        if (n == buf.Length)
            plannedMovementIds = buf;
        else
        {
            plannedMovementIds = new int[n];
            for (int i = 0; i < n; i++)
                plannedMovementIds[i] = buf[i];
        }

        plannedMovementIndex = startIndex < 0 ? 0 : startIndex;
        if (plannedMovementIndex >= n)
            plannedMovementIndex = n - 1;
        preferredMovementId = plannedMovementIds[plannedMovementIndex];
        if (ExerciseCatalog.TryGet((MovementId)preferredMovementId, out ExerciseDefinition def))
            preferredBodyRegionId = (int)def.RegionId;
    }
}
