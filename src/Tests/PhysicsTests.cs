using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// Headless physics checks. Run: godot --headless --fixed-fps 240 -- --test
/// Pass bands are sanity bands for a ~480 hp GT car, not lab-validated numbers.
public partial class PhysicsTests : Node3D
{
    readonly List<string> _report = new();
    bool _failed;
    Car _car;
    int _steps;

    public override async void _Ready()
    {
        var ground = new StaticBody3D();
        ground.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() }); // endless flat ground
        AddChild(ground);

        _report.Add("== join codes");
        foreach (var ip in new[] { "0.0.0.0", "192.168.1.20", "255.255.255.255", "83.110.7.9" })
            Check(NetSession.Decode(NetSession.Encode(ip)) == ip, $"{ip} -> {NetSession.Encode(ip)} -> back");
        await Run("at rest", AtRest);
        await Run("0-100 km/h and top speed", Accel);
        await Run("100-0 km/h braking", Braking);
        await Run("max lateral grip", Lateral);
        await Run("reverse", Reverse);
        await Run("AI lap of Yas Marina", AiLap);

        foreach (var line in _report) GD.Print(line);
        GD.Print(_failed ? "PHYSICS TESTS: FAIL" : "PHYSICS TESTS: PASS");
        GetTree().Quit(_failed ? 1 : 0);
    }

    async Task Run(string name, Func<Task> test)
    {
        _car?.QueueFree();
        _car = new Car();
        AddChild(_car);
        _car.Place(new Transform3D(Basis.Identity, new Vector3(0, _car.S.WheelRadius + 0.02f, 0)));
        _steps = 0;
        _report.Add($"== {name}");
        try { await test(); }
        catch (Exception e) { Check(false, $"crashed: {e.Message}"); }
    }

    void Check(bool ok, string what)
    {
        _report.Add($"  {(ok ? "PASS" : "FAIL")}  {what}");
        _failed |= !ok;
    }

    async Task Step(int n, Action each = null)
    {
        for (int i = 0; i < n; i++)
        {
            each?.Invoke();
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            _steps++;
            var p = _car.GlobalPosition;
            if (!float.IsFinite(p.X + p.Y + p.Z)) throw new Exception("position is not finite");
        }
    }

    float Dt => 1f / Engine.PhysicsTicksPerSecond;
    float Speed => _car.ForwardSpeed;
    float Heading => Mathf.RadToDeg(Mathf.Atan2(-_car.GlobalBasis.Z.X, -_car.GlobalBasis.Z.Z));

    async Task AtRest()
    {
        await Step(720);
        var p0 = _car.GlobalPosition;
        float maxV = 0;
        await Step(1200, () => maxV = Mathf.Max(maxV, _car.LinearVelocity.Length()));
        float drift = p0.DistanceTo(_car.GlobalPosition);
        Check(drift < 0.02f, $"drift over 5 s {drift * 1000:F1} mm (< 20)");
        Check(maxV < 0.02f, $"max speed while parked {maxV:F4} m/s (< 0.02)");
        var s = _car.S;
        float rearShare = 0.5f + s.CgZ / s.Wheelbase;
        foreach (var w in _car.Wheels)
        {
            float expect = s.Mass * 9.81f * (w.Front ? 1 - rearShare : rearShare) / 2 / (w.Front ? s.SpringFront : s.SpringRear);
            Check(w.Contact && Mathf.Abs(w.Compression - expect) < 0.3f * expect,
                $"{(w.Front ? "front" : "rear")} {(w.Left ? "left" : "right")} static compression {w.Compression * 1000:F1} mm (expect {expect * 1000:F1} +-30%)");
        }
    }

    async Task Accel()
    {
        await Step(240);
        float h0 = Heading, t100 = -1, vmax = 0;
        var p0 = _car.GlobalPosition;
        int start = _steps;
        await Step(240 * 70, () =>
        {
            _car.In = new CarInput { Throttle = 1 };
            if (t100 < 0 && Speed >= 100 / 3.6f) t100 = (_steps - start) * Dt;
            vmax = Mathf.Max(vmax, Speed);
        });
        Check(t100 > 2.8f && t100 < 4.8f, $"0-100 km/h {t100:F2} s (2.8-4.8)");
        Check(vmax * 3.6f > 260 && vmax * 3.6f < 300, $"top speed {vmax * 3.6f:F0} km/h (260-300)");
        Check(Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(h0), Mathf.DegToRad(Heading))) < Mathf.DegToRad(1), $"heading change at full throttle {Heading - h0:F2} deg (< 1)");
        Check(_car.Gear == _car.S.Gears.Length, $"reached top gear ({_car.Gear})");
    }

    async Task Braking()
    {
        await Step(240);
        while (Speed < 100 / 3.6f) await Step(1, () => _car.In = new CarInput { Throttle = 1 });
        var p0 = _car.GlobalPosition;
        float h0 = Heading;
        int n = 0;
        while (Speed > 0.1f && n++ < 240 * 10) await Step(1, () => _car.In = new CarInput { Brake = 1 });
        float dist = p0.DistanceTo(_car.GlobalPosition);
        Check(dist > 24 && dist < 45, $"100-0 km/h in {dist:F1} m (24-45)");
        Check(Mathf.Abs(Heading - h0) < 3, $"heading change under braking {Heading - h0:F2} deg (< 3)");
    }

    async Task Lateral()
    {
        await Step(240);
        const float target = 30f;
        float steer = 0, maxG = 0;
        bool spun = false;
        await Step(240 * 14, () =>
        {
            float err = target - Speed;
            float t = _steps * Dt;
            if (t > 6) steer = Mathf.Min(1, (t - 6) / 8f);
            _car.In = new CarInput { Throttle = Mathf.Clamp(err * 0.5f, 0, 1), Brake = Mathf.Clamp(-err * 0.2f, 0, 1), Steer = steer };
            var v = _car.LinearVelocity;
            float beta = Mathf.RadToDeg(Mathf.Atan2(v.Dot(_car.GlobalBasis.X), Mathf.Max(0.1f, Speed)));
            if (Mathf.Abs(beta) > 12) spun = true;
            if (!spun) maxG = Mathf.Max(maxG, Mathf.Abs(v.Length() * _car.AngularVelocity.Y) / 9.81f);
        });
        Check(maxG > 1.2f && maxG < 2.2f, $"max lateral acceleration at ~108 km/h {maxG:F2} g (1.2-2.2)");
    }

    async Task AiLap()
    {
        var track = new TrackBuilder();
        AddChild(track);
        _car.Place(track.PoseAt(-6, 0.4f));
        var ai = new AiDriver { Car = _car, Track = track };
        AddChild(ai);
        var timer = new LapTimer { Car = _car, Track = track };
        AddChild(timer);
        await Step(2);
        timer.Reset();
        float lap = -1, worstOff = 0, vmax = 0;
        timer.LapDone += (_, t) => lap = t;
        int hint = 0;
        while (lap < 0 && _steps < 240 * 240)
            await Step(1, () =>
            {
                hint = track.Nearest(_car.GlobalPosition, hint, 40);
                float off = (_car.GlobalPosition - track.Centre[hint]).Dot(track.Left[hint]);
                if (Mathf.Abs(off) > worstOff && Mathf.Abs(off) > track.HalfWidth + 1.2f && worstOff <= track.HalfWidth + 1.2f)
                    _report.Add($"  first off-track at idx {hint}/{track.Centre.Length}: v {Speed * 3.6f:F0} km/h, plan {ai.Profile[hint] * 3.6f:F0}, curv {track.Curv[hint]:F4}, steer {_car.In.Steer:F2}, off {off:F1}");
                worstOff = Mathf.Max(worstOff, Mathf.Abs(off));
                vmax = Mathf.Max(vmax, _car.ForwardSpeed);
                if (_steps % 480 == 0) _report.Add($"  t {_steps / 240} s idx {hint} v {Speed * 3.6f:F0} plan {ai.Profile[hint] * 3.6f:F0} off {off:F1} steer {_car.In.Steer:F2} thr {_car.In.Throttle:F2} brk {_car.In.Brake:F2}");
            });
        ai.QueueFree(); timer.QueueFree(); track.QueueFree();
        for (int i = 0; i < track.Centre.Length; i += 50) _report.Add($"  plan idx {i}: {ai.Profile[i] * 3.6f:F0} km/h, curv {track.Curv[i]:F4}");
        Check(lap > 0, $"completed a lap: {LapTimer.Fmt(lap)} (top speed {vmax * 3.6f:F0} km/h)");
        Check(lap > 105 && lap < 160, "lap time in 1:45-2:40");
        Check(worstOff < track.HalfWidth + 1.2f, $"stayed on the road + kerbs: worst offset {worstOff:F1} m (< {track.HalfWidth + 1.2f:F1})");
    }

    async Task Reverse()
    {
        await Step(240);
        await Step(240 * 3, () => _car.In = new CarInput { Brake = 1 });
        Check(_car.Gear == -1, $"holding brake at a standstill selects reverse (gear {_car.Gear})");
        Check(Speed < -1, $"reversing at {Speed * 3.6f:F1} km/h (< -3.6)");
    }
}
