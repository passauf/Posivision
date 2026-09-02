using UnityEngine;
using UnityEngine.UI;
using Mediapipe.Unity;

/// <summary>
/// PiP webcam (sağ alt sabit panel) + Y/X Bot Humanoid spawn &amp; sürüm.
/// Not: Runtime'da oluşan controller Inspector ref taşımaz — botlar Assets'ten yüklenir.
/// </summary>
public class AvatarStageController : MonoBehaviour
{
    [Header("PiP Webcam")]
    [SerializeField] private Vector2 pipSize = new Vector2(280f, 210f);
    [SerializeField] private Vector2 pipOffset = new Vector2(-16f, 16f);
    [SerializeField] private bool showDebugSkeleton = true;

    [Header("Sahne")]
    [SerializeField] private Color stageBackground = new Color(0.12f, 0.14f, 0.18f, 1f);
    [SerializeField] private Vector3 avatarCameraPosition = new Vector3(0f, 1.4f, -3.8f);
    [SerializeField] private Vector3 botSpawnPosition = new Vector3(0f, 0f, 0f);
    [Tooltip("Model rotasyonu sabit kalır; kamera öne alınır.")]
    [SerializeField] private Vector3 botSpawnEuler = new Vector3(0f, 0f, 0f);
    [Tooltip("Açık: kamera modelin önüne geçer (180°) — laptop ön kamerası ile sol/sağ uyumu.")]
    [SerializeField] private bool avatarCameraFrontView = true;
    [Tooltip("Açık: avatar kamerasını karşı tarafa alır (ön↔arka / sol↔sağ).")]
    [SerializeField] private bool mirrorAvatarCameraSide = true;

    [Header("Karakterler (Inspector veya otomatik yükleme)")]
    [SerializeField] private GameObject yBot;
    [SerializeField] private GameObject xBot;
    [SerializeField] private bool createProceduralFallback = false;

    [Header("Bileşenler")]
    [SerializeField] private AvatarBodyDriver bodyDriver;
    [SerializeField] private FaceStrainAnalyzer strainAnalyzer;

    private ProceduralMannequin _mannequin;
    private Camera _avatarCamera;
    private bool _built;
    private GameObject _activeBot;
    private RectTransform _pipHost;
    private RawImage _pipRaw;
    private bool _pipReady;

    public AvatarBodyDriver BodyDriver => bodyDriver;
    public FaceStrainAnalyzer StrainAnalyzer => strainAnalyzer;
    public GameObject ActiveBot => _activeBot;
    public bool IsPipReady => _pipReady;
    public bool IsWebcamFullscreen { get; private set; }

    /// <summary>Tam ekran selfie (konum ayarı) veya sağ alt PiP.</summary>
    public void SetWebcamFullscreen(bool fullscreen)
    {
        if (!_pipReady)
            SetupDedicatedPipPanel();
        if (_pipHost == null) return;

        IsWebcamFullscreen = fullscreen;
        _pipLayoutApplied = false;
        if (_pipHost != null)
        {
            var bg = _pipHost.GetComponent<Image>();
            if (bg != null)
                bg.color = fullscreen
                    ? new Color(0f, 0f, 0f, 0f)
                    : new Color(0.05f, 0.06f, 0.08f, 0.9f);
        }
        MaintainPipHost();
        if (_pipHost != null)
            _pipHost.SetAsLastSibling();
    }

    public void SetWebcamPipCorner()
    {
        SetWebcamFullscreen(false);
    }

    private void Awake()
    {
        EnsureComponents();
        BuildStage();
        EnsureBotsSpawned();
        ApplyGender(PatientProfile.GenderMale);
        ConfigureSkeletonDebug();
    }

    private void Start()
    {
        EnsureBotsSpawned();
        ApplyGender(PatientProfile.GenderMale);
        FrameCameraOnActiveBot();
    }

    private void LateUpdate()
    {
        if (_pipReady)
            MaintainPipHost();
    }

    public void ApplyPipLayoutPublic()
    {
        SetupDedicatedPipPanel();
    }

    public void ApplyGender(int gender)
    {
        EnsureBotsSpawned();

        bool female = gender == PatientProfile.GenderFemale;
        if (yBot != null) yBot.SetActive(!female);
        if (xBot != null) xBot.SetActive(female);

        GameObject selected = female ? xBot : yBot;
        if (selected == null) selected = yBot != null ? yBot : xBot;
        _activeBot = selected;

        HideProceduralMannequin();
        if (bodyDriver == null) return;

        if (selected != null)
        {
            selected.SetActive(true);
            PrepareBotForDisplay(selected);

            Animator anim = selected.GetComponent<Animator>();
            if (anim == null) anim = selected.GetComponentInChildren<Animator>(true);

            if (anim != null && anim.isHuman && bodyDriver.BindHumanoid(anim))
            {
                var physio = FindObjectOfType<PhysioAnalyzer>(true);
                if (physio != null)
                {
                    bodyDriver.SetRegionMask(physio.RegionMask);
                    bodyDriver.SetArcRegion(physio.SelectedBodyRegionId);
                }
                else
                    bodyDriver.SetRegionMask(PoseRegionMask.ShoulderFlexion());

                TryBindHeadFromBot(selected);
                FrameCameraOnActiveBot();
                Debug.Log("[Avatar] Humanoid bağlandı: " + selected.name);
                return;
            }

            Debug.LogWarning("[Avatar] Bind başarısız. Humanoid + Animator kontrol et: " + (selected != null ? selected.name : "null"));
        }
        else
        {
            Debug.LogWarning("[Avatar] Y/X Bot yok. Assets/Y Bot.fbx ve Assets/X Bot.fbx olmalı veya Hierarchy'ye koy.");
        }
    }

    public void ApplyGenderFromProfile(PatientProfile profile)
    {
        ApplyGender(profile != null ? profile.gender : PatientProfile.GenderMale);
    }

    private void EnsureComponents()
    {
        if (bodyDriver == null)
            bodyDriver = GetComponent<AvatarBodyDriver>() ?? gameObject.AddComponent<AvatarBodyDriver>();
        if (strainAnalyzer == null)
            strainAnalyzer = GetComponent<FaceStrainAnalyzer>() ?? gameObject.AddComponent<FaceStrainAnalyzer>();
        if (GetComponent<ExampleMovementHologram>() == null)
            gameObject.AddComponent<ExampleMovementHologram>();
    }

    /// <summary>
    /// Sahnede yoksa Assets/Y Bot.fbx ve Assets/X Bot.fbx'ten instantiate eder.
    /// </summary>
    private void EnsureBotsSpawned()
    {
        yBot = ResolveOrSpawnBot(yBot, true);
        xBot = ResolveOrSpawnBot(xBot, false);
    }

    private GameObject ResolveOrSpawnBot(GameObject current, bool isY)
    {
        if (current != null && current.scene.IsValid() && current.scene.isLoaded)
            return current;

        // Sahnede ara
        GameObject found = FindSceneBot(isY);
        if (found != null) return found;

        // Assets'ten yükle (Editor Play Mode)
        GameObject prefab = LoadBotPrefab(isY);
        if (prefab == null) return null;

        GameObject instance = Instantiate(prefab);
        instance.name = isY ? "Y Bot" : "X Bot";
        instance.transform.position = botSpawnPosition;
        instance.transform.rotation = Quaternion.Euler(botSpawnEuler);
        PrepareBotForDisplay(instance);
        Debug.Log("[Avatar] Spawn edildi: " + instance.name);
        return instance;
    }

    private static GameObject FindSceneBot(bool isY)
    {
        Animator[] animators = FindObjectsOfType<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator a = animators[i];
            if (a == null || !a.gameObject.scene.IsValid()) continue;
            string n = a.gameObject.name.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
            if (isY && n.Contains("ybot")) return a.gameObject;
            if (!isY && n.Contains("xbot")) return a.gameObject;
        }

        string[] names = isY
            ? new[] { "Y Bot", "YBot", "ybot" }
            : new[] { "X Bot", "XBot", "xbot" };
        Transform[] all = FindObjectsOfType<Transform>(true);
        for (int n = 0; n < names.Length; n++)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject.scene.IsValid()
                    && string.Equals(all[i].name, names[n], System.StringComparison.OrdinalIgnoreCase))
                    return all[i].gameObject;
            }
        }
        return null;
    }

    private static GameObject LoadBotPrefab(bool isY)
    {
        string fileName = isY ? "Y Bot" : "X Bot";

        // Resources/Avatars/Y Bot (opsiyonel)
        GameObject fromRes = Resources.Load<GameObject>("Avatars/" + fileName);
        if (fromRes != null) return fromRes;
        fromRes = Resources.Load<GameObject>(fileName);
        if (fromRes != null) return fromRes;

#if UNITY_EDITOR
        string[] paths =
        {
            "Assets/" + fileName + ".fbx",
            "Assets/" + fileName + ".prefab",
            "Assets/Models/" + fileName + ".fbx",
            "Assets/Characters/" + fileName + ".fbx"
        };
        for (int i = 0; i < paths.Length; i++)
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            if (go != null) return go;
        }

        string[] guids = UnityEditor.AssetDatabase.FindAssets(fileName + " t:Model");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            if (path.IndexOf(fileName, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) return go;
        }
#endif
        return null;
    }

    private static void PrepareBotForDisplay(GameObject bot)
    {
        if (bot == null) return;
        bot.SetActive(true);
        int defaultLayer = LayerMask.NameToLayer("Default");
        SetLayerRecursive(bot.transform, defaultLayer >= 0 ? defaultLayer : 0);

        var renderers = bot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].enabled = true;
        }
    }

    private static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i), layer);
    }

    /// <summary>
    /// Omuz fleksiyonu yan profil: kamera çalışan kol tarafına orbit eder (model sabit).
    /// anatomicalRight ölçülüyorsa kamera bot.right yönünden bakar.
    /// </summary>
    public void ApplySideOrbitForMeasuredArm(bool measureRight, bool measureLeft, bool sideProfile)
    {
        if (!sideProfile)
        {
            avatarCameraFrontView = true;
            _sideOrbitMode = SideOrbitMode.Front;
            FrameCameraOnActiveBot();
            return;
        }

        if (measureRight && !measureLeft)
            _sideOrbitMode = SideOrbitMode.Right;
        else if (measureLeft && !measureRight)
            _sideOrbitMode = SideOrbitMode.Left;
        else
            _sideOrbitMode = SideOrbitMode.Right;

        avatarCameraFrontView = false;
        FrameCameraOnActiveBot();
    }

    private enum SideOrbitMode
    {
        Front = 0,
        Right = 1,
        Left = 2
    }

    private SideOrbitMode _sideOrbitMode = SideOrbitMode.Front;

    private void FrameCameraOnActiveBot()
    {
        if (_avatarCamera == null || _activeBot == null) return;

        Bounds bounds = new Bounds(_activeBot.transform.position, Vector3.one * 0.1f);
        bool has = false;
        var renderers = _activeBot.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i].enabled) continue;
            if (!has) { bounds = renderers[i].bounds; has = true; }
            else bounds.Encapsulate(renderers[i].bounds);
        }

        Vector3 center = has ? bounds.center : _activeBot.transform.position + Vector3.up * 1f;
        float size = has ? Mathf.Max(bounds.size.y, bounds.size.x, 1.2f) : 1.8f;
        float dist = size * 2.2f;
        float yOff = size * 0.1f;

        Vector3 forward = _activeBot.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
        else forward.Normalize();

        Vector3 right = _activeBot.transform.right;
        right.y = 0f;
        if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
        else right.Normalize();

        Vector3 camOffset;
        switch (_sideOrbitMode)
        {
            case SideOrbitMode.Right:
                camOffset = right * dist;
                break;
            case SideOrbitMode.Left:
                camOffset = -right * dist;
                break;
            case SideOrbitMode.Front:
            default:
                camOffset = avatarCameraFrontView ? forward * dist : -forward * dist;
                break;
        }

        // Ön görünüm: laptop ön kamerası ile sol/sağ uyumu. Yan profil orbit'te aynalama yok —
        // çalışan kol tarafı doğrudan SideOrbitMode ile seçilir.
        if (mirrorAvatarCameraSide && _sideOrbitMode == SideOrbitMode.Front)
            camOffset = -camOffset;

        _avatarCamera.transform.position = center + Vector3.up * yOff + camOffset;
        _avatarCamera.transform.LookAt(center);
        _avatarCamera.fieldOfView = 40f;
        _avatarCamera.cullingMask = ~0;
    }

    private void BuildStage()
    {
        if (_built) return;

        GameObject stageRoot = new GameObject("AvatarStage");
        stageRoot.transform.SetParent(transform, false);

        GameObject camGo = new GameObject("AvatarCamera");
        camGo.transform.SetParent(stageRoot.transform, false);
        camGo.transform.position = avatarCameraPosition;
        camGo.transform.LookAt(botSpawnPosition + Vector3.up);
        _avatarCamera = camGo.AddComponent<Camera>();
        _avatarCamera.clearFlags = CameraClearFlags.SolidColor;
        _avatarCamera.backgroundColor = stageBackground;
        _avatarCamera.fieldOfView = 40f;
        _avatarCamera.nearClipPlane = 0.05f;
        _avatarCamera.farClipPlane = 50f;
        _avatarCamera.depth = 0f;
        _avatarCamera.cullingMask = ~0;
        _avatarCamera.tag = "MainCamera";

        Camera[] cams = FindObjectsOfType<Camera>();
        for (int i = 0; i < cams.Length; i++)
        {
            if (cams[i] == _avatarCamera) continue;
            AudioListener al = cams[i].GetComponent<AudioListener>();
            if (al != null) al.enabled = false;
            cams[i].enabled = false;
        }

        if (camGo.GetComponent<AudioListener>() == null)
            camGo.AddComponent<AudioListener>();

        EnsureCanvasesOverlay();

        GameObject lightGo = new GameObject("AvatarKeyLight");
        lightGo.transform.SetParent(stageRoot.transform, false);
        lightGo.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;

        GameObject fill = new GameObject("AvatarFillLight");
        fill.transform.SetParent(stageRoot.transform, false);
        fill.transform.rotation = Quaternion.Euler(20f, 140f, 0f);
        var fillLight = fill.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.intensity = 0.35f;

        // Zemin
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "AvatarFloor";
        floor.transform.SetParent(stageRoot.transform, false);
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(1.5f, 1f, 1.5f);
        var fr = floor.GetComponent<Renderer>();
        if (fr != null) fr.material.color = new Color(0.16f, 0.18f, 0.22f);

        if (createProceduralFallback && yBot == null && xBot == null)
        {
            _mannequin = ProceduralMannequin.Build(stageRoot.transform);
            bodyDriver.BindMannequin(_mannequin);
            strainAnalyzer.BindHeadRenderer(_mannequin.HeadRenderer);
        }

        _built = true;
    }

    /// <summary>
    /// Orijinal Annotatable Screen'i gizler; Canvas altında sabit PiP RawImage oluşturur.
    /// Böylece Screen.Resize / stretch Y sorununu bypass eder.
    /// </summary>
    private void SetupDedicatedPipPanel()
    {
        EnsureCanvasesOverlay();
        HideMediapipeSampleChrome();
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        Mediapipe.Unity.Screen mpScreen = FindObjectOfType<Mediapipe.Unity.Screen>();
        RawImage sourceRaw = null;
        Texture webcamTex = null;

        if (mpScreen != null)
        {
            sourceRaw = mpScreen.GetComponent<RawImage>();
            if (sourceRaw == null) sourceRaw = mpScreen.GetComponentInChildren<RawImage>(true);
            if (sourceRaw != null) webcamTex = sourceRaw.texture;

            // MediaPipe Screen hâlâ texture üretir ama görünmez
            CanvasGroup cg = mpScreen.GetComponent<CanvasGroup>();
            if (cg == null) cg = mpScreen.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;
            cg.interactable = false;

            // Stretch'i de kır
            RectTransform screenRt = mpScreen.transform as RectTransform;
            if (screenRt != null)
            {
                screenRt.anchorMin = new Vector2(1f, 0f);
                screenRt.anchorMax = new Vector2(1f, 0f);
                screenRt.pivot = new Vector2(1f, 0f);
                screenRt.sizeDelta = Vector2.one;
                screenRt.anchoredPosition = new Vector2(-8f, 8f);
            }

            Mediapipe.Unity.Screen.PipLockEnabled = true;
        }

        // Mevcut PipHost
        Transform existing = canvas.transform.Find("WebcamPipHost");
        GameObject hostGo;
        if (existing != null) hostGo = existing.gameObject;
        else
        {
            hostGo = new GameObject("WebcamPipHost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            hostGo.transform.SetParent(canvas.transform, false);
        }

        _pipHost = hostGo.GetComponent<RectTransform>();
        var bg = hostGo.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.06f, 0.08f, 0.9f);
        bg.raycastTarget = false;

        Transform rawT = hostGo.transform.Find("PipRaw");
        GameObject rawGo;
        if (rawT != null) rawGo = rawT.gameObject;
        else
        {
            rawGo = new GameObject("PipRaw", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            rawGo.transform.SetParent(hostGo.transform, false);
        }

        _pipRaw = rawGo.GetComponent<RawImage>();
        _pipRaw.raycastTarget = false;
        if (webcamTex != null) _pipRaw.texture = webcamTex;
        else if (sourceRaw != null) _pipRaw.texture = sourceRaw.texture;

        // UV mirror (ön kamera)
        if (sourceRaw != null) _pipRaw.uvRect = sourceRaw.uvRect;

        MaintainPipHost();
        _pipReady = true;
        hostGo.transform.SetAsLastSibling();
    }

    /// <summary>
    /// MediaPipe örnek UI kabı (Container Panel): Header/Footer/Modal + gri arka plan.
    /// Bizim HUD + PiP yeterli; Body/Screen webcam kaynağı olduğu için bırakılır.
    /// </summary>
    private static void HideMediapipeSampleChrome()
    {
        Transform[] all = FindObjectsOfType<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.name != "Container Panel") continue;

            var img = t.GetComponent<Image>();
            if (img != null)
            {
                Color c = img.color;
                c.a = 0f;
                img.color = c;
                img.raycastTarget = false;
            }

            for (int c = 0; c < t.childCount; c++)
            {
                Transform child = t.GetChild(c);
                string n = child.name;
                if (n == "Header" || n == "Footer" || n == "Modal Panel")
                    child.gameObject.SetActive(false);
            }
        }
    }

    private Mediapipe.Unity.Screen _cachedMpScreen;
    private RawImage _cachedMpSourceRaw;
    private Texture _lastPipTexture;
    private Rect _lastPipUv;
    private bool _pipLayoutApplied;

    private void MaintainPipHost()
    {
        if (_pipHost == null) return;

        // cmd: layout bir kez yeter — her LateUpdate SetSize/anchor GC+CPU yakar
        if (!_pipLayoutApplied)
        {
            if (IsWebcamFullscreen)
            {
                _pipHost.anchorMin = Vector2.zero;
                _pipHost.anchorMax = Vector2.one;
                _pipHost.pivot = new Vector2(0.5f, 0.5f);
                _pipHost.anchoredPosition = Vector2.zero;
                _pipHost.offsetMin = Vector2.zero;
                _pipHost.offsetMax = Vector2.zero;
                _pipHost.localScale = Vector3.one;
                _pipHost.localEulerAngles = Vector3.zero;
            }
            else
            {
                _pipHost.anchorMin = new Vector2(1f, 0f);
                _pipHost.anchorMax = new Vector2(1f, 0f);
                _pipHost.pivot = new Vector2(1f, 0f);
                _pipHost.anchoredPosition = pipOffset;
                _pipHost.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, pipSize.x);
                _pipHost.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, pipSize.y);
                _pipHost.localScale = Vector3.one;
                _pipHost.localEulerAngles = Vector3.zero;
            }

            if (_pipRaw != null)
            {
                RectTransform rt = _pipRaw.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(4f, 4f);
                rt.offsetMax = new Vector2(-4f, -4f);
            }
            _pipLayoutApplied = true;
        }

        if (_pipRaw == null) return;

        // cmd: FindObjectOfType her kare = Android'de pahalı; Screen ref cache
        if (_cachedMpScreen == null)
            _cachedMpScreen = FindObjectOfType<Mediapipe.Unity.Screen>();
        if (_cachedMpScreen == null) return;

        if (_cachedMpSourceRaw == null)
        {
            _cachedMpSourceRaw = _cachedMpScreen.GetComponent<RawImage>();
            if (_cachedMpSourceRaw == null)
                _cachedMpSourceRaw = _cachedMpScreen.GetComponentInChildren<RawImage>(true);
        }
        if (_cachedMpSourceRaw == null || _cachedMpSourceRaw.texture == null) return;

        Texture tex = _cachedMpSourceRaw.texture;
        Rect uv = _cachedMpSourceRaw.uvRect;
        if (tex != _lastPipTexture)
        {
            _pipRaw.texture = tex;
            _lastPipTexture = tex;
        }
        if (uv.x != _lastPipUv.x || uv.y != _lastPipUv.y
            || uv.width != _lastPipUv.width || uv.height != _lastPipUv.height)
        {
            _pipRaw.uvRect = uv;
            _lastPipUv = uv;
        }
    }

    private void HideProceduralMannequin()
    {
        if (_mannequin != null && _mannequin.Root != null)
            _mannequin.Root.gameObject.SetActive(false);

        Transform[] all = FindObjectsOfType<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == "ProceduralMannequin")
                all[i].gameObject.SetActive(false);
        }
    }

    private void TryBindHeadFromBot(GameObject bot)
    {
        if (bot == null || strainAnalyzer == null) return;
        if (!bot.scene.IsValid() || !bot.scene.isLoaded) return;

        var smrs = bot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < smrs.Length; i++)
        {
            if (smrs[i] == null) continue;
            if (smrs[i].name.IndexOf("head", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                strainAnalyzer.BindHeadRenderer(smrs[i]);
                return;
            }
        }
        if (smrs.Length > 0) strainAnalyzer.BindHeadRenderer(smrs[0]);
    }

    private static void EnsureCanvasesOverlay()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>();
        for (int i = 0; i < canvases.Length; i++)
        {
            canvases[i].renderMode = RenderMode.ScreenSpaceOverlay;
            if (canvases[i].sortingOrder < 50)
                canvases[i].sortingOrder = 50;
        }
    }

    private void ConfigureSkeletonDebug()
    {
        var annotation = FindObjectOfType<PoseLandmarkerResultAnnotationController>(true);
        if (annotation == null) return;
        annotation.enabled = showDebugSkeleton;
        annotation.gameObject.SetActive(showDebugSkeleton);
        for (int i = 0; i < annotation.transform.childCount; i++)
            annotation.transform.GetChild(i).gameObject.SetActive(showDebugSkeleton);
    }
}
