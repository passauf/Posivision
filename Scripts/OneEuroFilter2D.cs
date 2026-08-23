using UnityEngine;

/// <summary>
/// One Euro Filter (Casiez et al.) — MediaPipe landmark gürültüsünü yumuşatır.
/// Struct tabanlı; heap allocation oluşturmaz.
/// SaMD Class B: filtreleme klinik ölçüm zincirinin parçasıdır; parametreler Inspector'dan ayarlanmalıdır.
/// </summary>
public struct OneEuroFilter1D
{
    private float _minCutoff;
    private float _beta;
    private float _dCutoff;
    private float _xPrev;
    private float _dxPrev;
    private float _tPrev;
    private bool _initialized;

    public void Configure(float minCutoff, float beta, float dCutoff)
    {
        _minCutoff = minCutoff;
        _beta = beta;
        _dCutoff = dCutoff;
    }

    public void Reset()
    {
        _initialized = false;
        _xPrev = 0f;
        _dxPrev = 0f;
        _tPrev = 0f;
    }

    public float Filter(float x, float timestamp)
    {
        if (!_initialized)
        {
            _initialized = true;
            _xPrev = x;
            _dxPrev = 0f;
            _tPrev = timestamp;
            return x;
        }

        float dt = timestamp - _tPrev;
        if (dt <= 0f)
        {
            return _xPrev;
        }

        float dx = (x - _xPrev) / dt;
        float edx = LowPass(dx, _dxPrev, Alpha(dt, _dCutoff));
        float cutoff = _minCutoff + _beta * Mathf.Abs(edx);
        float filtered = LowPass(x, _xPrev, Alpha(dt, cutoff));

        _xPrev = filtered;
        _dxPrev = edx;
        _tPrev = timestamp;
        return filtered;
    }

    private static float Alpha(float dt, float cutoff)
    {
        float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-5f));
        return 1f / (1f + tau / dt);
    }

    private static float LowPass(float x, float xPrev, float alpha)
    {
        return alpha * x + (1f - alpha) * xPrev;
    }
}

/// <summary>
/// XY düzlemi için One Euro Filter çifti (Z kullanılmaz — 2D kamera kuralı).
/// </summary>
public struct OneEuroFilter2D
{
    private OneEuroFilter1D _fx;
    private OneEuroFilter1D _fy;

    public void Configure(float minCutoff, float beta, float dCutoff)
    {
        _fx.Configure(minCutoff, beta, dCutoff);
        _fy.Configure(minCutoff, beta, dCutoff);
    }

    public void Reset()
    {
        _fx.Reset();
        _fy.Reset();
    }

    public Vector2 Filter(float x, float y, float timestamp)
    {
        return new Vector2(_fx.Filter(x, timestamp), _fy.Filter(y, timestamp));
    }
}
