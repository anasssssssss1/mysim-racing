using Godot;

/// Synthesised engine sound (no samples): firing-frequency harmonics with a little phase
/// jitter, plus intake noise on throttle and tyre squeal from slip.
public partial class EngineAudio : AudioStreamPlayer3D
{
    Car _car;
    AudioStreamGeneratorPlayback _pb;
    double _phase, _squealPhase;
    float _rpm, _load, _noise, _squeal;
    readonly RandomNumberGenerator _rng = new();
    const float Rate = 44100f;

    public static EngineAudio For(Car car)
    {
        var a = new EngineAudio { _car = car, Name = "Engine", MaxDistance = 400, UnitSize = 12, VolumeDb = -6 };
        a.Stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = 0.1f };
        car.AddChild(a);
        return a;
    }

    public override void _Ready()
    {
        Play();
        _pb = (AudioStreamGeneratorPlayback)GetStreamPlayback();
    }

    public override void _Process(double delta)
    {
        if (_pb == null || !_car.Ready) return;
        float targetLoad = Mathf.Max(_car.In.Throttle, 0.15f);
        float slip = 0;
        foreach (var w in _car.Wheels)
            if (w.Contact) slip = Mathf.Max(slip, Mathf.Max(Mathf.Abs(w.Kappa) / _car.S.Tyre.KappaPeak, Mathf.Abs(w.Alpha) / _car.S.Tyre.AlphaPeak));
        float targetSqueal = Mathf.Clamp((slip - 0.9f) * 1.5f, 0, 1) * Mathf.Clamp(Mathf.Abs(_car.ForwardSpeed) / 5f, 0, 1);

        int frames = _pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            _rpm = Mathf.Lerp(_rpm, _car.Rpm, 0.0015f);
            _load = Mathf.Lerp(_load, targetLoad, 0.0008f);
            _squeal = Mathf.Lerp(_squeal, targetSqueal, 0.001f);
            float fire = _rpm / 60f * 3f; // 6 cylinders, 4-stroke
            _phase += fire / Rate * (1 + 0.004f * _rng.Randfn());
            double p = _phase * Mathf.Tau;
            float s = 0.55f * (float)System.Math.Sin(p)
                    + 0.30f * (float)System.Math.Sin(2 * p + 0.3)
                    + 0.18f * (float)System.Math.Sin(3 * p + 1.1)
                    + 0.25f * (float)System.Math.Sin(0.5 * p);
            s = Mathf.Tanh(s * (1.2f + 1.5f * _load)); // harder, raspier on load
            _noise = Mathf.Lerp(_noise, _rng.Randf() * 2 - 1, 0.35f);
            s = s * (0.35f + 0.45f * _load) + _noise * 0.12f * _load;
            _squealPhase += (900 + 150 * _rng.Randfn() * 0.05f) / Rate;
            s += _squeal * 0.25f * (float)System.Math.Sin(_squealPhase * Mathf.Tau) * (0.7f + 0.3f * _noise);
            _pb.PushFrame(new Vector2(s, s) * 0.5f);
        }
    }
}
