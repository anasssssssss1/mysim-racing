using Godot;
using System.Linq;

/// Scene root: lighting, sky, the track, a camera. With `-- --shots=<dir>` it saves preview renders and quits.
public partial class Main : Node3D
{
    TrackBuilder _track;
    Camera3D _cam;

    public override async void _Ready()
    {
        var env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial { SkyTopColor = new Color(0.22f, 0.42f, 0.75f), SkyHorizonColor = new Color(0.72f, 0.78f, 0.85f), GroundHorizonColor = new Color(0.6f, 0.58f, 0.52f) } },
            AmbientLightSource = Environment.AmbientSource.Sky,
            ReflectedLightSource = Environment.ReflectionSource.Sky,
            TonemapMode = Environment.ToneMapper.Filmic,
            SsaoEnabled = true,
            GlowEnabled = true,
            FogEnabled = true,
            FogDensity = 0.00012f,
            FogLightColor = new Color(0.75f, 0.8f, 0.88f),
        };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-38, -35, 0),
            LightEnergy = 1.3f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 600,
        });

        _track = new TrackBuilder();
        AddChild(_track);
        _cam = new Camera3D { Far = 12000, Fov = 60 };
        AddChild(_cam);

        var arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--shots="));
        if (arg == null) { Look(Overview()); return; }

        string dir = arg["--shots=".Length..];
        DirAccess.MakeDirRecursiveAbsolute(dir);
        await ToSignal(GetTree().CreateTimer(4), Timer.SignalName.Timeout); // procedural textures build on a thread
        int n = _track.Centre.Length, hair = _track.TightestCorner();
        var shots = new (string, Vector3, Vector3)[]
        {
            ("1_overview", Overview().Item1, Overview().Item2),
            ("2_start_straight", P(0, 0, 1.1f), P(60, 0, 1f)),
            ("3_hairpin", P(hair - 70, -_track.Curv[hair] > 0 ? 14 : -14, 9), P(hair, 0, 0)),
            ("4_low_corner", P(hair - 25, 0, 1.1f), P(hair, 0, 1f)),
            ("5_mid_lap", P(n / 2, 0, 18), P(n / 2 + 120, 0, 0)),
        };
        foreach (var (name, pos, at) in shots)
        {
            Look((pos, at));
            for (int f = 0; f < 12; f++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); // let TAA/SSAO settle
            GetViewport().GetTexture().GetImage().SavePng($"{dir}/{name}.png");
            GD.Print($"shot {name}");
        }
        GetTree().Quit();
    }

    Vector3 P(int i, float left, float up)
    {
        int n = _track.Centre.Length;
        i = ((i % n) + n) % n;
        return _track.Centre[i] + _track.Left[i] * left + Vector3.Up * up;
    }

    (Vector3, Vector3) Overview()
    {
        var b = _track.Bounds();
        var c = b.GetCenter();
        float size = Mathf.Max(b.Size.X, b.Size.Z);
        return (c + new Vector3(0, size * 0.75f, size * 0.55f), c);
    }

    void Look((Vector3 pos, Vector3 at) v)
    {
        _cam.Position = v.pos;
        _cam.LookAt(v.at, Vector3.Up);
    }
}
