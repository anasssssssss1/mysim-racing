using Godot;
using System.Linq;

/// Scene root. Modes (after `--`):
///   (none)          drive Yas Marina
///   --test          headless physics tests, exit code = result
///   --shots=<dir>   save preview renders and quit
public partial class Main : Node3D
{
    TrackBuilder _track;
    ChaseCamera _cam;
    Race _race;
    Car _car => _race?.Local;

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--test")) { AddChild(new PhysicsTests()); return; }

        PlayerController.SetupActions();
        AddChild(new WorldEnvironment { Environment = Env() });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-38, -35, 0),
            LightEnergy = 1.8f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 400,
        });

        _track = new TrackBuilder { Name = "Track" };
        AddChild(_track);
        _cam = new ChaseCamera();
        AddChild(_cam);
        var hud = new Hud();
        AddChild(hud);
        _race = new Race { Name = "Race", Track = _track, Cam = _cam, Hud = hud };
        AddChild(_race);

        var shotArg = args.FirstOrDefault(a => a.StartsWith("--shots="));
        if (shotArg != null) { hud.Visible = false; _race.StartPractice(); await Shots(shotArg["--shots=".Length..]); return; }

        var net = new NetSession { Name = "Net" };
        AddChild(net);
        AddChild(new Menu { Net = net, Race = _race });
        AddChild(new TouchControls());
        var b = _track.Bounds();
        _cam.Position = b.GetCenter() + new Vector3(0, 900, 700);
        _cam.LookAt(b.GetCenter(), Vector3.Up);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_car == null || _car.Freeze) return;
        bool fell = _car.GlobalPosition.Y < -20;
        if (fell || Input.IsActionJustPressed("reset"))
            _car.Place(_track.PoseAt(_track.Nearest(_car.GlobalPosition, 0, 0)));
    }

    const string SkyPhoto = "res://assets/cc0/sky/kloofendal_48d_partly_cloudy_puresky_4k.hdr";

    static Environment Env() => new()
    {
        BackgroundMode = Environment.BGMode.Sky,
        Sky = new Sky
        {
            SkyMaterial = ResourceLoader.Exists(SkyPhoto)
                ? new PanoramaSkyMaterial { Panorama = GD.Load<Texture2D>(SkyPhoto), EnergyMultiplier = 1f }
                : new ProceduralSkyMaterial { SkyTopColor = new Color(0.22f, 0.42f, 0.75f), SkyHorizonColor = new Color(0.72f, 0.78f, 0.85f), GroundHorizonColor = new Color(0.6f, 0.58f, 0.52f) },
            ProcessMode = Sky.ProcessModeEnum.Quality,
        },
        AmbientLightSource = Environment.AmbientSource.Sky,
        ReflectedLightSource = Environment.ReflectionSource.Sky,
        TonemapMode = Environment.ToneMapper.Filmic,
        SsaoEnabled = true,
        GlowEnabled = true,
        FogEnabled = true,
        FogDensity = 0.00004f,
        FogLightColor = new Color(0.75f, 0.8f, 0.88f),
        FogSkyAffect = 0.1f,
    };

    async System.Threading.Tasks.Task Shots(string dir)
    {
        DirAccess.MakeDirRecursiveAbsolute(dir);
        var cam = new Camera3D { Far = 12000, Fov = 60 };
        AddChild(cam);
        await ToSignal(GetTree().CreateTimer(4), Timer.SignalName.Timeout); // car settles, textures build
        int n = _track.Centre.Length, hair = _track.TightestCorner();
        var slow = _track.Corners.OrderBy(c => c.ApexSpeed).First();
        var b = _track.Bounds();
        float size = Mathf.Max(b.Size.X, b.Size.Z);
        var shots = new (string, Camera3D, Vector3, Vector3)[]
        {
            ("1_overview", cam, b.GetCenter() + new Vector3(0, size * 0.75f, size * 0.55f), b.GetCenter()),
            ("2_car_chase", _cam, default, default),
            ("3_car_front", cam, _car.GlobalTransform * new Vector3(2.8f, 0.6f, -5.5f), _car.GlobalPosition + Vector3.Up * 0.2f),
            ("4_hairpin", cam, P(hair - 70, _track.Curv[hair] > 0 ? -14 : 14, 9), P(hair, 0, 0)),
            ("5_low_corner", cam, P(hair - 25, 0, 1.1f), P(hair, 0, 1f)),
            ("6_main_straight", cam, P(-160, 0, 1.6f), P(-40, 0, 3f)),
            ("7_grid_and_gantry", cam, P(-70, 4, 6f), P(0, 0, 4f)),
            ("8_corner_outside", cam, P(slow.Apex - 30, slow.Dir * (_track.HalfWidth - 1), 1.8f), P(slow.Apex, -slow.Dir * _track.WallOffset, 2f)),
            ("10_car_side", cam, _car.GlobalTransform * new Vector3(-4.5f, 0.3f, -0.3f), _car.GlobalPosition),
            ("11_car_rear_quarter", cam, _car.GlobalTransform * new Vector3(3.2f, 1.2f, 4.8f), _car.GlobalPosition + Vector3.Up * 0.2f),
            ("9_aerial_low", cam, P(-60, 0, 0) + new Vector3(160, 120, 160), P(-60, 0, 0)),
        };
        foreach (var (name, c, pos, at) in shots)
        {
            if (c == _cam) _cam.Snap();
            else { cam.Position = pos; cam.LookAt(at, Vector3.Up); }
            c.MakeCurrent();
            for (int f = 0; f < 12; f++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
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
}
