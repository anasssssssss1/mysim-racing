using Godot;

/// Surface materials. Uses the CC0 scanned textures from tools/fetch_assets.py when they are
/// present, otherwise procedural noise, so the game always runs.
public static class Materials
{
    public static Texture2D Tex(string id, string map)
    {
        string p = $"res://assets/cc0/{id}/{id}_{map}_2k.jpg";
        return ResourceLoader.Exists(p) ? GD.Load<Texture2D>(p) : null;
    }

    static NoiseTexture2D Noise(int seed, float freq, int size = 512, bool normal = false, float bump = 4f) => new()
    {
        Noise = new FastNoiseLite { Seed = seed, Frequency = freq, FractalOctaves = 5 },
        Width = size, Height = size, Seamless = true, AsNormalMap = normal, BumpStrength = bump,
    };

    static StandardMaterial3D Procedural(Color a, Color b, float rough, float freq, float scale, float bump, int seed)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, a);
        ramp.SetColor(1, b);
        var tex = Noise(seed, freq, 1024);
        tex.ColorRamp = ramp;
        return new StandardMaterial3D
        {
            AlbedoTexture = tex,
            NormalEnabled = true,
            NormalTexture = Noise(seed, freq, 1024, true, bump),
            Roughness = rough,
            Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * scale,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    }

    /// Scanned PBR set, world-space triplanar, `metres` per texture repeat.
    static Material Scanned(string id, float metres, Color tint, StandardMaterial3D fallback)
    {
        var d = Tex(id, "diff");
        if (d == null) return fallback;
        return new StandardMaterial3D
        {
            AlbedoTexture = d,
            AlbedoColor = tint,
            NormalEnabled = true,
            NormalTexture = Tex(id, "nor_gl"),
            RoughnessTexture = Tex(id, "rough"),
            Roughness = 1,
            Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One / metres,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    }

    static Material Road(string id, float rubber)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://src/Track/asphalt.gdshader") };
        var d = Tex(id, "diff");
        m.SetShaderParameter("use_tex", d != null);
        if (d != null)
        {
            m.SetShaderParameter("albedo_tex", d);
            m.SetShaderParameter("normal_tex", Tex(id, "nor_gl"));
            m.SetShaderParameter("rough_tex", Tex(id, "rough"));
        }
        m.SetShaderParameter("macro", Noise(21, 0.01f));
        m.SetShaderParameter("rubber", rubber);
        return m;
    }

    public static Material Asphalt() => Road("asphalt_track", 0.3f);
    public static Material PitLane() => Road("asphalt_pit_lane", 0f);
    public static Material Runoff() => Scanned("concrete_floor_worn_001", 4f, new Color(0.75f, 0.75f, 0.78f),
        Procedural(new Color(0.16f, 0.17f, 0.18f), new Color(0.27f, 0.28f, 0.29f), 0.92f, 0.06f, 0.08f, 3f, 12));
    public static Material Gravel() => Scanned("coral_gravel", 2.5f, Colors.White,
        Procedural(new Color(0.55f, 0.5f, 0.42f), new Color(0.75f, 0.7f, 0.6f), 0.95f, 0.3f, 0.3f, 6f, 15));
    public static Material Concrete() => Scanned("painted_concrete", 3f, Colors.White,
        Procedural(new Color(0.55f, 0.55f, 0.53f), new Color(0.72f, 0.72f, 0.7f), 0.85f, 0.05f, 0.25f, 2f, 14));
    public static Material Building() => Scanned("brushed_concrete", 4f, new Color(0.92f, 0.92f, 0.95f),
        Procedural(new Color(0.7f, 0.7f, 0.72f), new Color(0.85f, 0.85f, 0.86f), 0.7f, 0.05f, 0.1f, 1f, 16));
    public static Material Paint() => new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.92f, 0.9f), Roughness = 0.6f };
    public static StandardMaterial3D Flat(Color c, float rough = 0.6f, float metal = 0f) => new() { AlbedoColor = c, Roughness = rough, Metallic = metal };
    public static StandardMaterial3D Glow(Color c, float energy) => new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy };

    public static Material Ground(Rect2 maskRect, ImageTexture distMask)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://src/Track/ground.gdshader") };
        var g = Tex("grass_ground", "diff");
        var s = Tex("aerial_sand", "diff");
        m.SetShaderParameter("use_tex", g != null && s != null);
        m.SetShaderParameter("grass_tex", g);
        m.SetShaderParameter("grass_n", Tex("grass_ground", "nor_gl"));
        m.SetShaderParameter("sand_tex", s);
        m.SetShaderParameter("sand_n", Tex("aerial_sand", "nor_gl"));
        m.SetShaderParameter("dist_mask", distMask);
        m.SetShaderParameter("noise", Noise(31, 0.02f));
        m.SetShaderParameter("mask_rect", new Vector4(maskRect.Position.X, maskRect.Position.Y, maskRect.Size.X, 0));
        return m;
    }

    public static Material Kerb()
    {
        var img = Image.CreateEmpty(2, 1, false, Image.Format.Rgb8);
        img.SetPixel(0, 0, new Color(0.78f, 0.06f, 0.05f));
        img.SetPixel(1, 0, new Color(0.93f, 0.93f, 0.9f));
        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(img),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Uv1Scale = new Vector3(1f / 3f, 1, 1), // one red + one white block = 3 m
            Roughness = 0.55f,
        };
    }

    /// Chain-link fence: alpha-scissored diamond mesh, generated.
    public static Material Fence()
    {
        const int S = 64;
        var img = Image.CreateEmpty(S, S, true, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int i = 0; i < S; i++)
            for (int w = -1; w <= 1; w++)
            {
                img.SetPixel((i + w + S) % S, i, new Color(0.7f, 0.72f, 0.74f, 1));
                img.SetPixel((S - 1 - i + w + S) % S, i, new Color(0.7f, 0.72f, 0.74f, 1));
            }
        img.GenerateMipmaps();
        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(img),
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.5f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Metallic = 0.8f, Roughness = 0.4f,
            Uv1Scale = new Vector3(1f / 0.12f, 1f / 0.12f, 1), // 12 cm mesh, UVs are in metres
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    }

    /// Black/white chequer for the start line.
    public static Material Chequer()
    {
        var img = Image.CreateEmpty(2, 2, false, Image.Format.Rgb8);
        img.SetPixel(0, 0, Colors.White); img.SetPixel(1, 1, Colors.White);
        img.SetPixel(1, 0, new Color(0.05f, 0.05f, 0.05f)); img.SetPixel(0, 1, new Color(0.05f, 0.05f, 0.05f));
        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(img),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Uv1Scale = new Vector3(1f / 0.6f, 2, 1),
            Roughness = 0.6f,
        };
    }
}
