using Godot;
using System.Linq;

/// Main menu and lobby. Esc toggles it during a race.
public partial class Menu : CanvasLayer
{
    public NetSession Net;
    public Race Race;
    PanelContainer _panel;
    Label _status, _players;
    LineEdit _code;
    Button _start;
    SpinBox _laps;

    public override void _Ready()
    {
        _panel = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f, OffsetLeft = -280, OffsetRight = 280, OffsetTop = -260, OffsetBottom = 260 };
        AddChild(_panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        var margin = new MarginContainer();
        _panel.AddChild(margin);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 24);
        margin.AddChild(box);

        var title = new Label { Text = "MySim Racing", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 40);
        box.AddChild(title);
        box.AddChild(Btn("Practice", () => { Race.StartPractice(); Visible = false; }));

        var lapsRow = new HBoxContainer();
        lapsRow.AddChild(new Label { Text = "Laps" });
        _laps = new SpinBox { MinValue = 1, MaxValue = 50, Value = Race.Laps };
        lapsRow.AddChild(_laps);
        box.AddChild(lapsRow);

        box.AddChild(Btn("Host online race", () => { if (Net.Host() == Error.Ok) Refresh(); }));
        _code = new LineEdit { PlaceholderText = "Join code or IP address" };
        box.AddChild(_code);
        box.AddChild(Btn("Join", () => Net.Join(_code.Text)));
        _start = Btn("Start race", () => { Race.Laps = (int)_laps.Value; Race.HostStart(); });
        box.AddChild(_start);
        _players = new Label();
        box.AddChild(_players);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(480, 0) };
        box.AddChild(_status);
        box.AddChild(Btn("Quit", () => GetTree().Quit()));

        Net.Status += s => _status.Text = s;
        Net.PlayerJoined += _ => Refresh();
        Net.PlayerLeft += _ => Refresh();
        Race.Started += () => Visible = false;
        Refresh();
    }

    Button Btn(string text, System.Action onPress)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 44) };
        b.Pressed += onPress;
        return b;
    }

    void Refresh()
    {
        bool online = Net.Active;
        _start.Visible = online && Multiplayer.IsServer();
        _players.Text = online ? $"Players: {Multiplayer.GetPeers().Length + 1}" : "";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { Visible = !Visible; Refresh(); }
    }
}
