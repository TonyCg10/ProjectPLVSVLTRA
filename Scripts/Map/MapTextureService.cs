using Godot;
using System;
using Engine.Services;

namespace PLVSVLTRA.Map;

/// <summary>
/// Converts pure C# data from MapDataService into Godot GPU textures.
/// Generates lookup textures for shaders and loads world_data into Texture2DArray.
/// Lives exclusively in the Godot layer.
/// </summary>
public static class MapTextureService
{
    public static ImageTexture CountryLookup { get; private set; }
    public static ImageTexture StateLookup { get; private set; }
    public static Texture2DArray WorldDataTex { get; private set; }

    /// <summary>
    /// Generates country and state lookup textures from MapDataService palettes
    /// and injects them into the given shader material.
    /// </summary>
    public static void SetupShaderLookups(ShaderMaterial material)
    {
        int totalNodes = MapDataService.Nodes.Length;
        int texSize = 4096;
        int maxNodes = texSize * texSize;
        if (totalNodes > maxNodes)
            throw new InvalidOperationException($"map_nodes.bin exceeds lookup capacity ({totalNodes} > {maxNodes}).");

        // Generate palettes (pure C#) and convert to byte buffers
        float[] countryPalette = MapDataService.GeneratePalette(MapDataService.CountryCatalog.Length, 123);
        float[] statePalette = MapDataService.GeneratePalette(MapDataService.StateCatalog.Length, 456);

        byte[] countryData = new byte[texSize * texSize * 3];
        byte[] stateData = new byte[texSize * texSize * 3];

        for (int i = 0; i < totalNodes; i++)
        {
            int cIdx = MapDataService.Nodes[i].CountryIdx;
            int sIdx = MapDataService.Nodes[i].StateIdx;
            if (cIdx >= MapDataService.CountryCatalog.Length) cIdx = 0;
            if (sIdx >= MapDataService.StateCatalog.Length) sIdx = 0;

            int p = i * 3;

            // Country pixel
            countryData[p]     = (byte)(countryPalette[cIdx * 3] * 255);
            countryData[p + 1] = (byte)(countryPalette[cIdx * 3 + 1] * 255);
            countryData[p + 2] = (byte)(countryPalette[cIdx * 3 + 2] * 255);

            // State pixel
            stateData[p]     = (byte)(statePalette[sIdx * 3] * 255);
            stateData[p + 1] = (byte)(statePalette[sIdx * 3 + 1] * 255);
            stateData[p + 2] = (byte)(statePalette[sIdx * 3 + 2] * 255);
        }

        // Create Godot ImageTextures
        Image imgCountry = Image.CreateFromData(texSize, texSize, false, Image.Format.Rgb8, countryData);
        Image imgState = Image.CreateFromData(texSize, texSize, false, Image.Format.Rgb8, stateData);

        CountryLookup = ImageTexture.CreateFromImage(imgCountry);
        StateLookup = ImageTexture.CreateFromImage(imgState);

        // Inject into shader
        material.SetShaderParameter("country_lookup", CountryLookup);
        material.SetShaderParameter("state_lookup", StateLookup);

        if (WorldDataTex != null)
            material.SetShaderParameter("world_data_tex", WorldDataTex);

        // Free temp CPU images
        imgCountry.Dispose();
        imgState.Dispose();

        GD.Print($"[MapTextureService] Shader updated with {totalNodes} nodes.");
    }

    /// <summary>
    /// Scans the ID map image to compute country bounds via MapDataService.
    /// Extracts raw bytes from Godot Image and passes them to the pure C# scanner.
    /// </summary>
    public static void ScanBoundsFromImage(Image idMapImage)
    {
        int w = idMapImage.GetWidth();
        int h = idMapImage.GetHeight();

        // Convert to RGBA8 if needed
        if (idMapImage.GetFormat() != Image.Format.Rgba8)
            idMapImage.Convert(Image.Format.Rgba8);

        byte[] pixels = idMapImage.GetData();
        MapDataService.ScanCountryBounds(pixels, w, h);
    }

    /// <summary>
    /// Loads world_data.bin into a Texture2DArray for GPU access.
    /// </summary>
    public static void LoadWorldDataTexture(string dataFolder)
    {
        var result = MapDataService.LoadWorldDataRaw(dataFolder);
        if (result == null) return;

        var (data, w, h, numLayers) = result.Value;
        int layerSize = w * h * 4;

        var images = new Godot.Collections.Array<Image>();
        for (int i = 0; i < numLayers; i++)
        {
            byte[] layerBytes = new byte[layerSize];
            Array.Copy(data, i * layerSize, layerBytes, 0, layerSize);

            Image img = Image.CreateFromData(w, h, false, Image.Format.Rg16, layerBytes);
            images.Add(img);
        }

        var texArray = new Texture2DArray();
        texArray.CreateFromImages(images);
        WorldDataTex = texArray;

        GD.Print($"[MapTextureService] Texture2DArray created: {w}x{h} with {numLayers} layers.");
    }

    /// <summary>
    /// Returns the country palette color for a given index (for UI/selection highlighting).
    /// </summary>
    public static Color GetCountryColor(int countryIdx)
    {
        float[] palette = MapDataService.GeneratePalette(MapDataService.CountryCatalog.Length, 123);
        if (countryIdx < 0 || countryIdx * 3 + 2 >= palette.Length)
            return Colors.White;
        return new Color(palette[countryIdx * 3], palette[countryIdx * 3 + 1], palette[countryIdx * 3 + 2]);
    }
}
