using Godot;
using System;
using Engine.Services;

namespace PLVSVLTRA.Map;

/// <summary>
/// Configures Terrain3D, camera, and overlay for all views.
/// All scenes now use the SAME unified 8K Terrain3D world to allow seamless LOD.
/// National/Micro views just focus the camera on the country/state area.
/// </summary>
public static class MapBuilder
{
    // Full world map configuration (8K resolution)
    public const int WorldMapWidth = 8192;
    public const int WorldMapHeight = 4096;
    public const float WorldHeightScale = 25f; // Reduced from 80 to fix exaggerated peaks

    private const float SmallCountryMinSpan = 0.02f;

    /// <summary>
    /// Initializes the Terrain3D with the global DEM heightmap.
    /// Called once on first load for any scene; subsequent loads use cached region data.
    /// </summary>
    public static void BuildInternationalView(MapView mapView)
    {
        EnsureTerrainLoaded(mapView);
        ConfigureInternationalCamera(mapView);
        UpdateOverlayPlane(mapView);
    }

    /// <summary>
    /// Initializes the international view using procedural GeoJSON rendering.
    /// This is the new default method that doesn't rely on Terrain3D or pre-baked textures.
    /// </summary>
    public static void BuildProceduralInternationalView(ProceduralMapView mapView)
    {
        // The ProceduralMapView handles everything in its _Ready()
        GD.Print("[MapBuilder] Procedural international view ready");
    }

    private static void EnsureTerrainLoaded(MapView mapView)
    {
        var terrain = mapView.FindChild("Terrain3D");
        if (terrain == null)
        {
            GD.PrintErr("[MapBuilder] No Terrain3D node found in scene");
            return;
        }

        // Set properties explicitly to ensure consistency across scenes
        terrain.Set("region_size", 1024);
        terrain.Set("vertex_spacing", 1.0f);

        var data = terrain.Get("data").AsGodotObject();
        if (data == null) return;

        var regions = data.Call("get_region_count");
        if ((int)regions > 0)
        {
            GD.Print($"[MapBuilder] Terrain already loaded ({regions} regions)");
            return;
        }

        // First launch — import the 8K DEM heightmap
        GD.Print("[MapBuilder] First launch: importing 8K DEM into Terrain3D...");

        var heightTex = GD.Load<Texture2D>("res://Assets/Terrain/16_bit_dem_8k.png");
        if (heightTex == null) return;

        var heightImg = heightTex.GetImage();
        // Convert to FORMAT_RF (32-bit float) for Terrain3D
        heightImg.Convert(Image.Format.Rf);

        // Center the terrain at origin
        float halfW = WorldMapWidth / 2f;
        float halfH = WorldMapHeight / 2f;
        var offset = new Vector3(-halfW, 0, -halfH);

        // Import satellite colormap
        Image colorImg = null;
        var colorTex = GD.Load<Texture2D>("res://Assets/Terrain/HYP_HR_SR_OB_DR.png");
        if (colorTex != null)
        {
            colorImg = colorTex.GetImage();
            colorImg.Resize(WorldMapWidth, WorldMapHeight);
        }

        var images = new Godot.Collections.Array { heightImg, Variant.From<GodotObject>(null), colorImg != null ? Variant.From(colorImg) : Variant.From<GodotObject>(null) };
        data.Call("import_images", images, offset, 0.0f, WorldHeightScale);

        GD.Print("[MapBuilder] Unified world terrain imported successfully");
    }

    private static void ConfigureInternationalCamera(MapView mapView)
    {
        var cam = mapView.GetViewport().GetCamera3D() as PLVSVLTRA.Camera.StrategyCamera;
        if (cam == null) return;

        cam.MapWidth = WorldMapWidth;
        cam.LimitZ = new Vector2(-WorldMapWidth * 0.25f, WorldMapWidth * 0.25f);
        // International view should be high up
        cam.GlobalPosition = new Vector3(0, 4500f, WorldMapHeight * 0.1f);
        cam.LookAt(Vector3.Zero);
        cam.SetTargetState(cam.GlobalPosition, cam.RotationDegrees);
    }

    // ============================================================
    // NATIONAL VIEW
    // ============================================================

    public static void BuildNationalView(MapView mapView, int countryIdx)
    {
        if (countryIdx < 0 || countryIdx >= MapDataService.CountryCatalog.Length) return;

        EnsureTerrainLoaded(mapView); // Ensure the unified world is loaded
        UpdateOverlayPlane(mapView);

        var bounds = MapDataService.GetCountryBounds(countryIdx);
        if (bounds.W < 0.0001f && bounds.H < 0.0001f) return;

        // Calculate UV window with margin for the overlay shader
        float span = Mathf.Max(bounds.W, bounds.H);
        float margin = MapDataService.ScaleConfig.GetMargin(span);
        Vector2 uvMin = new(Mathf.Max(0f, bounds.MinU - margin), Mathf.Max(0f, bounds.MinV - margin));
        Vector2 uvMax = new(Mathf.Min(1f, bounds.MinU + bounds.W + margin), Mathf.Min(1f, bounds.MinV + bounds.H + margin));
        ExpandWindowToMinSpan(ref uvMin, ref uvMax, span);
        
        mapView.SetUVWindow(uvMin, uvMax);
        mapView.SetMeshSize(new Vector2(WorldMapWidth, WorldMapHeight));

        ConfigureOverlayShader(mapView, uvMin, uvMax, countryIdx);
        ConfigureLocalCamera(mapView, bounds.MinU + bounds.W/2f, bounds.MinV + bounds.H/2f, span, false);

        GD.Print($"[MapBuilder] National view focused on {MapDataService.CountryCatalog[countryIdx]}");
    }

    // ============================================================
    // MICRO VIEW
    // ============================================================

    public static void BuildMicroView(MapView mapView, int countryIdx, int stateIdx)
    {
        if (countryIdx < 0 || countryIdx >= MapDataService.CountryCatalog.Length) return;

        EnsureTerrainLoaded(mapView);
        UpdateOverlayPlane(mapView);

        var bounds = (stateIdx >= 0 && stateIdx < MapDataService.StateCatalog.Length)
            ? MapDataService.GetStateBounds(stateIdx, countryIdx)
            : MapDataService.GetCountryBounds(countryIdx);

        if (bounds.W < 0.0001f && bounds.H < 0.0001f) return;

        float span = Mathf.Max(bounds.W, bounds.H);
        float margin = MapDataService.ScaleConfig.GetMargin(span);
        Vector2 uvMin = new(Mathf.Max(0f, bounds.MinU - margin), Mathf.Max(0f, bounds.MinV - margin));
        Vector2 uvMax = new(Mathf.Min(1f, bounds.MinU + bounds.W + margin), Mathf.Min(1f, bounds.MinV + bounds.H + margin));
        ExpandWindowToMinSpan(ref uvMin, ref uvMax, span);
        
        mapView.SetUVWindow(uvMin, uvMax);
        mapView.SetMeshSize(new Vector2(WorldMapWidth, WorldMapHeight));

        ConfigureMicroOverlayShader(mapView, uvMin, uvMax, countryIdx, stateIdx);
        ConfigureLocalCamera(mapView, bounds.MinU + bounds.W/2f, bounds.MinV + bounds.H/2f, span, true);

        string stateLabel = (stateIdx >= 0 && stateIdx < MapDataService.StateCatalog.Length)
            ? MapDataService.StateCatalog[stateIdx] : "unknown";
        GD.Print($"[MapBuilder] Micro view focused on {MapDataService.CountryCatalog[countryIdx]}/{stateLabel}");
    }

    // ============================================================
    // CAMERA FOCUS
    // ============================================================

    private static void ConfigureLocalCamera(MapView v, float centerU, float centerV, float span, bool isMicro)
    {
        var cam = v.GetViewport().GetCamera3D() as PLVSVLTRA.Camera.StrategyCamera;
        if (cam == null) return;

        cam.MapWidth = WorldMapWidth;
        cam.LimitZ = new Vector2(-WorldMapWidth * 0.5f, WorldMapWidth * 0.5f);

        // Convert UV to World Position
        float worldX = (centerU - 0.5f) * WorldMapWidth;
        float worldZ = (centerV - 0.5f) * WorldMapHeight;

        // Camera height depends on span. A small span (like Jamaica) needs a low height to fill the screen.
        // A span of 1.0 would need ~6000m height.
        float targetHeight = span * WorldMapWidth * 0.7f;
        if (isMicro) targetHeight *= 0.6f; // Get even closer for micro

        targetHeight = Mathf.Clamp(targetHeight, isMicro ? 10f : 30f, 2000f);

        cam.GlobalPosition = new Vector3(worldX, targetHeight, worldZ + targetHeight * 0.2f); // Slight offset for angle
        cam.LookAt(new Vector3(worldX, 0, worldZ));
        cam.SetTargetState(cam.GlobalPosition, cam.RotationDegrees);
    }

    // ============================================================
    // OVERLAY SHADER CONFIGURATION
    // ============================================================

    private static void UpdateOverlayPlane(MapView mapView)
    {
        var overlay = mapView.GetNodeOrNull<MeshInstance3D>("PoliticalOverlay");
        if (overlay?.Mesh is PlaneMesh pm)
        {
            pm.Size = new Vector2(WorldMapWidth, WorldMapHeight);
            
            // Subdivide the mesh so it can track vertex displacement from the mountains
            // We need vertex displacement back in the overlay so it doesn't clip into terrain
            pm.SubdivideWidth = 1024;
            pm.SubdivideDepth = 512;
        }

        // Load and bind the sand texture to the shaders
        var mat = mapView.GetOverlayMaterial();
        if (mat != null)
        {
            var sandTex = GD.Load<Texture2D>("res://Assets/Textures/Ground093C_2K-JPG/Ground093C_2K-JPG_Color.jpg");
            if (sandTex != null)
            {
                mat.SetShaderParameter("sand_tex", sandTex);
            }
        }
    }

    private static void ConfigureOverlayShader(MapView v, Vector2 uvMin, Vector2 uvMax, int cIdx)
    {
        var mat = v.GetOverlayMaterial();
        if (mat == null) return;

        mat.SetShaderParameter("country_uv_min", uvMin);
        mat.SetShaderParameter("country_uv_max", uvMax);
        MapTextureService.InjectTextures(mat);

        mat.SetShaderParameter("focus_radius", 0.48f);
        mat.SetShaderParameter("focus_feather", 0.18f);

        Color col = MapTextureService.GetCountryColor(cIdx);
        mat.SetShaderParameter("selection_color", new Vector3(col.R, col.G, col.B));
    }

    private static void ConfigureMicroOverlayShader(MapView v, Vector2 uvMin, Vector2 uvMax, int cIdx, int sIdx)
    {
        var mat = v.GetOverlayMaterial();
        if (mat == null) return;

        mat.SetShaderParameter("country_uv_min", uvMin);
        mat.SetShaderParameter("country_uv_max", uvMax);
        MapTextureService.InjectTextures(mat);

        mat.SetShaderParameter("focus_radius", 0.48f);
        mat.SetShaderParameter("focus_feather", 0.15f);

        Color col = MapTextureService.GetCountryColor(cIdx);
        mat.SetShaderParameter("selection_color", new Vector3(col.R, col.G, col.B));
    }

    private static void ExpandWindowToMinSpan(ref Vector2 uvMin, ref Vector2 uvMax, float sourceSpan)
    {
        if (sourceSpan >= SmallCountryMinSpan) return;

        Vector2 center = (uvMin + uvMax) * 0.5f;
        float half = SmallCountryMinSpan * 0.5f;
        Vector2 newMin = center - new Vector2(half, half);
        Vector2 newMax = center + new Vector2(half, half);

        if (newMin.X < 0f) { newMax.X -= newMin.X; newMin.X = 0f; }
        if (newMin.Y < 0f) { newMax.Y -= newMin.Y; newMin.Y = 0f; }
        if (newMax.X > 1f) { newMin.X -= (newMax.X - 1f); newMax.X = 1f; }
        if (newMax.Y > 1f) { newMin.Y -= (newMax.Y - 1f); newMax.Y = 1f; }

        uvMin = new Vector2(Mathf.Clamp(newMin.X, 0f, 1f), Mathf.Clamp(newMin.Y, 0f, 1f));
        uvMax = new Vector2(Mathf.Clamp(newMax.X, 0f, 1f), Mathf.Clamp(newMax.Y, 0f, 1f));
    }
}
