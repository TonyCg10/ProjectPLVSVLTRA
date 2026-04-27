using Godot;
using System;
using Engine.Services;

namespace PLVSVLTRA.Map;

/// <summary>
/// Configures map mesh, camera, and scene nodes for National/Micro views.
/// Computes UV windows and mesh dimensions from MapDataService bounds.
/// </summary>
public static class MapBuilder
{
    private const float WorldMapWidth = 540f;
    private const float WorldMapHeight = 270f;
    private const float NationalMeshWidth = 420f;
    private const float MicroMeshWidth = 420f;
    private const float SmallCountryMinSpan = 0.02f;

    public static void BuildNationalView(MapView mapView, int countryIdx)
    {
        if (countryIdx < 0 || countryIdx >= MapDataService.CountryCatalog.Length) return;

        var bounds = MapDataService.GetCountryBounds(countryIdx);
        if (bounds.W < 0.0001f && bounds.H < 0.0001f) return;

        float span = Mathf.Max(bounds.W, bounds.H);
        float margin = MapDataService.ScaleConfig.GetMargin(span);
        Vector2 uvMin = new(Mathf.Max(0f, bounds.MinU - margin), Mathf.Max(0f, bounds.MinV - margin));
        Vector2 uvMax = new(Mathf.Min(1f, bounds.MinU + bounds.W + margin), Mathf.Min(1f, bounds.MinV + bounds.H + margin));
        ExpandWindowToMinSpan(ref uvMin, ref uvMax, span);
        mapView.SetUVWindow(uvMin, uvMax);

        float worldW = (uvMax.X - uvMin.X) * WorldMapWidth;
        float worldH = (uvMax.Y - uvMin.Y) * WorldMapHeight;
        float aspect = worldW / Mathf.Max(worldH, 0.01f);
        float natH = NationalMeshWidth / Mathf.Max(aspect, 0.01f);
        Vector2 meshSize = new(NationalMeshWidth, natH);
        mapView.SetMeshSize(meshSize);

        var mapMesh = mapView.MapMesh;
        // meshY=0: terrain heights are absolute (ocean at Y=1, land at Y=1.3+)
        float meshY = 0f;

        // Generate 3D terrain mesh from heightmap data
        float uvSpan = Mathf.Max(uvMax.X - uvMin.X, uvMax.Y - uvMin.Y);
        float terrainHeight = ComputeTerrainHeight(uvSpan, meshSize);
        var collisionShape = mapView.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");

        ArrayMesh generatedMesh = TerrainGenerator.Generate(
            mapMesh,
            mapView.HeightMapImage,
            mapView.WaterMapImage,
            meshSize,
            uvMin, uvMax,
            terrainHeight,
            collisionShape,
            512);

        if (mapMesh != null)
            mapMesh.Position = new Vector3(0, meshY, 0);

        ConfigureClones(mapView, meshSize, meshY, generatedMesh);
        ConfigureOcean(mapView, meshSize);
        ConfigureDecorations(mapView, meshSize);
        ConfigureShader(mapView, uvMin, uvMax, countryIdx);
        ConfigureCamera(mapView, meshSize);

        GD.Print($"[MapBuilder] National view built for {MapDataService.CountryCatalog[countryIdx]} (terrain height: {terrainHeight:F1})");
    }

    /// <summary>
    /// Computes terrain height scale based on UV span and mesh size.
    /// Larger countries get taller mountains; tiny countries get subtle relief.
    /// </summary>
    private static float ComputeTerrainHeight(float uvSpan, Vector2 meshSize)
    {
        // Base: proportional to the smaller mesh dimension, clamped to reasonable range
        float baseDim = Mathf.Min(meshSize.X, meshSize.Y);
        // Scale height relative to mesh size — about 3-8% of mesh width
        float heightRatio = Mathf.Clamp(uvSpan * 0.8f, 0.015f, 0.25f);
        float terrainHeight = Mathf.Clamp(baseDim * heightRatio * 0.15f, 2f, 25f);
        return terrainHeight;
    }

    private static void ConfigureClones(MapView v, Vector2 s, float y, ArrayMesh terrainMesh = null)
    {
        var l = v.GetNodeOrNull<MeshInstance3D>("CountryMapL");
        var r = v.GetNodeOrNull<MeshInstance3D>("CountryMapR");
        if (l != null)
        {
            l.Position = new Vector3(-s.X, y, 0);
            if (terrainMesh != null) l.Mesh = terrainMesh;
        }
        if (r != null)
        {
            r.Position = new Vector3(s.X, y, 0);
            if (terrainMesh != null) r.Mesh = terrainMesh;
        }
    }

    // ConfigureCollision is now handled by TerrainGenerator (ConcavePolygonShape3D)
    // Kept as fallback for cases where terrain generation is skipped.
    private static void ConfigureCollisionFallback(MapView v, Vector2 s)
    {
        var cs = v.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        if (cs != null) { cs.Shape = new BoxShape3D { Size = new Vector3(s.X, 1f, s.Y) }; cs.Position = new Vector3(0, 0.5f, 0); }
    }

    private static void ConfigureOcean(MapView v, Vector2 s)
    {
        var o = v.GetNodeOrNull<MeshInstance3D>("Ocean");
        if (o?.Mesh is PlaneMesh om) { om.Size = s; o.Position = Vector3.Zero; }
        var ol = v.GetNodeOrNull<MeshInstance3D>("OceanLeft");
        var or2 = v.GetNodeOrNull<MeshInstance3D>("OceanRight");
        if (ol != null && o != null) ol.Position = new Vector3(-s.X, o.Position.Y, 0);
        if (or2 != null && o != null) or2.Position = new Vector3(s.X, o.Position.Y, 0);
    }

    private static void ConfigureDecorations(MapView v, Vector2 s)
    {
        var paper = v.GetNodeOrNull<MeshInstance3D>("Paper");
        if (paper?.Mesh is PlaneMesh pp) pp.Size = new Vector2(s.X * 3f + 100f, s.Y + 100f);
        var table = v.GetNodeOrNull<MeshInstance3D>("Table");
        if (table?.Mesh is PlaneMesh tp) tp.Size = new Vector2(s.X * 3f + 500f, s.Y + 500f);
    }

    private static void ConfigureShader(MapView v, Vector2 uvMin, Vector2 uvMax, int cIdx)
    {
        var mat = v.GetMapMaterial();
        if (mat == null) return;
        mat.SetShaderParameter("country_uv_min", uvMin);
        mat.SetShaderParameter("country_uv_max", uvMax);
        MapTextureService.InjectTextures(mat);

        float uvSpan = Mathf.Max(uvMax.X - uvMin.X, uvMax.Y - uvMin.Y);

        // Dynamic detail scale — smaller UV span = higher detail tiling
        float zf = 1.0f / Mathf.Max(uvSpan / 0.72f, 0.01f);
        mat.SetShaderParameter("detail_scale", Mathf.Clamp(zf * 48.0f, 180.0f, 1600.0f));

        // Disable vertex displacement — the ArrayMesh already has real height geometry
        mat.SetShaderParameter("use_height_displacement", false);
        mat.SetShaderParameter("height_scale", 0.0f);
        mat.SetShaderParameter("detail_displacement_scale", 0.0f);

        mat.SetShaderParameter("focus_radius", 0.48f);
        mat.SetShaderParameter("focus_feather", 0.18f);
        mat.SetShaderParameter("enable_global_overview", false);
        mat.SetShaderParameter("brightness_mult", 1.4f);
        mat.SetShaderParameter("fog_strength", 0.15f);
        mat.SetShaderParameter("overview_dim", 0.15f);
        mat.SetShaderParameter("political_edge_aa", 0.8f);

        Color col = MapTextureService.GetCountryColor(cIdx);
        mat.SetShaderParameter("selection_color", new Vector3(col.R, col.G, col.B));
    }

    private static void ConfigureCamera(MapView v, Vector2 s)
    {
        var cam = v.GetViewport().GetCamera3D() as PLVSVLTRA.Camera.StrategyCamera;
        if (cam == null) return;
        cam.MapWidth = s.X;
        float h = Mathf.Clamp(Mathf.Max(s.X, s.Y) * 0.78f, 38f, 280f);
        cam.GlobalPosition = new Vector3(0, h, s.Y * 0.1f);
        cam.LookAt(Vector3.Zero);
        cam.SetTargetState(cam.GlobalPosition, cam.RotationDegrees);
    }

    // ============================================================
    // MICRO VIEW (state-level focus)
    // ============================================================

    public static void BuildMicroView(MapView mapView, int countryIdx, int stateIdx)
    {
        if (countryIdx < 0 || countryIdx >= MapDataService.CountryCatalog.Length) return;

        // Use state bounds if available, fallback to country
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

        float worldW = (uvMax.X - uvMin.X) * WorldMapWidth;
        float worldH = (uvMax.Y - uvMin.Y) * WorldMapHeight;
        float aspect = worldW / Mathf.Max(worldH, 0.01f);
        float microH = MicroMeshWidth / Mathf.Max(aspect, 0.01f);
        Vector2 meshSize = new(MicroMeshWidth, microH);
        mapView.SetMeshSize(meshSize);

        // Micro scene uses "StateMap" mesh
        var mapMesh = mapView.MapMesh;
        // meshY=0: terrain heights are absolute
        float meshY = 0f;

        // Generate 3D terrain mesh from heightmap data
        float uvSpan = Mathf.Max(uvMax.X - uvMin.X, uvMax.Y - uvMin.Y);
        float terrainHeight = ComputeTerrainHeight(uvSpan, meshSize);
        var collisionShape = mapView.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");

        TerrainGenerator.Generate(
            mapMesh,
            mapView.HeightMapImage,
            mapView.WaterMapImage,
            meshSize,
            uvMin, uvMax,
            terrainHeight,
            collisionShape,
            512);

        if (mapMesh != null)
            mapMesh.Position = new Vector3(0, meshY, 0);

        ConfigureMicroShader(mapView, uvMin, uvMax, countryIdx, stateIdx);
        ConfigureCamera(mapView, meshSize);

        string stateLabel = (stateIdx >= 0 && stateIdx < MapDataService.StateCatalog.Length)
            ? MapDataService.StateCatalog[stateIdx]
            : "unknown";
        GD.Print($"[MapBuilder] Micro view built for {MapDataService.CountryCatalog[countryIdx]}/{stateLabel} (terrain height: {terrainHeight:F1})");
    }

    private static void ConfigureMicroShader(MapView v, Vector2 uvMin, Vector2 uvMax, int cIdx, int sIdx)
    {
        var mat = v.GetMapMaterial();
        if (mat == null) return;
        mat.SetShaderParameter("country_uv_min", uvMin);
        mat.SetShaderParameter("country_uv_max", uvMax);
        MapTextureService.InjectTextures(mat);

        float uvSpan = Mathf.Max(uvMax.X - uvMin.X, uvMax.Y - uvMin.Y);

        // Higher detail tiling for micro
        float zf = 1.0f / Mathf.Max(uvSpan / 0.72f, 0.01f);
        mat.SetShaderParameter("detail_scale", Mathf.Clamp(zf * 80.0f, 300.0f, 2400.0f));

        // Disable vertex displacement — the ArrayMesh already has real height geometry
        mat.SetShaderParameter("use_height_displacement", false);
        mat.SetShaderParameter("height_scale", 0.0f);
        mat.SetShaderParameter("detail_displacement_scale", 0.0f);

        mat.SetShaderParameter("focus_radius", 0.48f);
        mat.SetShaderParameter("focus_feather", 0.15f);
        mat.SetShaderParameter("enable_global_overview", false);
        mat.SetShaderParameter("brightness_mult", 1.5f);
        mat.SetShaderParameter("fog_strength", 0.1f);
        mat.SetShaderParameter("overview_dim", 0.1f);
        mat.SetShaderParameter("political_edge_aa", 0.8f);

        Color col = MapTextureService.GetCountryColor(cIdx);
        mat.SetShaderParameter("selection_color", new Vector3(col.R, col.G, col.B));
    }

    private static void ExpandWindowToMinSpan(ref Vector2 uvMin, ref Vector2 uvMax, float sourceSpan)
    {
        // For tiny countries/states, force a minimum zoom window so they occupy more screen area.
        if (sourceSpan >= SmallCountryMinSpan) return;

        Vector2 center = (uvMin + uvMax) * 0.5f;
        float half = SmallCountryMinSpan * 0.5f;
        Vector2 newMin = center - new Vector2(half, half);
        Vector2 newMax = center + new Vector2(half, half);

        // Clamp to world UV bounds while keeping window size stable where possible.
        if (newMin.X < 0f) { newMax.X -= newMin.X; newMin.X = 0f; }
        if (newMin.Y < 0f) { newMax.Y -= newMin.Y; newMin.Y = 0f; }
        if (newMax.X > 1f) { newMin.X -= (newMax.X - 1f); newMax.X = 1f; }
        if (newMax.Y > 1f) { newMin.Y -= (newMax.Y - 1f); newMax.Y = 1f; }

        uvMin = new Vector2(Mathf.Clamp(newMin.X, 0f, 1f), Mathf.Clamp(newMin.Y, 0f, 1f));
        uvMax = new Vector2(Mathf.Clamp(newMax.X, 0f, 1f), Mathf.Clamp(newMax.Y, 0f, 1f));
    }
}
