using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Hasta başına rapor klasörü + AES şifreleme (diskte düz HTML yok).
/// Yazma: cihaz anahtarıyla; okuma/açma: klinisyen PIN.
/// KVKK: yerel gizlilik; mutlak OS kilidi değildir (aynı Windows kullanıcısı key dosyasını görebilir).
/// SaMD Class B: karar-destek dosyaları.
/// </summary>
public static class PatientVault
{
    public const string EncExtension = ".enc";
    public const string SubdirHtml = "Html";
    public const string SubdirCsv = "Csv";
    public const string SubdirExcel = "Excel";
    /// <summary>Hasta/Hareketler/{Omuz_fleksiyonu}/Html|Csv|Excel</summary>
    public const string SubdirMovements = "Hareketler";
    public const string NotebookSuffixTr = "_NotDefteri.txt";
    public const string NotebookSuffixEn = "_Notebook.txt";

    private const string Magic = "HTPV1";
    private const int KeyBytes = 32;
    private const int IvBytes = 16;
    private const int PbkdfIterations = 100000;

    private const string PrefDekPinWrap = "vault_dek_pin_wrap";
    private const string PrefDekDeviceWrap = "vault_dek_device_wrap";
    private const string PrefVaultSalt = "vault_kek_salt";

    /// <summary>PIN doğrulandıktan sonra bellek içi DEK (oturum).</summary>
    private static byte[] _sessionDek;

    public static bool HasSessionUnlock => _sessionDek != null && _sessionDek.Length == KeyBytes;

    public static void ClearSessionUnlock()
    {
        if (_sessionDek == null) return;
        Array.Clear(_sessionDek, 0, _sessionDek.Length);
        _sessionDek = null;
    }

    /// <summary>PIN sıfırlanırken wrap'leri sil — eski .enc dosyalar okunamaz hale gelir.</summary>
    public static void ClearWrappedKeys()
    {
        PlayerPrefs.DeleteKey(PrefDekPinWrap);
        PlayerPrefs.DeleteKey(PrefDekDeviceWrap);
        PlayerPrefs.DeleteKey(PrefVaultSalt);
        PlayerPrefs.Save();
        ClearSessionUnlock();
    }

    public static string PatientsRoot
    {
        get
        {
            string dir = Path.Combine(ReportExporter.ReportsDirectory, "Patients");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string GetPatientDirectory(PatientProfile profile)
    {
        string safe = SanitizeFolder(profile != null ? profile.FileNameSafe : "Hasta");
        string dir = Path.Combine(PatientsRoot, safe);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        // Gezginde biraz daha az görünür (zayıf koruma)
        try { File.SetAttributes(dir, File.GetAttributes(dir) | FileAttributes.Hidden); } catch { }
        EnsurePatientFolderLayout(dir, safe);
        return dir;
    }

    public static string GetPatientDirectory(SessionEntry entry)
    {
        string name = "Hasta";
        if (entry != null)
        {
            string a = entry.firstName != null ? entry.firstName.Trim() : "";
            string b = entry.lastName != null ? entry.lastName.Trim() : "";
            if (a.Length > 0 || b.Length > 0)
                name = (a.Length == 0 ? b : (b.Length == 0 ? a : a + "_" + b));
        }
        string safe = SanitizeFolder(name);
        string dir = Path.Combine(PatientsRoot, safe);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        try { File.SetAttributes(dir, File.GetAttributes(dir) | FileAttributes.Hidden); } catch { }
        EnsurePatientFolderLayout(dir, safe);
        return dir;
    }

    /// <summary>
    /// Hasta klasörü düzeni:
    /// NotDefteri.txt + genel ilerleme HTML (kök) + Html/Csv/Excel + Hareketler/{hareket}/
    /// </summary>
    public static void EnsurePatientFolderLayout(string patientDir, string namePrefix)
    {
        if (string.IsNullOrEmpty(patientDir)) return;
        if (!Directory.Exists(patientDir)) Directory.CreateDirectory(patientDir);
        string prefix = SanitizeFolder(string.IsNullOrEmpty(namePrefix) ? "Hasta" : namePrefix);
        EnsureSubdir(patientDir, SubdirHtml);
        EnsureSubdir(patientDir, SubdirCsv);
        EnsureSubdir(patientDir, SubdirExcel);
        EnsureNotebookFile(patientDir, prefix);
        EnsureLiveMovementFolders(patientDir);
    }

    /// <summary>Canlı katalog hareketleri için Html/Csv/Excel alt klasörleri.</summary>
    public static void EnsureLiveMovementFolders(string patientDir)
    {
        if (string.IsNullOrEmpty(patientDir)) return;
        MovementId[] live = new MovementId[16];
        int n = ExerciseCatalog.CopyLiveMovements(live);
        for (int i = 0; i < n; i++)
            EnsureMovementReportLayout(patientDir, live[i]);
    }

    /// <summary>Reports/Patients/{Hasta}/Hareketler/{Omuz_fleksiyonu}/</summary>
    public static string EnsureMovementReportLayout(string patientDir, MovementId movementId)
    {
        string moves = EnsureSubdir(patientDir, SubdirMovements);
        string moveDir = EnsureSubdir(moves, MovementFolderSlug(movementId));
        EnsureSubdir(moveDir, SubdirHtml);
        EnsureSubdir(moveDir, SubdirCsv);
        EnsureSubdir(moveDir, SubdirExcel);
        return moveDir;
    }

    public static string MovementFolderSlug(MovementId movementId)
    {
        MovementId id = ExerciseCatalog.ClampMovement((int)movementId);
        string label = ExerciseCatalog.ReportFolderLabel(id);
        string slug = SanitizeFolder(label);
        if (string.IsNullOrEmpty(slug) || slug == "Hasta")
            slug = SanitizeFolder(id.ToString());
        return slug;
    }

    public static string GetMovementDirectory(PatientProfile profile, MovementId movementId)
    {
        string root = GetPatientDirectory(profile);
        return EnsureMovementReportLayout(root, movementId);
    }

    public static string GetMovementHtmlDirectory(PatientProfile profile, MovementId movementId)
    {
        return EnsureSubdir(GetMovementDirectory(profile, movementId), SubdirHtml);
    }

    public static string GetMovementCsvDirectory(PatientProfile profile, MovementId movementId)
    {
        return EnsureSubdir(GetMovementDirectory(profile, movementId), SubdirCsv);
    }

    public static string GetMovementExcelDirectory(PatientProfile profile, MovementId movementId)
    {
        return EnsureSubdir(GetMovementDirectory(profile, movementId), SubdirExcel);
    }

    public static string GetHtmlDirectory(PatientProfile profile)
    {
        string root = GetPatientDirectory(profile);
        return EnsureSubdir(root, SubdirHtml);
    }

    public static string GetCsvDirectory(PatientProfile profile)
    {
        string root = GetPatientDirectory(profile);
        return EnsureSubdir(root, SubdirCsv);
    }

    public static string GetExcelDirectory(PatientProfile profile)
    {
        string root = GetPatientDirectory(profile);
        return EnsureSubdir(root, SubdirExcel);
    }

    public static string GetPatientNamePrefix(PatientProfile profile)
    {
        return SanitizeFolder(profile != null ? profile.FileNameSafe : "Hasta");
    }

    /// <summary>
    /// Ad/soyad değişince Patients/{EskiAd} klasörünü yeni ada taşır (raporlar kaybolmasın).
    /// Hedef varsa birleştirmez — çakışmada eski klasör bırakılır.
    /// </summary>
    public static void TryRenamePatientFolder(string oldFirst, string oldLast, string newFirst, string newLast)
    {
        string oldName = BuildFileNameSafe(oldFirst, oldLast);
        string newName = BuildFileNameSafe(newFirst, newLast);
        string oldSafe = SanitizeFolder(oldName);
        string newSafe = SanitizeFolder(newName);
        if (string.IsNullOrEmpty(oldSafe) || string.IsNullOrEmpty(newSafe)) return;
        if (string.Equals(oldSafe, newSafe, StringComparison.OrdinalIgnoreCase)) return;

        string oldDir = Path.Combine(PatientsRoot, oldSafe);
        string newDir = Path.Combine(PatientsRoot, newSafe);
        if (!Directory.Exists(oldDir)) return;
        if (Directory.Exists(newDir)) return;

        try
        {
            Directory.Move(oldDir, newDir);
            try { File.SetAttributes(newDir, File.GetAttributes(newDir) | FileAttributes.Hidden); } catch { }
        }
        catch (Exception)
        {
            // Klasör taşınamazsa sessiz — geçmiş JSON zaten güncellenir
        }
    }

    private static string BuildFileNameSafe(string first, string last)
    {
        string a = first != null ? first.Trim() : "";
        string b = last != null ? last.Trim() : "";
        if (a.Length == 0 && b.Length == 0) return "Hasta";
        if (b.Length == 0) return a;
        if (a.Length == 0) return b;
        return a + "_" + b;
    }

    /// <summary>Kökteki düz metin not defteri yolu (şifrelenmez — dışarıdan Notepad ile açılabilir).</summary>
    public static string GetNotebookPath(PatientProfile profile)
    {
        string root = GetPatientDirectory(profile);
        string prefix = GetPatientNamePrefix(profile);
        return Path.Combine(root, prefix + NotebookSuffixTr);
    }

    public static string ReadNotebook(PatientProfile profile)
    {
        string path = GetNotebookPath(profile);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
        try { return File.ReadAllText(path, Encoding.UTF8); }
        catch { return ""; }
    }

    public static bool WriteNotebook(PatientProfile profile, string text)
    {
        string path = GetNotebookPath(profile);
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, text ?? "", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return true;
        }
        catch { return false; }
    }

    private static string EnsureSubdir(string patientDir, string sub)
    {
        string d = Path.Combine(patientDir, sub);
        if (!Directory.Exists(d)) Directory.CreateDirectory(d);
        return d;
    }

    private static void EnsureNotebookFile(string patientDir, string prefix)
    {
        string path = Path.Combine(patientDir, prefix + NotebookSuffixTr);
        if (File.Exists(path)) return;
        // Eski İngilizce ad varsa taşı
        string en = Path.Combine(patientDir, prefix + NotebookSuffixEn);
        if (File.Exists(en))
        {
            try { File.Move(en, path); return; } catch { }
        }
        try
        {
            File.WriteAllText(path,
                "# " + prefix + " — Hasta not defteri\r\n"
                + "# Bu dosya uygulamadan veya Notepad ile düzenlenebilir.\r\n"
                + "# Klinik notlar (yerel; KVKK — dışarı gönderilmez).\r\n\r\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch { }
    }

    /// <summary>Kökteki dağınık html/csv/xlsx dosyalarını Html/Csv/Excel altına taşır.</summary>
    public static int MigratePatientSubfolders()
    {
        if (!Directory.Exists(PatientsRoot)) return 0;
        string[] dirs;
        try { dirs = Directory.GetDirectories(PatientsRoot); }
        catch { return 0; }

        int moved = 0;
        for (int d = 0; d < dirs.Length; d++)
        {
            string patientDir = dirs[d];
            string prefix = Path.GetFileName(patientDir);
            EnsurePatientFolderLayout(patientDir, prefix);

            string[] files;
            try { files = Directory.GetFiles(patientDir); }
            catch { continue; }

            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) continue;
                if (IsNotebookFileName(name)) continue;
                if (IsClinicianReportFileName(name)) continue;
                if (IsCompareReportFileName(name)) continue;

                string lower = name.ToLowerInvariant();
                string destSub = null;
                bool keepAtRoot = IsProgressReportFileName(name);

                if (keepAtRoot && (lower.Contains(".html")))
                    continue; // genel ilerleme kökte kalır

                if (lower.Contains(".html"))
                    destSub = SubdirHtml;
                else if (lower.Contains(".csv"))
                    destSub = SubdirCsv;
                else if (lower.Contains(".xlsx"))
                    destSub = SubdirExcel;
                else
                    continue;

                string destDir = EnsureSubdir(patientDir, destSub);
                string dest = Path.Combine(destDir, name);
                if (File.Exists(dest))
                {
                    try { File.Delete(path); moved++; } catch { }
                    continue;
                }
                try
                {
                    File.Move(path, dest);
                    moved++;
                }
                catch { }
            }
        }
        return moved;
    }

    public static bool IsNotebookFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        string lower = fileName.ToLowerInvariant();
        return lower.EndsWith("_notdefteri.txt")
               || lower.EndsWith("_notebook.txt")
               || lower.EndsWith("_notdefteri.txt.enc")
               || lower.EndsWith("_notebook.txt.enc");
    }

    public static bool IsProgressReportFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        return fileName.IndexOf("_Ilerleme_", StringComparison.OrdinalIgnoreCase) >= 0
               || fileName.IndexOf("_Progress_", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>PIN ilk kurulum veya doğrulama sonrası DEK hazırla.</summary>
    public static bool EnsureDekAfterPin(string pin)
    {
        if (!ClinicianPin.IsValidFormat(pin)) return false;

        byte[] dek = TryUnwrapWithPin(pin);
        if (dek == null)
        {
            // Eski wrap varsa yanlış PIN — mevcut şifreli dosyaları ezme
            if (!string.IsNullOrEmpty(PlayerPrefs.GetString(PrefDekPinWrap, "")))
                return false;

            dek = new byte[KeyBytes];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(dek);
            if (!WrapAndStore(dek, pin))
            {
                Array.Clear(dek, 0, dek.Length);
                return false;
            }
        }

        _sessionDek = dek;
        WrapDeviceOnly(dek);
        return true;
    }

    public static bool UnlockSession(string pin)
    {
        if (!ClinicianPin.Verify(pin)) return false;
        return EnsureDekAfterPin(pin);
    }

    /// <summary>Düz metni hasta klasörüne şifreli yazar. Dönüş: .enc yolu.</summary>
    public static string WriteEncrypted(string directory, string fileName, string utf8Content)
    {
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName)) return null;
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

        byte[] dek = GetDekForWrite();
        if (dek == null)
        {
            // PIN hiç kurulmamış: düz yaz (geçiş); klasör yine hasta bazlı
            string plain = Path.Combine(directory, fileName);
            // CSV: Excel UTF-8 tanıması için BOM
            Encoding enc = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
                : new UTF8Encoding(false);
            File.WriteAllText(plain, utf8Content ?? "", enc);
            return plain;
        }

        string encPath = Path.Combine(directory, fileName + EncExtension);
        Encoding textEnc = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            : new UTF8Encoding(false);
        byte[] plainBytes = textEnc.GetBytes(utf8Content ?? "");
        byte[] blob = Encrypt(plainBytes, dek);
        File.WriteAllBytes(encPath, blob);
        return encPath;
    }

    /// <summary>Binary içeriği (ör. .xlsx) hasta klasörüne şifreli yazar.</summary>
    public static string WriteEncryptedBytes(string directory, string fileName, byte[] content)
    {
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName)) return null;
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

        byte[] payload = content ?? Array.Empty<byte>();
        byte[] dek = GetDekForWrite();
        if (dek == null)
        {
            string plain = Path.Combine(directory, fileName);
            File.WriteAllBytes(plain, payload);
            return plain;
        }

        string encPath = Path.Combine(directory, fileName + EncExtension);
        byte[] blob = Encrypt(payload, dek);
        File.WriteAllBytes(encPath, blob);
        return encPath;
    }

    public static bool TryDecryptToTemp(string encOrPlainPath, out string tempPath)
    {
        tempPath = null;
        if (string.IsNullOrEmpty(encOrPlainPath) || !File.Exists(encOrPlainPath)) return false;

        if (!encOrPlainPath.EndsWith(EncExtension, StringComparison.OrdinalIgnoreCase))
        {
            tempPath = encOrPlainPath;
            return true;
        }

        // Okuma yalnızca PIN oturumu — cihaz anahtarı yazma içindir (KVKK).
        if (!HasSessionUnlock) return false;
        byte[] dek = _sessionDek;

        byte[] blob = File.ReadAllBytes(encOrPlainPath);
        if (!TryDecrypt(blob, dek, out byte[] plain)) return false;

        // Orijinal okunabilir ad (…Seans31_….html) — GUID kullanma
        string name = Path.GetFileName(encOrPlainPath);
        if (name.EndsWith(EncExtension, StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - EncExtension.Length);

        string tempDir = Path.Combine(Application.temporaryCachePath, "PatientVaultUnlock");
        if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
        tempPath = Path.Combine(tempDir, name);
        File.WriteAllBytes(tempPath, plain);
        return true;
    }

    /// <summary>Hasta klasörünü (Html/Csv/Excel + kök) temp altına çözer; klasör yolu döner.</summary>
    public static string UnlockPatientFolderToTemp(string patientDir)
    {
        if (string.IsNullOrEmpty(patientDir) || !Directory.Exists(patientDir)) return null;
        if (!HasSessionUnlock) return null;

        string tempRoot = Path.Combine(Application.temporaryCachePath, "PatientVaultUnlock", "OpenReports");
        if (Directory.Exists(tempRoot))
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
        Directory.CreateDirectory(tempRoot);
        try
        {
            File.SetAttributes(tempRoot, File.GetAttributes(tempRoot) & ~FileAttributes.Hidden);
        }
        catch { }

        string patientLabel = Path.GetFileName(patientDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        try
        {
            File.WriteAllText(
                Path.Combine(tempRoot, "_hasta.txt"),
                patientLabel ?? "",
                new UTF8Encoding(false));
        }
        catch { }

        // Klasör iskeleti
        EnsureSubdir(tempRoot, SubdirHtml);
        EnsureSubdir(tempRoot, SubdirCsv);
        EnsureSubdir(tempRoot, SubdirExcel);

        int copied = CopyUnlockedTree(patientDir, tempRoot);
        return copied > 0 || File.Exists(Path.Combine(tempRoot, "_hasta.txt")) ? tempRoot : null;
    }

    /// <summary>
    /// Hasta Html/ klasöründeki seans raporlarını (şifreli → düz) hedef klasöre yazar.
    /// İlerleme raporu göreli linkleri için; PIN oturumu gerekir.
    /// </summary>
    public static int MaterializeSessionHtmlBeside(PatientProfile profile, string destDir)
    {
        if (string.IsNullOrEmpty(destDir)) return 0;
        if (!HasSessionUnlock) return 0;
        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

        string htmlDir = GetHtmlDirectory(profile);
        int n = MaterializeHtmlFilesFromDir(htmlDir, destDir);

        string patientRoot = GetPatientDirectory(profile);
        if (!string.IsNullOrEmpty(patientRoot)
            && !string.Equals(patientRoot, htmlDir, StringComparison.OrdinalIgnoreCase))
            n += MaterializeHtmlFilesFromDir(patientRoot, destDir);

        string movesRoot = Path.Combine(patientRoot, SubdirMovements);
        if (Directory.Exists(movesRoot))
        {
            string[] moveDirs;
            try { moveDirs = Directory.GetDirectories(movesRoot); }
            catch { moveDirs = null; }
            if (moveDirs != null)
            {
                for (int i = 0; i < moveDirs.Length; i++)
                {
                    n += MaterializeHtmlFilesFromDir(Path.Combine(moveDirs[i], SubdirHtml), destDir);
                    n += MaterializeHtmlFilesFromDir(moveDirs[i], destDir);
                }
            }
        }

        return n;
    }

    private static int MaterializeHtmlFilesFromDir(string sourceDir, string destDir)
    {
        if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir)) return 0;
        string[] files;
        try { files = Directory.GetFiles(sourceDir); }
        catch { return 0; }

        int copied = 0;
        for (int i = 0; i < files.Length; i++)
        {
            string f = files[i];
            string name = Path.GetFileName(f);
            if (string.IsNullOrEmpty(name)) continue;
            if (IsClinicianReportFileName(name)) continue;
            if (IsCompareReportFileName(name)) continue;
            if (IsProgressReportFileName(name)) continue;
            if (IsNotebookFileName(name)) continue;

            string lower = name.ToLowerInvariant();
            bool isHtmlEnc = lower.EndsWith(".html" + EncExtension) || lower.EndsWith(".htm" + EncExtension);
            bool isHtmlPlain = (lower.EndsWith(".html") || lower.EndsWith(".htm")) && !isHtmlEnc;
            if (!isHtmlEnc && !isHtmlPlain) continue;

            string plainName = name;
            if (plainName.EndsWith(EncExtension, StringComparison.OrdinalIgnoreCase))
                plainName = plainName.Substring(0, plainName.Length - EncExtension.Length);
            if (plainName.IndexOf("_Seans", StringComparison.OrdinalIgnoreCase) < 0
                && plainName.IndexOf("_Session", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            string dest = Path.Combine(destDir, plainName);
            try
            {
                if (f.EndsWith(EncExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryDecryptToTemp(f, out string unlocked) || string.IsNullOrEmpty(unlocked))
                        continue;
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Copy(unlocked, dest, true);
                    copied++;
                }
                else
                {
                    File.Copy(f, dest, true);
                    copied++;
                }
            }
            catch { }
        }
        return copied;
    }

    private static int CopyUnlockedTree(string sourceDir, string destDir)
    {
        int copied = 0;
        string[] files;
        try { files = Directory.GetFiles(sourceDir); }
        catch { return 0; }

        for (int i = 0; i < files.Length; i++)
        {
            string f = files[i];
            string destName = Path.GetFileName(f);
            if (string.IsNullOrEmpty(destName)) continue;
            if (IsClinicianReportFileName(destName)) continue;

            if (destName.EndsWith(EncExtension, StringComparison.OrdinalIgnoreCase))
                destName = destName.Substring(0, destName.Length - EncExtension.Length);

            string dest = Path.Combine(destDir, destName);
            try
            {
                if (f.EndsWith(EncExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryDecryptToTemp(f, out string unlocked) || string.IsNullOrEmpty(unlocked))
                        continue;
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Copy(unlocked, dest, true);
                    copied++;
                }
                else
                {
                    File.Copy(f, dest, true);
                    copied++;
                }
            }
            catch { }
        }

        string[] subdirs;
        try { subdirs = Directory.GetDirectories(sourceDir); }
        catch { return copied; }

        for (int s = 0; s < subdirs.Length; s++)
        {
            string subName = Path.GetFileName(subdirs[s]);
            if (string.IsNullOrEmpty(subName)) continue;
            string destSub = Path.Combine(destDir, subName);
            if (!Directory.Exists(destSub)) Directory.CreateDirectory(destSub);
            copied += CopyUnlockedTree(subdirs[s], destSub);
        }
        return copied;
    }

    public static bool IsClinicianReportFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        return fileName.StartsWith("Clinician_", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCompareReportFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        string n = fileName;
        if (n.EndsWith(EncExtension, StringComparison.OrdinalIgnoreCase))
            n = n.Substring(0, n.Length - EncExtension.Length);
        return n.IndexOf("_Compare_", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("_Karsilastirma_", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Patients/{Ad}/ altındaki Clinician_*.html(.enc) dosyalarını Reports/Clinician/{Ad}/ altına taşır.
    /// </summary>
    public static int MigrateClinicianFilesOutOfPatientFolders()
    {
        string patientsRoot = PatientsRoot;
        if (!Directory.Exists(patientsRoot)) return 0;
        int moved = 0;
        string[] dirs;
        try { dirs = Directory.GetDirectories(patientsRoot); }
        catch { return 0; }

        for (int d = 0; d < dirs.Length; d++)
        {
            string patientDir = dirs[d];
            string folderName = Path.GetFileName(patientDir);
            string destDir = Path.Combine(ReportExporter.ReportsDirectory, "Clinician", folderName);
            moved += MoveMatchingFilesFromPatientTree(patientDir, destDir, IsClinicianReportFileName);
        }
        return moved;
    }

    /// <summary>
    /// Patients/{Ad}/Html (ve kök) altındaki *_Compare_* / *_Karsilastirma_* dosyalarını
    /// Reports/Compare/{Ad}/ altına taşır.
    /// </summary>
    public static int MigrateCompareFilesOutOfPatientFolders()
    {
        string patientsRoot = PatientsRoot;
        if (!Directory.Exists(patientsRoot)) return 0;
        int moved = 0;
        string[] dirs;
        try { dirs = Directory.GetDirectories(patientsRoot); }
        catch { return 0; }

        for (int d = 0; d < dirs.Length; d++)
        {
            string patientDir = dirs[d];
            string folderName = Path.GetFileName(patientDir);
            string destDir = Path.Combine(ReportExporter.ReportsDirectory, "Compare", folderName);
            moved += MoveMatchingFilesFromPatientTree(patientDir, destDir, IsCompareReportFileName);
        }
        return moved;
    }

    private static int MoveMatchingFilesFromPatientTree(
        string patientDir, string destDir, System.Func<string, bool> nameMatch)
    {
        if (string.IsNullOrEmpty(patientDir) || !Directory.Exists(patientDir) || nameMatch == null)
            return 0;

        int moved = 0;
        moved += MoveMatchingFilesInDir(patientDir, destDir, nameMatch);

        string htmlDir = Path.Combine(patientDir, SubdirHtml);
        if (Directory.Exists(htmlDir))
            moved += MoveMatchingFilesInDir(htmlDir, destDir, nameMatch);

        return moved;
    }

    private static int MoveMatchingFilesInDir(
        string sourceDir, string destDir, System.Func<string, bool> nameMatch)
    {
        string[] files;
        try { files = Directory.GetFiles(sourceDir); }
        catch { return 0; }

        int moved = 0;
        for (int i = 0; i < files.Length; i++)
        {
            string name = Path.GetFileName(files[i]);
            if (!nameMatch(name)) continue;
            try
            {
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                string dest = Path.Combine(destDir, name);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(files[i], dest);
                moved++;
            }
            catch { }
        }
        return moved;
    }

    public static string FindLatestProgressEnc(string patientDir)
    {
        if (string.IsNullOrEmpty(patientDir) || !Directory.Exists(patientDir)) return null;
        string[] patterns = { "*_Ilerleme_*.html.enc", "*_Progress_*.html.enc", "*_Ilerleme_*.html", "*_Progress_*.html" };
        string best = null;
        DateTime bestT = DateTime.MinValue;
        for (int p = 0; p < patterns.Length; p++)
        {
            string[] files;
            try { files = Directory.GetFiles(patientDir, patterns[p]); }
            catch { continue; }
            for (int i = 0; i < files.Length; i++)
            {
                DateTime t = File.GetLastWriteTimeUtc(files[i]);
                if (t > bestT) { bestT = t; best = files[i]; }
            }
        }
        return best;
    }

    public static PatientHistory FilterHistoryForPatient(PatientHistory history, PatientProfile profile)
    {
        return FilterHistoryForPatient(history, profile, fallbackToAll: true);
    }

    public static PatientHistory FilterHistoryForPatient(PatientHistory history, PatientProfile profile, bool fallbackToAll)
    {
        if (history == null || history.sessions == null) return new PatientHistory();
        if (profile == null || string.IsNullOrWhiteSpace(profile.firstName))
            return fallbackToAll ? history : new PatientHistory();

        string fn = profile.firstName.Trim();
        string ln = profile.lastName != null ? profile.lastName.Trim() : "";
        var filtered = new PatientHistory();
        for (int i = 0; i < history.sessions.Count; i++)
        {
            SessionEntry s = history.sessions[i];
            string sFn = s.firstName != null ? s.firstName.Trim() : "";
            string sLn = s.lastName != null ? s.lastName.Trim() : "";
            if (string.Equals(sFn, fn, StringComparison.OrdinalIgnoreCase)
                && string.Equals(sLn, ln, StringComparison.OrdinalIgnoreCase))
                filtered.sessions.Add(s);
        }
        if (fallbackToAll && filtered.sessions.Count == 0 && history.sessions.Count > 0)
            return history;
        return filtered;
    }

    private static byte[] GetDekForWrite()
    {
        if (_sessionDek != null) return _sessionDek;
        return TryUnwrapDevice();
    }

    private static bool WrapAndStore(byte[] dek, string pin)
    {
        string salt = Guid.NewGuid().ToString("N");
        byte[] pinKek = DeriveKey(pin, salt);
        string pinWrap = Convert.ToBase64String(Encrypt(dek, pinKek));
        Array.Clear(pinKek, 0, pinKek.Length);

        byte[] deviceKek = DeriveDeviceKey();
        string deviceWrap = Convert.ToBase64String(Encrypt(dek, deviceKek));
        Array.Clear(deviceKek, 0, deviceKek.Length);

        PlayerPrefs.SetString(PrefVaultSalt, salt);
        PlayerPrefs.SetString(PrefDekPinWrap, pinWrap);
        PlayerPrefs.SetString(PrefDekDeviceWrap, deviceWrap);
        PlayerPrefs.Save();
        return true;
    }

    private static void WrapDeviceOnly(byte[] dek)
    {
        byte[] deviceKek = DeriveDeviceKey();
        string deviceWrap = Convert.ToBase64String(Encrypt(dek, deviceKek));
        Array.Clear(deviceKek, 0, deviceKek.Length);
        PlayerPrefs.SetString(PrefDekDeviceWrap, deviceWrap);
        PlayerPrefs.Save();
    }

    private static byte[] TryUnwrapWithPin(string pin)
    {
        string salt = PlayerPrefs.GetString(PrefVaultSalt, "");
        string wrap = PlayerPrefs.GetString(PrefDekPinWrap, "");
        if (string.IsNullOrEmpty(salt) || string.IsNullOrEmpty(wrap)) return null;
        try
        {
            byte[] kek = DeriveKey(pin, salt);
            byte[] blob = Convert.FromBase64String(wrap);
            bool ok = TryDecrypt(blob, kek, out byte[] dek);
            Array.Clear(kek, 0, kek.Length);
            return ok ? dek : null;
        }
        catch { return null; }
    }

    private static byte[] TryUnwrapDevice()
    {
        string wrap = PlayerPrefs.GetString(PrefDekDeviceWrap, "");
        if (string.IsNullOrEmpty(wrap)) return null;
        try
        {
            byte[] kek = DeriveDeviceKey();
            byte[] blob = Convert.FromBase64String(wrap);
            bool ok = TryDecrypt(blob, kek, out byte[] dek);
            Array.Clear(kek, 0, kek.Length);
            return ok ? dek : null;
        }
        catch { return null; }
    }

    private static byte[] DeriveKey(string pin, string salt)
    {
        using (var pbkdf = new Rfc2898DeriveBytes(pin, Encoding.UTF8.GetBytes(salt), PbkdfIterations, HashAlgorithmName.SHA256))
            return pbkdf.GetBytes(KeyBytes);
    }

    private static byte[] DeriveDeviceKey()
    {
        string material = Application.persistentDataPath + "|HandTrackingPatientVault|v1";
        using (var sha = SHA256.Create())
            return sha.ComputeHash(Encoding.UTF8.GetBytes(material));
    }

    private static byte[] Encrypt(byte[] plain, byte[] key)
    {
        using (var aes = Aes.Create())
        {
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.GenerateIV();
            using (var enc = aes.CreateEncryptor())
            {
                byte[] cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
                byte[] magic = Encoding.ASCII.GetBytes(Magic);
                byte[] outBuf = new byte[magic.Length + IvBytes + cipher.Length];
                Buffer.BlockCopy(magic, 0, outBuf, 0, magic.Length);
                Buffer.BlockCopy(aes.IV, 0, outBuf, magic.Length, IvBytes);
                Buffer.BlockCopy(cipher, 0, outBuf, magic.Length + IvBytes, cipher.Length);
                return outBuf;
            }
        }
    }

    private static bool TryDecrypt(byte[] blob, byte[] key, out byte[] plain)
    {
        plain = null;
        byte[] magic = Encoding.ASCII.GetBytes(Magic);
        if (blob == null || blob.Length < magic.Length + IvBytes + 1) return false;
        for (int i = 0; i < magic.Length; i++)
            if (blob[i] != magic[i]) return false;

        byte[] iv = new byte[IvBytes];
        Buffer.BlockCopy(blob, magic.Length, iv, 0, IvBytes);
        int cipherLen = blob.Length - magic.Length - IvBytes;
        byte[] cipher = new byte[cipherLen];
        Buffer.BlockCopy(blob, magic.Length + IvBytes, cipher, 0, cipherLen);

        try
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                using (var dec = aes.CreateDecryptor())
                {
                    plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                    return true;
                }
            }
        }
        catch
        {
            plain = null;
            return false;
        }
    }

    private static string SanitizeFolder(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Hasta";
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            else if (c == ' ') sb.Append('_');
        }
        string s = sb.ToString();
        return s.Length > 0 ? s : "Hasta";
    }

    /// <summary>
    /// Eski Reports kökündeki düz HTML/CSV dosyalarını Patients/{Ad}/ altına taşır (mümkünse şifreler).
    /// Patients/ ve zaten .enc olan hedef dosyalar atlanır. Tekrar çağrılabilir.
    /// </summary>
    public static int MigrateLegacyReports(PatientProfile fallbackProfile)
    {
        string root = ReportExporter.ReportsDirectory;
        if (!Directory.Exists(root)) return 0;

        string fallback = SanitizeFolder(fallbackProfile != null ? fallbackProfile.FileNameSafe : "Legacy");
        int moved = 0;

        string[] rootFiles;
        try { rootFiles = Directory.GetFiles(root); }
        catch { return 0; }

        for (int i = 0; i < rootFiles.Length; i++)
            if (TryMigrateFile(rootFiles[i], fallback)) moved++;

        string clinDir = Path.Combine(root, "Clinician");
        if (Directory.Exists(clinDir))
        {
            string[] clinFiles;
            try { clinFiles = Directory.GetFiles(clinDir); }
            catch { clinFiles = null; }
            if (clinFiles != null)
            {
                for (int i = 0; i < clinFiles.Length; i++)
                    if (TryMigrateFile(clinFiles[i], fallback)) moved++;
            }
        }

        return moved;
    }

    /// <summary>
    /// Patients/* altındaki düz .html/.csv/.xlsx dosyalarını DEK varsa .enc yapıp düz kopyayı siler.
    /// Not defteri (.txt) şifrelenmez — dışarıdan düzenlenebilir kalır.
    /// </summary>
    public static int EncryptPlainFilesInPatientFolders()
    {
        byte[] dek = GetDekForWrite();
        if (dek == null) return 0;
        if (!Directory.Exists(PatientsRoot)) return 0;

        int n = 0;
        string[] dirs;
        try { dirs = Directory.GetDirectories(PatientsRoot); }
        catch { return 0; }

        for (int d = 0; d < dirs.Length; d++)
            n += EncryptPlainFilesRecursive(dirs[d]);
        return n;
    }

    private static int EncryptPlainFilesRecursive(string dir)
    {
        int n = 0;
        string[] files;
        try { files = Directory.GetFiles(dir); }
        catch { return 0; }

        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i];
            string name = Path.GetFileName(path);
            string lower = path.ToLowerInvariant();
            if (lower.EndsWith(EncExtension)) continue;
            if (IsNotebookFileName(name)) continue;

            bool isHtml = lower.EndsWith(".html");
            bool isCsv = lower.EndsWith(".csv");
            bool isXlsx = lower.EndsWith(".xlsx");
            if (!isHtml && !isCsv && !isXlsx) continue;

            string encPath = path + EncExtension;
            if (File.Exists(encPath))
            {
                try { File.Delete(path); n++; } catch { }
                continue;
            }

            string written;
            if (isXlsx)
            {
                byte[] bytes;
                try { bytes = File.ReadAllBytes(path); }
                catch { continue; }
                written = WriteEncryptedBytes(dir, name, bytes);
            }
            else
            {
                string content;
                try { content = File.ReadAllText(path, Encoding.UTF8); }
                catch { continue; }
                written = WriteEncrypted(dir, name, content);
            }
            if (string.IsNullOrEmpty(written)) continue;
            try { File.Delete(path); } catch { }
            n++;
        }

        string[] subdirs;
        try { subdirs = Directory.GetDirectories(dir); }
        catch { return n; }
        for (int s = 0; s < subdirs.Length; s++)
            n += EncryptPlainFilesRecursive(subdirs[s]);
        return n;
    }

    private static bool TryMigrateFile(string sourcePath, string fallbackFolder)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return false;

        string name = Path.GetFileName(sourcePath);
        if (string.IsNullOrEmpty(name)) return false;

        string lower = name.ToLowerInvariant();
        bool isHtml = lower.EndsWith(".html") || lower.EndsWith(".html.enc");
        bool isCsv = lower.EndsWith(".csv") || lower.EndsWith(".csv.enc");
        bool isXlsx = lower.EndsWith(".xlsx") || lower.EndsWith(".xlsx.enc");
        if (!isHtml && !isCsv && !isXlsx) return false;

        string patientKey = ParsePatientFolderFromFileName(name);
        if (string.IsNullOrEmpty(patientKey)) patientKey = fallbackFolder;

        string destDir;
        if (IsCompareReportFileName(name))
        {
            destDir = Path.Combine(ReportExporter.ReportsDirectory, "Compare", patientKey);
        }
        else if (IsClinicianReportFileName(name))
        {
            destDir = Path.Combine(ReportExporter.ReportsDirectory, "Clinician", patientKey);
        }
        else
        {
            destDir = Path.Combine(PatientsRoot, patientKey);
        }
        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
        try { File.SetAttributes(destDir, File.GetAttributes(destDir) | FileAttributes.Hidden); } catch { }

        // Zaten .enc → doğrudan taşı
        if (lower.EndsWith(EncExtension))
        {
            string dest = Path.Combine(destDir, name);
            if (File.Exists(dest)) return false;
            try
            {
                File.Move(sourcePath, dest);
                return true;
            }
            catch { return false; }
        }

        // Düz dosya → WriteEncrypted / WriteEncryptedBytes sonra kaynak sil
        string destEnc = Path.Combine(destDir, name + EncExtension);
        string destPlain = Path.Combine(destDir, name);
        if (File.Exists(destEnc) || File.Exists(destPlain)) return false;

        string written;
        if (isXlsx || lower.EndsWith(".xlsx"))
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(sourcePath); }
            catch { return false; }
            written = WriteEncryptedBytes(destDir, name, bytes);
        }
        else
        {
            string content;
            try { content = File.ReadAllText(sourcePath, Encoding.UTF8); }
            catch { return false; }
            written = WriteEncrypted(destDir, name, content);
        }
        if (string.IsNullOrEmpty(written)) return false;

        try { File.Delete(sourcePath); }
        catch { /* taşındı sayılır */ }
        return true;
    }

    /// <summary>Kadir_Ozdemir_Seans3_20260717.html → Kadir_Ozdemir</summary>
    private static string ParsePatientFolderFromFileName(string fileName)
    {
        string baseName = fileName;
        if (baseName.EndsWith(EncExtension, StringComparison.OrdinalIgnoreCase))
            baseName = baseName.Substring(0, baseName.Length - EncExtension.Length);
        if (baseName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            baseName = baseName.Substring(0, baseName.Length - 5);
        else if (baseName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            baseName = baseName.Substring(0, baseName.Length - 4);
        else if (baseName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            baseName = baseName.Substring(0, baseName.Length - 5);

        string[] markers =
        {
            "_Seans", "_Session", "_Ilerleme_", "_Progress_", "_NotDefteri", "_Notebook",
            "_Compare_", "_Karsilastirma_", "_Clinician_", "Clinician_"
        };
        int cut = -1;
        for (int m = 0; m < markers.Length; m++)
        {
            int idx = baseName.IndexOf(markers[m], StringComparison.OrdinalIgnoreCase);
            if (idx > 0 && (cut < 0 || idx < cut)) cut = idx;
            if (idx == 0 && markers[m].StartsWith("Clinician", StringComparison.OrdinalIgnoreCase))
                return "Clinician";
        }
        if (cut > 0) return SanitizeFolder(baseName.Substring(0, cut));
        return null;
    }
}
