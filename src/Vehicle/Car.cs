using Godot;

public struct CarInput
{
    public float Throttle, Brake, Steer; // 0..1, 0..1, -1 (left) .. +1 (right)
    public bool Handbrake;
}

public sealed class Wheel
{
    public Vector3 Mount;            // body frame
    public bool Front, Left, Driven;
    public float Omega, SpinAngle, Steer;
    public float Compression, PrevCompression, SuspLength;
    public bool Contact;
    public Vector3 ContactPoint, Normal;
    public float Load, Kappa, Alpha, Fx, Fy;
    public float Grip = 1f, Drag;   // from the surface under the tyre
    public float DriveTorque, BrakeTorque;
}

/// Ray-cast suspension car on a rigid body. Forces are applied in _IntegrateForces at the tyre contact points.
public partial class Car : RigidBody3D
{
    public CarSpec S = new();
    public CarInput In;
    public Wheel[] Wheels;
    public int Gear = 1;           // -1 reverse, 0 neutral, 1..n
    public float Rpm;
    public new bool Ready => Wheels != null;
    float _shift, _reverseHold, _tc = 1f;
    public float Tc => _tc;
    PhysicsRayQueryParameters3D _ray;
    const float G = 9.81f, Rho = 1.2f;

    public float ForwardSpeed => LinearVelocity.Dot(-GlobalBasis.Z);

    public override void _Ready()
    {
        Mass = S.Mass;
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        CenterOfMass = new Vector3(0, S.CgHeight, S.CgZ);
        Inertia = S.Inertia;
        LinearDampMode = DampMode.Replace; LinearDamp = 0;
        AngularDampMode = DampMode.Replace; AngularDamp = 0;
        CustomIntegrator = false;
        ContinuousCd = true;
        CanSleep = false;
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.9f, 0.62f, 4.4f) }, Position = new Vector3(0, 0.08f, 0) });

        float hz = S.Wheelbase / 2;
        Wheels = new[]
        {
            new Wheel { Mount = new(-S.TrackFront / 2, S.MountHeight, -hz), Front = true, Left = true },
            new Wheel { Mount = new(S.TrackFront / 2, S.MountHeight, -hz), Front = true, Left = false },
            new Wheel { Mount = new(-S.TrackRear / 2, S.MountHeight, hz), Front = false, Left = true, Driven = true },
            new Wheel { Mount = new(S.TrackRear / 2, S.MountHeight, hz), Front = false, Left = false, Driven = true },
        };
        Rpm = S.IdleRpm;
    }

    float Ratio => Gear > 0 ? S.Gears[Gear - 1] * S.FinalDrive : Gear < 0 ? -S.ReverseGear * S.FinalDrive : 0;

    void Drivetrain(float dt)
    {
        float v = ForwardSpeed;
        // arcade gear logic: brake at a standstill selects reverse, throttle selects drive
        if (Mathf.Abs(v) < 0.5f && In.Brake > 0.1f && In.Throttle < 0.05f) _reverseHold += dt; else _reverseHold = 0;
        if (Gear > 0 && _reverseHold > 0.4f) Gear = -1;
        if (Gear < 0 && In.Throttle > 0.1f && v > -0.5f) Gear = 1;

        float throttle = Gear < 0 ? In.Brake : In.Throttle;
        if (S.TractionControl)
        {
            // combined slip of the driven wheels: 1.0 = at the tyre's peak (wheelspin and sideways slip together)
            float slip = 0;
            for (int k = 2; k < 4; k++)
            {
                float sx = Mathf.Max(0, Wheels[k].Kappa) / S.Tyre.KappaPeak, sy = Wheels[k].Alpha / S.Tyre.AlphaPeak;
                slip = Mathf.Max(slip, Mathf.Sqrt(sx * sx + sy * sy));
            }
            _tc = slip > 0.95f ? Mathf.Max(0.05f, _tc - dt * 12f) : Mathf.Min(1f, _tc + dt * 2.5f);
            throttle *= _tc;
        }
        float wheelOmega = 0.5f * (Wheels[2].Omega + Wheels[3].Omega);
        float wheelRpm = Mathf.Abs(wheelOmega * Ratio) * 60f / Mathf.Tau;
        Rpm = Mathf.Max(S.IdleRpm, wheelRpm);
        bool clutchSlip = wheelRpm < 3500f && throttle > 0.01f;
        if (clutchSlip) Rpm = Mathf.Max(Rpm, S.IdleRpm + 3000f * throttle);

        if (_shift > 0) _shift -= dt;
        else if (Gear > 0)
        {
            if (Rpm > S.UpshiftRpm && Gear < S.Gears.Length) { Gear++; _shift = S.ShiftTime; }
            else if (Gear > 1 && wheelRpm < S.DownshiftRpm && wheelRpm * S.Gears[Gear - 2] / S.Gears[Gear - 1] < S.UpshiftRpm - 800f) { Gear--; _shift = S.ShiftTime; }
        }

        float te;
        if (_shift > 0 || Gear == 0) te = 0;
        else if (throttle > 0.01f) te = Rpm >= S.Redline ? 0 : S.EngineTorque(Rpm) * throttle;
        else te = wheelRpm > S.IdleRpm ? -S.EngineDrag(Rpm) : 0;
        float axle = te * Ratio * S.Efficiency;
        Wheels[2].DriveTorque = Wheels[3].DriveTorque = axle / 2;

        float brake = Gear < 0 ? In.Throttle : In.Brake;
        foreach (var w in Wheels)
        {
            w.BrakeTorque = brake * S.BrakeTorque * (w.Front ? S.BrakeBias : 1 - S.BrakeBias) / 2;
            if (!w.Front && In.Handbrake) w.BrakeTorque += S.HandbrakeTorque / 2;
            if (S.Abs && brake > 0 && w.Kappa < -1.2f * S.Tyre.KappaPeak) w.BrakeTorque *= 0.3f;
            w.Steer = w.Front ? -In.Steer * S.MaxSteer : 0; // + angle = left
        }
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D st)
    {
        if (!Ready) return;
        float dt = st.Step;
        var xf = st.Transform;
        Vector3 up = xf.Basis.Y, fwd = -xf.Basis.Z;
        var space = st.GetSpaceState();
        Drivetrain(dt);

        // pass 1: suspension geometry
        foreach (var w in Wheels)
        {
            Vector3 from = xf * w.Mount, to = from - up * (S.RestLength + S.WheelRadius);
            _ray ??= new PhysicsRayQueryParameters3D { Exclude = new Godot.Collections.Array<Rid> { GetRid() } };
            _ray.From = from; _ray.To = to;
            using var hit = space.IntersectRay(_ray);
            w.PrevCompression = w.Compression;
            w.Contact = hit.Count > 0;
            if (w.Contact)
            {
                w.ContactPoint = (Vector3)hit["position"];
                w.Grip = 1f; w.Drag = 0f;
                if (hit["collider"].AsGodotObject() is Node c && c.HasMeta("grip"))
                {
                    w.Grip = (float)c.GetMeta("grip");
                    w.Drag = (float)c.GetMeta("drag", 0f);
                }
                w.Normal = (Vector3)hit["normal"];
                float d = from.DistanceTo(w.ContactPoint);
                w.SuspLength = d - S.WheelRadius;
                w.Compression = S.RestLength - Mathf.Clamp(w.SuspLength, 0, S.RestLength);
            }
            else { w.Compression = 0; w.SuspLength = S.RestLength; }
        }

        // pass 2: loads, tyres, wheel spin
        for (int i = 0; i < 4; i++)
        {
            var w = Wheels[i];
            var other = Wheels[i ^ 1]; // same axle, other side
            if (!w.Contact)
            {
                w.Load = w.Fx = w.Fy = 0;
                w.Omega += dt * (w.DriveTorque - Mathf.Sign(w.Omega) * w.BrakeTorque) / S.WheelInertia;
                w.Omega *= 0.999f;
                w.SpinAngle += w.Omega * dt;
                continue;
            }
            float k = w.Front ? S.SpringFront : S.SpringRear;
            float compVel = (w.Compression - w.PrevCompression) / dt;
            float c = compVel > 0 ? (w.Front ? S.BumpFront : S.BumpRear) : (w.Front ? S.ReboundFront : S.ReboundRear);
            float arb = (w.Front ? S.ArbFront : S.ArbRear) * (w.Compression - other.Compression);
            float stop = w.SuspLength < 0 ? -w.SuspLength * S.BumpStop : 0;
            float susp = Mathf.Max(0, k * w.Compression + c * compVel + arb + stop);
            w.Load = susp;

            Vector3 n = w.Normal;
            Vector3 heading = fwd.Rotated(up, w.Steer);
            Vector3 lng = (heading - n * heading.Dot(n)).Normalized();
            Vector3 lat = lng.Cross(n);
            Vector3 vel = st.GetVelocityAtLocalPosition(w.ContactPoint - xf.Origin);
            float vx = vel.Dot(lng), vy = vel.Dot(lat);
            float r = S.WheelRadius;

            w.Alpha = Mathf.Atan2(-vy, Mathf.Max(Mathf.Abs(vx), 2f));
            float vk = Mathf.Max(Mathf.Abs(vx), 3f);
            // implicit (linearised backward Euler) wheel spin: stable at any speed
            float k0 = (w.Omega * r - vx) / vk;
            float fx0 = w.Grip * S.Tyre.Force(k0, w.Alpha, w.Load).X;
            float dFdk = (w.Grip * S.Tyre.Force(k0 + 1e-3f, w.Alpha, w.Load).X - fx0) / 1e-3f;
            float iEff = S.WheelInertia + dt * r * Mathf.Max(0, dFdk) * r / vk;
            float omega = w.Omega + dt * (w.DriveTorque - r * fx0) / iEff;
            float brakeStep = dt * w.BrakeTorque / iEff;
            omega = Mathf.Abs(omega) <= brakeStep ? 0 : omega - Mathf.Sign(omega) * brakeStep;
            w.Omega = omega;
            w.SpinAngle += omega * dt;
            w.Kappa = (omega * r - vx) / vk;

            var f = S.Tyre.Force(w.Kappa, w.Alpha, w.Load) * w.Grip;
            float m = w.Load / G; // share of mass this corner carries
            float slipVel = omega * r - vx;
            // never push past zero relative velocity in one step (keeps the car still at rest)
            f.X = Mathf.Clamp(f.X, -0.5f * m * Mathf.Abs(slipVel) / dt - 1f, 0.5f * m * Mathf.Abs(slipVel) / dt + 1f);
            f.Y = Mathf.Clamp(f.Y, -0.5f * m * Mathf.Abs(vy) / dt, 0.5f * m * Mathf.Abs(vy) / dt);
            f.X -= (S.RollingResistance + w.Drag) * w.Load * Mathf.Clamp(vx, -1, 1);
            w.Fx = f.X; w.Fy = f.Y;

            st.ApplyForce(n * susp + lng * f.X + lat * f.Y, w.ContactPoint - xf.Origin);
        }

        // limited-slip differential: move the two driven wheels toward each other's speed, capped
        {
            var l = Wheels[2]; var rr = Wheels[3];
            float cap = S.LsdPreload + S.LsdRamp * Mathf.Abs(l.DriveTorque + rr.DriveTorque);
            float want = (l.Omega - rr.Omega) * S.WheelInertia / (2 * dt);
            float t = Mathf.Clamp(want, -cap, cap) * dt / S.WheelInertia;
            l.Omega -= t; rr.Omega += t;
        }

        // aero
        Vector3 v = st.LinearVelocity;
        float vf = v.Dot(fwd);
        st.ApplyCentralForce(-0.5f * Rho * S.CdA * v.Length() * v);
        float down = 0.5f * Rho * S.ClA * vf * vf;
        float hz = S.Wheelbase / 2;
        st.ApplyForce(-up * down * S.AeroBalance, xf.Basis * new Vector3(0, 0, -hz));
        st.ApplyForce(-up * down * (1 - S.AeroBalance), xf.Basis * new Vector3(0, 0, hz));
    }

    /// Put the car at rest at a spot, wheels stopped.
    public void Place(Transform3D at)
    {
        GlobalTransform = at;
        LinearVelocity = AngularVelocity = Vector3.Zero;
        if (Wheels != null) foreach (var w in Wheels) { w.Omega = 0; w.Compression = w.PrevCompression = 0; }
        Gear = 1;
    }
}
