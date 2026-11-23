using Godot;
using System;
using System.Collections.Generic;

/// Owner simulates its own car and sends 60 Hz snapshots; other peers show it 100 ms in the past,
/// interpolating between snapshots (short extrapolation if packets are late).
public partial class NetCarSync : Node
{
    const double Delay = 0.1, MaxExtrapolate = 0.25;
    Car _car;
    int _tick;
    double _offset = double.MaxValue;
    readonly List<Snap> _buf = new();

    struct Snap
    {
        public double T;
        public Vector3 P, V;
        public Quaternion Q;
        public float[] Susp, Spin;
        public float Steer, Rpm;
        public int Gear;
    }

    public override void _Ready() => _car = GetParent<Car>();

    static bool Online(Node n) => n.Multiplayer.MultiplayerPeer is ENetMultiplayerPeer;

    public override void _PhysicsProcess(double delta)
    {
        if (!Online(this) || !_car.Ready) return;
        if (IsMultiplayerAuthority()) { if (++_tick % 4 == 0) Rpc(MethodName.State, Pack()); } // 240 Hz physics -> 60 Hz
        else Apply();
    }

    byte[] Pack()
    {
        var b = new StreamPeerBuffer();
        b.PutDouble(Time.GetTicksUsec() / 1e6);
        var t = _car.GlobalTransform;
        var q = t.Basis.GetRotationQuaternion();
        foreach (float f in new[] { t.Origin.X, t.Origin.Y, t.Origin.Z, q.X, q.Y, q.Z, q.W })
            b.PutFloat(f);
        var v = _car.LinearVelocity;
        b.PutFloat(v.X); b.PutFloat(v.Y); b.PutFloat(v.Z);
        foreach (var w in _car.Wheels) { b.PutFloat(w.SuspLength); b.PutFloat(w.SpinAngle); }
        b.PutFloat(_car.Wheels[0].Steer);
        b.PutFloat(_car.Rpm);
        b.Put8((sbyte)_car.Gear);
        return b.DataArray;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    void State(byte[] data)
    {
        if (data.Length < 8 + 4 * 19 + 1) return;
        var b = new StreamPeerBuffer { DataArray = data };
        var s = new Snap { T = b.GetDouble() };
        s.P = new Vector3(b.GetFloat(), b.GetFloat(), b.GetFloat());
        s.Q = new Quaternion(b.GetFloat(), b.GetFloat(), b.GetFloat(), b.GetFloat()).Normalized();
        s.V = new Vector3(b.GetFloat(), b.GetFloat(), b.GetFloat());
        s.Susp = new float[4]; s.Spin = new float[4];
        for (int i = 0; i < 4; i++) { s.Susp[i] = b.GetFloat(); s.Spin[i] = b.GetFloat(); }
        s.Steer = b.GetFloat();
        s.Rpm = b.GetFloat();
        s.Gear = b.Get8();
        if (!s.P.IsFinite() || !s.V.IsFinite()) return;

        double now = Time.GetTicksUsec() / 1e6;
        _offset = Math.Min(_offset + 0.0005, now - s.T); // clock offset + best-case latency, relaxes slowly
        if (_buf.Count > 0 && s.T <= _buf[^1].T) return;
        _buf.Add(s);
        if (_buf.Count > 64) _buf.RemoveAt(0);
    }

    void Apply()
    {
        if (_buf.Count == 0) return;
        double t = Time.GetTicksUsec() / 1e6 - _offset - Delay;
        int i = _buf.FindLastIndex(x => x.T <= t);
        Snap a, b2;
        float k;
        if (i < 0) { a = b2 = _buf[0]; k = 0; }
        else if (i == _buf.Count - 1)
        {
            a = _buf[i];
            double ahead = Math.Min(t - a.T, MaxExtrapolate);
            Set(a, a, 0, a.P + a.V * (float)ahead);
            return;
        }
        else { a = _buf[i]; b2 = _buf[i + 1]; k = (float)((t - a.T) / (b2.T - a.T)); }
        Set(a, b2, k, a.P.Lerp(b2.P, k));
    }

    void Set(Snap a, Snap b, float k, Vector3 pos)
    {
        _car.GlobalTransform = new Transform3D(new Basis(a.Q.Slerp(b.Q, k)), pos);
        for (int i = 0; i < 4; i++)
        {
            var w = _car.Wheels[i];
            w.SuspLength = Mathf.Lerp(a.Susp[i], b.Susp[i], k);
            w.SpinAngle = Mathf.Lerp(a.Spin[i], b.Spin[i], k);
            if (w.Front) w.Steer = Mathf.Lerp(a.Steer, b.Steer, k);
        }
        _car.Rpm = a.Rpm;
        _car.Gear = a.Gear;
    }
}
