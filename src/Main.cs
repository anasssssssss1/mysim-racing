using Godot;
using System.Linq;

/// Scene root. Modes (after `--`):
///   (none)          drive Yas Marina
///   --test          headless physics tests, exit code = result
///   --shots=<dir>   save preview renders and quit
public partial class Main : Node3D
{
    TrackBuilder _track;
    Car _car;
    ChaseCamera _cam;

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--test")) { AddChild(new PhysicsTests()); return; }

        PlayerController.SetupActions();
        AddChild(new WorldEnvironment { Environment = Env() });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-38, -35, 0),
            LightEnergy = 1.3f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 400,
        });

        _track = new TrackBuilder();
        AddChild(_track);
        _car = new Car();
        AddChild(_car);
        _car.Place(_track.PoseAt(0, 0.4f, -2f));
        CarVisual.For(_car);
        _cam = new ChaseCamera { Target = _car };
        AddChild(_cam);
        var laps = new LapTimer { Car = _car, Track = _track };
        AddChild(laps);

        var shotArg = args.FirstOrDefault(a => a.StartsWith("--shots="));
        if (shotArg != null) { await Shots(shotArg["--shots=".Length..]); return; }

        AddChild(new PlayerController { Car = _car });
        AddChild(new Hud { Car = _car, Laps = laps });
        _cam.Snap();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_car == null || _track == null) return;
        bool fell = _car.GlobalPosition.Y < -20;
        if (fell || Input.IsActionJustPressed("reset"))
            _car.Place(_track.PoseAt(_track.Nearest(_car.GlobalPosition, 0, 0)));
    }

    static Environment Env() => new()
    {
        BackgroundMode = Environment.BGMode.Sky,
        Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial { SkyTopColor = new Color(0.22f, 0.42f, 0.75f), SkyHorizonColor = new Color(0.72f, 0.78f, 0.85f), GroundHorizonColor = new Color(0.6f, 0.58f, 0.52f) } },
        AmbientLightSource = Environment.AmbientSource.Sky,
        ReflectedLightSource = Environment.ReflectionSource.Sky,
        TonemapMode = Environment.ToneMapper.Filmic,
        SsaoEnabled = true,
        GlowEnabled = true,
        FogEnabled = true,
        FogDensity = 0.00004f,
        FogLightColor = new Color(0.75f, 0.8f, 0.88f),
    };

    async System.Threading.Tasks.Task Shots(string dir)
    {
        DirAccess.MakeDirRecursiveAbsolute(dir);
        var cam = new Camera3D { Far = 12000, Fov = 60 };
        AddChild(cam);
        await ToSignal(GetTree().CreateTimer(4), Timer.SignalName.Timeout); // car settles, textures build
        int n = _track.Centre.Length, hair = _track.TightestCorner();
        var b = _track.Bounds();
        float size = Mathf.Max(b.Size.X, b.Size.Z);
        var shots = new (string, Camera3D, Vector3, Vector3)[]
        {
            ("1_overview", cam, b.GetCenter() + new Vector3(0, size * 0.75f, size * 0.55f), b.GetCenter()),
            ("2_car_chase", _cam, default, default),
            ("3_car_front", cam, _car.GlobalTransform * new Vector3(2.8f, 0.6f, -5.5f), _car.GlobalPosition + Vector3.Up * 0.2f),
            ("4_hairpin", cam, P(hair - 70, _track.Curv[hair] > 0 ? -14 : 14, 9), P(hair, 0, 0)),
            ("5_low_corner", cam, P(hair - 25, 0, 1.1f), P(hair, 0, 1f)),
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
