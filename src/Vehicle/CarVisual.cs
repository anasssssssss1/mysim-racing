using Godot;

/// Car look: sculpted body (CarBody) and wheels that follow the physics wheels.
public partial class CarVisual : Node3D
{
    Car _car;
    Node3D[] _steer, _spin;
    public static readonly Color[] Paints =
    {
        new(0.04f, 0.2f, 0.62f), new(0.75f, 0.06f, 0.05f), new(0.92f, 0.92f, 0.9f), new(0.05f, 0.05f, 0.06f),
        new(0.95f, 0.55f, 0.05f), new(0.1f, 0.45f, 0.25f), new(0.5f, 0.52f, 0.56f), new(0.9f, 0.8f, 0.1f),
        new(0.4f, 0.1f, 0.5f), new(0.1f, 0.6f, 0.75f), new(0.6f, 0.15f, 0.2f), new(0.2f, 0.2f, 0.22f),
    };

    public static CarVisual For(Car car, int paint = 0)
    {
        var v = new CarVisual { _car = car, Name = "Visual" };
        car.AddChild(v);
        v.AddChild(CarBody.Build(Paints[paint % Paints.Length]));
        return v;
    }

    public override void _Ready()
    {
        var s = _car.S;
        _steer = new Node3D[4];
        _spin = new Node3D[4];
        for (int i = 0; i < 4; i++)
        {
            var steer = new Node3D();
            AddChild(steer);
            CarBody.Wheel(s.WheelRadius, _car.Wheels[i].Front ? 0.29f : 0.33f, out _spin[i], steer);
            if (_car.Wheels[i].Left) steer.Scale = new Vector3(-1, 1, 1); // rim face outward on both sides
            _steer[i] = steer;
        }
    }

    public override void _Process(double delta)
    {
        if (!_car.Ready) return;
        for (int i = 0; i < 4; i++)
        {
            var w = _car.Wheels[i];
            float len = Mathf.Clamp(w.SuspLength, 0, _car.S.RestLength);
            _steer[i].Position = w.Mount - Vector3.Up * len;
            _steer[i].Rotation = new Vector3(0, w.Steer, 0);
            _spin[i].Rotation = new Vector3(-w.SpinAngle, 0, 0);
        }
    }
}
