using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Yerel klinisyen PIN — düz metin saklanmaz (SHA256 + salt).
/// KVKK: yalnızca erişim kontrolü; hasta PII içermez.
/// SaMD: klinisyen notlarına erişim kapısı.
/// </summary>
public static class ClinicianPin
{
    private const string PrefHash = "clinician_pin_hash";
    private const string PrefSalt = "clinician_pin_salt";
    private const int MinPinLength = 4;
    private const int MaxPinLength = 8;

    public static bool IsConfigured =>
        !string.IsNullOrEmpty(PlayerPrefs.GetString(PrefHash, ""));

    public static bool IsValidFormat(string pin)
    {
        if (string.IsNullOrEmpty(pin)) return false;
        if (pin.Length < MinPinLength || pin.Length > MaxPinLength) return false;
        for (int i = 0; i < pin.Length; i++)
        {
            if (pin[i] < '0' || pin[i] > '9') return false;
        }
        return true;
    }

    public static bool SetPin(string pin)
    {
        if (!IsValidFormat(pin)) return false;
        string salt = Guid.NewGuid().ToString("N");
        string hash = Hash(pin, salt);
        PlayerPrefs.SetString(PrefSalt, salt);
        PlayerPrefs.SetString(PrefHash, hash);
        PlayerPrefs.Save();
        // Hasta rapor kasası DEK'ini PIN ile bağla
        PatientVault.EnsureDekAfterPin(pin);
        return true;
    }

    public static bool Verify(string pin)
    {
        if (!IsConfigured || !IsValidFormat(pin)) return false;
        string salt = PlayerPrefs.GetString(PrefSalt, "");
        string expected = PlayerPrefs.GetString(PrefHash, "");
        if (string.IsNullOrEmpty(salt) || string.IsNullOrEmpty(expected)) return false;
        if (!FixedTimeEquals(Hash(pin, salt), expected)) return false;
        PatientVault.EnsureDekAfterPin(pin);
        return true;
    }

    /// <summary>PIN sıfırlama: hash + oturum DEK silinir. Enc dosyalar kalır; yeni PIN ile yeniden sarılamaz (cihaz wrap varsa yazma devam).</summary>
    public static void ClearPin()
    {
        PlayerPrefs.DeleteKey(PrefHash);
        PlayerPrefs.DeleteKey(PrefSalt);
        PlayerPrefs.Save();
        PatientVault.ClearSessionUnlock();
        PatientVault.ClearWrappedKeys();
    }

    private static string Hash(string pin, string salt)
    {
        using (var sha = SHA256.Create())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(salt + "|" + pin);
            byte[] dig = sha.ComputeHash(bytes);
            var sb = new StringBuilder(dig.Length * 2);
            for (int i = 0; i < dig.Length; i++)
                sb.Append(dig[i].ToString("x2"));
            return sb.ToString();
        }
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
