using System.IO;
using UnityEngine;

/// <summary>
/// Hasta bazlı yüz zorlanma kalibrasyon profili (rest + strain ortalama vektörleri).
/// KVKK: yalnızca skaler özellik ortalamaları; video/landmark/kimlik yok.
/// SaMD Class B: karar-destek şablonu; teşhis değildir.
/// </summary>
[System.Serializable]
public class FaceStrainProfile
{
    public string savedAt;
    public int restSampleCount;
    public int strainSampleCount;
    public float[] restMean = new float[FaceFeatureVector.Dim];
    public float[] strainMean = new float[FaceFeatureVector.Dim];

    public bool IsValid =>
        restMean != null && strainMean != null
        && restMean.Length == FaceFeatureVector.Dim
        && strainMean.Length == FaceFeatureVector.Dim
        && restSampleCount >= 5
        && strainSampleCount >= 10;

    public FaceFeatureVector RestVector
    {
        get
        {
            FaceFeatureVector v = default;
            if (restMean == null) return v;
            for (int i = 0; i < FaceFeatureVector.Dim && i < restMean.Length; i++)
                v.Set(i, restMean[i]);
            return v;
        }
    }

    public FaceFeatureVector StrainVector
    {
        get
        {
            FaceFeatureVector v = default;
            if (strainMean == null) return v;
            for (int i = 0; i < FaceFeatureVector.Dim && i < strainMean.Length; i++)
                v.Set(i, strainMean[i]);
            return v;
        }
    }

    public static string FilePath =>
        Path.Combine(Application.persistentDataPath, "face_strain_profile.json");

    public static FaceStrainProfile Load()
    {
        string path = FilePath;
        if (!File.Exists(path)) return null;
        string json = File.ReadAllText(path);
        FaceStrainProfile p = JsonUtility.FromJson<FaceStrainProfile>(json);
        return p != null && p.IsValid ? p : null;
    }

    public void Save()
    {
        savedAt = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        string json = JsonUtility.ToJson(this, true);
        File.WriteAllText(FilePath, json);
    }

    public static void Delete()
    {
        string path = FilePath;
        if (File.Exists(path)) File.Delete(path);
    }

    public static FaceStrainProfile FromMeans(
        in FaceFeatureVector restMean, int restCount,
        in FaceFeatureVector strainMean, int strainCount)
    {
        var p = new FaceStrainProfile
        {
            restSampleCount = restCount,
            strainSampleCount = strainCount,
            restMean = new float[FaceFeatureVector.Dim],
            strainMean = new float[FaceFeatureVector.Dim]
        };
        for (int i = 0; i < FaceFeatureVector.Dim; i++)
        {
            p.restMean[i] = restMean.Get(i);
            p.strainMean[i] = strainMean.Get(i);
        }
        return p;
    }
}
