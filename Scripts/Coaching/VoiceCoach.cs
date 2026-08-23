using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Yerel sesli asistan — Android TTS; Editor/Windows'ta altyazı + isteğe bağlı AudioClip.
/// KVKK: metinler kimlik içermez; bulut API yok.
/// SaMD Class B: motivasyon / form uyarısı; klinik eşiği PhysioAnalyzer verir.
/// </summary>
public class VoiceCoach : MonoBehaviour
{
    private const float DefaultCueCooldownSeconds = 4.5f;
    private const float SlowDownCooldownSeconds = 6f;

    [SerializeField] private bool enableVoice = true;
    [SerializeField] private float cueCooldownSeconds = DefaultCueCooldownSeconds;
    [Tooltip("Boşsa yalnızca Android TTS / altyazı.")]
    [SerializeField] private AudioSource audioSource;

    private readonly Dictionary<CoachCue, float> _nextAllowed = new Dictionary<CoachCue, float>(8);
    private WarningManager _warningManager;
    private bool _androidReady;
    private bool _androidInitStarted;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject _tts;
    private AndroidJavaObject _unityActivity;
#endif

    public static VoiceCoach Ensure()
    {
        var existing = FindObjectOfType<VoiceCoach>(true);
        if (existing != null) return existing;
        var go = new GameObject("VoiceCoach");
        DontDestroyOnLoad(go);
        return go.AddComponent<VoiceCoach>();
    }

    private void Awake()
    {
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        _warningManager = FindObjectOfType<WarningManager>();
#if UNITY_ANDROID && !UNITY_EDITOR
        if (enableVoice && !_androidInitStarted)
            StartCoroutine(InitAndroidTts());
#endif
    }

    public void SetEnabled(bool on) => enableVoice = on;

    public void Speak(CoachCue cue)
    {
        if (!enableVoice) return;

        float cooldown = cue == CoachCue.SlowDown ? SlowDownCooldownSeconds : cueCooldownSeconds;
        float now = Time.unscaledTime;
        if (_nextAllowed.TryGetValue(cue, out float next) && now < next)
            return;
        _nextAllowed[cue] = now + cooldown;

        string text = PhraseFor(cue);
        if (string.IsNullOrEmpty(text)) return;

        SpeakText(text);
    }

    public void SpeakTargets(float angle, int reps)
    {
        if (!enableVoice) return;
        float now = Time.unscaledTime;
        if (_nextAllowed.TryGetValue(CoachCue.TargetsApplied, out float next) && now < next)
            return;
        _nextAllowed[CoachCue.TargetsApplied] = now + cueCooldownSeconds;
        SpeakText(CoachPhrases.TargetsApplied(angle, reps));
    }

    private static string PhraseFor(CoachCue cue)
    {
        switch (cue)
        {
            case CoachCue.SessionStart: return CoachPhrases.SessionStart;
            case CoachCue.StandStraight: return CoachPhrases.StandStraight;
            case CoachCue.HighStrain: return CoachPhrases.HighStrain;
            case CoachCue.RepInvalid: return CoachPhrases.RepInvalid;
            case CoachCue.SlowDown: return CoachPhrases.SlowDown;
            case CoachCue.GoodPace: return CoachPhrases.GoodPace;
            case CoachCue.AlmostDone: return CoachPhrases.AlmostDone;
            case CoachCue.DepthCollapse: return CoachPhrases.DepthCollapse;
            case CoachCue.FaceFront: return CoachPhrases.FaceFront;
            case CoachCue.TurnFront: return CoachPhrases.TurnFront;
            default: return null;
        }
    }

    private void SpeakText(string text)
    {
        // Altyazı — ses yoksa da görünsün
        if (_warningManager == null)
            _warningManager = FindObjectOfType<WarningManager>();
        if (_warningManager != null)
            _warningManager.TriggerWarning(text);

#if UNITY_ANDROID && !UNITY_EDITOR
        if (_androidReady && _tts != null)
        {
            // QUEUE_FLUSH = 0
            _tts.Call<int>("speak", text, 0, null, "posivision_coach");
            return;
        }
#endif
        // Editor / Windows: TTS yok — altyazı yeterli (isteğe bağlı ileride clip)
        Debug.Log("[VoiceCoach] " + text);
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private IEnumerator InitAndroidTts()
    {
        _androidInitStarted = true;
        yield return null;
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                _unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }
            _tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", _unityActivity, new TtsInitListener(this));
        }
        catch (System.Exception)
        {
            _androidReady = false;
        }
    }

    private void OnTtsInit(int status)
    {
        // SUCCESS == 0
        _androidReady = status == 0;
        if (_androidReady && _tts != null)
        {
            using (var locale = new AndroidJavaClass("java.util.Locale"))
            {
                var tr = locale.GetStatic<AndroidJavaObject>("forLanguageTag", "tr-TR");
                if (tr != null)
                    _tts.Call<int>("setLanguage", tr);
            }
        }
    }

    private class TtsInitListener : AndroidJavaProxy
    {
        private readonly VoiceCoach _owner;
        public TtsInitListener(VoiceCoach owner)
            : base("android.speech.tts.TextToSpeech$OnInitListener")
        {
            _owner = owner;
        }

        public void onInit(int status)
        {
            if (_owner != null) _owner.OnTtsInit(status);
        }
    }

    private void OnDestroy()
    {
        if (_tts != null)
        {
            _tts.Call("stop");
            _tts.Call("shutdown");
            _tts.Dispose();
            _tts = null;
        }
    }
#else
    private void OnDestroy() { }
#endif
}
