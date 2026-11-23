using System.Linq;
using Godot;

/// On-screen pedals and steering for touch screens. Each button drives an input action,
/// so the car code does not know or care where input comes from.
public partial class TouchControls : CanvasLayer
{
    public override void _Ready()
    {
        if (!DisplayServer.IsTouchscreenAvailable() && !OS.GetCmdlineUserArgs().Contains("--touch")) { QueueFree(); return; }
        GetViewport().SizeChanged += Layout;
        Layout();
    }

    void Layout()
    {
        foreach (var c in GetChildren()) c.QueueFree();
        var size = GetViewport().GetVisibleRect().Size;
        float u = size.Y / 7f; // button unit scales with screen height
        Add("steer_left", new Vector2(u * 0.4f, size.Y - u * 2.2f), new Vector2(u * 1.8f, u * 1.8f), "<");
        Add("steer_right", new Vector2(u * 2.5f, size.Y - u * 2.2f), new Vector2(u * 1.8f, u * 1.8f), ">");
        Add("brake", new Vector2(size.X - u * 4.4f, size.Y - u * 2.2f), new Vector2(u * 1.8f, u * 1.8f), "B");
        Add("throttle", new Vector2(size.X - u * 2.3f, size.Y - u * 3.2f), new Vector2(u * 1.9f, u * 2.8f), "GO");
        Add("handbrake", new Vector2(size.X - u * 4.4f, size.Y - u * 3.6f), new Vector2(u * 1.2f, u * 1.0f), "HB");
        Add("camera", new Vector2(size.X - u * 1.3f, u * 0.3f), new Vector2(u * 1.0f, u * 0.8f), "CAM");
    }

    void Add(string action, Vector2 pos, Vector2 size, string label)
    {
        var img = Image.CreateEmpty((int)size.X, (int)size.Y, false, Image.Format.Rgba8);
        img.Fill(new Color(1, 1, 1, 0.18f));
        var b = new TouchScreenButton
        {
            Action = action,
            TextureNormal = ImageTexture.CreateFromImage(img),
            Position = pos,
            PassbyPress = true,
        };
        var pressed = (Image)img.Duplicate();
        pressed.Fill(new Color(1, 1, 1, 0.38f));
        b.TexturePressed = ImageTexture.CreateFromImage(pressed);
        var l = new Label { Text = label, Size = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", (int)(size.Y * 0.3f));
        b.AddChild(l);
        AddChild(b);
    }
}
