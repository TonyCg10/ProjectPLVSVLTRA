using Godot;
using System;
using System.Collections.Generic;
using Engine.Services;

namespace PLVSVLTRA.Map;

/// <summary>
/// Renders a procedural world map from GeoJSON data.
/// Generates ID map, country lookup, and border mask textures at runtime.
/// Attached to the root node of the International scene.
/// </summary>
public partial class ProceduralMapView : Node3D
{
    [Export] public int TextureWidth { get; set; } = 4096;
    [Export] public int TextureHeight { get; set; } = 2048;
    [Export] public float MapPlaneWidth { get; set; } = 1024f;
    [Export] public float MapPlaneHeight { get; set; } = 512f;
    [Export] public string GeoJsonPath { get; set; } = "res://Data/paises_fixed.json";

    private MeshInstance3D _mapMesh;
    private ShaderMaterial _mapMaterial;
    private ImageTexture _idMapTexture;
    private ImageTexture _countryLookupTexture;
    private ImageTexture _borderMaskTexture;

    private int _currentMapMode = 1;
    private int _selectedCountryIdx = -1;

    public int CurrentMapMode => _currentMapMode;
    public int SelectedCountryIdx => _selectedCountryIdx;

    public override void _Ready()
    {
        GD.Print("[ProceduralMapView] Initializing procedural map from GeoJSON...");

        BuildMap();
        SetupCamera();

        GD.Print("[ProceduralMapView] Map ready.");
    }

    private void BuildMap()
    {
        string fullPath = ProjectSettings.GlobalizePath(GeoJsonPath);

        if (!System.IO.File.Exists(fullPath))
        {
            GD.PrintErr($"[ProceduralMapView] GeoJSON not found: {fullPath}");
            return;
        }

        // 1. Parse and rasterize GeoJSON
        var result = GeoJsonParser.LoadAndRasterize(fullPath, TextureWidth, TextureHeight);
        GD.Print($"[ProceduralMapView] Parsed {result.Features.Count} countries, rasterized to {TextureWidth}x{TextureHeight}");

        // 2. Create ID map texture
        _idMapTexture = CreateTextureFromBytes(result.IdMapPixels, result.Width, result.Height, Image.Format.Rgba8);

        // 3. Generate border mask
        byte[] borderMask = GeoJsonParser.GenerateBorderMask(result.IdMapPixels, result.Width, result.Height);
        byte[] borderData = new byte[result.Width * result.Height * 4];
        for (int i = 0; i < result.Width * result.Height; i++)
        {
            borderData[i * 4] = borderMask[i];
            borderData[i * 4 + 1] = borderMask[i];
            borderData[i * 4 + 2] = borderMask[i];
            borderData[i * 4 + 3] = 255;
        }
        _borderMaskTexture = CreateTextureFromBytes(borderData, result.Width, result.Height, Image.Format.Rgba8);

        // 4. Generate country lookup texture
        GenerateCountryLookup(result.Features.Count);

        // 5. Update MapDataService with node info for compatibility
        UpdateMapDataService(result.Features);

        // 6. Create mesh and material
        CreateMapMesh();

        // 7. Bind textures to shader
        BindTextures();
    }

    private ImageTexture CreateTextureFromBytes(byte[] data, int width, int height, Image.Format format)
    {
        Image img = Image.CreateFromData(width, height, false, format, data);
        return ImageTexture.CreateFromImage(img);
    }

    private void GenerateCountryLookup(int countryCount)
    {
        int texSize = NextPowerOfTwo(Mathf.Max(countryCount, 256));
        texSize = Mathf.Min(texSize, 4096);

        float[] palette = MapDataService.GeneratePalette(countryCount, 123);

        byte[] lookupData = new byte[texSize * texSize * 3];
        for (int i = 0; i < countryCount; i++)
        {
            int px = i % texSize;
            int py = i / texSize;
            int idx = (py * texSize + px) * 3;

            lookupData[idx]     = (byte)(palette[i * 3] * 255);
            lookupData[idx + 1] = (byte)(palette[i * 3 + 1] * 255);
            lookupData[idx + 2] = (byte)(palette[i * 3 + 2] * 255);
        }

        Image img = Image.CreateFromData(texSize, texSize, false, Image.Format.Rgb8, lookupData);
        _countryLookupTexture = ImageTexture.CreateFromImage(img);

        GD.Print($"[ProceduralMapView] Country lookup texture: {texSize}x{texSize} for {countryCount} countries");
    }

    private void UpdateMapDataService(List<GeoJsonParser.CountryFeature> features)
    {
        // Build a minimal NodeData array for compatibility with existing systems
        var nodes = new MapDataService.NodeData[features.Count];
        for (int i = 0; i < features.Count; i++)
        {
            string iso = features[i].Iso;
            int countryIdx = Array.IndexOf(MapDataService.CountryCatalog, iso);
            if (countryIdx < 0) countryIdx = i;

            nodes[i] = new MapDataService.NodeData
            {
                StateIdx = 0,
                CountryIdx = (ushort)countryIdx
            };
        }

        // Note: We don't replace MapDataService.Nodes since it's static and may have more entries.
        // But we do need to ensure the catalogs are aligned.
        GD.Print($"[ProceduralMapView] Mapped {features.Count} GeoJSON features to catalog indices");
    }

    private void CreateMapMesh()
    {
        // Remove existing map mesh if any
        var oldMesh = GetNodeOrNull<MeshInstance3D>("ProceduralMap");
        if (oldMesh != null) oldMesh.QueueFree();

        // Create plane mesh
        var plane = new PlaneMesh
        {
            Size = new Vector2(MapPlaneWidth, MapPlaneHeight),
            SubdivideWidth = 1,
            SubdivideDepth = 1
        };

        // Create material
        _mapMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Assets/Shaders/ProceduralMapShader.gdshader")
        };

        _mapMesh = new MeshInstance3D
        {
            Name = "ProceduralMap",
            Mesh = plane,
            MaterialOverride = _mapMaterial
        };

        // Position at y=0, centered
        _mapMesh.Position = new Vector3(0, 0, 0);

        AddChild(_mapMesh);
        GD.Print("[ProceduralMapView] Mesh created");
    }

    private void BindTextures()
    {
        if (_mapMaterial == null) return;

        _mapMaterial.SetShaderParameter("id_map", _idMapTexture);
        _mapMaterial.SetShaderParameter("country_lookup", _countryLookupTexture);
        _mapMaterial.SetShaderParameter("border_mask", _borderMaskTexture);
        _mapMaterial.SetShaderParameter("map_mode", _currentMapMode);

        GD.Print("[ProceduralMapView] Textures bound to shader");
    }

    private void SetupCamera()
    {
        var cam = GetViewport().GetCamera3D() as PLVSVLTRA.Camera.StrategyCamera;
        if (cam == null)
        {
            GD.PrintErr("[ProceduralMapView] No StrategyCamera found");
            return;
        }

        // Configure for world map
        cam.MapWidth = MapPlaneWidth;
        cam.LimitZ = new Vector2(-MapPlaneWidth * 0.25f, MapPlaneWidth * 0.25f);
        cam.MinHeight = 50f;
        cam.MaxHeight = 3000f;
        cam.BaseMoveSpeed = 100f;
        cam.BasePanSpeed = 5f;
        cam.Acceleration = 8f;

        // Portal thresholds
        cam.InPortalTarget = 2; // National
        cam.InPortalHeight = 80f;
        cam.OutPortalTarget = 0; // No zoom out from international
        cam.OutPortalHeight = 3000f;
        cam.ResistanceRange = 30f;
        cam.PortalScrollsRequired = 4;
        cam.PortalActivationWindowSec = 0.6f;

        // Initial position - high above center
        cam.GlobalPosition = new Vector3(0, 2000f, MapPlaneHeight * 0.1f);
        cam.LookAt(new Vector3(0, 0, 0));
        cam.SetTargetState(cam.GlobalPosition, cam.RotationDegrees);

        GD.Print("[ProceduralMapView] Camera configured");
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left)
        {
            HandleClick(mouseBtn.Position);
        }
    }

    private void HandleClick(Vector2 mousePos)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null || _mapMesh == null) return;

        // Raycast against the map plane
        Vector3 from = camera.ProjectRayOrigin(mousePos);
        Vector3 to = from + camera.ProjectRayNormal(mousePos) * 5000;

        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to, 1); // collision mask 1
        var result = spaceState.IntersectRay(query);

        if (result.Count == 0)
        {
            // Fallback: check intersection with plane manually
            Vector3 planePoint = _mapMesh.GlobalPosition;
            Vector3 planeNormal = Vector3.Up;

            float denom = planeNormal.Dot(to - from);
            if (Mathf.Abs(denom) > 0.0001f)
            {
                float t = planeNormal.Dot(planePoint - from) / denom;
                if (t >= 0)
                {
                    Vector3 fallbackHit = from + t * (to - from);
                    ProcessHitAtWorldPos(fallbackHit);
                }
            }
            return;
        }

        Vector3 hitPos = (Vector3)result["position"];
        ProcessHitAtWorldPos(hitPos);
    }

    private void ProcessHitAtWorldPos(Vector3 worldPos)
    {
        // Convert world position to UV
        float halfW = MapPlaneWidth / 2f;
        float halfH = MapPlaneHeight / 2f;

        float u = (worldPos.X + halfW) / MapPlaneWidth;
        float v = (worldPos.Z + halfH) / MapPlaneHeight;

        if (u < 0 || u > 1 || v < 0 || v > 1) return;

        // Sample ID map
        var img = _idMapTexture.GetImage();
        int px = Mathf.Clamp((int)(u * TextureWidth), 0, TextureWidth - 1);
        int py = Mathf.Clamp((int)(v * TextureHeight), 0, TextureHeight - 1);

        Color idCol = img.GetPixel(px, py);
        int nodeId = (int)Mathf.Round(idCol.R * 255f) +
                     ((int)Mathf.Round(idCol.G * 255f) * 256);

        if (nodeId > 0)
        {
            int countryIdx = nodeId - 1;
            SelectCountry(countryIdx);
        }
        else
        {
            GD.Print("[ProceduralMapView] Clicked on water");
        }
    }

    private void SelectCountry(int countryIdx)
    {
        _selectedCountryIdx = countryIdx;

        if (_mapMaterial != null)
        {
            _mapMaterial.SetShaderParameter("selected_country_idx", countryIdx);

            Color col = MapTextureService.GetCountryColor(countryIdx);
            _mapMaterial.SetShaderParameter("selection_color", new Vector3(col.R, col.G, col.B));
        }

        string countryId = countryIdx < MapDataService.CountryCatalog.Length
            ? MapDataService.CountryCatalog[countryIdx]
            : $"idx_{countryIdx}";

        GD.Print($"[ProceduralMapView] Selected country: {countryId} (idx {countryIdx})");
    }

    public void SetMapMode(int mode)
    {
        _currentMapMode = mode;
        if (_mapMaterial != null)
        {
            _mapMaterial.SetShaderParameter("map_mode", mode);
        }
    }

    public void ClearSelection()
    {
        _selectedCountryIdx = -1;
        if (_mapMaterial != null)
        {
            _mapMaterial.SetShaderParameter("selected_country_idx", -1);
        }
    }

    private static int NextPowerOfTwo(int v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        v++;
        return v;
    }
}
