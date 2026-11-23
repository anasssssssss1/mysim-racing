using Godot;
using System;
using System.Collections.Generic;

/// Builds a drivable circuit from a centreline JSON (tools/osm_track.py output):
/// road, edge lines, kerbs, run-off, gravel traps, walls, ground, and colliders that carry a
/// surface grip value the car reads. Dressing (stands, buildings, signs...) is in TrackDressing.
public partial class TrackBuilder : Node3D
{
    [Export] public string TrackFile = "res://data/yas_marina.json";
    [Export] public float HalfWidth = 7f;      // ponytail: one width for the whole lap; per-section widths when we survey them
    [Export] public float KerbWidth = 1.2f;
    [Export] public float RunoffWidth = 12f;
    [Export] public float KerbRadius = 220f;   // corners tighter than this get kerbs
    [Export] public bool Dress = true;

    public Vector3[] Centre;
    public Vector3[] Left;   // unit vector to the driver's left
    public float[] Curv;     // signed 1/m, + = turning left
    public float Length;
    public float[] Profile;  // planned AI speed, m/s
    public List<Corner> Corners = new();
    public float WallOffset => HalfWidth + RunoffWidth + 1f;
    public bool[] WallLeft, WallRight;
    public bool[] GravelLeft, GravelRight;

    public struct Corner { public int Start, Apex, End, Dir, Number; public float EntrySpeed, ApexSpeed; }

    Dictionary<Vector2I, List<int>> _grid;
    const float Cell = 25f;

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
        _grid = new();
        for (int i = 0; i < n; i++)
        {
            var key = new Vector2I(Mathf.FloorToInt(Centre[i].X / Cell), Mathf.FloorToInt(Centre[i].Z / Cell));
            if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<int>();
            list.Add(i);
        }
        Profile = AiDriver.SpeedProfile(this, 1.3f, 11f);
        FindCorners();
    }

    void FindCorners()
    {
        int n = Centre.Length;
        float k0 = 1f / 260f;
        // start scanning on a straight so no corner wraps the index origin
        int origin = 0;
        for (int i = 0; i < n; i++) if (Mathf.Abs(Curv[i]) < k0 * 0.5f) { origin = i; break; }
        Corner? cur = null;
        int gap = 0;
        for (int s = 0; s <= n; s++)
        {
            int i = (origin + s) % n;
            bool on = Mathf.Abs(Curv[i]) > k0;
            int dir = Math.Sign(Curv[i]);
            if (on && cur is { } c && (dir == c.Dir || gap < 4))
            {
                if (Mathf.Abs(Curv[i]) > Mathf.Abs(Curv[c.Apex])) c.Apex = i;
                c.End = i; gap = 0; cur = c;
            }
            else if (on)
            {
                if (cur is { } done) Corners.Add(done);
                cur = new Corner { Start = i, Apex = i, End = i, Dir = dir }; gap = 0;
            }
            else if (cur != null && ++gap > 12) { Corners.Add(cur.Value); cur = null; }
        }
        if (cur != null) Corners.Add(cur.Value);
        Corners.RemoveAll(c => Span(c.Start, c.End) < 6);
        // number from the start line, record speeds
        Corners.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int k = 0; k < Corners.Count; k++)
        {
            var c = Corners[k];
            c.Number = k + 1;
            c.ApexSpeed = Profile[c.Apex];
            c.EntrySpeed = 0;
            for (int b = 0; b < 150; b++) c.EntrySpeed = Mathf.Max(c.EntrySpeed, Profile[(c.Start - b + n) % n]);
            Corners[k] = c;
        }
    }

    public int Span(int a, int b) => ((b - a) % Centre.Length + Centre.Length) % Centre.Length;
    public int Wrap(int i) => ((i % Centre.Length) + Centre.Length) % Centre.Length;

    /// Distance from p to the nearest centreline point (ignoring indices within `skip` of `near`).
    public float DistToTrack(Vector3 p, int near = -1, int skip = 0)
    {
        float best = float.MaxValue;
        int cx = Mathf.FloorToInt(p.X / Cell), cz = Mathf.FloorToInt(p.Z / Cell);
        for (int r = 0; r < 12; r++)
        {
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    if (!_grid.TryGetValue(new Vector2I(cx + dx, cz + dz), out var list)) continue;
                    foreach (int i in list)
                    {
                        if (near >= 0 && Math.Min(Span(near, i), Span(i, near)) <= skip) continue;
                        float d = new Vector2(Centre[i].X - p.X, Centre[i].Z - p.Z).Length();
                        if (d < best) best = d;
                    }
                }
            if (best <= r * Cell) return best; // nothing closer can be in a farther ring
        }
        return best;
    }

    /// Offset from the centre, pulled in on the inside of tight turns so strips never fold over.
    public Vector3 At(int i, float off)
    {
        i = Wrap(i);
        float k = Curv[i];
        if (Mathf.Sign(off) == Mathf.Sign(k) && Mathf.Abs(k) > 1e-5f)
            off = Mathf.Sign(off) * Mathf.Min(Mathf.Abs(off), 0.85f / Mathf.Abs(k));
        return Centre[i] + Left[i] * off;
    }

    public void Strip(SurfaceTool st, float offA, float offB, float y, bool[] mask)
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

    /// Kerb with a real profile: flush at the road edge, rising to 5 cm, then dropping at the outer edge.
    void KerbStrip(SurfaceTool st, float side, bool[] mask)
    {
        float h = HalfWidth, w = KerbWidth;
        Strip(st, side * h, side * (h + w * 0.35f), 0.035f, mask);
        Strip(st, side * (h + w * 0.35f), side * (h + w), 0.075f, mask);
    }

    /// Concrete wall along an offset; skipped wherever it would cut across another part of the circuit.
    bool[] Wall(SurfaceTool st, float off)
    {
        const float H = 1.1f, T = 0.35f;
        int n = Centre.Length;
        float clear = Mathf.Abs(off) - 0.5f;
        var keep = new bool[n];
        for (int i = 0; i < n; i++) keep[i] = DistToTrack(At(i, off), i, 30) >= clear;
        float side = Mathf.Sign(off);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            if (!keep[i] || !keep[j]) continue;
            Vector3 a = At(i, off), b = At(j, off);
            Vector3 ao = a + Left[i] * side * T, bo = b + Left[j] * side * T;
            Vector3 hh = Vector3.Up * H;
            Vector3 inward = -Left[i] * side;
            Quad(st, a, b, b + hh, a + hh, inward);
            Quad(st, ao, bo, bo + hh, ao + hh, -inward);
            Quad(st, a + hh, b + hh, bo + hh, ao + hh, Vector3.Up);
        }
        return keep;
    }

    public static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
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

    /// grip: friction multiplier the car's tyres see; drag: extra rolling drag (N per N of load).
    public MeshInstance3D Add(string name, Action<SurfaceTool> fill, Material mat, bool collide, float grip = 1f, float drag = 0f)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        fill(st);
        st.GenerateTangents();
        var mesh = st.Commit();
        var mi = new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = mat };
        AddChild(mi);
        if (collide)
        {
            mi.CreateTrimeshCollision();
            var body = mi.GetChild<StaticBody3D>(0);
            body.SetMeta("grip", grip);
            body.SetMeta("drag", drag);
        }
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

        // gravel on the outside of corners that need heavy braking
        GravelLeft = new bool[n]; GravelRight = new bool[n];
        foreach (var c in Corners)
            if (c.EntrySpeed - c.ApexSpeed > 15f)
                for (int k = c.Start - 10; k <= c.End + 25; k++)
                    (c.Dir > 0 ? GravelRight : GravelLeft)[Wrap(k)] = true;

        float h = HalfWidth, r = h + RunoffWidth, g0 = h + KerbWidth + 2.5f;
        Add("Runoff", st => { Strip(st, h, r, 0f, null); Strip(st, -h, -r, 0f, null); }, Materials.Runoff(), true, 0.95f);
        Add("Gravel", st => { Strip(st, g0, r, 0.015f, GravelLeft); Strip(st, -g0, -r, 0.015f, GravelRight); }, Materials.Gravel(), true, 0.45f, 0.25f);
        Add("Road", st => Strip(st, -h, h, 0.03f, null), Materials.Asphalt(), true);
        Add("Lines", st => { Strip(st, h - 0.35f, h - 0.1f, 0.045f, null); Strip(st, -h + 0.1f, -h + 0.35f, 0.045f, null); }, Materials.Paint(), false);
        Add("Kerbs", st => { KerbStrip(st, 1, kerb); KerbStrip(st, -1, kerb); }, Materials.Kerb(), true, 0.9f);
        Add("Walls", st => { WallLeft = Wall(st, WallOffset); WallRight = Wall(st, -WallOffset); }, Materials.Concrete(), true);

        BuildGround();
        if (Dress) AddChild(new TrackDressing { Track = this, Name = "Dressing" });
    }

    void BuildGround()
    {
        var b = Bounds().Grow(900);
        float size = Mathf.Max(b.Size.X, b.Size.Z);
        var origin = new Vector2(b.GetCenter().X - size / 2, b.GetCenter().Z - size / 2);
        const int R = 256;
        var img = Image.CreateEmpty(R, R, false, Image.Format.Rf);
        for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                var p = new Vector3(origin.X + (x + 0.5f) / R * size, 0, origin.Y + (y + 0.5f) / R * size);
                img.SetPixel(x, y, new Color(Mathf.Min(DistToTrack(p), 300f) / 300f, 0, 0));
            }
        var mat = Materials.Ground(new Rect2(origin, new Vector2(size, size)), ImageTexture.CreateFromImage(img));
        var ground = new MeshInstance3D { Name = "Ground", Mesh = new PlaneMesh { Size = new Vector2(size, size), SubdivideWidth = 0, SubdivideDepth = 0 }, MaterialOverride = mat };
        ground.Position = new Vector3(origin.X + size / 2, -0.1f, origin.Y + size / 2);
        AddChild(ground);
        var floor = new StaticBody3D { Name = "GroundCollider", Position = new Vector3(0, -0.1f, 0) };
        floor.SetMeta("grip", 0.55f);
        floor.SetMeta("drag", 0.12f);
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
        i = Wrap(i);
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
