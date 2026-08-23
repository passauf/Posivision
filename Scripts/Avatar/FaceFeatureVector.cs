using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using UnityEngine;

/// <summary>
/// MediaPipe face blendshape kategorilerinden sabit boyutlu ifade vektörü.
/// Heap allocation yok (struct). KVKK: ham video/landmark değil, skaler özellikler.
/// </summary>
public struct FaceFeatureVector
{
    public const int Dim = 10;

    public float browDown;
    public float eyeSquint;
    public float mouthFrown;
    public float jawOpen;
    public float mouthPress;
    public float cheekSquint;
    public float noseSneer;
    public float mouthUpperUp;
    public float mouthStretch;
    public float eyeBlink;

    public float Get(int index)
    {
        switch (index)
        {
            case 0: return browDown;
            case 1: return eyeSquint;
            case 2: return mouthFrown;
            case 3: return jawOpen;
            case 4: return mouthPress;
            case 5: return cheekSquint;
            case 6: return noseSneer;
            case 7: return mouthUpperUp;
            case 8: return mouthStretch;
            case 9: return eyeBlink;
            default: return 0f;
        }
    }

    public void Set(int index, float value)
    {
        switch (index)
        {
            case 0: browDown = value; break;
            case 1: eyeSquint = value; break;
            case 2: mouthFrown = value; break;
            case 3: jawOpen = value; break;
            case 4: mouthPress = value; break;
            case 5: cheekSquint = value; break;
            case 6: noseSneer = value; break;
            case 7: mouthUpperUp = value; break;
            case 8: mouthStretch = value; break;
            case 9: eyeBlink = value; break;
        }
    }

    public void Add(in FaceFeatureVector other)
    {
        for (int i = 0; i < Dim; i++)
            Set(i, Get(i) + other.Get(i));
    }

    public void Scale(float s)
    {
        for (int i = 0; i < Dim; i++)
            Set(i, Get(i) * s);
    }

    public float Magnitude()
    {
        float sum = 0f;
        for (int i = 0; i < Dim; i++)
        {
            float v = Get(i);
            sum += v * v;
        }
        return Mathf.Sqrt(sum);
    }

    public static float L2Distance(in FaceFeatureVector a, in FaceFeatureVector b)
    {
        float sum = 0f;
        for (int i = 0; i < Dim; i++)
        {
            float d = a.Get(i) - b.Get(i);
            sum += d * d;
        }
        return Mathf.Sqrt(sum);
    }

    public static float CosineSimilarity(in FaceFeatureVector a, in FaceFeatureVector b)
    {
        float dot = 0f;
        float na = 0f;
        float nb = 0f;
        for (int i = 0; i < Dim; i++)
        {
            float av = a.Get(i);
            float bv = b.Get(i);
            dot += av * bv;
            na += av * av;
            nb += bv * bv;
        }
        float denom = Mathf.Sqrt(na) * Mathf.Sqrt(nb);
        if (denom < 1e-6f) return 0f;
        return Mathf.Clamp(dot / denom, -1f, 1f);
    }

    // cmd: IndexOf her yüz karesinde 10+ kez pahalı — isim→slot bir kez öğrenilir
    private static readonly Dictionary<string, sbyte> NameToSlot = new Dictionary<string, sbyte>(64);

    /// <summary>Classifications listesinden ifade vektörü çıkarır (string karşılaştırma yalnızca yeni isimde).</summary>
    public static FaceFeatureVector FromBlendshapes(List<Category> categories)
    {
        FaceFeatureVector v = default;
        int nBrow = 0, nSquint = 0, nFrown = 0, nJaw = 0, nPress = 0;
        int nCheek = 0, nNose = 0, nUpper = 0, nStretch = 0, nBlink = 0;

        if (categories == null) return v;

        for (int i = 0; i < categories.Count; i++)
        {
            Category c = categories[i];
            string name = c.categoryName;
            if (string.IsNullOrEmpty(name)) continue;
            float s = Mathf.Clamp01(c.score);

            sbyte slot = ResolveSlot(name);
            switch (slot)
            {
                case 0: v.browDown += s; nBrow++; break;
                case 1: v.eyeSquint += s; nSquint++; break;
                case 2: v.mouthFrown += s; nFrown++; break;
                case 3: v.jawOpen += s; nJaw++; break;
                case 4: v.mouthPress += s; nPress++; break;
                case 5: v.cheekSquint += s; nCheek++; break;
                case 6: v.noseSneer += s; nNose++; break;
                case 7: v.mouthUpperUp += s; nUpper++; break;
                case 8: v.mouthStretch += s; nStretch++; break;
                case 9: v.eyeBlink += s; nBlink++; break;
            }
        }

        if (nBrow > 0) v.browDown /= nBrow;
        if (nSquint > 0) v.eyeSquint /= nSquint;
        if (nFrown > 0) v.mouthFrown /= nFrown;
        if (nJaw > 0) v.jawOpen /= nJaw;
        if (nPress > 0) v.mouthPress /= nPress;
        if (nCheek > 0) v.cheekSquint /= nCheek;
        if (nNose > 0) v.noseSneer /= nNose;
        if (nUpper > 0) v.mouthUpperUp /= nUpper;
        if (nStretch > 0) v.mouthStretch /= nStretch;
        if (nBlink > 0) v.eyeBlink /= nBlink;

        return v;
    }

    private static sbyte ResolveSlot(string name)
    {
        if (NameToSlot.TryGetValue(name, out sbyte cached))
            return cached;

        sbyte slot = -1;
        if (Contains(name, "browDown")) slot = 0;
        else if (Contains(name, "eyeSquint")) slot = 1;
        else if (Contains(name, "mouthFrown")) slot = 2;
        else if (Contains(name, "jawOpen")) slot = 3;
        else if (Contains(name, "mouthPress")) slot = 4;
        else if (Contains(name, "cheekSquint")) slot = 5;
        else if (Contains(name, "noseSneer")) slot = 6;
        else if (Contains(name, "mouthUpperUp")) slot = 7;
        else if (Contains(name, "mouthStretch")) slot = 8;
        else if (Contains(name, "eyeBlink")) slot = 9;

        NameToSlot[name] = slot;
        return slot;
    }

    private static bool Contains(string name, string key)
    {
        return name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
