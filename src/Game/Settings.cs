using Godot;

/// Graphics quality presets, saved to user://settings.cfg. Phones default to Low.
public static class Settings
{
    public enum Quality { Low, Medium, High, Ultra }
    const string File = "user://settings.cfg";
    public static Quality Current { get; private set; } = OS.HasFeature("mobile") ? Quality.Low : Quality.High;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(File) == Error.Ok) Current = (Quality)(int)cfg.GetValue("graphics", "quality", (int)Current);
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("graphics", "quality", (int)Current);
        cfg.Save(File);
    }

    /// Apply to the running game: viewport AA/scaling, environment effects, shadows, sun.
    public static void Apply(Quality q, Viewport vp, Environment env, DirectionalLight3D sun)
    {
        Current = q;
        vp.Msaa3D = q >= Quality.High ? Viewport.Msaa.Msaa4X : q == Quality.Medium ? Viewport.Msaa.Msaa2X : Viewport.Msaa.Disabled;
        vp.UseTaa = q >= Quality.High;
        vp.Scaling3DMode = q == Quality.Low ? Viewport.Scaling3DModeEnum.Fsr2 : Viewport.Scaling3DModeEnum.Bilinear;
        vp.Scaling3DScale = q == Quality.Low ? 0.67f : 1f;
        env.SsaoEnabled = q >= Quality.Medium;
        env.SsilEnabled = q == Quality.Ultra;
        env.SsrEnabled = q == Quality.Ultra;
        env.GlowEnabled = q >= Quality.Medium;
        env.SdfgiEnabled = false; // ponytail: SDFGI off everywhere until we profile it on real GPUs
        sun.DirectionalShadowMaxDistance = q switch { Quality.Low => 150, Quality.Medium => 300, Quality.High => 450, _ => 700 };
        sun.ShadowEnabled = true;
        RenderingServer.DirectionalShadowAtlasSetSize(q switch { Quality.Low => 2048, Quality.Medium => 4096, _ => 8192 }, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(q >= Quality.High ? RenderingServer.ShadowQuality.SoftHigh : RenderingServer.ShadowQuality.SoftLow);
    }

    /// Prop density multiplier for TrackDressing (crowds, palms, city).
    public static float Density => Current switch { Quality.Low => 0.4f, Quality.Medium => 0.7f, _ => 1f };
}
