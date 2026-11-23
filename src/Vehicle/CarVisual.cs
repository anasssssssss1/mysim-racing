using Godot;

/// Placeholder car look built from primitives (original design). Real car models replace this later.
public partial class CarVisual : Node3D
{
    Car _car;
    Node3D[] _wheels;
    Node3D[] _spin;

    public static CarVisual For(Car car)
    {
        var v = new CarVisual { _car = car, Name = "Visual" };
        car.AddChild(v);
        return v;
    }

    public override void _Ready()
    {
        var paint = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.22f, 0.65f), Metallic = 0.6f, Roughness = 0.25f, Clearcoat = 1f, ClearcoatRoughness = 0.05f };
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.025f, 0.03f), Metallic = 0.2f, Roughness = 0.05f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.03f, 0.03f, 0.03f), Roughness = 0.7f };
        var tyre = new StandardMaterial3D { AlbedoColor = new Color(0.04f, 0.04f, 0.04f), Roughness = 0.9f };
        var rim = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.56f, 0.58f), Metallic = 0.9f, Roughness = 0.3f };

        Box(new Vector3(1.92f, 0.42f, 4.5f), new Vector3(0, -0.02f, 0), paint);       // main body
        Box(new Vector3(1.5f, 0.38f, 1.9f), new Vector3(0, 0.36f, 0.25f), glass);      // cabin
        Box(new Vector3(1.4f, 0.06f, 1.5f), new Vector3(0, 0.56f, 0.3f), paint);       // roof
        Box(new Vector3(1.9f, 0.05f, 0.5f), new Vector3(0, -0.2f, -2.45f), dark);      // splitter
        Box(new Vector3(1.8f, 0.04f, 0.32f), new Vector3(0, 0.52f, 2.15f), dark);      // rear wing
        Box(new Vector3(0.05f, 0.3f, 0.2f), new Vector3(-0.6f, 0.36f, 2.1f), dark);
        Box(new Vector3(0.05f, 0.3f, 0.2f), new Vector3(0.6f, 0.36f, 2.1f), dark);

        var s = _car.S;
        _wheels = new Node3D[4];
        _spin = new Node3D[4];
        for (int i = 0; i < 4; i++)
        {
            var steer = new Node3D();
            var spin = new Node3D();
            AddChild(steer);
            steer.AddChild(spin);
            var t = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = s.WheelRadius, BottomRadius = s.WheelRadius, Height = 0.3f, RadialSegments = 32 }, MaterialOverride = tyre, RotationDegrees = new Vector3(0, 0, 90) };
            var r = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.23f, BottomRadius = 0.23f, Height = 0.31f, RadialSegments = 10 }, MaterialOverride = rim, RotationDegrees = new Vector3(0, 0, 90) };
            spin.AddChild(t);
            spin.AddChild(r);
            _wheels[i] = steer;
            _spin[i] = spin;
        }
    }

    void Box(Vector3 size, Vector3 pos, Material m) =>
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, MaterialOverride = m });

    public override void _Process(double delta)
    {
        if (!_car.Ready) return;
        for (int i = 0; i < 4; i++)
        {
            var w = _car.Wheels[i];
            float len = Mathf.Clamp(w.SuspLength, 0, _car.S.RestLength);
            _wheels[i].Position = w.Mount - Vector3.Up * len;
            _wheels[i].Rotation = new Vector3(0, w.Steer, 0);
            _spin[i].Rotation = new Vector3(-w.SpinAngle, 0, 0);
        }
    }
}
