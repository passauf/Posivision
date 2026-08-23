using UnityEngine;
using TMPro;

/// <summary>
/// Kalça–diz radial yay (uyluk elevasyonu). Omuz yayından bağımsız — koltuk altı / otururken uyluk.
/// 0° = bacak aşağı, hedefe doğru dolgu. SaMD Class B motivasyon; teşhis değildir.
/// </summary>
public class HipKneeArcIndicator : MonoBehaviour
{
    private const int ArcSegments = 28;
    private const float TargetTrackAlpha = 0.40f;
    private const float FillAlphaMin = 0.75f;
    private const float FillAlphaMax = 0.95f;
    private const float ColorDirtyEpsilon = 0.01f;
    private const float AngleDirtyEpsilon = 0.4f;
    private const float RadiusDirtyEpsilon = 0.003f;
    private const float MinPersonalTargetDegrees = 5f;
    private const float LabelAboveOuterRatio = 0.18f;
    private const float LabelWorldScale = 0.55f;
    private const float MaxThighDegrees = 120f;

    [SerializeField] private float thicknessRatio = 0.16f;
    [SerializeField] private float kneeReachRatio = 1f;
    [SerializeField] private float maxVisualDegrees = 120f;
    [SerializeField] private float planeOffset = 0.015f;
    [SerializeField] private Color lowColor = new Color(1f, 0.18f, 0.22f, 1f);
    [SerializeField] private Color midColor = new Color(1f, 0.88f, 0.12f, 1f);
    [SerializeField] private Color highColor = new Color(0.12f, 1f, 0.48f, 1f);
    [SerializeField] private Color trackColor = new Color(0.75f, 0.9f, 1f, 1f);

    private ArcSide _right;
    private ArcSide _left;
    private bool _built;
    private Material _sharedMatTemplate;
    private AvatarBodyDriver.ArmRaisePlane _raisePlane = AvatarBodyDriver.ArmRaisePlane.Sagittal;

    private struct ArcSide
    {
        public Transform hip;
        public Transform knee;
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
        public TextMeshPro angleLabel;
        public int lastDrawnLabelInt;
        public int lastDrawnTargetInt;
    }

    public void SetRaisePlane(AvatarBodyDriver.ArmRaisePlane plane)
    {
        _raisePlane = plane;
    }

    public void Bind(
        Transform rightHip, Transform leftHip,
        Transform rightKnee, Transform leftKnee,
        Transform avatarRoot)
    {
        EnsureMaterial();
        Transform parent = avatarRoot != null ? avatarRoot : null;
        if (rightHip != null)
            BuildSide(ref _right, rightHip, rightKnee, parent, isRight: true);
        if (leftHip != null)
            BuildSide(ref _left, leftHip, leftKnee, parent, isRight: false);
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
        SetSideVisible(ref _right, visible && _right.hip != null);
        SetSideVisible(ref _left, visible && _left.hip != null);
    }

    public void SetLegActive(bool rightActive, bool leftActive)
    {
        SetSideVisible(ref _right, rightActive && _right.hip != null);
        SetSideVisible(ref _left, leftActive && _left.hip != null);
    }

    /// <summary>
    /// Açı kemikten (uyluk vs aşağı) hesaplanır; target seans hedefi ile aynı ölçek.
    /// </summary>
    public void UpdateArcs(bool rightOk, float rightTarget, bool leftOk, float leftTarget)
    {
        if (!_built) return;
        UpdateSide(ref _right, rightOk, rightTarget);
        UpdateSide(ref _left, leftOk, leftTarget);
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
    }

    private void BuildSide(ref ArcSide side, Transform hip, Transform knee, Transform avatarRoot, bool isRight)
    {
        DestroySide(ref side);
        side.isRight = isRight;
        side.hip = hip;
        side.knee = knee;
        side.avatarRoot = avatarRoot;
        side.lastAngle = -1f;
        side.lastTarget = -1f;
        side.lastProgress = -1f;
        side.lastOuterRadius = -1f;
        side.lastRotation = Quaternion.identity;
        side.lastDrawnLabelInt = int.MinValue;
        side.lastDrawnTargetInt = int.MinValue;

        Transform parent = avatarRoot != null ? avatarRoot : hip;
        side.root = new GameObject(isRight ? "HipKneeArc_R" : "HipKneeArc_L");
        side.root.transform.SetParent(parent, false);
        side.root.layer = hip.gameObject.layer;

        CreateArcMesh(side.root.transform, isRight ? "Fill_R" : "Fill_L",
            out side.fillFilter, out side.fillRenderer, out side.fillMesh,
            out side.fillVerts, out side.fillColors, out side.tris);
        CreateArcMesh(side.root.transform, isRight ? "Track_R" : "Track_L",
            out side.trackFilter, out side.trackRenderer, out side.trackMesh,
            out side.trackVerts, out side.trackColors, out _);
        side.trackRenderer.sortingOrder = 0;
        side.fillRenderer.sortingOrder = 1;
        CreateAngleLabel(ref side);
    }

    private void CreateAngleLabel(ref ArcSide side)
    {
        GameObject go = new GameObject(side.isRight ? "ThighLabel_R" : "ThighLabel_L");
        go.transform.SetParent(side.root.transform, false);
        go.transform.localScale = Vector3.one * LabelWorldScale;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = "0° / 0°";
        tmp.fontSize = 2.6f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.outlineWidth = 0.22f;
        tmp.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        tmp.rectTransform.sizeDelta = new Vector2(2.2f, 0.5f);
        side.angleLabel = tmp;
    }

    private void CreateArcMesh(
        Transform parent, string name,
        out MeshFilter filter, out MeshRenderer renderer, out Mesh mesh,
        out Vector3[] verts, out Color[] colors, out int[] tris)
    {
        GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        filter = go.GetComponent<MeshFilter>();
        renderer = go.GetComponent<MeshRenderer>();
        mesh = new Mesh { name = name };
        mesh.MarkDynamic();
        filter.sharedMesh = mesh;
        renderer.sharedMaterial = _sharedMatTemplate;
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

    private void UpdateSide(ref ArcSide side, bool ok, float targetDeg)
    {
        if (side.root == null) return;
        if (!ok || side.hip == null || side.knee == null)
        {
            SetSideVisible(ref side, false);
            return;
        }

        if (!AlignToHipKnee(ref side, out float outerR, out float currentDeg))
        {
            SetSideVisible(ref side, false);
            return;
        }

        float personalTarget = targetDeg > 1f ? targetDeg : maxVisualDegrees;
        personalTarget = Mathf.Clamp(personalTarget, MinPersonalTargetDegrees, maxVisualDegrees);
        float angle = Mathf.Clamp(currentDeg, 0f, MaxThighDegrees);
        SetSideVisible(ref side, true);

        float progress = Mathf.Clamp01(angle / personalTarget);
        float fillDegrees = Mathf.Min(angle, personalTarget);
        int shownInt = Mathf.RoundToInt(angle);
        float innerR = Mathf.Max(0.01f, outerR * (1f - thicknessRatio));

        Quaternion rot = side.root.transform.rotation;
        bool rotDirty = Quaternion.Angle(rot, side.lastRotation) > 0.5f;
        bool angleDirty = Mathf.Abs(fillDegrees - side.lastAngle) >= AngleDirtyEpsilon
                          || Mathf.Abs(personalTarget - side.lastTarget) >= AngleDirtyEpsilon
                          || Mathf.Abs(outerR - side.lastOuterRadius) >= RadiusDirtyEpsilon
                          || rotDirty;
        bool colorDirty = Mathf.Abs(progress - side.lastProgress) >= ColorDirtyEpsilon;

        Color fill = EvaluateProgressColor(progress);
        fill.a = Mathf.Lerp(FillAlphaMin, FillAlphaMax, progress);

        if (angleDirty || colorDirty)
        {
            side.lastAngle = fillDegrees;
            side.lastTarget = personalTarget;
            side.lastProgress = progress;
            side.lastOuterRadius = outerR;
            side.lastRotation = rot;

            WriteArcMesh(side.trackMesh, side.trackVerts, side.trackColors, side.tris,
                personalTarget, innerR, outerR, WithAlpha(trackColor, TargetTrackAlpha));
            WriteArcMesh(side.fillMesh, side.fillVerts, side.fillColors, side.tris,
                fillDegrees, innerR, outerR, fill);
        }

        UpdateAngleLabel(ref side, shownInt, personalTarget, outerR, fill);
    }

    private bool AlignToHipKnee(ref ArcSide side, out float outerRadius, out float elevationDegrees)
    {
        outerRadius = 0f;
        elevationDegrees = 0f;
        if (side.root == null || side.hip == null || side.knee == null) return false;

        Transform root = side.avatarRoot != null ? side.avatarRoot : side.hip.root;
        Vector3 hipPos = side.hip.position;
        Vector3 kneePos = side.knee.position;

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
        if (down0.sqrMagnitude < 1e-8f) return false;
        down0.Normalize();

        Vector3 thigh = Vector3.ProjectOnPlane(kneePos - hipPos, planeNormal);
        float thighLen = thigh.magnitude;
        if (thighLen < 1e-4f)
            thighLen = Vector3.Distance(hipPos, kneePos);
        if (thighLen < 1e-4f)
            thighLen = 0.4f;
        outerRadius = thighLen * Mathf.Clamp(kneeReachRatio, 0.7f, 1.05f);

        if (thigh.sqrMagnitude > 1e-8f)
        {
            thigh.Normalize();
            elevationDegrees = Vector3.Angle(down0, thigh);
            Vector3 cross = Vector3.Cross(down0, thigh);
            if (Vector3.Dot(cross, planeNormal) < 0f)
                elevationDegrees = -elevationDegrees;
            elevationDegrees = Mathf.Abs(elevationDegrees);
        }

        Vector3 center = hipPos + planeNormal * planeOffset;
        Quaternion rot = Quaternion.LookRotation(planeNormal, -down0);

        desiredRaise = Vector3.ProjectOnPlane(desiredRaise, planeNormal);
        Vector3 localRaise = rot * Vector3.right;
        if (desiredRaise.sqrMagnitude > 1e-8f
            && Vector3.Dot(localRaise, desiredRaise.normalized) < 0f)
        {
            planeNormal = -planeNormal;
            center = hipPos + planeNormal * planeOffset;
            rot = Quaternion.LookRotation(planeNormal, -down0);
        }

        side.root.transform.SetPositionAndRotation(center, rot);
        return true;
    }

    private void UpdateAngleLabel(ref ArcSide side, int shownDeg, float targetDeg, float outerR, Color fillColor)
    {
        if (side.angleLabel == null || side.root == null) return;

        float spanDeg = Mathf.Clamp(targetDeg > 1f ? targetDeg : maxVisualDegrees, 10f, maxVisualDegrees);
        Vector3 bestRimLocal = Vector3.zero;
        float bestWorldY = float.PositiveInfinity;
        const int samples = 20;
        // Koltuk altı: yayın alt kenarına yakın etiket
        for (int i = 0; i <= samples; i++)
        {
            float u = samples > 0 ? (float)i / samples : 0f;
            float ang = spanDeg * u * Mathf.Deg2Rad;
            Vector3 local = (-Vector3.up * Mathf.Cos(ang)) + (Vector3.right * Mathf.Sin(ang));
            local *= outerR;
            Vector3 world = side.root.transform.TransformPoint(local);
            if (world.y < bestWorldY)
            {
                bestWorldY = world.y;
                bestRimLocal = local;
            }
        }

        Vector3 rimWorld = side.root.transform.TransformPoint(bestRimLocal);
        Vector3 belowWorld = rimWorld - Vector3.up * (outerR * LabelAboveOuterRatio);
        side.angleLabel.transform.position = belowWorld;

        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 toCam = cam.transform.position - belowWorld;
            if (toCam.sqrMagnitude > 1e-6f)
                side.angleLabel.transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        }

        int targetInt = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(1f, targetDeg)), 1, 180);
        if (shownDeg != side.lastDrawnLabelInt || targetInt != side.lastDrawnTargetInt)
        {
            side.lastDrawnLabelInt = shownDeg;
            side.lastDrawnTargetInt = targetInt;
            side.angleLabel.text = shownDeg.ToString() + "° / " + targetInt.ToString() + "°";
        }

        Color c = fillColor;
        c.a = 1f;
        side.angleLabel.color = c;
    }

    private void WriteArcMesh(
        Mesh mesh, Vector3[] verts, Color[] colors, int[] tris,
        float degrees, float innerR, float outerR, Color color)
    {
        if (mesh == null || verts == null) return;
        float clamped = Mathf.Clamp(degrees, 0f, maxVisualDegrees);
        for (int i = 0; i <= ArcSegments; i++)
        {
            float u = ArcSegments > 0 ? (float)i / ArcSegments : 0f;
            float ang = clamped * u * Mathf.Deg2Rad;
            Vector3 dir = (-Vector3.up * Mathf.Cos(ang)) + (Vector3.right * Mathf.Sin(ang));
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

    private Color EvaluateProgressColor(float progress01)
    {
        float t = Mathf.Clamp01(progress01);
        if (t < 0.5f)
            return Color.Lerp(lowColor, midColor, t * 2f);
        return Color.Lerp(midColor, highColor, (t - 0.5f) * 2f);
    }

    private static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    private void SetSideVisible(ref ArcSide side, bool visible)
    {
        if (side.root == null) return;
        if (side.root.activeSelf != visible)
            side.root.SetActive(visible);
        if (side.angleLabel != null)
            side.angleLabel.gameObject.SetActive(visible);
    }

    private void DestroySide(ref ArcSide side)
    {
        if (side.fillMesh != null) Destroy(side.fillMesh);
        if (side.trackMesh != null) Destroy(side.trackMesh);
        if (side.angleLabel != null) Destroy(side.angleLabel.gameObject);
        if (side.root != null) Destroy(side.root);
        side = default;
    }
}
