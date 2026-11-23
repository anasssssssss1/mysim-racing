using Godot;
using System;
using System.Collections.Generic;

/// Everything around the circuit, placed from the track data: start gantry and grid, catch fences,
/// tyre walls, advertising, braking and corner boards, grandstands with crowds, pit lane and
/// building, floodlight poles, city blocks and palms. Static geometry is merged per material;
/// repeated props are MultiMeshes.
public partial class TrackDressing : Node3D
{
    public TrackBuilder Track;
    readonly RandomNumberGenerator _rng = new() { Seed = 7 };
    readonly Dictionary<Material, SurfaceTool> _batches = new();
    readonly List<(Vector2 c, float r)> _taken = new();
    readonly List<Transform3D> _posts = new(), _poles = new(), _heads = new(), _trunks = new();
    readonly List<(Transform3D, Color)> _tyres = new(), _crowd = new(), _city = new();
    int _side; // side of the main straight the stands are on (+1 left / -1 right)

    static readonly (string name, Color bg, Color fg)[] Brands =
    {
        ("VELTRA", new(0.85f, 0.1f, 0.12f), Colors.White), ("NORDAX OIL", new(0.05f, 0.2f, 0.5f), new(1f, 0.85f, 0.1f)),
        ("KAIRO TYRES", new(0.08f, 0.08f, 0.08f), new(1f, 0.85f, 0.1f)), ("SOLENNE", Colors.White, new(0.1f, 0.3f, 0.7f)),
        ("ARCLINE", new(0.1f, 0.55f, 0.35f), Colors.White), ("HELIOS FUEL", new(1f, 0.55f, 0.05f), new(0.1f, 0.1f, 0.1f)),
        ("MERIDIAN", new(0.35f, 0.1f, 0.5f), Colors.White), ("QUARZ", new(0.9f, 0.9f, 0.9f), new(0.8f, 0.1f, 0.1f)),
    };

    public override void _Ready()
    {
        _side = ClearSide(Track.Wrap(-60), 45f);
        StartLine();
        Pits();
        Grandstands();
        Fences();
        TyreWalls();
        Hoardings();
        CornerBoards();
        Lights();
        City();
        Palms();
        Commit();
    }

    // ---------- geometry helpers ----------

    SurfaceTool Batch(Material m)
    {
        if (!_batches.TryGetValue(m, out var st))
        {
            st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            _batches[m] = st;
        }
        return st;
    }

    void Box(Material m, Transform3D t, Vector3 size)
    {
        var st = Batch(m);
        Vector3 h = size / 2;
        Vector3 C(float x, float y, float z) => t * new Vector3(x * h.X, y * h.Y, z * h.Z);
        var b = t.Basis.Orthonormalized();
        TrackBuilder.Quad(st, C(-1, -1, 1), C(1, -1, 1), C(1, 1, 1), C(-1, 1, 1), b.Z);
        TrackBuilder.Quad(st, C(-1, -1, -1), C(1, -1, -1), C(1, 1, -1), C(-1, 1, -1), -b.Z);
        TrackBuilder.Quad(st, C(1, -1, -1), C(1, -1, 1), C(1, 1, 1), C(1, 1, -1), b.X);
        TrackBuilder.Quad(st, C(-1, -1, -1), C(-1, -1, 1), C(-1, 1, 1), C(-1, 1, -1), -b.X);
        TrackBuilder.Quad(st, C(-1, 1, -1), C(1, 1, -1), C(1, 1, 1), C(-1, 1, 1), b.Y);
    }

    void Commit()
    {
        foreach (var (m, st) in _batches)
        {
            st.GenerateTangents();
            AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = m });
        }
        Instances(new BoxMesh { Size = new Vector3(0.08f, 3f, 0.08f) }, Materials.Flat(new Color(0.5f, 0.52f, 0.55f), 0.4f, 0.8f), _posts, null, 500);
        Instances(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.32f, Height = 22f, RadialSegments = 8 }, Materials.Flat(new Color(0.75f, 0.76f, 0.78f), 0.35f, 0.8f), _poles, null, 2500);
        Instances(new BoxMesh { Size = new Vector3(3.2f, 1.4f, 0.5f) }, Materials.Glow(new Color(1f, 0.97f, 0.9f), 0.6f), _heads, null, 2500);
        Instances(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.75f, RadialSegments = 14 }, VertexColored(0.9f), null, _tyres, 400);
        Instances(new BoxMesh { Size = new Vector3(0.42f, 0.72f, 0.32f) }, VertexColored(0.8f), null, _crowd, 700);
        var city = new ShaderMaterial { Shader = GD.Load<Shader>("res://src/Track/building.gdshader") };
        Instances(new BoxMesh { Size = Vector3.One }, city, null, _city, 0);
        Instances(new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.26f, Height = 9f, RadialSegments = 8 }, Materials.Flat(new Color(0.45f, 0.36f, 0.26f), 0.9f), _trunks, null, 1500);
        Instances(PalmCrown(), new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.42f, 0.14f), Roughness = 0.8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled }, _trunks, null, 1500);
    }

    static StandardMaterial3D VertexColored(float rough) => new() { VertexColorUseAsAlbedo = true, Roughness = rough };

    void Instances(Mesh mesh, Material mat, List<Transform3D> xs, List<(Transform3D, Color)> coloured, float range)
    {
        int count = xs?.Count ?? coloured.Count;
        if (count == 0) return;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = coloured != null, Mesh = mesh, InstanceCount = count };
        for (int i = 0; i < count; i++)
        {
            if (xs != null) mm.SetInstanceTransform(i, xs[i]);
            else { mm.SetInstanceTransform(i, coloured[i].Item1); mm.SetInstanceColor(i, coloured[i].Item2); }
        }
        var mi = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat };
        if (range > 0) { mi.VisibilityRangeEnd = range; mi.VisibilityRangeEndMargin = range * 0.1f; }
        AddChild(mi);
    }

    Mesh PalmCrown()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int f = 0; f < 9; f++)
        {
            float yaw = f * Mathf.Tau / 9;
            var dir = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw));
            var side = new Vector3(-dir.Z, 0, dir.X);
            Vector3 prev = new(0, 4.5f, 0);
            for (int s = 1; s <= 4; s++)
            {
                float t = s / 4f;
                Vector3 p = new Vector3(0, 4.5f, 0) + dir * (3.2f * t) + Vector3.Up * (0.8f * t - 2.2f * t * t);
                float w0 = 0.55f * Mathf.Sin((s - 1) / 4f * Mathf.Pi + 0.3f), w1 = 0.55f * Mathf.Sin(t * Mathf.Pi + 0.3f);
                TrackBuilder.Quad(st, prev - side * w0, prev + side * w0, p + side * w1, p - side * w1, Vector3.Up);
                prev = p;
            }
        }
        st.GenerateNormals();
        return st.Commit();
    }

    // ---------- placement helpers ----------

    bool Free(Vector3 p, float r)
    {
        var q = new Vector2(p.X, p.Z);
        foreach (var (c, cr) in _taken) if (q.DistanceTo(c) < r + cr) return false;
        return true;
    }

    void Take(Vector3 p, float r) => _taken.Add((new Vector2(p.X, p.Z), r));

    /// Which side of index i has `depth` metres of clear ground behind the wall (+1 left, -1 right, 0 none).
    int ClearSide(int i, float depth)
    {
        float best = 0; int side = 0;
        foreach (int s in new[] { 1, -1 })
        {
            float d = Track.DistToTrack(Track.At(i, s * (Track.WallOffset + depth)), i, 40);
            if (d > best) { best = d; side = s; }
        }
        return best > Track.WallOffset + depth - 2 ? side : 0;
    }

    /// Local frame for a straight structure beside index i on `side`: X along the track, Z away from it.
    Transform3D Frame(int i, int side, float offset)
    {
        int n = Track.Centre.Length;
        Vector3 t = (Track.Centre[Track.Wrap(i + 8)] - Track.Centre[Track.Wrap(i - 8)]).Normalized();
        Vector3 away = side * Track.Left[Track.Wrap(i)];
        var basis = new Basis(-side * t, Vector3.Up, away);
        return new Transform3D(basis, Track.At(i, side * offset));
    }

    /// True when a box footprint (local X ±len/2, Z 0..depth) stays clear of every part of the track.
    bool FootprintClear(Transform3D f, float len, float depth, float margin)
    {
        for (float x = -len / 2; x <= len / 2; x += 8)
            for (float z = 0; z <= depth; z += 6)
                if (Track.DistToTrack(f * new Vector3(x, 0, z)) < Track.WallOffset + margin) return false;
        return true;
    }

    void TakeFootprint(Transform3D f, float len, float depth)
    {
        for (float x = -len / 2; x <= len / 2; x += 10) Take(f * new Vector3(x, 0, depth / 2), depth / 2 + 5);
    }

    // ---------- pieces ----------

    void StartLine()
    {
        var pose = Track.PoseAt(0, 0.048f);
        var chq = (StandardMaterial3D)Materials.Chequer();
        chq.Uv1Scale = new Vector3(Track.HalfWidth * 2 / 0.6f, 2, 1);
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(Track.HalfWidth * 2, 1.2f) }, Transform = pose, MaterialOverride = chq });

        // grid boxes where Race spawns cars: index -(6+5*slot), ±3 m
        var paint = Materials.Paint();
        for (int slot = 0; slot < 12; slot++)
        {
            var p = Track.PoseAt(-(6 + 5 * slot), 0.04f, slot % 2 == 0 ? 3f : -3f);
            Box(paint, p.Translated(p.Basis * new Vector3(0, 0, -2.6f)), new Vector3(2.2f, 0.01f, 0.15f));
            Box(paint, p.Translated(p.Basis * new Vector3(-1.1f, 0, -2.0f)), new Vector3(0.15f, 0.01f, 1.2f));
            Box(paint, p.Translated(p.Basis * new Vector3(1.1f, 0, -2.0f)), new Vector3(0.15f, 0.01f, 1.2f));
        }

        // gantry with start lights
        float span = Track.WallOffset + 1.5f;
        var steel = Materials.Flat(new Color(0.18f, 0.19f, 0.21f), 0.4f, 0.7f);
        var g = Track.PoseAt(-2, 0);
        foreach (int s in new[] { 1, -1 }) Box(steel, g.Translated(g.Basis * new Vector3(-s * span, 3.6f, 0)), new Vector3(0.8f, 7.2f, 0.8f));
        Box(steel, g.Translated(g.Basis * new Vector3(0, 6.9f, 0)), new Vector3(span * 2 + 0.8f, 0.9f, 0.7f));
        Box(Materials.Flat(new Color(0.05f, 0.05f, 0.05f)), g.Translated(g.Basis * new Vector3(0, 6.0f, 0.4f)), new Vector3(4.2f, 1.1f, 0.3f));
        var red = Materials.Glow(new Color(0.9f, 0.05f, 0.03f), 0.4f);
        for (int k = 0; k < 5; k++)
            for (int r = 0; r < 2; r++)
                AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.16f, Height = 0.32f }, MaterialOverride = red, Transform = g.Translated(g.Basis * new Vector3(-1.6f + k * 0.8f, 6.25f - r * 0.45f, 0.58f)) });
        AddChild(Text("START", g.Translated(g.Basis * new Vector3(0, 7.0f, 0.37f)), 96, Colors.White, 0.012f));
        Take(Track.Centre[0], Track.WallOffset + 3);
    }

    void Pits()
    {
        int side = -_side;
        if (side == 0 || ClearSide(Track.Wrap(-40), 45f) == 0) side = _side == 0 ? 1 : -_side;
        int mid = Track.Wrap(-40);
        var f = Frame(mid, side, Track.WallOffset + 0.4f);
        const float len = 260f;
        if (!FootprintClear(f, len, 48, 2)) { GD.Print("pits: no room beside the main straight"); return; }

        // pit lane surface (drivable) and pit-side wall line already exists (the track wall)
        float o = Track.WallOffset + 0.4f;
        var lane = new bool[Track.Centre.Length];
        for (int k = mid - 75; k <= mid + 75; k++) lane[Track.Wrap(k)] = true;
        Track.Add("PitLane", st => Track.Strip(st, side * o, side * (o + 13f), 0.02f, lane), Materials.PitLane(), true);

        var bld = Materials.Building();
        var glass = Materials.Flat(new Color(0.08f, 0.1f, 0.13f), 0.05f, 0.6f);
        var door = Materials.Flat(new Color(0.12f, 0.12f, 0.14f), 0.5f, 0.3f);
        var white = Materials.Flat(new Color(0.92f, 0.93f, 0.95f), 0.4f, 0.1f);
        Transform3D L(float x, float y, float z) => f.Translated(f.Basis * new Vector3(x, y, z));
        Box(bld, L(0, 2.6f, 26), new Vector3(len, 5.2f, 20));                 // garages
        for (float x = -len / 2 + 7; x < len / 2 - 5; x += 13)
        {
            Box(door, L(x, 2.1f, 15.95f), new Vector3(10.5f, 4.2f, 0.1f));    // garage doors
            Box(white, L(x, 4.65f, 15.9f), new Vector3(11f, 0.5f, 0.12f));     // team name band
        }
        Box(glass, L(0, 7.2f, 27), new Vector3(len, 4f, 17));                 // hospitality glass floor
        Box(white, L(0, 9.45f, 25.5f), new Vector3(len + 4, 0.5f, 24));        // roof slab
        Box(glass, L(-len / 2 + 30, 14f, 27), new Vector3(16, 9, 14));        // race control tower
        Box(white, L(-len / 2 + 30, 18.7f, 27), new Vector3(18, 0.5f, 16));
        AddChild(Text("RACE CONTROL", L(-len / 2 + 30, 16.5f, 19.9f).RotatedLocal(Vector3.Up, Mathf.Pi), 96, Colors.White, 0.02f));
        TakeFootprint(f, len + 10, 48);
    }

    void Grandstands()
    {
        // main straight, then the outside of the slowest corners
        var spots = new List<(int i, int side, float len)>();
        if (_side != 0) spots.Add((Track.Wrap(-60), _side, 180f));
        var slow = new List<TrackBuilder.Corner>(Track.Corners);
        slow.Sort((a, b) => a.ApexSpeed.CompareTo(b.ApexSpeed));
        foreach (var c in slow.GetRange(0, Math.Min(5, slow.Count))) spots.Add((c.Apex, -c.Dir, 90f));

        var structure = Materials.Building();
        var seats = Materials.Flat(new Color(0.08f, 0.25f, 0.6f), 0.6f);
        var roof = Materials.Flat(new Color(0.93f, 0.94f, 0.96f), 0.3f, 0.6f);
        Color[] shirts = { new(0.9f, 0.1f, 0.1f), new(0.95f, 0.95f, 0.95f), new(0.1f, 0.2f, 0.6f), new(0.1f, 0.1f, 0.1f), new(0.95f, 0.75f, 0.1f), new(0.2f, 0.55f, 0.3f), new(0.6f, 0.6f, 0.65f), new(0.95f, 0.5f, 0.6f) };
        int built = 0;
        foreach (var (i, side, len0) in spots)
        {
            const int rows = 16;
            const float stepD = 0.85f, rise = 0.42f, depth = rows * stepD + 3;
            float len = len0;
            Transform3D f = default;
            bool ok = false;
            for (; len >= 40; len -= 20)
            {
                f = Frame(i, side, Track.WallOffset + 7f);
                if (FootprintClear(f, len, depth, 5) && Free(f.Origin, len / 2)) { ok = true; break; }
            }
            if (!ok) continue;
            Transform3D L(float x, float y, float z) => f.Translated(f.Basis * new Vector3(x, y, z));
            float top = 0.6f + rows * rise;
            for (int r = 0; r < rows; r++)
            {
                float h = 0.6f + r * rise;
                Box(structure, L(0, h / 2, r * stepD + stepD / 2), new Vector3(len, h, stepD));
                Box(seats, L(0, h + 0.2f, r * stepD + stepD * 0.75f), new Vector3(len, 0.4f, 0.12f));
                for (float x = -len / 2 + 0.4f; x < len / 2 - 0.3f; x += 0.55f)
                    if (_rng.Randf() < 0.78f)
                        _crowd.Add((L(x, h + 0.36f, r * stepD + stepD * 0.45f).RotatedLocal(Vector3.Up, _rng.RandfRange(-0.25f, 0.25f)), shirts[_rng.RandiRange(0, shirts.Length - 1)]));
            }
            Box(structure, L(0, (top + 3) / 2, rows * stepD + 0.2f), new Vector3(len, top + 3, 0.4f));
            foreach (int s in new[] { 1, -1 }) Box(structure, L(s * len / 2, (top + 3) / 2, depth / 2 - 1), new Vector3(0.4f, top + 3, depth - 2));
            for (float x = -len / 2; x <= len / 2; x += 12) Box(roof, L(x, (top + 4) / 2, rows * stepD - 0.3f), new Vector3(0.35f, top + 4, 0.35f));
            Box(roof, L(0, top + 4.2f, depth / 2 - 2), new Vector3(len + 2, 0.3f, depth + 2));
            // brand along the roof edge
            var b = Brands[built % Brands.Length];
            Box(Materials.Flat(b.bg), L(0, top + 3.4f, -1.05f), new Vector3(Mathf.Min(len, 40), 1.4f, 0.1f));
            AddChild(Text(b.name, L(0, top + 3.4f, -1.12f).RotatedLocal(Vector3.Up, Mathf.Pi), 128, b.fg, 0.012f));
            TakeFootprint(f, len, depth);
            built++;
        }
    }

    void Fences()
    {
        int n = Track.Centre.Length;
        float o = Track.WallOffset + 0.17f;
        var fence = Materials.Fence();
        var st = Batch(fence);
        foreach (int side in new[] { 1, -1 })
        {
            var keep = side > 0 ? Track.WallLeft : Track.WallRight;
            float u = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 a = Track.At(i, side * o), b = Track.At(j, side * o);
                float du = a.DistanceTo(b);
                if (keep[i] && keep[j])
                {
                    Vector3 a0 = a + Vector3.Up * 1.1f, b0 = b + Vector3.Up * 1.1f, a1 = a + Vector3.Up * 4f, b1 = b + Vector3.Up * 4f;
                    foreach (var (p, uv) in new[] { (a0, new Vector2(u, 0)), (b0, new Vector2(u + du, 0)), (b1, new Vector2(u + du, 2.9f)), (a0, new Vector2(u, 0)), (b1, new Vector2(u + du, 2.9f)), (a1, new Vector2(u, 2.9f)) })
                    {
                        st.SetNormal(-side * Track.Left[i]);
                        st.SetUV(uv);
                        st.AddVertex(p);
                    }
                    if (i % 2 == 0) _posts.Add(new Transform3D(Basis.Identity, a + Vector3.Up * 2.55f));
                }
                u += du;
            }
        }
    }

    void TyreWalls()
    {
        Color[] bands = { new(0.85f, 0.08f, 0.06f), new(0.92f, 0.92f, 0.9f) };
        foreach (var c in Track.Corners)
        {
            if (c.EntrySpeed - c.ApexSpeed < 12f) continue;
            int side = -c.Dir;
            var keep = side > 0 ? Track.WallLeft : Track.WallRight;
            float o = Track.WallOffset - 0.4f;
            Vector3 last = Vector3.Inf;
            for (int k = c.Start - 5; k <= c.End + 25; k++)
            {
                int i = Track.Wrap(k);
                if (!keep[i]) continue;
                for (int sub = 0; sub < 4; sub++) // 0.5 m steps between 2 m centreline points
                {
                    Vector3 p = Track.At(i, side * o).Lerp(Track.At(i + 1, side * o), sub / 4f);
                    if (p.DistanceTo(last) < 0.6f) continue;
                    last = p;
                    var col = (_tyres.Count / 3) % 2 == 0 ? bands[0] : bands[1];
                    _tyres.Add((new Transform3D(Basis.Identity, p + Vector3.Up * 0.375f), col));
                }
            }
        }
    }

    Label3D Text(string text, Transform3D at, int size, Color color, float pixel)
    {
        var l = new Label3D { Text = text, FontSize = size, Modulate = color, PixelSize = pixel, Transform = at, OutlineSize = 0, Shaded = true, DoubleSided = false };
        l.VisibilityRangeEnd = 900;
        return l;
    }

    void Hoardings()
    {
        int n = Track.Centre.Length, b = 0;
        float o = Track.WallOffset - 0.06f;
        for (int i = 0; i < n; i += 9)
        {
            if (Mathf.Abs(Track.Curv[i]) > 1f / 500f) continue;
            int side = (i / 9) % 2 == 0 ? 1 : -1;
            var keep = side > 0 ? Track.WallLeft : Track.WallRight;
            if (!keep[Track.Wrap(i - 2)] || !keep[Track.Wrap(i + 2)]) continue;
            var f = Frame(i, side, o);
            var brand = Brands[b++ % Brands.Length];
            var face = f.Translated(f.Basis * new Vector3(0, 1.75f, 0));
            Box(Materials.Flat(brand.bg, 0.5f), face, new Vector3(7.6f, 1.2f, 0.08f));
            // text faces the track (-Z of the frame)
            AddChild(Text(brand.name, face.Translated(face.Basis * new Vector3(0, 0, -0.05f)).RotatedLocal(Vector3.Up, Mathf.Pi), 64, brand.fg, 0.011f));
        }
    }

    void CornerBoards()
    {
        var post = Materials.Flat(new Color(0.85f, 0.85f, 0.85f), 0.5f);
        var panel = Materials.Flat(new Color(0.05f, 0.05f, 0.05f), 0.6f);
        foreach (var c in Track.Corners)
        {
            int side = -c.Dir;
            // turn number at the apex, outside, near the wall
            var f = Frame(c.Apex, side, Track.WallOffset - 2f);
            Box(post, f.Translated(f.Basis * new Vector3(0, 1.1f, 0)), new Vector3(0.12f, 2.2f, 0.12f));
            var face = f.Translated(f.Basis * new Vector3(0, 2.4f, -0.08f));
            Box(panel, face, new Vector3(1.4f, 1f, 0.06f));
            AddChild(Text($"T{c.Number}", face.Translated(face.Basis * new Vector3(0, 0, -0.04f)).RotatedLocal(Vector3.Up, Mathf.Pi), 64, Colors.White, 0.01f));

            // braking boards before heavy stops, on the outside of the approach
            if (c.EntrySpeed - c.ApexSpeed < 15f) continue;
            foreach (int m in new[] { 150, 100, 50 })
            {
                int i = Track.Wrap(c.Start - m / 2);
                var g = Frame(i, side, Track.HalfWidth + Track.KerbWidth + 2.5f);
                Box(post, g.Translated(g.Basis * new Vector3(0, 0.6f, 0)), new Vector3(0.1f, 1.2f, 0.1f));
                // faces oncoming cars: rotate so its front looks back down the track
                var board = new Transform3D(Basis.LookingAt(-(Track.Centre[Track.Wrap(i + 1)] - Track.Centre[Track.Wrap(i - 1)]).Normalized(), Vector3.Up), g.Origin + Vector3.Up * 1.55f);
                Box(Materials.Flat(Colors.White, 0.5f), board, new Vector3(1f, 0.9f, 0.05f));
                AddChild(Text(m.ToString(), board.Translated(board.Basis * new Vector3(0, 0, -0.03f)).RotatedLocal(Vector3.Up, Mathf.Pi), 64, new Color(0.05f, 0.05f, 0.05f), 0.009f));
            }
        }
    }

    void Lights()
    {
        int n = Track.Centre.Length;
        for (int i = 0; i < n; i += 30)
        {
            int side = (i / 30) % 2 == 0 ? 1 : -1;
            float o = Track.WallOffset + 3.5f;
            Vector3 p = Track.At(i, side * o);
            if (Track.DistToTrack(p, i, 30) < o - 1 || !Free(p, 2)) continue;
            _poles.Add(new Transform3D(Basis.Identity, p + Vector3.Up * 11f));
            var toTrack = -side * Track.Left[i];
            var head = new Transform3D(Basis.LookingAt(toTrack, Vector3.Up).Rotated(Basis.LookingAt(toTrack, Vector3.Up).X, -0.35f), p + Vector3.Up * 22.3f + toTrack * 0.6f);
            _heads.Add(head);
            Take(p, 2);
        }
    }

    void City()
    {
        var b = Track.Bounds().Grow(650);
        Color[] facades = { new(0.85f, 0.83f, 0.78f), new(0.75f, 0.76f, 0.78f), new(0.62f, 0.64f, 0.68f), new(0.9f, 0.88f, 0.84f), new(0.55f, 0.5f, 0.45f) };
        for (int tries = 0; tries < 4000 && _city.Count < 420; tries++)
        {
            var p = new Vector3(_rng.RandfRange(b.Position.X, b.End.X), 0, _rng.RandfRange(b.Position.Z, b.End.Z));
            float d = Track.DistToTrack(p);
            if (d < 110 || !Free(p, 30)) continue;
            float w = _rng.RandfRange(18, 55), dpt = _rng.RandfRange(18, 55);
            float h = _rng.RandfRange(8, 30) + (d > 250 ? _rng.RandfRange(0, 1) * _rng.RandfRange(0, 110) : 0);
            var basis = new Basis(Vector3.Up, _rng.RandfRange(0, Mathf.Tau)) * Basis.FromScale(new Vector3(w, h, dpt));
            _city.Add((new Transform3D(basis, p + Vector3.Up * h / 2), facades[_rng.RandiRange(0, facades.Length - 1)]));
            Take(p, Mathf.Max(w, dpt) * 0.6f);
        }
    }

    void Palms()
    {
        var b = Track.Bounds().Grow(220);
        for (int tries = 0; tries < 6000 && _trunks.Count < 450; tries++)
        {
            var p = new Vector3(_rng.RandfRange(b.Position.X, b.End.X), 0, _rng.RandfRange(b.Position.Z, b.End.Z));
            float d = Track.DistToTrack(p);
            if (d < Track.WallOffset + 6 || d > 160 || !Free(p, 2.5f)) continue;
            float s = _rng.RandfRange(0.8f, 1.3f);
            var basis = new Basis(Vector3.Up, _rng.RandfRange(0, Mathf.Tau)).Rotated(Vector3.Right, _rng.RandfRange(-0.06f, 0.06f)) * Basis.FromScale(Vector3.One * s);
            _trunks.Add(new Transform3D(basis, p + Vector3.Up * 4.5f * s));
            Take(p, 2.5f);
        }
    }
}
