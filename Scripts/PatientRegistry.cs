using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Yerel çoklu hasta kaydı. KVKK: yalnızca persistentDataPath; buluta gönderilmez.
/// SaMD Class B: aktif hasta seçimi rapor/seans bağlamını belirler.
/// </summary>
[Serializable]
public class RegisteredPatient
{
    public string patientId = "";
    public string firstName = "";
    public string lastName = "";
    public float heightCm = 170f;
    public int ageYears;
    public int gender;
    public bool measureRightArm = true;
    public bool measureLeftArm = true;
    public bool sequentialBothArms;
    /// <summary>Neden fizyoterapi (serbest metin; teşhis değildir). KVKK yerel.</summary>
    public string reasonForCare = "";
    public bool consentAccepted;
    public int consentVersion;
    public string consentAcceptedAt = "";
    public string lastSessionAt = "";
    public float lastMaxRom;
    public int sessionCount;
    /// <summary>Hasta tercih edilen bölge/hareket (egzersiz filtresi).</summary>
    public int preferredBodyRegionId = (int)BodyRegionId.Shoulder;
    public int preferredMovementId = (int)MovementId.ShoulderFlexion;
    public float lastSessionTargetAngle;
    public int lastSessionTargetReps;
    public bool hasSessionTargets;
    public int[] plannedMovementIds;
    public int plannedMovementIndex;

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

    public PatientProfile ToProfile()
    {
        return new PatientProfile
        {
            patientId = patientId ?? "",
            firstName = firstName ?? "",
            lastName = lastName ?? "",
            heightCm = heightCm,
            ageYears = ageYears,
            gender = gender,
            measureRightArm = measureRightArm,
            measureLeftArm = measureLeftArm,
            sequentialBothArms = sequentialBothArms,
            reasonForCare = reasonForCare ?? "",
            consentAccepted = consentAccepted,
            consentVersion = consentVersion,
            consentAcceptedAt = consentAcceptedAt ?? "",
            lastUpdated = lastSessionAt ?? "",
            preferredBodyRegionId = preferredBodyRegionId,
            preferredMovementId = preferredMovementId,
            lastSessionTargetAngle = lastSessionTargetAngle,
            lastSessionTargetReps = lastSessionTargetReps,
            hasSessionTargets = hasSessionTargets,
            plannedMovementIds = plannedMovementIds,
            plannedMovementIndex = plannedMovementIndex
        };
    }

    public static RegisteredPatient FromProfile(PatientProfile p)
    {
        if (p == null) return null;
        return new RegisteredPatient
        {
            patientId = string.IsNullOrEmpty(p.patientId) ? Guid.NewGuid().ToString("N") : p.patientId,
            firstName = p.firstName ?? "",
            lastName = p.lastName ?? "",
            heightCm = p.heightCm,
            ageYears = p.ageYears,
            gender = p.gender,
            measureRightArm = p.measureRightArm,
            measureLeftArm = p.measureLeftArm,
            sequentialBothArms = p.sequentialBothArms,
            reasonForCare = PatientProfile.NormalizeReasonForCare(p.reasonForCare),
            consentAccepted = p.consentAccepted,
            consentVersion = p.consentVersion,
            consentAcceptedAt = p.consentAcceptedAt ?? "",
            preferredBodyRegionId = p.preferredBodyRegionId,
            preferredMovementId = p.preferredMovementId,
            lastSessionTargetAngle = p.lastSessionTargetAngle,
            lastSessionTargetReps = p.lastSessionTargetReps,
            hasSessionTargets = p.hasSessionTargets,
            plannedMovementIds = p.plannedMovementIds,
            plannedMovementIndex = p.plannedMovementIndex
        };
    }

    public static RegisteredPatient FromSession(SessionEntry e)
    {
        if (e == null) return null;
        string fn = e.firstName != null ? e.firstName.Trim() : "";
        string ln = e.lastName != null ? e.lastName.Trim() : "";
        if (fn.Length == 0 && ln.Length == 0) return null;
        return new RegisteredPatient
        {
            patientId = Guid.NewGuid().ToString("N"),
            firstName = fn,
            lastName = ln,
            heightCm = e.heightCm > 0f ? e.heightCm : 170f,
            ageYears = e.ageYears,
            gender = e.gender,
            measureRightArm = e.rightArmEnabled || e.rightMaxROM > 0f,
            measureLeftArm = e.leftArmEnabled || e.leftMaxROM > 0f,
            lastSessionAt = e.dateTime ?? "",
            lastMaxRom = Mathf.Max(e.rightMaxROM, e.leftMaxROM, e.maxROM),
            sessionCount = 1,
            preferredBodyRegionId = e.bodyRegionId,
            preferredMovementId = e.movementId
        };
    }
}

[Serializable]
public class PatientRegistryData
{
    public List<RegisteredPatient> patients = new List<RegisteredPatient>();
    public string activePatientId = "";
}

/// <summary>Kayıt birleştirme / arama yardımcıları (heap yalnızca menü/UI yolunda).</summary>
public static class PatientRegistry
{
    public const int RecentCount = 8;
    private static readonly CompareInfo TrCompare =
        CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    public static string NameKey(string firstName, string lastName)
    {
        string a = firstName != null ? firstName.Trim() : "";
        string b = lastName != null ? lastName.Trim() : "";
        return (a + "|" + b).ToLowerInvariant();
    }

    public static void UpsertFromProfile(PatientRegistryData data, PatientProfile profile)
    {
        if (data == null || profile == null) return;
        if (string.IsNullOrWhiteSpace(profile.firstName) && string.IsNullOrWhiteSpace(profile.lastName))
            return;

        if (string.IsNullOrEmpty(profile.patientId))
            profile.patientId = Guid.NewGuid().ToString("N");

        if (data.patients == null) data.patients = new List<RegisteredPatient>();

        int idx = IndexOfId(data, profile.patientId);
        if (idx < 0)
            idx = IndexOfName(data, profile.firstName, profile.lastName);

        RegisteredPatient rp = RegisteredPatient.FromProfile(profile);
        if (idx >= 0)
        {
            RegisteredPatient old = data.patients[idx];
            rp.patientId = old.patientId;
            profile.patientId = old.patientId;
            rp.lastSessionAt = old.lastSessionAt;
            rp.lastMaxRom = old.lastMaxRom;
            rp.sessionCount = old.sessionCount;
            data.patients[idx] = rp;
        }
        else
        {
            data.patients.Add(rp);
        }

        data.activePatientId = profile.patientId;
    }

    public static void SyncFromHistory(PatientRegistryData data, PatientHistory history)
    {
        if (data == null) return;
        if (data.patients == null) data.patients = new List<RegisteredPatient>();
        if (history == null || history.sessions == null) return;

        for (int i = 0; i < history.sessions.Count; i++)
        {
            SessionEntry s = history.sessions[i];
            if (s == null) continue;
            string fn = s.firstName != null ? s.firstName.Trim() : "";
            string ln = s.lastName != null ? s.lastName.Trim() : "";
            if (fn.Length == 0 && ln.Length == 0) continue;

            int idx = IndexOfName(data, fn, ln);
            float rom = Mathf.Max(s.rightMaxROM, s.leftMaxROM, s.maxROM);
            if (idx < 0)
            {
                RegisteredPatient created = RegisteredPatient.FromSession(s);
                if (created != null) data.patients.Add(created);
            }
            else
            {
                RegisteredPatient rp = data.patients[idx];
                if (s.heightCm > 0f) rp.heightCm = s.heightCm;
                if (s.ageYears > 0) rp.ageYears = s.ageYears;
                rp.gender = s.gender;
                if (IsNewerSession(s.dateTime, rp.lastSessionAt))
                {
                    rp.lastSessionAt = s.dateTime ?? rp.lastSessionAt;
                    rp.lastMaxRom = rom;
                }
                else if (rom > rp.lastMaxRom)
                {
                    rp.lastMaxRom = rom;
                }
                data.patients[idx] = rp;
            }
        }

        // sessionCount yeniden say (Sync tekrar çağrılabilir)
        RecountSessions(data, history);
    }

    public static void TouchSession(PatientRegistryData data, SessionEntry entry)
    {
        if (data == null || entry == null) return;
        if (data.patients == null) data.patients = new List<RegisteredPatient>();

        string fn = entry.firstName != null ? entry.firstName.Trim() : "";
        string ln = entry.lastName != null ? entry.lastName.Trim() : "";
        if (fn.Length == 0 && ln.Length == 0) return;

        int idx = IndexOfName(data, fn, ln);
        float rom = Mathf.Max(entry.rightMaxROM, entry.leftMaxROM, entry.maxROM);
        if (idx < 0)
        {
            RegisteredPatient created = RegisteredPatient.FromSession(entry);
                if (created != null)
                {
                    data.patients.Add(created);
                    if (string.IsNullOrEmpty(data.activePatientId))
                        data.activePatientId = created.patientId;
                }
            return;
        }

        RegisteredPatient rp = data.patients[idx];
        rp.sessionCount = Mathf.Max(1, rp.sessionCount + 1);
        rp.lastSessionAt = entry.dateTime ?? rp.lastSessionAt;
        rp.lastMaxRom = rom;
        if (entry.heightCm > 0f) rp.heightCm = entry.heightCm;
        if (entry.ageYears > 0) rp.ageYears = entry.ageYears;
        data.patients[idx] = rp;
    }

    public static RegisteredPatient FindById(PatientRegistryData data, string id)
    {
        if (data == null || data.patients == null || string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < data.patients.Count; i++)
        {
            if (data.patients[i] != null && data.patients[i].patientId == id)
                return data.patients[i];
        }
        return null;
    }

    public static List<RegisteredPatient> GetAllSorted(PatientRegistryData data)
    {
        var list = new List<RegisteredPatient>();
        if (data == null || data.patients == null) return list;
        for (int i = 0; i < data.patients.Count; i++)
        {
            if (data.patients[i] != null && !string.IsNullOrEmpty(data.patients[i].DisplayName))
                list.Add(data.patients[i]);
        }
        list.Sort(CompareByDisplayName);
        return list;
    }

    public static List<RegisteredPatient> GetRecent(PatientRegistryData data, int count)
    {
        var list = new List<RegisteredPatient>();
        if (data == null || data.patients == null) return list;
        for (int i = 0; i < data.patients.Count; i++)
        {
            if (data.patients[i] != null && !string.IsNullOrEmpty(data.patients[i].DisplayName))
                list.Add(data.patients[i]);
        }
        list.Sort(CompareByLastSessionDesc);
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        return list;
    }

    public static List<RegisteredPatient> FilterBySearch(List<RegisteredPatient> source, string query)
    {
        var result = new List<RegisteredPatient>();
        if (source == null) return result;
        string q = query != null ? query.Trim() : "";
        if (q.Length == 0)
        {
            for (int i = 0; i < source.Count; i++) result.Add(source[i]);
            return result;
        }

        for (int i = 0; i < source.Count; i++)
        {
            RegisteredPatient p = source[i];
            if (p == null) continue;
            string hay = (p.firstName + " " + p.lastName).Trim();
            if (TrCompare.IndexOf(hay, q, CompareOptions.IgnoreCase) >= 0)
                result.Add(p);
        }
        return result;
    }

    public static string FormatRowSubtitle(RegisteredPatient p)
    {
        if (p == null) return "";
        var sb = new StringBuilder(64);
        if (p.ageYears > 0)
            sb.Append(p.ageYears).Append(Loc.T("picker.yearsSuffix"));
        if (!string.IsNullOrEmpty(p.lastSessionAt))
        {
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(p.lastSessionAt);
        }
        if (p.lastMaxRom > 0f)
        {
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(p.lastMaxRom.ToString("F0")).Append('°');
        }
        if (p.sessionCount > 0)
        {
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(Loc.Format("picker.sessions", p.sessionCount));
        }
        return sb.ToString();
    }

    private static void RecountSessions(PatientRegistryData data, PatientHistory history)
    {
        for (int i = 0; i < data.patients.Count; i++)
        {
            RegisteredPatient rp = data.patients[i];
            if (rp == null) continue;
            int n = 0;
            string lastAt = rp.lastSessionAt;
            float lastRom = 0f;
            DateTime best = DateTime.MinValue;
            for (int s = 0; s < history.sessions.Count; s++)
            {
                SessionEntry e = history.sessions[s];
                if (e == null) continue;
                if (!NamesMatch(rp.firstName, rp.lastName, e.firstName, e.lastName)) continue;
                n++;
                if (SessionHistoryFilter.TryParseSessionDate(e.dateTime, out DateTime dt) && dt >= best)
                {
                    best = dt;
                    lastAt = e.dateTime;
                    lastRom = Mathf.Max(e.rightMaxROM, e.leftMaxROM, e.maxROM);
                }
            }
            rp.sessionCount = n;
            if (n > 0)
            {
                rp.lastSessionAt = lastAt;
                if (lastRom > 0f) rp.lastMaxRom = lastRom;
            }
            data.patients[i] = rp;
        }
    }

    private static bool IsNewerSession(string candidate, string current)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        if (string.IsNullOrEmpty(current)) return true;
        bool cOk = SessionHistoryFilter.TryParseSessionDate(candidate, out DateTime cDt);
        bool curOk = SessionHistoryFilter.TryParseSessionDate(current, out DateTime curDt);
        if (cOk && curOk) return cDt >= curDt;
        return string.CompareOrdinal(candidate, current) >= 0;
    }

    private static int IndexOfId(PatientRegistryData data, string id)
    {
        if (string.IsNullOrEmpty(id) || data.patients == null) return -1;
        for (int i = 0; i < data.patients.Count; i++)
        {
            if (data.patients[i] != null && data.patients[i].patientId == id) return i;
        }
        return -1;
    }

    private static int IndexOfName(PatientRegistryData data, string first, string last)
    {
        if (data.patients == null) return -1;
        for (int i = 0; i < data.patients.Count; i++)
        {
            RegisteredPatient p = data.patients[i];
            if (p != null && NamesMatch(p.firstName, p.lastName, first, last)) return i;
        }
        return -1;
    }

    private static bool NamesMatch(string aFn, string aLn, string bFn, string bLn)
    {
        return string.Equals(
            NameKey(aFn, aLn),
            NameKey(bFn, bLn),
            StringComparison.Ordinal);
    }

    private static int CompareByDisplayName(RegisteredPatient a, RegisteredPatient b)
    {
        string an = a != null ? a.DisplayName : "";
        string bn = b != null ? b.DisplayName : "";
        return TrCompare.Compare(an, bn, CompareOptions.IgnoreCase);
    }

    private static int CompareByLastSessionDesc(RegisteredPatient a, RegisteredPatient b)
    {
        string at = a != null ? a.lastSessionAt : "";
        string bt = b != null ? b.lastSessionAt : "";
        bool aOk = SessionHistoryFilter.TryParseSessionDate(at, out DateTime aDt);
        bool bOk = SessionHistoryFilter.TryParseSessionDate(bt, out DateTime bDt);
        if (aOk && bOk) return bDt.CompareTo(aDt);
        if (aOk) return -1;
        if (bOk) return 1;
        return CompareByDisplayName(a, b);
    }
}
