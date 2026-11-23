using Godot;
using System;

/// Original GT-car body generated from profile curves: a lofted shell (superellipse sections
/// with fender bulges and wheel-arch openings), a fastback glasshouse, aero parts and lights.
/// Car frame: -Z forward, +X right, +Y up, origin at hub height (ground at y = -0.34).
public static class CarBody
{
    const int Ring = 48, Stations = 72;
    const float Nose = -2.38f, Tail = 2.28f;
    static readonly float[] ArchZ = { -1.35f, 1.35f };
    const float ArchR = 0.405f;

    static float S(float a, float b, float x) => Mathf.SmoothStep(a, b, x);
    static float Bump(float x, float c, float w) => Mathf.Exp(-(x - c) * (x - c) / (w * w));

    // ---- profile curves over t in [0,1] (nose -> tail) ----
    static float HalfWidth(float t) =>
        0.72f + 0.22f * S(0f, 0.16f, t) + 0.07f * Bump(t, 0.79f, 0.1f) - 0.04f * Bump(t, 0.5f, 0.12f) - 0.1f * S(0.93f, 1f, t);
    static float Hood(float t) =>
        -0.1f + 0.3f * S(0f, 0.3f, t) + 0.06f * S(0.62f, 0.85f, t) - 0.03f * S(0.94f, 1f, t);
    static float Fender(float t) =>
        0.17f * Bump(t, 0.21f, 0.085f) + 0.13f * Bump(t, 0.79f, 0.09f);
    static float Bottom(float t) => -0.25f + 0.07f * (1 - S(0f, 0.08f, t)) + 0.04f * S(0.9f, 1f, t);

    static Vector3 Point(float t, float theta)
    {
        float z = Mathf.Lerp(Nose, Tail, t);
        float hw = HalfWidth(t);
        float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
        const float n = 4.2f; // squareness of the section
        float x = hw * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / n);
        float bot = Bottom(t);
        float side = Mathf.Abs(x) / hw;
        float top = Hood(t) + Fender(t) * S(0.45f, 0.92f, side);
        float mid = bot + 0.42f * (top - bot);
        float y = s >= 0 ? mid + (top - mid) * Mathf.Pow(s, 2f / n) : mid - (mid - bot) * Mathf.Pow(-s, 2f / n);
        // wheel-arch openings: lift the lower flanks onto the arch circle
        foreach (float az in ArchZ)
        {
            float dz = z - az;
            if (Mathf.Abs(dz) < ArchR && side > 0.62f && s < 0.35f)
            {
                float arch = Mathf.Sqrt(ArchR * ArchR - dz * dz) - 0.02f;
                float k = S(0.62f, 0.78f, side);
                y = Mathf.Max(y, Mathf.Lerp(y, arch, k));
            }
        }
        return new Vector3(x, y, z);
    }

    static void Loft(SurfaceTool st, Func<float, float, Vector3> f, int rings, int stations, float u0, float u1, float t0, float t1, Func<float, bool> band = null)
    {
        for (int i = 0; i < stations; i++)
            for (int j = 0; j < rings; j++)
            {
                float ta = Mathf.Lerp(t0, t1, i / (float)stations), tb = Mathf.Lerp(t0, t1, (i + 1) / (float)stations);
                float ua = Mathf.Lerp(u0, u1, j / (float)rings), ub = Mathf.Lerp(u0, u1, (j + 1) / (float)rings);
                if (band != null && !band((ua + ub) / 2)) continue;
                Vector3 a = f(ta, ua), b = f(tb, ua), c = f(tb, ub), d = f(ta, ub);
                // outward winding: Godot front faces are clockwise
                foreach (var (p, uv) in new[] { (a, new Vector2(ua, ta)), (c, new Vector2(ub, tb)), (b, new Vector2(ua, tb)), (a, new Vector2(ua, ta)), (d, new Vector2(ub, ta)), (c, new Vector2(ub, tb)) })
                {
                    st.SetUV(new Vector2(uv.X / Mathf.Tau, uv.Y));
                    st.AddVertex(p);
                }
            }
    }

    static void Cap(SurfaceTool st, float t, bool front)
    {
        Vector3 centre = Vector3.Zero;
        for (int j = 0; j < Ring; j++) centre += Point(t, j * Mathf.Tau / Ring);
        centre /= Ring;
        for (int j = 0; j < Ring; j++)
        {
            Vector3 a = Point(t, j * Mathf.Tau / Ring), b = Point(t, (j + 1) * Mathf.Tau / Ring);
            foreach (var p in front ? new[] { centre, a, b } : new[] { centre, b, a })
            {
                st.SetUV(new Vector2(0.5f, t));
                st.AddVertex(p);
            }
        }
    }

    // ---- glasshouse ----
    static Vector3 Cabin(float t, float theta)
    {
        // t: 0 at the windscreen base, 1 at the tail of the fastback
        float z = Mathf.Lerp(-0.72f, 1.62f, t);
        float baseY = Hood(Mathf.InverseLerp(Nose, Tail, z)) - 0.02f;
        float roof = 0.74f;
        float h = t < 0.3f ? Mathf.Lerp(baseY, roof, S(0f, 0.3f, t)) : Mathf.Lerp(roof, baseY + 0.02f, S(0.45f, 1f, t));
        float hw = 0.7f - 0.08f * S(0.5f, 1f, t);
        float c = Mathf.Cos(theta), s = Mathf.Max(0, Mathf.Sin(theta));
        const float n = 3.2f;
        float y = baseY + (h - baseY) * Mathf.Pow(s, 2f / n);
        float k = (y - baseY) / Mathf.Max(0.01f, h - baseY);
        float x = hw * (1 - 0.22f * k) * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / n);
        return new Vector3(x, y, z);
    }

    static Material Livery(Color paint)
    {
        const int W = 256;
        var img = Image.CreateEmpty(W, 4, true, Image.Format.Rgb8);
        for (int x = 0; x < W; x++)
        {
            float u = x / (float)W; // 0.25 = top centre of the loft
            bool stripe = Mathf.Abs(u - 0.25f) is > 0.012f and < 0.03f;
            var c = stripe ? new Color(0.95f, 0.95f, 0.95f) : paint;
            for (int y = 0; y < 4; y++) img.SetPixel(x, y, c);
        }
        img.GenerateMipmaps();
        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(img),
            Metallic = 0.55f, Roughness = 0.28f, Clearcoat = 1f, ClearcoatRoughness = 0.04f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    }

    static MeshInstance3D Mesh(Action<SurfaceTool> fill, Material m)
    {
        var st = new SurfaceTool();
        st.Begin(Godot.Mesh.PrimitiveType.Triangles);
        fill(st);
        if (m is BaseMaterial3D bm) bm.CullMode = BaseMaterial3D.CullModeEnum.Disabled; // closed shells; avoids winding mistakes
        st.Index();
        st.GenerateNormals();
        st.GenerateTangents();
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = m };
    }

    static void Box(Node3D parent, Vector3 size, Vector3 pos, Material m, Vector3 rot = default) =>
        parent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, RotationDegrees = rot, MaterialOverride = m });

    public static Node3D Build(Color paint)
    {
        var root = new Node3D { Name = "Body" };
        var carbon = new StandardMaterial3D { AlbedoColor = new Color(0.03f, 0.03f, 0.035f), Roughness = 0.35f, Metallic = 0.3f };
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.025f, 0.03f), Roughness = 0.03f, Metallic = 0.4f, Clearcoat = 1f };

        root.AddChild(Mesh(st =>
        {
            Loft(st, Point, Ring, Stations, 0, Mathf.Tau, 0, 1);
            Cap(st, 0, true);
            Cap(st, 1, false);
        }, Livery(paint)));
        // glasshouse with a painted roof panel
        Func<float, bool> roofBand = u => Mathf.Sin(u) > 0.93f;
        root.AddChild(Mesh(st => Loft(st, Cabin, 36, 40, 0, Mathf.Pi, 0, 1, u => !roofBand(u)), glass));
        root.AddChild(Mesh(st => Loft(st, Cabin, 36, 40, 0, Mathf.Pi, 0.06f, 0.92f, roofBand), Livery(paint)));

        // aero
        Box(root, new Vector3(1.9f, 0.025f, 0.32f), new Vector3(0, -0.245f, -2.3f), carbon);                 // splitter
        Box(root, new Vector3(1.5f, 0.12f, 0.1f), new Vector3(0, -0.14f, -2.36f), carbon);                    // intake
        Box(root, new Vector3(1.6f, 0.05f, 0.55f), new Vector3(0, -0.22f, 2.05f), carbon, new Vector3(-9, 0, 0)); // diffuser
        for (int f = -2; f <= 2; f++) Box(root, new Vector3(0.02f, 0.14f, 0.5f), new Vector3(f * 0.3f, -0.19f, 2.05f), carbon);
        Box(root, new Vector3(1.86f, 0.035f, 0.34f), new Vector3(0, 0.74f, 2.08f), carbon, new Vector3(-8, 0, 0)); // wing
        foreach (int s in new[] { -1, 1 })
        {
            Box(root, new Vector3(0.025f, 0.3f, 0.42f), new Vector3(s * 0.94f, 0.68f, 2.08f), carbon);         // endplates
            Box(root, new Vector3(0.04f, 0.34f, 0.12f), new Vector3(s * 0.42f, 0.52f, 2.02f), carbon, new Vector3(12, 0, 0)); // pylons
            Box(root, new Vector3(0.14f, 0.08f, 0.1f), new Vector3(s * 0.83f, 0.44f, -0.62f), carbon);         // mirror
            Box(root, new Vector3(0.12f, 0.02f, 0.02f), new Vector3(s * 0.75f, 0.42f, -0.6f), carbon);
            Box(root, new Vector3(0.36f, 0.06f, 0.06f), new Vector3(s * 0.55f, 0.02f, -2.33f), Materials.Glow(new Color(0.95f, 0.97f, 1f), 2f), new Vector3(0, s * 12, 0)); // headlights
            Box(root, new Vector3(0.42f, 0.05f, 0.04f), new Vector3(s * 0.55f, 0.2f, 2.26f), Materials.Glow(new Color(0.9f, 0.05f, 0.02f), 1.2f)); // tail lights
        }
        return root;
    }

    /// Tyre (rounded sidewalls) + rim (dish and five spokes) + brake disc, axis along X.
    public static Node3D Wheel(float radius, float width, out Node3D spinPart, Node3D steerPart)
    {
        var tyreMat = new StandardMaterial3D { AlbedoColor = new Color(0.035f, 0.035f, 0.035f), Roughness = 0.85f };
        var rimMat = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.13f), Metallic = 0.9f, Roughness = 0.25f };
        var discMat = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.33f, 0.32f), Metallic = 0.8f, Roughness = 0.5f };
        var spin = new Node3D { Name = "Spin" };
        // lathe the tyre section around the X axis
        var profile = new System.Collections.Generic.List<Vector2>();
        float hw = width / 2, rIn = radius * 0.72f;
        for (int k = 0; k <= 12; k++)
        {
            float a = -Mathf.Pi / 2 + k * Mathf.Pi / 12;
            profile.Add(new Vector2(Mathf.Lerp(rIn, radius, 0.5f) + (radius - rIn) * 0.5f * Mathf.Cos(a) * 1.0f, hw * Mathf.Sin(a) * 0.98f));
        }
        spin.AddChild(Mesh(st => Lathe(st, profile, 40), tyreMat));
        var dish = new System.Collections.Generic.List<Vector2> { new(rIn * 1.01f, hw * 0.8f), new(rIn * 0.9f, hw * 0.55f), new(0.07f, hw * 0.35f), new(0.0f, hw * 0.35f) };
        spin.AddChild(Mesh(st => Lathe(st, dish, 32), rimMat));
        for (int k = 0; k < 5; k++)
        {
            var spoke = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, rIn * 0.95f, 0.07f) }, MaterialOverride = rimMat };
            spoke.Position = new Vector3(hw * 0.4f, 0, 0) + new Vector3(0, Mathf.Cos(k * Mathf.Tau / 5), Mathf.Sin(k * Mathf.Tau / 5)) * rIn * 0.48f;
            spoke.Rotation = new Vector3(k * Mathf.Tau / 5, 0, 0);
            spin.AddChild(spoke);
        }
        spin.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = rIn * 0.88f, BottomRadius = rIn * 0.88f, Height = 0.03f, RadialSegments = 28 }, MaterialOverride = discMat, RotationDegrees = new Vector3(0, 0, 90), Position = new Vector3(-hw * 0.2f, 0, 0) });
        steerPart.AddChild(spin);
        // caliper does not spin
        var caliper = Materials.Flat(new Color(0.85f, 0.1f, 0.05f), 0.4f);
        caliper.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        Box(steerPart, new Vector3(0.07f, 0.16f, 0.1f), new Vector3(-hw * 0.2f, rIn * 0.65f, -0.05f), caliper);
        spinPart = spin;
        return steerPart;
    }

    static void Lathe(SurfaceTool st, System.Collections.Generic.List<Vector2> profile, int seg)
    {
        for (int i = 0; i < seg; i++)
            for (int k = 0; k + 1 < profile.Count; k++)
            {
                float a0 = i * Mathf.Tau / seg, a1 = (i + 1) * Mathf.Tau / seg;
                Vector3 P(Vector2 p, float a) => new(p.Y, p.X * Mathf.Cos(a), p.X * Mathf.Sin(a));
                Vector3 p00 = P(profile[k], a0), p01 = P(profile[k], a1), p10 = P(profile[k + 1], a0), p11 = P(profile[k + 1], a1);
                foreach (var p in new[] { p00, p11, p10, p00, p01, p11 }) { st.SetUV(new Vector2(i / (float)seg, k / (float)profile.Count)); st.AddVertex(p); }
            }
    }
}
