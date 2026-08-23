using UnityEngine;

/// <summary>
/// Hedef hareket şablonu ile hasta hareketini karşılaştıran DTW (Dynamic Time Warping).
/// SaMD notu: Bu skor bir klinik karar-destek göstergesidir (IEC 62304 Class B yorumu);
/// tek başına tanı/tedavi kararı için kullanılmamalıdır.
///
/// Performans: Sakoe-Chiba bant kısıtı ile O(N * bant) çalışır ve iki kayan satır (rolling row)
/// kullanır; tam N*M matris tahsis edilmez. Seans sonunda bir kez çalışır (hot path değildir),
/// ancak buffer'lar örnek (instance) düzeyinde yeniden kullanılır.
///
/// Veri XY düzleminden türetilmiş açı serileridir (Vector3/Z bileşeni kullanılmaz).
/// </summary>
public sealed class MovementDtw
{
    // Bant genişliği = max(|n-m|, seriUzunluğu * BandRatio). Zamansal esneme toleransı.
    private const float DefaultBandRatio = 0.15f;

    // Skor eşlemesi: normalize edilmiş DTW mesafesi (derece) bu değerde 0 puana iner.
    private const float ScoreZeroDistanceDegrees = 45f;

    // NaN olmayan minimum örnek sayısı; altındaysa karşılaştırma geçersiz sayılır.
    private const int MinUsableSamples = 8;

    private readonly float _bandRatio;

    // Instance buffer'ları (yeniden kullanım için; ihtiyaç halinde büyür).
    private float[] _cleanTarget;
    private float[] _cleanPatient;
    private float[] _prevRow;
    private float[] _currRow;

    public struct Result
    {
        public bool valid;
        public float distance;            // Toplam DTW yol maliyeti (derece)
        public float normalizedDistance;  // Yol uzunluğuna bölünmüş ortalama (derece)
        public float similarity;          // 0..100 benzerlik skoru
    }

    public MovementDtw(float bandRatio = DefaultBandRatio)
    {
        _bandRatio = Mathf.Clamp(bandRatio, 0.02f, 1f);
    }

    /// <summary>
    /// İki açı serisini karşılaştırır. NaN örnekler otomatik ayıklanır.
    /// </summary>
    public Result Compare(float[] target, int targetLen, float[] patient, int patientLen)
    {
        Result result = default;

        int n = Compact(target, targetLen, ref _cleanTarget);
        int m = Compact(patient, patientLen, ref _cleanPatient);

        if (n < MinUsableSamples || m < MinUsableSamples)
        {
            result.valid = false;
            return result;
        }

        EnsureRow(ref _prevRow, m + 1);
        EnsureRow(ref _currRow, m + 1);

        int band = Mathf.Max(Mathf.Abs(n - m), Mathf.CeilToInt(Mathf.Max(n, m) * _bandRatio));

        const float inf = float.PositiveInfinity;
        for (int j = 0; j <= m; j++) _prevRow[j] = inf;
        _prevRow[0] = 0f;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 0; j <= m; j++) _currRow[j] = inf;

            int jStart = Mathf.Max(1, i - band);
            int jEnd = Mathf.Min(m, i + band);

            for (int j = jStart; j <= jEnd; j++)
            {
                float cost = Mathf.Abs(_cleanTarget[i - 1] - _cleanPatient[j - 1]);
                float best = _prevRow[j];             // ekleme
                float left = _currRow[j - 1];          // silme
                float diag = _prevRow[j - 1];          // eşleşme
                if (left < best) best = left;
                if (diag < best) best = diag;
                _currRow[j] = cost + best;
            }

            float[] swap = _prevRow;
            _prevRow = _currRow;
            _currRow = swap;
        }

        float total = _prevRow[m];
        if (float.IsInfinity(total))
        {
            result.valid = false;
            return result;
        }

        float pathLen = n + m; // üst sınır normalizasyonu; kararlı ve monotonik
        result.valid = true;
        result.distance = total;
        result.normalizedDistance = total / pathLen;
        result.similarity = Mathf.Clamp01(1f - result.normalizedDistance / ScoreZeroDistanceDegrees) * 100f;
        return result;
    }

    /// <summary>
    /// İdeal tek-tekrar açı şablonu üretir: 0 → hedef açı → 0 (yumuşatılmış yarım-sinüs).
    /// Editor asset gerektirmez; hedef açıdan türetilir.
    /// </summary>
    public static float[] BuildIdealRepTemplate(float targetAngle, int points)
    {
        if (points < 2) points = 2;
        float[] template = new float[points];
        for (int i = 0; i < points; i++)
        {
            float t = i / (float)(points - 1);       // 0..1
            float shape = Mathf.Sin(t * Mathf.PI);   // 0 → 1 → 0
            template[i] = shape * targetAngle;
        }
        return template;
    }

    // NaN olmayan örnekleri sıkıştırıp buffer'a kopyalar; kullanılabilir uzunluğu döndürür.
    private static int Compact(float[] source, int length, ref float[] buffer)
    {
        if (source == null || length <= 0) return 0;
        length = Mathf.Min(length, source.Length);
        EnsureRow(ref buffer, length);

        int count = 0;
        for (int i = 0; i < length; i++)
        {
            float v = source[i];
            if (float.IsNaN(v)) continue;
            buffer[count++] = v;
        }
        return count;
    }

    private static void EnsureRow(ref float[] buffer, int size)
    {
        if (buffer == null || buffer.Length < size)
            buffer = new float[size];
    }
}
