using Godot;

/// Speed, gear, rpm, lap timer, countdown and standings.
public partial class Hud : CanvasLayer
{
    public Car Car;
    public LapTimer Laps;
    public Race Race;
    Label _left, _right, _centre, _table;

    public override void _Ready()
    {
        _left = new Label { Position = new Vector2(40, 40) };
        _right = new Label { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -460, OffsetRight = -40, OffsetTop = 40, HorizontalAlignment = HorizontalAlignment.Right };
        _centre = new Label { AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -300, OffsetRight = 300, OffsetTop = 200, HorizontalAlignment = HorizontalAlignment.Center };
        _table = new Label { Position = new Vector2(40, 260) };
        foreach (var l in new[] { _left, _right, _centre, _table })
        {
            l.AddThemeFontSizeOverride("font_size", l == _centre ? 96 : l == _table ? 24 : 34);
            l.AddThemeColorOverride("font_outline_color", Colors.Black);
            l.AddThemeConstantOverride("outline_size", 8);
            AddChild(l);
        }
    }

    public override void _Process(double delta)
    {
        if (Car == null) return;
        string gear = Car.Gear < 0 ? "R" : Car.Gear == 0 ? "N" : Car.Gear.ToString();
        _left.Text = $"{Mathf.Abs(Car.ForwardSpeed) * 3.6f:F0} km/h\nGear {gear}\n{Car.Rpm:F0} rpm";
        if (Laps != null)
            _right.Text = $"Lap {Mathf.Min(Laps.Lap, Race?.Laps ?? 99)}/{Race?.Laps}\n{LapTimer.Fmt(Laps.Current)}\nBest {LapTimer.Fmt(Laps.Best)}";
        _centre.Text = Race == null ? "" : Race.State switch
        {
            Race.Phase.Countdown => Mathf.CeilToInt(Race.Countdown).ToString(),
            Race.Phase.Finished => "FINISHED",
            _ => "",
        };
        _table.Text = Race?.Standings ?? "";
    }
}
