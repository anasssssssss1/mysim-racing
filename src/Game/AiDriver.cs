using Godot;

/// Drives a car around the track: speed profile from curvature + braking distance,
/// pure-pursuit steering on the centreline, wheelspin-limited throttle.
public partial class AiDriver : Node
{
    public Car Car;
    public TrackBuilder Track;
    public float Grip = 1.3f;       // lateral g the profile plans for (below the car's ~1.6 g limit)
    public float BrakeDecel = 11f;  // m/s^2 planned under braking
    public float Offset;            // lateral offset from the centreline (m, + = left)
    public float[] Profile;
    int _index;

    public override void _Ready() => Profile = SpeedProfile(Track, Grip, BrakeDecel);

    public static float[] SpeedProfile(TrackBuilder t, float grip, float decel)
    {
        int n = t.Centre.Length;
        var v = new float[n];
        for (int i = 0; i < n; i++)
        {
            float k = Mathf.Abs(t.Curv[i]);
            v[i] = k < 1e-4f ? 90f : Mathf.Min(90f, Mathf.Sqrt(grip * 9.81f / k));
        }
        for (int pass = 0; pass < 2; pass++) // loop twice so braking wraps over the start line
            for (int i = n - 1; i >= 0; i--)
            {
                int j = (i + 1) % n;
                float ds = t.Centre[i].DistanceTo(t.Centre[j]);
                v[i] = Mathf.Min(v[i], Mathf.Sqrt(v[j] * v[j] + 2 * decel * ds));
            }
        return v;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Car == null || !Car.Ready) return;
        int n = Track.Centre.Length;
        _index = Track.Nearest(Car.GlobalPosition, _index, 40);
        float speed = Car.ForwardSpeed;

        // pure pursuit
        float look = 8f + 0.45f * Mathf.Max(0, speed);
        int ahead = (_index + Mathf.RoundToInt(look / 2f)) % n; // 2 m per centreline point
        Vector3 target = Track.Centre[ahead] + Track.Left[ahead] * Offset;
        Vector3 local = Car.GlobalTransform.AffineInverse() * target;
        float alpha = Mathf.Atan2(local.X, -local.Z); // + = target to the right
        float steerAngle = Mathf.Atan(2f * Car.S.Wheelbase * Mathf.Sin(alpha) / look);
        float steer = Mathf.Clamp(steerAngle / Car.S.MaxSteer, -1, 1);

        // speed: aim for the profile a little ahead (covers the car's response lag)
        float want = Profile[(_index + 6) % n];
        float err = want - speed;
        float throttle = Mathf.Clamp(err * 0.4f + 0.3f, 0, 1);
        float brake = Mathf.Clamp(-err * 0.25f, 0, 1);
        if (err < -1.5f) throttle = 0;
        float rearSlip = Mathf.Max(Car.Wheels[2].Kappa, Car.Wheels[3].Kappa);
        if (rearSlip > Car.S.Tyre.KappaPeak) throttle *= 0.5f;

        Car.In = new CarInput { Throttle = throttle, Brake = brake, Steer = steer };
    }
}
