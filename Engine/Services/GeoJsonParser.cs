using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Engine.Services;

/// <summary>
/// Parses GeoJSON FeatureCollection files and rasterizes country polygons into ID maps.
/// Supports MultiPolygon and Polygon geometry types.
/// </summary>
public static class GeoJsonParser
{
    public const int DefaultTextureSize = 4096;

    public struct CountryFeature
    {
        public string Iso;
        public string Name;
        public List<List<List<double[]>>> Polygons; // MultiPolygon: [polygon][ring][coord]
    }

    public struct GeoJsonResult
    {
        public List<CountryFeature> Features;
        public byte[] IdMapPixels;
        public int Width;
        public int Height;
    }

    /// <summary>
    /// Loads and parses a GeoJSON file, then rasterizes all country polygons into an ID map texture.
    /// </summary>
    public static GeoJsonResult LoadAndRasterize(string filePath, int textureWidth = DefaultTextureSize, int textureHeight = DefaultTextureSize / 2)
    {
        string json = File.ReadAllText(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var features = new List<CountryFeature>();
        var featureArray = root.GetProperty("features");

        foreach (var feature in featureArray.EnumerateArray())
        {
            var props = feature.GetProperty("properties");
            string iso = props.TryGetProperty("iso", out var isoProp) ? isoProp.GetString() : "???";
            string name = props.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : iso;

            var geometry = feature.GetProperty("geometry");
            string geomType = geometry.GetProperty("type").GetString();
            var coords = geometry.GetProperty("coordinates");

            var polygons = ParseCoordinates(geomType, coords);

            features.Add(new CountryFeature
            {
                Iso = iso,
                Name = name,
                Polygons = polygons
            });
        }

        // Rasterize to ID map
        byte[] idMap = new byte[textureWidth * textureHeight * 4]; // RGBA8
        ushort[] nodeIds = new ushort[textureWidth * textureHeight];

        var countryToIdx = BuildCountryIndex(features);

        for (int i = 0; i < features.Count; i++)
        {
            ushort nodeId = (ushort)(i + 1); // 1-based, 0 = water
            foreach (var polygon in features[i].Polygons)
            {
                RasterizePolygon(polygon, nodeId, idMap, nodeIds, textureWidth, textureHeight);
            }
        }

        return new GeoJsonResult
        {
            Features = features,
            IdMapPixels = idMap,
            Width = textureWidth,
            Height = textureHeight
        };
    }

    private static Dictionary<string, int> BuildCountryIndex(List<CountryFeature> features)
    {
        var dict = new Dictionary<string, int>();
        for (int i = 0; i < features.Count; i++)
        {
            dict[features[i].Iso] = i;
        }
        return dict;
    }

    private static List<List<List<double[]>>> ParseCoordinates(string geomType, JsonElement coords)
    {
        var result = new List<List<List<double[]>>>();

        if (geomType == "Polygon")
        {
            var polygon = ParsePolygon(coords);
            if (polygon.Count > 0) result.Add(polygon);
        }
        else if (geomType == "MultiPolygon")
        {
            foreach (var polyElem in coords.EnumerateArray())
            {
                var polygon = ParsePolygon(polyElem);
                if (polygon.Count > 0) result.Add(polygon);
            }
        }

        return result;
    }

    private static List<List<double[]>> ParsePolygon(JsonElement rings)
    {
        var polygon = new List<List<double[]>>();
        foreach (var ringElem in rings.EnumerateArray())
        {
            var ring = new List<double[]>();
            foreach (var coord in ringElem.EnumerateArray())
            {
                double lon = coord[0].GetDouble();
                double lat = coord[1].GetDouble();
                ring.Add(new[] { lon, lat });
            }
            if (ring.Count > 2) polygon.Add(ring);
        }
        return polygon;
    }

    /// <summary>
    /// Converts lon/lat to pixel coordinates in equirectangular projection.
    /// Longitude: -180..180 -> 0..width
    /// Latitude: 85..-85 -> 0..height (clamped to avoid pole distortion)
    /// </summary>
    private static (int x, int y) LonLatToPixel(double lon, double lat, int width, int height)
    {
        double maxLat = 85.0;
        lat = Math.Max(-maxLat, Math.Min(maxLat, lat));

        int x = (int)((lon + 180.0) / 360.0 * width);
        int y = (int)((maxLat - lat) / (2.0 * maxLat) * height);

        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);

        return (x, y);
    }

    /// <summary>
    /// Rasterizes a polygon (with holes) into the ID map using scanline fill.
    /// The outer ring is index 0, inner rings (holes) are index 1+.
    /// </summary>
    private static void RasterizePolygon(List<List<double[]>> polygon, ushort nodeId, byte[] idMap, ushort[] nodeIds, int width, int height)
    {
        if (polygon.Count == 0) return;

        // First, rasterize the outer ring as a filled polygon
        var outerRing = polygon[0];
        FillPolygon(outerRing, nodeId, idMap, nodeIds, width, height);

        // Then punch out holes by re-filling with 0
        for (int i = 1; i < polygon.Count; i++)
        {
            FillPolygon(polygon[i], (ushort)0, idMap, nodeIds, width, height);
        }
    }

    /// <summary>
    /// Fills a polygon using scanline algorithm with optimized edge table.
    /// </summary>
    private static void FillPolygon(List<double[]> ring, ushort nodeId, byte[] idMap, ushort[] nodeIds, int width, int height)
    {
        int n = ring.Count;
        if (n < 3) return;

        // Pre-convert all vertices to pixel coordinates
        var pixelX = new int[n];
        var pixelY = new int[n];
        int minY = height, maxY = 0;

        for (int i = 0; i < n; i++)
        {
            var (px, py) = LonLatToPixel(ring[i][0], ring[i][1], width, height);
            pixelX[i] = px;
            pixelY[i] = py;
            if (py < minY) minY = py;
            if (py > maxY) maxY = py;
        }

        // Use a flat edge buffer: for each scanline, store X intersections
        // Max intersections per scanline = number of edges, so we use a simple List per row
        var intersections = new List<float>[height];
        for (int y = 0; y < height; y++) intersections[y] = new List<float>();

        // Build edge intersections
        for (int i = 0; i < n; i++)
        {
            int y1 = pixelY[i];
            int y2 = pixelY[(i + 1) % n];
            int x1 = pixelX[i];
            int x2 = pixelX[(i + 1) % n];

            if (y1 == y2) continue; // Skip horizontal edges

            // Ensure y1 < y2
            if (y1 > y2)
            {
                (y1, y2) = (y2, y1);
                (x1, x2) = (x2, x1);
            }

            // Clamp to valid range
            if (y2 < 0 || y1 >= height) continue;
            int startY = Math.Max(y1, 0);
            int endY = Math.Min(y2, height - 1);

            float dxPerY = (float)(x2 - x1) / (y2 - y1);
            for (int y = startY; y <= endY; y++)
            {
                float x = x1 + dxPerY * (y - y1);
                intersections[y].Add(x);
            }
        }

        // Fill scanlines
        for (int y = minY; y <= maxY && y < height; y++)
        {
            if (y < 0) continue;
            var xList = intersections[y];
            if (xList.Count < 2) continue;

            xList.Sort();

            // Fill between pairs (odd-even rule)
            for (int i = 0; i < xList.Count - 1; i += 2)
            {
                int xStart = Math.Clamp((int)xList[i], 0, width - 1);
                int xEnd = Math.Clamp((int)xList[i + 1], 0, width - 1);

                int rowStart = y * width + xStart;
                int rowEnd = y * width + xEnd;

                for (int idx = rowStart; idx <= rowEnd; idx++)
                {
                    nodeIds[idx] = nodeId;
                    int byteIdx = idx * 4;
                    idMap[byteIdx]     = (byte)(nodeId & 0xFF);
                    idMap[byteIdx + 1] = (byte)((nodeId >> 8) & 0xFF);
                    idMap[byteIdx + 2] = 0;
                    idMap[byteIdx + 3] = nodeId > 0 ? (byte)255 : (byte)0;
                }
            }
        }
    }

    /// <summary>
    /// Draws a border around country boundaries in the ID map for visual separation.
    /// This creates a 1-pixel-wide border by marking edge pixels.
    /// </summary>
    public static byte[] GenerateBorderMask(byte[] idMap, int width, int height)
    {
        byte[] borderMask = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                ushort currentId = (ushort)(idMap[idx * 4] | (idMap[idx * 4 + 1] << 8));

                bool isBorder = false;
                for (int dy = -1; dy <= 1 && !isBorder; dy++)
                {
                    for (int dx = -1; dx <= 1 && !isBorder; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;

                        int nx = x + dx;
                        int ny = y + dy;

                        if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                        {
                            isBorder = currentId > 0;
                            continue;
                        }

                        int nIdx = ny * width + nx;
                        ushort neighborId = (ushort)(idMap[nIdx * 4] | (idMap[nIdx * 4 + 1] << 8));

                        if (currentId != neighborId)
                        {
                            isBorder = true;
                        }
                    }
                }

                borderMask[idx] = isBorder ? (byte)255 : (byte)0;
            }
        }

        return borderMask;
    }
}
