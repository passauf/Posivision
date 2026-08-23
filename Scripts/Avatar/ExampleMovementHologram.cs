using UnityEngine;

/// <summary>
/// Seans başlamadan önce örnek omuz fleksiyonu: ana model üzerinde
/// (MediaPipe takip yok, radial yay yok). Zamanlama: 5s kaldır → 2s indir → 2s bekle.
/// SaMD: eğitimsel UI; klinik ölçüm değildir.
/// </summary>
public class ExampleMovementHologram : MonoBehaviour
{
    private const float MinRaiseDegrees = 25f;
    private const float DefaultMaxRaiseDegrees = 140f;
    private const float RaiseSeconds = 5f;
    private const float LowerSeconds = 2f;
    private const float WaitSeconds = 2f;

    private enum DemoPhase
    {
        Raise = 0,
        Lower = 1,
        Wait = 2
    }

    [SerializeField] private bool enabledWhenIdle = true;
    [SerializeField] private float minRaiseDegrees = MinRaiseDegrees;
    [SerializeField] private float maxRaiseDegrees = DefaultMaxRaiseDegrees;
    [SerializeField] private float raiseSeconds = RaiseSeconds;
    [SerializeField] private float lowerSeconds = LowerSeconds;
    [SerializeField] private float waitSeconds = WaitSeconds;

    private AvatarStageController _stage;
    private PhysioAnalyzer _analyzer;
    private AvatarBodyDriver _bodyDriver;
    private bool _demoActive;
    private DemoPhase _phase;
    private float _phaseElapsed;
    private float _angle;

    private void Awake()
    {
        _stage = GetComponent<AvatarStageController>();
        if (_stage == null)
            _stage = FindObjectOfType<AvatarStageController>(true);
    }

    private void OnEnable()
    {
        SessionStatus.Changed -= OnSessionStatusChanged;
        SessionStatus.Changed += OnSessionStatusChanged;
        PreSessionPositionGuide.PositioningCompleted -= OnPositioningCompleted;
        PreSessionPositionGuide.PositioningCompleted += OnPositioningCompleted;
        OnSessionStatusChanged();
    }

    private void OnDisable()
    {
        SessionStatus.Changed -= OnSessionStatusChanged;
        PreSessionPositionGuide.PositioningCompleted -= OnPositioningCompleted;
        StopDemo();
    }

    private void OnPositioningCompleted()
    {
        if (ShouldRunDemo())
            StartDemo();
    }

    private void OnDestroy()
    {
        StopDemo();
    }

    private void OnSessionStatusChanged()
    {
        if (ShouldRunDemo())
            StartDemo();
        else
            StopDemo();
    }

    /// <summary>PhysioAnalyzer.BeginSession — örnek demoyu kapatır.</summary>
    public void NotifySessionStarted()
    {
        StopDemo();
    }

    private void Update()
    {
        if (!enabledWhenIdle) return;

        if (_analyzer == null)
            _analyzer = FindObjectOfType<PhysioAnalyzer>(true);

        bool want = ShouldRunDemo();
        if (want && !_demoActive)
            StartDemo();
        else if (!want && _demoActive)
            StopDemo();

        if (!_demoActive || _bodyDriver == null) return;

        float maxDeg = ResolveMaxDegrees();
        float raiseDur = Mathf.Max(0.5f, raiseSeconds);
        float lowerDur = Mathf.Max(0.25f, lowerSeconds);
        float waitDur = Mathf.Max(0f, waitSeconds);

        _phaseElapsed += Time.deltaTime;

        switch (_phase)
        {
            case DemoPhase.Raise:
            {
                float t = Mathf.Clamp01(_phaseElapsed / raiseDur);
                // Ease in-out
                float u = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI);
                _angle = Mathf.Lerp(minRaiseDegrees, maxDeg, u);
                if (_phaseElapsed >= raiseDur)
                {
                    _angle = maxDeg;
                    _phase = DemoPhase.Lower;
                    _phaseElapsed = 0f;
                }
                break;
            }
            case DemoPhase.Lower:
            {
                float t = Mathf.Clamp01(_phaseElapsed / lowerDur);
                float u = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI);
                _angle = Mathf.Lerp(maxDeg, minRaiseDegrees, u);
                if (_phaseElapsed >= lowerDur)
                {
                    _angle = minRaiseDegrees;
                    _phase = DemoPhase.Wait;
                    _phaseElapsed = 0f;
                }
                break;
            }
            default: // Wait
            {
                _angle = minRaiseDegrees;
                if (_phaseElapsed >= waitDur)
                {
                    _phase = DemoPhase.Raise;
                    _phaseElapsed = 0f;
                }
                break;
            }
        }

        bool right = true;
        bool left = false;
        bool side = true;
        if (_analyzer != null)
        {
            side = _analyzer.PatientSideView
                || ExerciseCatalog.UsesSideProfile(_analyzer.SelectedMovementId);
            if (_analyzer.IsSequentialBothArms)
            {
                right = true;
                left = false;
            }
            else
            {
                right = _analyzer.IsMeasuringRightArm;
                left = _analyzer.IsMeasuringLeftArm;
                if (!right && !left) { right = true; left = false; }
                if (side && right && left) left = false;
            }
        }

        _bodyDriver.SetMeasuredArms(right, left);
        _bodyDriver.ApplyShoulderFlexionAngles(
            right, right ? _angle : 0f,
            left, left ? _angle : 0f,
            maxDeg, maxDeg);

        if (_stage != null)
            _stage.ApplySideOrbitForMeasuredArm(right, left, side);
    }

    private bool ShouldRunDemo()
    {
        if (!enabledWhenIdle) return false;
        if (_analyzer != null && _analyzer.IsSessionRunning)
            return false;
        if (SessionStatus.Current == SessionStatus.Phase.Active)
            return false;
        // Konum ayarı bitmeden örnek hareket oynatma
        if (!PreSessionPositionGuide.IsPositioningComplete)
            return false;
        return true;
    }

    private float ResolveMaxDegrees()
    {
        // Düşük ROM hedefleri (örn. 20°) de örnek harekete ve yay rengine yansısın
        if (_analyzer != null && _analyzer.targetAngleDegrees > 1f)
            return Mathf.Clamp(_analyzer.targetAngleDegrees, minRaiseDegrees, 170f);
        return Mathf.Clamp(maxRaiseDegrees, minRaiseDegrees, 170f);
    }

    private void StartDemo()
    {
        if (_stage == null)
            _stage = FindObjectOfType<AvatarStageController>(true);
        _bodyDriver = _stage != null ? _stage.BodyDriver : null;
        if (_bodyDriver == null)
            _bodyDriver = FindObjectOfType<AvatarBodyDriver>(true);
        if (_bodyDriver == null) return;

        _demoActive = true;
        _phase = DemoPhase.Raise;
        _phaseElapsed = 0f;
        _angle = minRaiseDegrees;
        _bodyDriver.SetExampleDemoMode(true);

        // Yan/ön demo: seçilen protokole göre orbit
        bool right = true;
        bool left = false;
        bool side = true;
        if (_analyzer != null)
        {
            side = _analyzer.PatientSideView
                || ExerciseCatalog.UsesSideProfile(_analyzer.SelectedMovementId);
            if (_analyzer.IsSequentialBothArms)
            {
                right = true;
                left = false;
            }
            else
            {
                right = _analyzer.IsMeasuringRightArm;
                left = _analyzer.IsMeasuringLeftArm;
                if (!right && !left) { right = true; left = false; }
                if (side && right && left) left = false;
            }
        }
        _bodyDriver.SetMeasuredArms(right, left);
        if (_stage != null)
            _stage.ApplySideOrbitForMeasuredArm(right, left, side);
    }

    private void StopDemo()
    {
        _demoActive = false;
        if (_bodyDriver != null)
            _bodyDriver.SetExampleDemoMode(false);
    }
}
