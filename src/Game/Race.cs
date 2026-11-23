using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// Race flow. Offline practice, or online where the host starts the race and keeps the standings.
/// Each peer drives its own car; laps are timed locally and reported to the host.
public partial class Race : Node3D
{
    public TrackBuilder Track;
    public ChaseCamera Cam;
    public Hud Hud;
    public int Laps = 5;
    public enum Phase { Menu, Countdown, Racing, Finished }
    public Phase State = Phase.Menu;
    public float Countdown;
    public string Standings = "";
    public event Action Started;
    public Car Local { get; private set; }

    Node3D _cars;
    LapTimer _timer;
    PlayerController _pc;
    readonly Dictionary<long, (int lap, float best, float total, bool done)> _table = new();

    public override void _Ready()
    {
        _cars = new Node3D { Name = "Cars" };
        AddChild(_cars);
        Multiplayer.PeerDisconnected += id => _cars.GetNodeOrNull($"Car_{id}")?.QueueFree();
    }

    public void StartPractice() => Begin(new[] { (long)Multiplayer.GetUniqueId() }, 0);

    /// Host only: start a race with everyone connected.
    public void HostStart()
    {
        if (!Multiplayer.IsServer()) return;
        var ids = Multiplayer.GetPeers().Select(p => (long)p).Prepend(1L).ToArray();
        Rpc(MethodName.Begin, ids, Laps);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Begin(long[] ids, int laps)
    {
        foreach (var c in _cars.GetChildren()) { _cars.RemoveChild(c); c.QueueFree(); }
        _timer?.QueueFree();
        _pc?.QueueFree();
        Laps = laps;
        _table.Clear();
        long me = Multiplayer.GetUniqueId();
        for (int i = 0; i < ids.Length; i++) Spawn(ids[i], i, ids[i] == me);
        State = ids.Length > 1 ? Phase.Countdown : Phase.Racing;
        Countdown = ids.Length > 1 ? 3f : 0f;
        Standings = "";
        Started?.Invoke();
    }

    void Spawn(long id, int slot, bool local)
    {
        var car = new Car { Name = $"Car_{id}" };
        _cars.AddChild(car);
        car.SetMultiplayerAuthority((int)id);
        car.Place(Track.PoseAt(-(6 + 5 * slot), 0.4f, slot % 2 == 0 ? 3f : -3f));
        CarVisual.For(car);
        EngineAudio.For(car);
        car.AddChild(new NetCarSync { Name = "Sync" });
        if (!local) { car.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic; car.Freeze = true; return; }

        Local = car;
        _pc = new PlayerController { Car = car };
        AddChild(_pc);
        _timer = new LapTimer { Car = car, Track = Track, Name = "LapTimer" };
        _timer.LapDone += OnLap;
        AddChild(_timer);
        Cam.Target = car;
        Cam.Snap();
        Hud.Car = car;
        Hud.Laps = _timer;
        Hud.Race = this;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (State == Phase.Countdown)
        {
            Countdown -= (float)delta;
            if (Countdown <= 0) { State = Phase.Racing; if (_timer != null) _timer.Reset(); }
        }
        if (_pc != null) _pc.SetPhysicsProcess(State != Phase.Countdown);
        if (State == Phase.Countdown && Local != null) Local.In = new CarInput { Brake = 1 };
        if (_timer != null) _timer.SetPhysicsProcess(State == Phase.Racing);
    }

    void OnLap(int lap, float time)
    {
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer) RpcId(1, MethodName.ReportLap, lap, time);
        else ReportLap(lap, time);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ReportLap(int lap, float time)
    {
        if (!Multiplayer.IsServer()) return;
        long id = Multiplayer.GetRemoteSenderId();
        if (id == 0) id = 1;
        _table.TryGetValue(id, out var e);
        e = (lap, e.best <= 0 ? time : Mathf.Min(e.best, time), e.total + time, lap >= Laps);
        _table[id] = e;
        var order = _table.OrderByDescending(x => x.Value.lap).ThenBy(x => x.Value.total).ToList();
        string text = string.Join("\n", order.Select((x, i) =>
            $"{i + 1}. P{x.Key % 1000:000}  lap {x.Value.lap}{(x.Value.done ? " FIN" : "")}  best {LapTimer.Fmt(x.Value.best)}"));
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer) Rpc(MethodName.SetStandings, text);
        else SetStandings(text);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void SetStandings(string text)
    {
        Standings = text;
        if (_timer != null && _timer.Lap > Laps && State == Phase.Racing && Multiplayer.MultiplayerPeer is ENetMultiplayerPeer) State = Phase.Finished;
    }
}
