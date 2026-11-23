using Godot;

/// Procedural surface materials (no external textures): noise albedo + noise normal, world triplanar.
public static class Materials
{
    static StandardMaterial3D Surface(Color a, Color b, float rough, float freq, float scale, float bump, int seed)
    {
        var noise = new FastNoiseLite { Seed = seed, Frequency = freq, FractalOctaves = 5 };
        var ramp = new Gradient();
        ramp.SetColor(0, a);
        ramp.SetColor(1, b);
        return new StandardMaterial3D
        {
            AlbedoTexture = new NoiseTexture2D { Noise = noise, Width = 1024, Height = 1024, Seamless = true, ColorRamp = ramp },
            NormalEnabled = true,
            NormalTexture = new NoiseTexture2D { Noise = noise, Width = 1024, Height = 1024, Seamless = true, AsNormalMap = true, BumpStrength = bump },
            Roughness = rough,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = Vector3.One * scale,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    }

    public static Material Asphalt() => Surface(new Color(0.085f, 0.087f, 0.09f), new Color(0.17f, 0.17f, 0.175f), 0.88f, 0.08f, 0.12f, 6f, 11);
    public static Material Runoff() => Surface(new Color(0.16f, 0.17f, 0.18f), new Color(0.27f, 0.28f, 0.29f), 0.92f, 0.06f, 0.08f, 3f, 12);
    public static Material Grass() => Surface(new Color(0.16f, 0.25f, 0.08f), new Color(0.32f, 0.42f, 0.16f), 0.95f, 0.02f, 0.02f, 4f, 13);
    public static Material Paint() => new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.92f, 0.9f), Roughness = 0.6f };

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
}
