using System;
using System.Collections.Generic;
using System.IO;

namespace Engine.Services;

/// <summary>
/// Pure C# service for map data: node binary, catalogs, country bounds.
/// No Godot dependencies — all GPU texture generation lives in the Godot-side MapTextureService.
/// </summary>
public static class MapDataService
{
    public struct NodeData
    {
        public ushort StateIdx;
        public ushort CountryIdx;
    }

    public static NodeData[] Nodes = Array.Empty<NodeData>();
    public static string[] StateCatalog = Array.Empty<string>();
    public static string[] CountryCatalog = Array.Empty<string>();

    // Country bounds in UV space: (minU, minV, width, height)
    private static readonly Dictionary<int, (float MinU, float MinV, float W, float H)> _countryBoundsCache = new();
    private static readonly Dictionary<int, (float MinU, float MinV, float W, float H)> _stateBoundsCache = new();

    /// <summary>
    /// Returns the UV bounding box for a country, or full map if not cached.
    /// </summary>
    public static (float MinU, float MinV, float W, float H) GetCountryBounds(int countryIdx)
    {
        if (_countryBoundsCache.TryGetValue(countryIdx, out var bounds)) return bounds;
        return (0f, 0f, 1f, 1f);
    }

    /// <summary>
    /// Returns the UV bounding box for a state, or parent country bounds if not cached.
    /// </summary>
    public static (float MinU, float MinV, float W, float H) GetStateBounds(int stateIdx, int countryIdx)
    {
        if (_stateBoundsCache.TryGetValue(stateIdx, out var bounds)) return bounds;
        return GetCountryBounds(countryIdx);
    }

    /// <summary>
    /// Scans raw RGBA pixel data from the ID map to compute country AND state bounding boxes.
    /// </summary>
    public static void ScanCountryBounds(byte[] pixels, int width, int height)
    {
        if (_countryBoundsCache.Count > 0) return; // Already scanned

        GameLogger.Info("MapDataService", "Scanning country + state bounds for regional focus...");

        var cMinU = new Dictionary<int, float>();
        var cMaxU = new Dictionary<int, float>();
        var cMinV = new Dictionary<int, float>();
        var cMaxV = new Dictionary<int, float>();

        var sMinU = new Dictionary<int, float>();
        var sMaxU = new Dictionary<int, float>();
        var sMinV = new Dictionary<int, float>();
        var sMaxV = new Dictionary<int, float>();

        // Sample every 4th pixel for speed
        for (int y = 0; y < height; y += 4)
        {
            for (int x = 0; x < width; x += 4)
            {
                int idx = (y * width + x) * 4; // RGBA8 = 4 bytes per pixel
                if (idx + 2 >= pixels.Length) continue;

                int r = pixels[idx];
                int g = pixels[idx + 1];
                int b = pixels[idx + 2];
                int nodeId = r + (g * 256) + (b * 65536);

                if (nodeId <= 0 || nodeId > Nodes.Length) continue;
                var node = Nodes[nodeId - 1];
                int cIdx = node.CountryIdx;
                int sIdx = node.StateIdx;

                float u = (float)x / width;
                float v = (float)y / height;

                // Country bounds
                if (!cMinU.ContainsKey(cIdx))
                {
                    cMinU[cIdx] = u; cMaxU[cIdx] = u;
                    cMinV[cIdx] = v; cMaxV[cIdx] = v;
                }
                else
                {
                    cMinU[cIdx] = Math.Min(cMinU[cIdx], u);
                    cMaxU[cIdx] = Math.Max(cMaxU[cIdx], u);
                    cMinV[cIdx] = Math.Min(cMinV[cIdx], v);
                    cMaxV[cIdx] = Math.Max(cMaxV[cIdx], v);
                }

                // State bounds
                if (!sMinU.ContainsKey(sIdx))
                {
                    sMinU[sIdx] = u; sMaxU[sIdx] = u;
                    sMinV[sIdx] = v; sMaxV[sIdx] = v;
                }
                else
                {
                    sMinU[sIdx] = Math.Min(sMinU[sIdx], u);
                    sMaxU[sIdx] = Math.Max(sMaxU[sIdx], u);
                    sMinV[sIdx] = Math.Min(sMinV[sIdx], v);
                    sMaxV[sIdx] = Math.Max(sMaxV[sIdx], v);
                }
            }
        }

        foreach (var id in cMinU.Keys)
            _countryBoundsCache[id] = (cMinU[id], cMinV[id], cMaxU[id] - cMinU[id], cMaxV[id] - cMinV[id]);

        foreach (var id in sMinU.Keys)
            _stateBoundsCache[id] = (sMinU[id], sMinV[id], sMaxU[id] - sMinU[id], sMaxV[id] - sMinV[id]);

        GameLogger.Info("MapDataService", $"Bounds computed: {_countryBoundsCache.Count} countries, {_stateBoundsCache.Count} states.");
    }

    /// <summary>
    /// Loads map_nodes.bin and catalog JSON files from disk.
    /// </summary>
    public static void LoadMapData(string dataFolder)
    {
        _countryBoundsCache.Clear();
        _stateBoundsCache.Clear();

        // 1. Load catalogs
        string statesPath = Path.Combine(dataFolder, "Map", "catalogs", "catalog_states.json");
        StateCatalog = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(statesPath))
                       ?? Array.Empty<string>();

        string countriesPath = Path.Combine(dataFolder, "Map", "catalogs", "catalog_countries.json");
        CountryCatalog = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(countriesPath))
                         ?? Array.Empty<string>();

        // 2. Load binary node data
        string binPath = Path.Combine(dataFolder, "Map", "map_nodes.bin");
        byte[] bytes = File.ReadAllBytes(binPath);
        int totalNodes = bytes.Length / 4;
        Nodes = new NodeData[totalNodes];

        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms);
        for (int i = 0; i < totalNodes; i++)
        {
            Nodes[i] = new NodeData
            {
                StateIdx = br.ReadUInt16(),
                CountryIdx = br.ReadUInt16()
            };
        }

        GameLogger.Info("MapDataService", $"{totalNodes} nodes loaded.");
    }

    /// <summary>
    /// Generates a deterministic color palette with maximally distinct colors.
    /// Uses HSV with golden-angle hue distribution for visual separation.
    /// </summary>
    public static float[] GeneratePalette(int size, int seed)
    {
        var palette = new float[size * 3];
        float goldenAngle = 137.508f; // degrees — golden angle for max hue separation
        var rand = new Random(seed);
        float startHue = (float)(rand.NextDouble() * 360.0);

        for (int i = 0; i < size; i++)
        {
            float hue = (startHue + i * goldenAngle) % 360f;
            float sat = 0.55f + (float)(rand.NextDouble() * 0.35f); // 0.55-0.90
            float val = 0.60f + (float)(rand.NextDouble() * 0.30f); // 0.60-0.90

            // HSV to RGB conversion
            float c = val * sat;
            float x = c * (1f - Math.Abs((hue / 60f) % 2f - 1f));
            float m = val - c;
            float r, g, b;

            if (hue < 60) { r = c; g = x; b = 0; }
            else if (hue < 120) { r = x; g = c; b = 0; }
            else if (hue < 180) { r = 0; g = c; b = x; }
            else if (hue < 240) { r = 0; g = x; b = c; }
            else if (hue < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            palette[i * 3]     = r + m;
            palette[i * 3 + 1] = g + m;
            palette[i * 3 + 2] = b + m;
        }
        return palette;
    }

    /// <summary>
    /// Reads world_data.bin and returns raw bytes + detected dimensions.
    /// The Godot side converts this into a Texture2DArray.
    /// </summary>
    public static (byte[] data, int width, int height, int layers)? LoadWorldDataRaw(string dataFolder)
    {
        string binPath = Path.Combine(dataFolder, "Map", "world_data.bin");
        if (!File.Exists(binPath))
        {
            GameLogger.Warning("MapDataService", "world_data.bin not found.");
            return null;
        }

        byte[] allBytes = File.ReadAllBytes(binPath);

        // Detect resolution from file size
        int w = 1024, h = 1024;
        if (allBytes.Length >= 16777216 && allBytes.Length % 16777216 == 0)
        { w = 2048; h = 2048; }
        else if (allBytes.Length >= 8388608 && allBytes.Length % 8388608 == 0)
        { w = 2048; h = 1024; }

        int layerSize = w * h * 4;
        int numLayers = allBytes.Length / layerSize;

        if (numLayers == 0)
        {
            GameLogger.Warning("MapDataService", $"world_data.bin has invalid size ({allBytes.Length} bytes).");
            return null;
        }

        GameLogger.Info("MapDataService", $"world_data.bin loaded: {w}x{h} with {numLayers} layers.");
        return (allBytes, w, h, numLayers);
    }

    /// <summary>
    /// Configuration for country scale normalization in zoom views.
    /// </summary>
    public static class ScaleConfig
    {
        /// <summary>Target mesh width for National view (Godot units).</summary>
        public const float NationalMeshWidth = 420f;

        /// <summary>UV spans smaller than this get capped (prevents infinite zoom on tiny islands).</summary>
        public const float MinUVSpan = 0.008f;

        /// <summary>UV spans larger than this still zoom but with less magnification.</summary>
        public const float MaxUVSpan = 0.40f;

        /// <summary>
        /// Computes dynamic UV margin for a country's bounding box.
        /// Small countries get larger relative margins for context.
        /// </summary>
        public static float GetMargin(float uvSpan)
        {
            float relMargin = Math.Clamp(0.35f, 0.10f, 0.60f);
            float absMinMargin = 0.03f;
            return Math.Max(uvSpan * relMargin, absMinMargin);
        }
    }
}
