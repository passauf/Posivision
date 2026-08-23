using UnityEngine;
using TMPro;

/// <summary>
/// Avatar omuz–kalça radial yay (coronal / sagittal).
/// Renk ve track, seans hedef açısına göre ölçeklenir: 0° kırmızı → hedef açı yeşil
/// (örn. hedef 20° ise 20°'de tam yeşil). SaMD Class B motivasyonel geri bildirim.
/// </summary>
public class ShoulderFlexionArcIndicator : MonoBehaviour
{
    private const int ArcSegments = 32;
    private const float TargetTrackAlpha = 0.42f;
    private const float FillAlphaMin = 0.78f;
    private const float FillAlphaMax = 0.96f;
    private const float ColorDirtyEpsilon = 0.01f;
    private const float AngleDirtyEpsilon = 0.35f;
    private const float RadiusDirtyEpsilon = 0.003f;
    /// <summary>Düşük ROM seansları için alt sınır (hedef 20° vb.).</summary>
    private const float MinPersonalTargetDegrees = 5f;
    /// <summary>Yazı, yayın dış kenarının üstüne (dünya yukarı) bu oran kadar çıkar.</summary>
    private const float LabelAboveOuterRatio = 0.22f;
    private const float LabelWorldScale = 0.55f;
    /// <summary>Gösterim rakamı: bu kadar derece sapmadan önceki tamsayıyı tut (titreme önleme).</summary>
    private const float DisplayIntegerHoldDegrees = 0.9f;

    [Tooltip("Yay kalınlığı: omuz–kalça mesafesinin oranı.")]
    [SerializeField] private float thicknessRatio = 0.18f;
    [SerializeField] private float hipReachRatio = 1f;
    [SerializeField] private float maxVisualDegrees = 180f;
    [Tooltip("Vücut mesh'inin önüne (kameraya) çok hafif çek — arkaya düşmesin.")]
    [SerializeField] private float planeOffset = 0.02f;
    [SerializeField] private Color lowColor = new Color(1f, 0.18f, 0.22f, 1f);
    [SerializeField] private Color midColor = new Color(1f, 0.88f, 0.12f, 1f);
    [SerializeField] private Color highColor = new Color(0.12f, 1f, 0.48f, 1f);
    [SerializeField] private Color trackColor = new Color(0.85f, 0.95f, 1f, 1f);
    [Header("Açı gösterimi (titreme azaltma)")]
    [Tooltip("Düşük = daha sakin rakam; yüksek beta = büyük harekette gecikmesiz takip.")]
    [SerializeField] private float displayMinCutoff = 1.15f;
    [SerializeField] private float displayBeta = 0.65f;
    [SerializeField] private float displayDCutoff = 1f;

    private ArcSide _right;
    private ArcSide _left;
    private bool _built;
    private Material _sharedMatTemplate;
    private AvatarBodyDriver.ArmRaisePlane _raisePlane = AvatarBodyDriver.ArmRaisePlane.Coronal;

    private struct ArcSide
    {
        public Transform shoulder;
        public Transform elbow;
        public Transform hip;
        public Transform avatarRoot;
        public GameObject root;
        public MeshFilter fillFilter;
        public MeshRenderer fillRenderer;
        public MeshFilter trackFilter;
        public MeshRenderer trackRenderer;
        public Mesh fillMesh;
        public Mesh trackMesh;
        public Vector3[] fillVerts;
        public Vector3[] trackVerts;
        public int[] tris;
        public Color[] fillColors;
        public Color[] trackColors;
        public bool isRight;
        public float lastAngle;
        public float lastTarget;
        public float lastProgress;
        public float lastOuterRadius;
        public Quaternion lastRotation;
        public bool visible;
        public TextMeshPro angleLabel;
        public int lastShownAngleInt;
        public int lastDrawnLabelInt;
        public int lastDrawnTargetInt;
        public OneEuroFilter1D displayFilter;
        public bool displayFilterReady;
        public float displaySmoothed;
        public bool hasDisplaySmoothed;
    }

    public void SetRaisePlane(AvatarBodyDriver.ArmRaisePlane plane)
    {
        _raisePlane = plane;
    }

    public void Bind(
        Transform rightUpperArm, Transform leftUpperArm,
        Transform rightElbow, Transform leftElbow,
        Transform rightHip, Transform leftHip,
        Transform avatarRoot)
    {
        EnsureMaterial();
        Transform parent = avatarRoot != null ? avatarRoot : null;
        if (rightUpperArm != null)
            BuildSide(ref _right, rightUpperArm, rightElbow, rightHip, parent, isRight: true);
        if (leftUpperArm != null)
            BuildSide(ref _left, leftUpperArm, leftElbow, leftHip, parent, isRight: false);
        _built = _right.root != null || _left.root != null;
        SetVisible(false);
    }

    public void Clear()
    {
        DestroySide(ref _right);
        DestroySide(ref _left);
        _built = false;
        if (_sharedMatTemplate != null)
        {
            Destroy(_sharedMatTemplate);
            _sharedMatTemplate = null;
        }
    }

    public void SetVisible(bool visible)
    {
        SetSideVisible(ref _right, visible && _right.shoulder != null);
        SetSideVisible(ref _left, visible && _left.shoulder != null);
    }

    public void SetArmActive(bool rightActive, bool leftActive)
    {
        SetSideVisible(ref _right, rightActive && _right.shoulder != null);
        SetSideVisible(ref _left, leftActive && _left.shoulder != null);
    }

    public void UpdateArcs(
        bool rightOk, float rightCurrent, float rightTarget,
        bool leftOk, float leftCurrent, float leftTarget)
    {
        if (!_built) return;
        UpdateSide(ref _right, rightOk, rightCurrent, rightTarget);
        UpdateSide(ref _left, leftOk, leftCurrent, leftTarget);
    }

    private void OnDestroy()
    {
        Clear();
    }

    private void EnsureMaterial()
    {
        if (_sharedMatTemplate != null) return;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        _sharedMatTemplate = new Material(shader);
        _sharedMatTemplate.renderQueue = 3000;
        if (_sharedMatTemplate.HasProperty("_Surface"))
            _sharedMatTemplate.SetFloat("_Surface", 1f);
        if (_sharedMatTemplate.HasProperty("_ZWrite"))
            _sharedMatTemplate.SetFloat("_ZWrite", 0f);
    }

    private void BuildSide(ref ArcSide side, Transform shoulder, Transform elbow, Transform hip, Transform avatarRoot, bool isRight)
    {
        DestroySide(ref side);
        side.isRight = isRight;
        side.shoulder = shoulder;
        side.elbow = elbow;
        side.hip = hip;
        side.avatarRoot = avatarRoot;
        side.lastAngle = -1f;
        side.lastTarget = -1f;
        side.lastProgress = -1f;
        side.lastOuterRadius = -1f;
        side.lastRotation = Quaternion.identity;

        Transform parent = avatarRoot != null ? avatarRoot : shoulder;
        side.root = new GameObject(isRight ? "ShoulderArc_R" : "ShoulderArc_L");
        side.root.transform.SetParent(parent, false);
        side.root.transform.localScale = Vector3.one;
        side.root.layer = shoulder.gameObject.layer;

        CreateArcMeshObjects(side.root.transform, isRight ? "Fill_R" : "Fill_L",
            out side.fillFilter, out side.fillRenderer, out side.fillMesh,
            out side.fillVerts, out side.fillColors, out side.tris);
        CreateArcMeshObjects(side.root.transform, isRight ? "Track_R" : "Track_L",
            out side.trackFilter, out side.trackRenderer, out side.trackMesh,
            out side.trackVerts, out side.trackColors, out _);

        side.trackRenderer.sortingOrder = 0;
        side.fillRenderer.sortingOrder = 1;
        CreateAngleLabel(ref side);
        side.displayFilter = default;
        side.displayFilter.Configure(displayMinCutoff, displayBeta, displayDCutoff);
        side.displayFilter.Reset();
        side.displayFilterReady = true;
        side.lastShownAngleInt = int.MinValue;
        side.lastDrawnLabelInt = int.MinValue;
        side.lastDrawnTargetInt = int.MinValue;
        side.visible = true;
    }

    private void CreateAngleLabel(ref ArcSide side)
    {
        if (side.root == null) return;
        GameObject go = new GameObject(side.isRight ? "AngleLabel_R" : "AngleLabel_L");
        go.transform.SetParent(side.root.transform, false);
        go.transform.localScale = Vector3.one * LabelWorldScale;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = "0°";
        tmp.fontSize = 3.2f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        var rt = tmp.rectTransform;
        rt.sizeDelta = new Vector2(2.4f, 0.55f);
        side.angleLabel = tmp;
        side.lastShownAngleInt = int.MinValue;
        side.lastDrawnTargetInt = int.MinValue;
    }

    private void CreateArcMeshObjects(
        Transform parent, string name,
        out MeshFilter filter, out MeshRenderer renderer, out Mesh mesh,
        out Vector3[] verts, out Color[] colors, out int[] tris)
    {
        GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        filter = go.GetComponent<MeshFilter>();
        renderer = go.GetComponent<MeshRenderer>();
        mesh = new Mesh { name = name };
        mesh.MarkDynamic();
        filter.sharedMesh = mesh;

        Material mat = new Material(_sharedMatTemplate);
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        int vertCount = (ArcSegments + 1) * 2;
        verts = new Vector3[vertCount];
        colors = new Color[vertCount];
        tris = new int[ArcSegments * 6];
        for (int i = 0; i < ArcSegments; i++)
        {
            int v = i * 2;
            int t = i * 6;
            tris[t] = v;
            tris[t + 1] = v + 1;
            tris[t + 2] = v + 3;
            tris[t + 3] = v;
            tris[t + 4] = v + 3;
            tris[t + 5] = v + 2;
        }
    }

    /// <summary>
    /// AvatarBodyDriver.ApplyElevationToUpperArm ile aynı düzlem: 0° = karakter aşağısı.
    /// Yay omuzda; yarıçap omuz–kalça mesafesinden.
    /// </summary>
    private bool AlignToShoulderHip(ref ArcSide side, out float outerRadius)
    {
        outerRadius = 0f;
        if (side.root == null || side.shoulder == null) return false;

        Transform root = side.avatarRoot != null ? side.avatarRoot : side.shoulder.root;
        Vector3 shoulderPos = side.shoulder.position;

        Vector3 planeNormal;
        Vector3 desiredRaise;
        if (_raisePlane == AvatarBodyDriver.ArmRaisePlane.Sagittal)
        {
            planeNormal = side.isRight ? root.right : -root.right;
            desiredRaise = root.forward;
        }
        else
        {
            planeNormal = root.forward;
            desiredRaise = side.isRight ? root.right : -root.right;
        }

        if (planeNormal.sqrMagnitude < 1e-8f) return false;
        planeNormal.Normalize();

        Vector3 down0 = Vector3.ProjectOnPlane(-root.up, planeNormal);
        if (down0.sqrMagnitude < 1e-8f)
        {
            Vector3 hipPos = side.hip != null ? side.hip.position : shoulderPos - root.up;
            down0 = Vector3.ProjectOnPlane(hipPos - shoulderPos, planeNormal);
        }
        if (down0.sqrMagnitude < 1e-8f) return false;
        down0.Normalize();

        Vector3 hipPosForRadius = side.hip != null ? side.hip.position : shoulderPos + down0;
        Vector3 toHip = hipPosForRadius - shoulderPos;
        float hipDist = Vector3.ProjectOnPlane(toHip, planeNormal).magnitude;
        if (hipDist < 1e-4f)
            hipDist = toHip.magnitude;
        if (hipDist < 1e-4f)
            hipDist = 0.35f;
        outerRadius = hipDist * Mathf.Clamp(hipReachRatio, 0.7f, 1.05f);

        Vector3 center = shoulderPos + planeNormal * planeOffset;
        Quaternion rot = Quaternion.LookRotation(planeNormal, -down0);

        Vector3 localRaise = rot * Vector3.right;
        desiredRaise = Vector3.ProjectOnPlane(desiredRaise, planeNormal);
        if (desiredRaise.sqrMagnitude > 1e-8f
            && Vector3.Dot(localRaise, desiredRaise.normalized) < 0f)
        {
            planeNormal = -planeNormal;
            center = shoulderPos + planeNormal * planeOffset;
            rot = Quaternion.LookRotation(planeNormal, -down0);
        }

        side.root.transform.SetPositionAndRotation(center, rot);
        return true;
    }

    /// <summary>Sürülmüş üst kol kemik yönüne göre yay düzlemini ince ayar.</summary>
    private static void RefineRotationFromBone(ref ArcSide side, float degrees)
    {
        if (side.elbow == null || side.shoulder == null || side.root == null) return;
        if (degrees < 3f) return;

        Vector3 boneDir = side.elbow.position - side.shoulder.position;
        if (boneDir.sqrMagnitude < 1e-8f) return;
        boneDir.Normalize();

        Quaternion baseRot = side.root.transform.rotation;
        Vector3 planeNormal = baseRot * Vector3.forward;
        Vector3 boneInPlane = Vector3.ProjectOnPlane(boneDir, planeNormal);
        if (boneInPlane.sqrMagnitude < 1e-8f) return;
        boneInPlane.Normalize();

        float theta = Mathf.Clamp(degrees, 0f, JointAngleJob.MaxShoulderElevationDegrees) * Mathf.Deg2Rad;
        Vector3 expected = baseRot * ((-Vector3.up) * Mathf.Cos(theta) + (Vector3.right * Mathf.Sin(theta)));
        if (expected.sqrMagnitude < 1e-8f) return;
        expected.Normalize();

        side.root.transform.rotation = Quaternion.FromToRotation(expected, boneInPlane) * baseRot;
    }

    private void UpdateSide(ref ArcSide side, bool ok, float currentDeg, float targetDeg)
    {
        if (side.root == null) return;
        if (!ok)
        {
            side.displayFilterReady = false;
            side.lastShownAngleInt = int.MinValue;
            side.lastDrawnLabelInt = int.MinValue;
            side.hasDisplaySmoothed = false;
            SetSideVisible(ref side, false);
            return;
        }

        if (!AlignToShoulderHip(ref side, out float outerR))
        {
            SetSideVisible(ref side, false);
            return;
        }

        // Kişisel seans hedefi: track ucu + renk skalası (0→hedef = kırmızı→yeşil)
        float personalTarget = targetDeg > 1f ? targetDeg : maxVisualDegrees;
        personalTarget = Mathf.Clamp(personalTarget, MinPersonalTargetDegrees, maxVisualDegrees);
        float angle = Mathf.Clamp(currentDeg, 0f, JointAngleJob.MaxShoulderElevationDegrees);
        RefineRotationFromBone(ref side, angle);

        SetSideVisible(ref side, true);

        // Hedefi aşınca da yeşil kalsın; dolgu track ucunu geçmesin
        float progress = Mathf.Clamp01(angle / personalTarget);
        float fillDegrees = Mathf.Min(angle, personalTarget);
        int shownInt = Mathf.RoundToInt(angle);
        float innerR = Mathf.Max(0.01f, outerR * (1f - thicknessRatio));
        float target = personalTarget;

        Quaternion rot = side.root.transform.rotation;
        bool rotDirty = Quaternion.Angle(rot, side.lastRotation) > 0.5f;
        bool angleDirty = Mathf.Abs(fillDegrees - side.lastAngle) >= AngleDirtyEpsilon
                          || Mathf.Abs(target - side.lastTarget) >= AngleDirtyEpsilon
                          || Mathf.Abs(outerR - side.lastOuterRadius) >= RadiusDirtyEpsilon
                          || rotDirty;
        bool colorDirty = Mathf.Abs(progress - side.lastProgress) >= ColorDirtyEpsilon;

        Color fill = EvaluateProgressColor(progress);
        fill.a = Mathf.Lerp(FillAlphaMin, FillAlphaMax, progress);

        if (angleDirty || colorDirty)
        {
            side.lastAngle = fillDegrees;
            side.lastTarget = target;
            side.lastProgress = progress;
            side.lastOuterRadius = outerR;
            side.lastRotation = rot;

            WriteArcMesh(side.trackMesh, side.trackVerts, side.trackColors, side.tris,
                target, innerR, outerR, WithAlpha(trackColor, TargetTrackAlpha));
            WriteArcMesh(side.fillMesh, side.fillVerts, side.fillColors, side.tris,
                fillDegrees, innerR, outerR, fill);
        }

        UpdateAngleLabel(ref side, shownInt, target, outerR, fill);
    }

    /// <summary>
    /// Tamsayı: büyük sıçramada tek seferde Round etme — en fazla ±1 adım.
    /// </summary>
    private static int StickyDisplayInteger(float smoothedDeg, ref int lastShown)
    {
        int rounded = Mathf.RoundToInt(smoothedDeg);
        if (lastShown == int.MinValue)
        {
            lastShown = rounded;
            return lastShown;
        }

        if (smoothedDeg >= lastShown + DisplayIntegerHoldDegrees)
            lastShown = Mathf.Min(lastShown + 1, rounded);
        else if (smoothedDeg <= lastShown - DisplayIntegerHoldDegrees)
            lastShown = Mathf.Max(lastShown - 1, rounded);

        lastShown = Mathf.Clamp(lastShown, 0, Mathf.RoundToInt(JointAngleJob.MaxShoulderElevationDegrees));
        return lastShown;
    }

    private static float RateLimitDisplayAngle(ref ArcSide side, float filtered)
    {
        float dt = Mathf.Clamp(Time.unscaledDeltaTime, 1f / 120f, 0.1f);
        if (!side.hasDisplaySmoothed)
        {
            side.displaySmoothed = filtered;
            side.hasDisplaySmoothed = true;
            return filtered;
        }

        const float maxDeg = JointAngleJob.MaxShoulderElevationDegrees;
        float maxUp = 95f * dt;
        float maxDown = 120f * dt;
        if (side.displaySmoothed >= 145f) maxUp = 65f * dt;
        if (side.displaySmoothed >= 165f) maxUp = 50f * dt;

        float d = filtered - side.displaySmoothed;
        if (d > maxUp) d = maxUp;
        else if (d < -maxDown) d = -maxDown;
        side.displaySmoothed = Mathf.Clamp(side.displaySmoothed + d, 0f, maxDeg);
        return side.displaySmoothed;
    }

    /// <summary>
    /// Yazı yayın kenarının üstünde (dünya yukarı); yayın önüne/içine oturmaz.
    /// </summary>
    private void UpdateAngleLabel(ref ArcSide side, int shownDeg, float targetDeg, float outerR, Color fillColor)
    {
        if (side.angleLabel == null || side.root == null) return;

        float spanDeg = Mathf.Clamp(targetDeg > 1f ? targetDeg : maxVisualDegrees, 10f, maxVisualDegrees);
        Vector3 bestRimLocal = Vector3.zero;
        float bestWorldY = float.NegativeInfinity;
        const int samples = 24;
        for (int i = 0; i <= samples; i++)
        {
            float u = samples > 0 ? (float)i / samples : 0f;
            float ang = spanDeg * u * Mathf.Deg2Rad;
            Vector3 local = (-Vector3.up * Mathf.Cos(ang)) + (Vector3.right * Mathf.Sin(ang));
            local *= outerR;
            Vector3 world = side.root.transform.TransformPoint(local);
            if (world.y > bestWorldY)
            {
                bestWorldY = world.y;
                bestRimLocal = local;
            }
        }

        Vector3 rimWorld = side.root.transform.TransformPoint(bestRimLocal);
        Vector3 aboveWorld = rimWorld + Vector3.up * (outerR * LabelAboveOuterRatio);
        side.angleLabel.transform.position = aboveWorld;

        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 toCam = cam.transform.position - aboveWorld;
            if (toCam.sqrMagnitude > 1e-6f)
                side.angleLabel.transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        }

        int targetInt = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(1f, targetDeg)), 1, 180);
        if (shownDeg != side.lastDrawnLabelInt || targetInt != side.lastDrawnTargetInt)
        {
            side.lastDrawnLabelInt = shownDeg;
            side.lastDrawnTargetInt = targetInt;
            // Mevcut / hedef — hastanın kaldırmaya ne kadar yaklaştığını okusun
            side.angleLabel.text = shownDeg.ToString() + "° / " + targetInt.ToString() + "°";
        }

        Color c = fillColor;
        c.a = 1f;
        side.angleLabel.color = c;
    }

    private static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    private Color EvaluateProgressColor(float t)
    {
        t = Mathf.Clamp01(t);
        if (t < 0.5f)
            return Color.Lerp(lowColor, midColor, t * 2f);
        return Color.Lerp(midColor, highColor, (t - 0.5f) * 2f);
    }

    /// <summary>0° = lokal aşağı (kalça), artan açı = lokal +X (vücut dışı / öne).</summary>
    private static void WriteArcMesh(
        Mesh mesh, Vector3[] verts, Color[] colors, int[] tris,
        float degrees, float innerR, float outerR, Color color)
    {
        if (mesh == null || verts == null) return;

        float span = Mathf.Clamp(degrees, 0f, 180f) * Mathf.Deg2Rad;

        for (int i = 0; i <= ArcSegments; i++)
        {
            float u = ArcSegments > 0 ? (float)i / ArcSegments : 0f;
            float ang = span * u;
            float cos = Mathf.Cos(ang);
            float sin = Mathf.Sin(ang);
            Vector3 dir = (-Vector3.up * cos) + (Vector3.right * sin);
            int v = i * 2;
            verts[v] = dir * innerR;
            verts[v + 1] = dir * outerR;
            colors[v] = color;
            colors[v + 1] = color;
        }

        mesh.Clear();
        mesh.vertices = verts;
        mesh.colors = colors;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
    }

    private static void SetSideVisible(ref ArcSide side, bool visible)
    {
        if (side.root == null) return;
        if (side.visible == visible && side.root.activeSelf == visible) return;
        side.visible = visible;
        side.root.SetActive(visible);
        if (side.angleLabel != null)
            side.angleLabel.gameObject.SetActive(visible);
    }

    private static void DestroySide(ref ArcSide side)
    {
        if (side.angleLabel != null)
        {
            Object.Destroy(side.angleLabel.gameObject);
            side.angleLabel = null;
        }
        if (side.fillMesh != null) Object.Destroy(side.fillMesh);
        if (side.trackMesh != null) Object.Destroy(side.trackMesh);
        if (side.fillRenderer != null && side.fillRenderer.sharedMaterial != null)
            Object.Destroy(side.fillRenderer.sharedMaterial);
        if (side.trackRenderer != null && side.trackRenderer.sharedMaterial != null)
            Object.Destroy(side.trackRenderer.sharedMaterial);
        if (side.root != null) Object.Destroy(side.root);
        side = default;
    }
}
