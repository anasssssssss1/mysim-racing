using Godot;

/// Smoothed chase camera with a bonnet view (toggle with "camera").
public partial class ChaseCamera : Camera3D
{
    public Car Target;
    public float Distance = 6.2f, Height = 2.0f;
    bool _bonnet;

    public override void _Ready() { Far = 12000; Fov = 65; }

    public void Snap()
    {
        if (Target == null) return;
        GlobalPosition = Want();
        LookAt(Target.GlobalPosition + Vector3.Up, Vector3.Up);
    }

    Vector3 Want()
    {
        var b = Target.GlobalBasis;
        Vector3 back = new Vector3(b.Z.X, 0, b.Z.Z).Normalized();
        return _bonnet ? Target.GlobalTransform * new Vector3(0, 0.75f, -0.4f) : Target.GlobalPosition + back * Distance + Vector3.Up * Height;
    }

    public override void _Process(double delta)
    {
        if (Target == null) return;
        if (Input.IsActionJustPressed("camera")) _bonnet = !_bonnet;
        if (_bonnet)
        {
            GlobalTransform = Target.GlobalTransform;
            GlobalPosition = Want();
            return;
        }
        float k = 1f - Mathf.Exp(-8f * (float)delta);
        GlobalPosition = GlobalPosition.Lerp(Want(), k);
        LookAt(Target.GlobalPosition + Vector3.Up * 0.9f, Vector3.Up);
    }
}
