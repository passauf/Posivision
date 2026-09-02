using UnityEngine;

/// <summary>
/// Referans tekrarlarından kişisel açı trend şablonu üretir.
/// Hızdan bağımsız karşılaştırma için DTW seans sonunda kullanılır.
/// SaMD Class B: karar-destek; teşhis değildir.
/// </summary>
public static class MovementTemplateBuilder
{
    public static float[] ResampleRep(float[] samples, int sampleCount, int points)
    {
        if (samples == null || sampleCount < 2 || points < 2) return null;

        float[] output = new float[points];
        int last = sampleCount - 1;

        for (int i = 0; i < points; i++)
        {
            float t = i / (float)(points - 1);
            float src = t * last;
            int i0 = Mathf.FloorToInt(src);
            int i1 = Mathf.Min(i0 + 1, last);
            float frac = src - i0;
            output[i] = Mathf.Lerp(samples[i0], samples[i1], frac);
        }

        return output;
    }

    /// <summary>
    /// Her tekrarı sabit noktaya indirger ve ortalama trend şablonu döndürür.
    /// </summary>
    public static float[] BuildMeanTemplate(float[] repBuffer, int repCount, int pointsPerRep, int points)
    {
        if (repBuffer == null || repCount <= 0 || pointsPerRep < 2 || points < 2) return null;

        float[] mean = new float[points];
        int used = 0;

        for (int r = 0; r < repCount; r++)
        {
            int offset = r * pointsPerRep;
            float[] resampled = ResampleRep(repBuffer, pointsPerRep, points);
            if (resampled == null) continue;

            for (int i = 0; i < points; i++)
                mean[i] += resampled[i];
            used++;
        }

        if (used <= 0) return null;

        float inv = 1f / used;
        for (int i = 0; i < points; i++)
            mean[i] *= inv;

        return mean;
    }

    /// <summary>
    /// Önceden sabit uzunlukta saklanmış tekrarları ortalar.
    /// </summary>
    public static float[] BuildMeanTemplate(float[] flatReps, int repCount, int points)
    {
        if (flatReps == null || repCount <= 0 || points < 2) return null;
        if (flatReps.Length < repCount * points) return null;

        float[] mean = new float[points];
        for (int r = 0; r < repCount; r++)
        {
            int offset = r * points;
            for (int i = 0; i < points; i++)
                mean[i] += flatReps[offset + i];
        }

        float inv = 1f / repCount;
        for (int i = 0; i < points; i++)
            mean[i] *= inv;

        return mean;
    }
}
