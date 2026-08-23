using UnityEngine;
using TMPro;

/// <summary>
/// Üç noktalı menteşe yayı (dirsek: omuz–dirsek–el; ayak bileği: diz–ayak–parmak).
/// 0° = eklem açık (180° iç açı), dolgu fleksiyon. SaMD Class B görsel; teşhis değildir.
/// </summary>
public class JointHingeArcIndicator : MonoBehaviour
{
    private const int ArcSegments = 28;
    private const float TargetTrackAlpha = 0.40f;
    private const float FillAlphaMin = 0.75f;
    private const float FillAlphaMax = 0.95f;
    private const float ColorDirtyEpsilon = 0.01f;
    private const float AngleDirtyEpsilon = 0.4f;
    private const float RadiusDirtyEpsilon = 0.003f;
    private const float MinPersonalTargetDegrees = 5f;
    private const float LabelAboveOuterRatio = 0.20f;
    private const float LabelWorldScale = 0.55f;
    private const float MaxHingeDegrees = 160f;

    [SerializeField] private float thicknessRatio = 0.16f;
    [SerializeField] private float distalReachRatio = 1f;
    [SerializeField] private float maxVisualDegrees = 160f;
    [SerializeField] private float planeOffset = 0.015f;
    [SerializeField] private Color lowColor = new Color(1f, 0.18f, 0.22f, 1f);
    [SerializeField] private Color midColor = new Color(1f, 0.88f, 0.12f, 1f);
    [SerializeField] private Color highColor = new Color(0.12f, 1f, 0.48f, 1f);
    [SerializeField] private Color trackColor = new Color(0.75f, 0.9f, 1f, 1f);

    private ArcSide _right;
    private ArcSide _left;
    private bool _built;
    private Material _sharedMatTemplate;
    private string _rootName = "HingeArc";

    private struct ArcSide
    {
        public Transform proximal;
        public Transform hinge;
        public Transform distal;
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

    public void ConfigureName(string rootName)
    {
        if (!string.IsNullOrEmpty(rootName))
            _rootName = rootName;
    }

    public void Bind(
        Transform rightProximal, Transform leftProximal,
        Transform rightHinge, Transform leftHinge,
        Transform rightDistal, Transform leftDistal,
        Transform avatarRoot)
    {
        EnsureMaterial();
        Transform parent = avatarRoot != null ? avatarRoot : null;
        if (rightHinge != null)
            BuildSide(ref _right, rightProximal, rightHinge, rightDistal, parent, isRight: true);
        if (leftHinge != null)
            BuildSide(ref _left, leftProximal, leftHinge, leftDistal, parent, isRight: false);
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
        SetSideVisible(ref _right, visible && _right.hinge != null);
        SetSideVisible(ref _left, visible && _left.hinge != null);
    }

    public void SetSideActive(bool rightActive, bool leftActive)
    {
        SetSideVisible(ref _right, rightActive && _right.hinge != null);
        SetSideVisible(ref _left, leftActive && _left.hinge != null);
    }

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
        if (_sharedMatTemplate.HasProperty("_Surface"))
            _sharedMatTemplate.SetFloat("_Surface", 1f);
        if (_sharedMatTemplate.HasProperty("_ZWrite"))
            _sharedMatTemplate.SetFloat("_ZWrite", 0f);
    }

    private void BuildSide(
        ref ArcSide side,
        Transform proximal, Transform hinge, Transform distal,
        Transform avatarRoot, bool isRight)
    {
        DestroySide(ref side);
        if (hinge == null) return;

        side.isRight = isRight;
        side.proximal = proximal;
        side.hinge = hinge;
        side.distal = distal != null ? distal : hinge;
        side.avatarRoot = avatarRoot;
        side.lastAngle = -1f;
        side.lastTarget = -1f;
        side.lastProgress = -1f;
        side.lastOuterRadius = -1f;
        side.lastRotation = Quaternion.identity;
        side.lastDrawnLabelInt = int.MinValue;
        side.lastDrawnTargetInt = int.MinValue;

        Transform parent = avatarRoot != null ? avatarRoot : hinge;
        string sideTag = isRight ? "_R" : "_L";
        side.root = new GameObject(_rootName + sideTag);
        side.root.transform.SetParent(parent, false);
        side.root.layer = hinge.gameObject.layer;

        CreateArcMesh(side.root.transform, "Fill" + sideTag,
            out side.fillFilter, out side.fillRenderer, out side.fillMesh,
            out side.fillVerts, out side.fillColors, out side.tris);
        CreateArcMesh(side.root.transform, "Track" + sideTag,
            out side.trackFilter, out side.trackRenderer, out side.trackMesh,
            out side.trackVerts, out side.trackColors, out _);
        side.trackRenderer.sortingOrder = 0;
        side.fillRenderer.sortingOrder = 1;
        CreateAngleLabel(ref side);
    }

    private void CreateAngleLabel(ref ArcSide side)
    {
        GameObject go = new GameObject(side.isRight ? "HingeLabel_R" : "HingeLabel_L");
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
        if (!ok || side.hinge == null)
        {
            SetSideVisible(ref side, false);
            return;
        }

        if (!AlignToHinge(ref side, out float outerR, out float currentDeg))
        {
            SetSideVisible(ref side, false);
            return;
        }

        float personalTarget = targetDeg > 1f ? targetDeg : maxVisualDegrees;
        personalTarget = Mathf.Clamp(personalTarget, MinPersonalTargetDegrees, maxVisualDegrees);
        float angle = Mathf.Clamp(currentDeg, 0f, MaxHingeDegrees);
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

    private bool AlignToHinge(ref ArcSide side, out float outerRadius, out float flexionDegrees)
    {
        outerRadius = 0f;
        flexionDegrees = 0f;
        if (side.root == null || side.hinge == null) return false;

        Transform root = side.avatarRoot != null ? side.avatarRoot : side.hinge.root;
        Vector3 hingePos = side.hinge.position;
        Vector3 proxPos = side.proximal != null ? side.proximal.position : hingePos + root.up;
        Vector3 distPos = side.distal != null ? side.distal.position : hingePos - root.up;

        Vector3 toProx = proxPos - hingePos;
        Vector3 toDist = distPos - hingePos;
        float distLen = toDist.magnitude;
        if (distLen < 1e-4f)
            distLen = 0.22f;
        outerRadius = distLen * Mathf.Clamp(distalReachRatio, 0.7f, 1.05f);

        Vector3 planeNormal = Vector3.Cross(toProx, toDist);
        if (planeNormal.sqrMagnitude < 1e-8f)
            planeNormal = root.forward;
        if (planeNormal.sqrMagnitude < 1e-8f) return false;
        planeNormal.Normalize();

        Vector3 proxP = Vector3.ProjectOnPlane(toProx, planeNormal);
        Vector3 distP = Vector3.ProjectOnPlane(toDist, planeNormal);
        if (proxP.sqrMagnitude < 1e-8f || distP.sqrMagnitude < 1e-8f) return false;
        proxP.Normalize();
        distP.Normalize();

        float included = Vector3.Angle(proxP, distP);
        flexionDegrees = Mathf.Abs(180f - included);

        Vector3 center = hingePos + planeNormal * planeOffset;
        Quaternion rot = Quaternion.LookRotation(planeNormal, -proxP);

        Vector3 localDist = Quaternion.Inverse(rot) * distP;
        if (localDist.x < 0f)
        {
            planeNormal = -planeNormal;
            center = hingePos + planeNormal * planeOffset;
            rot = Quaternion.LookRotation(planeNormal, -proxP);
        }

        side.root.transform.SetPositionAndRotation(center, rot);
        return true;
    }

    private void UpdateAngleLabel(ref ArcSide side, int shownDeg, float targetDeg, float outerR, Color fillColor)
    {
        if (side.angleLabel == null || side.root == null) return;

        float spanDeg = Mathf.Clamp(targetDeg > 1f ? targetDeg : maxVisualDegrees, 10f, maxVisualDegrees);
        Vector3 bestRimLocal = Vector3.zero;
        float bestWorldY = float.NegativeInfinity;
        const int samples = 20;
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
