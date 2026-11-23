using Godot;

/// Speed, gear, rpm, lap timer.
public partial class Hud : CanvasLayer
{
    public Car Car;
    public LapTimer Laps;
    Label _left, _right;

    public override void _Ready()
    {
        _left = new Label { Position = new Vector2(40, 40) };
        _right = new Label { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -420, OffsetTop = 40, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var l in new[] { _left, _right })
        {
            l.AddThemeFontSizeOverride("font_size", 34);
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
            _right.Text = $"Lap {Laps.Lap}\n{LapTimer.Fmt(Laps.Current)}\nBest {LapTimer.Fmt(Laps.Best)}";
    }
}
