using System;
using UnityEngine;

/// <summary>
/// Bölgesel açık/kapalı maske — görünmeyen landmark tahmin edilmez; ilgili hesaplama kapanır.
/// Omuz fleksiyonu varsayılanı: kollar (kalça+omuz+dirsek) + gövde; önkol/bacak/baş kapalı.
/// SaMD Class B: eksik görünürlükte klinik çıktı üretilmez.
/// </summary>
[Serializable]
public struct PoseRegionMask
{
    [Tooltip("Sağ omuz fleksiyonu: sağ kalça + omuz + dirsek")]
    public bool rightArm;

    [Tooltip("Sol omuz fleksiyonu: sol kalça + omuz + dirsek")]
    public bool leftArm;

    [Tooltip("Gövde lean / kompansasyon: her iki kalça + her iki omuz")]
    public bool torso;

    [Tooltip("Önkol (bilek) — omuz fleksiyonunda kapalı")]
    public bool forearms;

    [Tooltip("Bacaklar — omuz fleksiyonunda kapalı. Kalça kabul edilir; diz/ayak bileği hesaplanmaz.")]
    public bool legs;

    [Tooltip("Baş — omuz fleksiyonunda kapalı")]
    public bool head;

    public static PoseRegionMask ShoulderFlexion()
    {
        return new PoseRegionMask
        {
            rightArm = true,
            leftArm = true,
            torso = true,
            forearms = false,
            legs = false,
            head = false
        };
    }
}

/// <summary>
/// Bu karede bölgeler için gerekli landmark'ların visibility/presence durumu.
/// Tahmin yok: false → o bölge hesaplaması atlanır.
/// </summary>
public struct PoseRegionVisibility
{
    public bool rightArm;
    public bool leftArm;
    public bool torso;
    public bool rightForearm;
    public bool leftForearm;
    public bool legs;
    public bool head;

    /// <summary>Omuz fleksiyonu için gerekli kadraj (açık kollar + gövde).</summary>
    public bool HasShoulderFlexionFrame(in PoseRegionMask mask)
    {
        bool armsOk = true;
        if (mask.rightArm) armsOk &= rightArm;
        if (mask.leftArm) armsOk &= leftArm;
        // En az bir kol ölçülüyorsa ve gövde açıksa gövde de gerekli
        bool needTorso = mask.torso && (mask.rightArm || mask.leftArm);
        if (needTorso) armsOk &= torso;
        return armsOk;
    }
}
