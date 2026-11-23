using Godot;
using System;
using System.Text;

/// Peer-to-peer session: one player hosts (listen server), others join by code or IP.
/// The host opens its router port with UPnP when the router allows it; LAN play needs nothing.
public partial class NetSession : Node
{
    public const int Port = 24680, MaxPlayers = 12;
    public event Action<long> PlayerJoined, PlayerLeft;
    public event Action<string> Status;
    public string JoinCode { get; private set; } = "";
    public bool Active => Multiplayer.MultiplayerPeer is ENetMultiplayerPeer;

    Upnp _upnp;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += id => PlayerJoined?.Invoke(id);
        Multiplayer.PeerDisconnected += id => PlayerLeft?.Invoke(id);
        Multiplayer.ConnectedToServer += () => Status?.Invoke("Connected");
        Multiplayer.ConnectionFailed += () => { Status?.Invoke("Could not connect"); Leave(); };
        Multiplayer.ServerDisconnected += () => { Status?.Invoke("Host left"); Leave(); };
    }

    public Error Host()
    {
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(Port, MaxPlayers);
        if (err != Error.Ok) { Status?.Invoke($"Could not host: {err}"); return err; }
        Multiplayer.MultiplayerPeer = peer;
        Status?.Invoke("Hosting on LAN, opening router port...");
        var t = new System.Threading.Thread(OpenRouterPort) { IsBackground = true };
        t.Start();
        return Error.Ok;
    }

    void OpenRouterPort()
    {
        string lan = LanAddress();
        _upnp = new Upnp();
        string msg;
        if (_upnp.Discover(2000, 2) == (int)Upnp.UpnpResult.Success && _upnp.GetGateway() is { } gw && gw.IsValidGateway()
            && _upnp.AddPortMapping(Port, 0, "MySim Racing", "UDP", 0) == (int)Upnp.UpnpResult.Success)
        {
            string ext = _upnp.QueryExternalAddress();
            JoinCode = Encode(ext);
            msg = $"Hosting. Internet code: {JoinCode}  LAN: {lan}";
        }
        else
        {
            _upnp = null;
            JoinCode = Encode(lan);
            msg = $"Hosting on LAN only (router refused UPnP). Code: {JoinCode}  LAN: {lan}";
        }
        CallDeferred(MethodName.EmitStatus, msg);
    }

    void EmitStatus(string s) => Status?.Invoke(s);

    public Error Join(string codeOrIp)
    {
        string ip = codeOrIp.Contains('.') ? codeOrIp.Trim() : Decode(codeOrIp);
        if (ip == null) { Status?.Invoke("Bad code"); return Error.InvalidParameter; }
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(ip, Port);
        if (err != Error.Ok) { Status?.Invoke($"Could not join: {err}"); return err; }
        Multiplayer.MultiplayerPeer = peer;
        Status?.Invoke($"Connecting to {ip}...");
        return Error.Ok;
    }

    public void Leave()
    {
        if (_upnp != null) { _upnp.DeletePortMapping(Port, "UDP"); _upnp = null; }
        Multiplayer.MultiplayerPeer?.Close();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
    }

    public override void _ExitTree() => Leave();

    static string LanAddress()
    {
        foreach (var a in IP.GetLocalAddresses())
            if (a.StartsWith("192.168.") || a.StartsWith("10.") || (a.StartsWith("172.") && int.TryParse(a.Split('.')[1], out int b) && b >= 16 && b <= 31))
                return a;
        return "127.0.0.1";
    }

    // Join code: the IPv4 address as 7 characters of Crockford base32 (no I, L, O, U).
    const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Encode(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length != 4) return ip;
        ulong v = 0;
        foreach (var p in parts) v = (v << 8) | byte.Parse(p);
        var sb = new StringBuilder();
        for (int i = 0; i < 7; i++) { sb.Insert(0, Alphabet[(int)(v & 31)]); v >>= 5; }
        return sb.ToString();
    }

    public static string Decode(string code)
    {
        code = code.Trim().ToUpperInvariant().Replace("O", "0").Replace("I", "1").Replace("L", "1");
        if (code.Length != 7) return null;
        ulong v = 0;
        foreach (char c in code)
        {
            int k = Alphabet.IndexOf(c);
            if (k < 0) return null;
            v = (v << 5) | (uint)k;
        }
        if (v > uint.MaxValue) return null;
        return $"{(v >> 24) & 255}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";
    }
}
