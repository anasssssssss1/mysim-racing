using Godot;
using System;

/// Builds a drivable circuit from a centreline JSON (tools/osm_track.py output):
/// road, white edge lines, kerbs on corners, asphalt run-off, and a trimesh collider.
public partial class TrackBuilder : Node3D
{
    [Export] public string TrackFile = "res://data/yas_marina.json";
    [Export] public float HalfWidth = 7f;      // ponytail: one width for the whole lap; per-section widths when we survey them
    [Export] public float KerbWidth = 1.2f;
    [Export] public float RunoffWidth = 12f;
    [Export] public float KerbRadius = 220f;   // corners tighter than this get kerbs

    public Vector3[] Centre;
    public Vector3[] Left;   // unit vector to the driver's left
    public float[] Curv;     // signed 1/m, + = turning left
    public float Length;

    public override void _Ready() => Build();

    void Load()
    {
        var d = Json.ParseString(FileAccess.GetFileAsString(TrackFile)).AsGodotDictionary();
        var pts = d["points"].AsGodotArray();
        int n = pts.Count;
        Centre = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var p = pts[i].AsGodotArray();
            Centre[i] = new Vector3((float)p[0], 0, -(float)p[1]); // x east, -z north
        }
        Left = new Vector3[n];
        var raw = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 a = Centre[(i - 1 + n) % n], b = Centre[i], c = Centre[(i + 1) % n];
            Left[i] = Vector3.Up.Cross((c - a).Normalized());
            Vector3 t0 = (b - a).Normalized(), t1 = (c - b).Normalized();
            raw[i] = Mathf.Atan2(t0.Cross(t1).Y, t0.Dot(t1)) / (0.5f * (a.DistanceTo(b) + b.DistanceTo(c)));
            Length += b.DistanceTo(c);
        }
        Curv = new float[n];
        const int W = 6; // smooth over ±12 m
        for (int i = 0; i < n; i++)
        {
            float s = 0;
            for (int k = -W; k <= W; k++) s += raw[(i + k + n) % n];
            Curv[i] = s / (2 * W + 1);
        }
    }

    /// Offset from the centre, pulled in on the inside of tight turns so strips never fold over.
    Vector3 At(int i, float off)
    {
        float k = Curv[i];
        if (Mathf.Sign(off) == Mathf.Sign(k) && Mathf.Abs(k) > 1e-5f)
            off = Mathf.Sign(off) * Mathf.Min(Mathf.Abs(off), 0.85f / Mathf.Abs(k));
        return Centre[i] + Left[i] * off;
    }

    void Strip(SurfaceTool st, float offA, float offB, float y, bool[] mask)
    {
        int n = Centre.Length;
        float s = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float ds = Centre[i].DistanceTo(Centre[j]);
            if (mask == null || (mask[i] && mask[j]))
            {
                Vector3 up = Vector3.Up * y;
                Vector3 a0 = At(i, offA) + up, b0 = At(i, offB) + up, a1 = At(j, offA) + up, b1 = At(j, offB) + up;
                Vector2 ua0 = new(s, 0), ub0 = new(s, 1), ua1 = new(s + ds, 0), ub1 = new(s + ds, 1);
                Tri(st, a0, ua0, a1, ua1, b0, ub0);
                Tri(st, a1, ua1, b1, ub1, b0, ub0);
            }
            s += ds;
        }
    }

    /// Concrete wall along an offset; skipped wherever it would cut across another part of the circuit.
    void Wall(SurfaceTool st, float off)
    {
        const float H = 1.1f, T = 0.35f;
        int n = Centre.Length;
        float clear = Mathf.Abs(off) - 0.5f;
        var keep = new bool[n];
        // ponytail: O(n^2) scan at load (~7M checks); use a grid if tracks get much longer
        for (int i = 0; i < n; i++)
        {
            Vector3 p = At(i, off);
            keep[i] = true;
            for (int j = 0; j < n && keep[i]; j++)
            {
                int gap = Mathf.Abs(i - j);
                if (Mathf.Min(gap, n - gap) > 30 && p.DistanceSquaredTo(Centre[j]) < clear * clear) keep[i] = false;
            }
        }
        float side = Mathf.Sign(off);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            if (!keep[i] || !keep[j]) continue;
            Vector3 a = At(i, off), b = At(j, off);
            Vector3 ao = a + Left[i] * side * T, bo = b + Left[j] * side * T;
            Vector3 h = Vector3.Up * H;
            Vector3 inward = -Left[i] * side;
            Quad(st, a, b, b + h, a + h, inward);           // face toward the track
            Quad(st, ao, bo, bo + h, ao + h, -inward);      // back face
            Quad(st, a + h, b + h, bo + h, ao + h, Vector3.Up);
        }
    }

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        foreach (var (p, q, r) in new[] { (a, b, c), (a, c, d) })
        {
            // clockwise when seen from the side the normal points to
            var (x, y, z) = (q - p).Cross(r - p).Dot(normal) > 0 ? (p, r, q) : (p, q, r);
            foreach (var v in new[] { x, y, z })
            {
                st.SetNormal(normal);
                st.SetUV(new Vector2(v.X + v.Z, v.Y));
                st.AddVertex(v);
            }
        }
    }

    static void Tri(SurfaceTool st, Vector3 a, Vector2 ua, Vector3 b, Vector2 ub, Vector3 c, Vector2 uc)
    {
        // Godot front faces are clockwise seen from the front; order so the face points +Y
        if ((b - a).Cross(c - a).Y > 0) { (b, c) = (c, b); (ub, uc) = (uc, ub); }
        foreach (var (p, u) in new[] { (a, ua), (b, ub), (c, uc) })
        {
            st.SetNormal(Vector3.Up);
            st.SetUV(u);
            st.AddVertex(p);
        }
    }

    MeshInstance3D Add(string name, Action<SurfaceTool> fill, Material mat, bool collide)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        fill(st);
        st.GenerateTangents();
        var mesh = st.Commit();
        var mi = new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = mat };
        AddChild(mi);
        if (collide) mi.CreateTrimeshCollision();
        return mi;
    }

    public void Build()
    {
        Load();
        int n = Centre.Length;
        var kerb = new bool[n];
        for (int i = 0; i < n; i++)
            if (Mathf.Abs(Curv[i]) > 1f / KerbRadius)
                for (int k = -8; k <= 8; k++) kerb[(i + k + n) % n] = true; // run 16 m past the turn

        float h = HalfWidth, r = h + RunoffWidth;
        Add("Runoff", st => { Strip(st, h, r, 0f, null); Strip(st, -h, -r, 0f, null); }, Materials.Runoff(), true);
        Add("Road", st => Strip(st, -h, h, 0.03f, null), Materials.Asphalt(), true);
        Add("Lines", st => { Strip(st, h - 0.35f, h - 0.1f, 0.05f, null); Strip(st, -h + 0.1f, -h + 0.35f, 0.05f, null); }, Materials.Paint(), false);
        Add("Kerbs", st => { Strip(st, h, h + KerbWidth, 0.06f, kerb); Strip(st, -h, -h - KerbWidth, 0.06f, kerb); }, Materials.Kerb(), true);

        Add("Walls", st => { Wall(st, r + 1f); Wall(st, -(r + 1f)); }, Materials.Concrete(), true);

        var ground = new MeshInstance3D { Name = "Ground", Mesh = new PlaneMesh { Size = new Vector2(8000, 8000) }, MaterialOverride = Materials.Grass() };
        ground.Position = new Vector3(Bounds().GetCenter().X, -0.1f, Bounds().GetCenter().Z);
        AddChild(ground);
        var floor = new StaticBody3D { Name = "GroundCollider", Position = new Vector3(0, -0.1f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        AddChild(floor);
    }

    public Aabb Bounds()
    {
        var b = new Aabb(Centre[0], Vector3.Zero);
        foreach (var p in Centre) b = b.Expand(p);
        return b;
    }

    /// Nearest centreline index, searching ±window around a hint (full search when window <= 0 or far off).
    public int Nearest(Vector3 p, int hint, int window)
    {
        int n = Centre.Length, best = hint;
        float bd = float.MaxValue;
        int from = window > 0 ? hint - window : 0, to = window > 0 ? hint + window : n - 1;
        for (int k = from; k <= to; k++)
        {
            int i = ((k % n) + n) % n;
            float d = Centre[i].DistanceSquaredTo(p);
            if (d < bd) { bd = d; best = i; }
        }
        return window > 0 && bd > 900 ? Nearest(p, 0, 0) : best;
    }

    /// Pose on the centreline at index i, facing the race direction, lifted by `up`.
    public Transform3D PoseAt(int i, float up = 0.4f, float left = 0)
    {
        int n = Centre.Length;
        i = ((i % n) + n) % n;
        Vector3 fwd = (Centre[(i + 1) % n] - Centre[(i - 1 + n) % n]).Normalized();
        var basis = Basis.LookingAt(fwd, Vector3.Up);
        return new Transform3D(basis, Centre[i] + Left[i] * left + Vector3.Up * up);
    }

    public int TightestCorner()
    {
        int best = 0;
        for (int i = 0; i < Curv.Length; i++) if (Mathf.Abs(Curv[i]) > Mathf.Abs(Curv[best])) best = i;
        return best;
    }
}
