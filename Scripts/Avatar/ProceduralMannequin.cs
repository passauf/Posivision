using UnityEngine;

/// <summary>
/// Humanoid manken: boş Transform kemikleri + ayrı görsel çocuklar.
/// (Joint üzerinde localScale kullanılmaz — iç içe scale çarpanı tek küpe çökertiyordu.)
/// </summary>
public sealed class ProceduralMannequin
{
    public Transform Root { get; private set; }
    public Transform Hips { get; private set; }
    public Transform Spine { get; private set; }
    public Transform Chest { get; private set; }
    public Transform Neck { get; private set; }
    public Transform Head { get; private set; }
    public Transform LeftShoulder { get; private set; }
    public Transform LeftElbow { get; private set; }
    public Transform LeftWrist { get; private set; }
    public Transform RightShoulder { get; private set; }
    public Transform RightElbow { get; private set; }
    public Transform RightWrist { get; private set; }
    public Transform LeftHip { get; private set; }
    public Transform LeftKnee { get; private set; }
    public Transform LeftAnkle { get; private set; }
    public Transform RightHip { get; private set; }
    public Transform RightKnee { get; private set; }
    public Transform RightAnkle { get; private set; }
    public Renderer HeadRenderer { get; private set; }

    private static readonly Color BodyColor = new Color(0.35f, 0.72f, 0.78f, 1f);
    private static readonly Color HeadColor = new Color(0.92f, 0.78f, 0.68f, 1f);

    public static ProceduralMannequin Build(Transform parent)
    {
        var m = new ProceduralMannequin();
        GameObject rootGo = new GameObject("ProceduralMannequin");
        rootGo.transform.SetParent(parent, false);
        m.Root = rootGo.transform;

        m.Hips = CreateJoint(m.Root, "Hips", new Vector3(0.28f, 0.16f, 0.18f), BodyColor);
        m.Spine = CreateJoint(m.Hips, "Spine", new Vector3(0.2f, 0.18f, 0.14f), BodyColor);
        m.Chest = CreateJoint(m.Spine, "Chest", new Vector3(0.32f, 0.22f, 0.16f), BodyColor);
        m.Neck = CreateJoint(m.Chest, "Neck", new Vector3(0.1f, 0.1f, 0.1f), BodyColor);
        m.Head = CreateJoint(m.Neck, "Head", new Vector3(0.2f, 0.22f, 0.2f), HeadColor);
        m.HeadRenderer = FindVisualRenderer(m.Head);

        m.LeftShoulder = CreateJoint(m.Chest, "LeftShoulder", new Vector3(0.12f, 0.12f, 0.12f), BodyColor);
        m.LeftElbow = CreateJoint(m.LeftShoulder, "LeftElbow", new Vector3(0.1f, 0.22f, 0.1f), BodyColor);
        m.LeftWrist = CreateJoint(m.LeftElbow, "LeftWrist", new Vector3(0.08f, 0.12f, 0.08f), BodyColor);

        m.RightShoulder = CreateJoint(m.Chest, "RightShoulder", new Vector3(0.12f, 0.12f, 0.12f), BodyColor);
        m.RightElbow = CreateJoint(m.RightShoulder, "RightElbow", new Vector3(0.1f, 0.22f, 0.1f), BodyColor);
        m.RightWrist = CreateJoint(m.RightElbow, "RightWrist", new Vector3(0.08f, 0.12f, 0.08f), BodyColor);

        m.LeftHip = CreateJoint(m.Hips, "LeftHip", new Vector3(0.12f, 0.12f, 0.12f), BodyColor);
        m.LeftKnee = CreateJoint(m.LeftHip, "LeftKnee", new Vector3(0.11f, 0.28f, 0.11f), BodyColor);
        m.LeftAnkle = CreateJoint(m.LeftKnee, "LeftAnkle", new Vector3(0.1f, 0.1f, 0.16f), BodyColor);

        m.RightHip = CreateJoint(m.Hips, "RightHip", new Vector3(0.12f, 0.12f, 0.12f), BodyColor);
        m.RightKnee = CreateJoint(m.RightHip, "RightKnee", new Vector3(0.11f, 0.28f, 0.11f), BodyColor);
        m.RightAnkle = CreateJoint(m.RightKnee, "RightAnkle", new Vector3(0.1f, 0.1f, 0.16f), BodyColor);

        // T-pose — joint Transform scale her zaman 1
        m.Hips.localPosition = new Vector3(0f, 0.95f, 0f);
        m.Spine.localPosition = new Vector3(0f, 0.2f, 0f);
        m.Chest.localPosition = new Vector3(0f, 0.22f, 0f);
        m.Neck.localPosition = new Vector3(0f, 0.18f, 0f);
        m.Head.localPosition = new Vector3(0f, 0.16f, 0f);

        m.LeftShoulder.localPosition = new Vector3(-0.26f, 0.14f, 0f);
        m.LeftElbow.localPosition = new Vector3(-0.32f, 0f, 0f);
        m.LeftWrist.localPosition = new Vector3(-0.3f, 0f, 0f);

        m.RightShoulder.localPosition = new Vector3(0.26f, 0.14f, 0f);
        m.RightElbow.localPosition = new Vector3(0.32f, 0f, 0f);
        m.RightWrist.localPosition = new Vector3(0.3f, 0f, 0f);

        m.LeftHip.localPosition = new Vector3(-0.12f, -0.06f, 0f);
        m.LeftKnee.localPosition = new Vector3(0f, -0.45f, 0f);
        m.LeftAnkle.localPosition = new Vector3(0f, -0.45f, 0f);

        m.RightHip.localPosition = new Vector3(0.12f, -0.06f, 0f);
        m.RightKnee.localPosition = new Vector3(0f, -0.45f, 0f);
        m.RightAnkle.localPosition = new Vector3(0f, -0.45f, 0f);

        return m;
    }

    private static Transform CreateJoint(Transform parent, string name, Vector3 visualScale, Color color)
    {
        GameObject joint = new GameObject(name);
        joint.transform.SetParent(parent, false);
        joint.transform.localScale = Vector3.one;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        visual.transform.SetParent(joint.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = visualScale;

        var col = visual.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        var renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = color;

        return joint.transform;
    }

    private static Renderer FindVisualRenderer(Transform joint)
    {
        Transform visual = joint.Find("Visual");
        return visual != null ? visual.GetComponent<Renderer>() : joint.GetComponentInChildren<Renderer>();
    }
}
