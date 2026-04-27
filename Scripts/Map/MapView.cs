using Godot;
using System;
using Engine.Services;

namespace PLVSVLTRA.Map;

/// <summary>
/// Handles map mesh interaction: shader setup, click detection, ID map queries.
/// Attached to the StaticBody3D root of each map scene.
/// </summary>
public partial class MapView : StaticBody3D
{
    [Export] public MeshInstance3D MapMesh { get; set; }
    [Export] public int InitialMapMode { get; set; } = 3; // 0=Nodes, 1=Countries, 2=States, 3=Natural

    private ShaderMaterial _mapMaterial;
    private Image _idMapImage;
    private Vector2 _idMapSize;
    private int _currentMapMode;

    // Heightmap and water map images for terrain generation
    public Image HeightMapImage { get; private set; }
    public Image WaterMapImage { get; private set; }

    // UV window mapping (full world = 0,0 to 1,1; national = country bounds)
    private Vector2 _countryUVMin = Vector2.Zero;
    private Vector2 _countryUVMax = Vector2.One;
    private Vector2 _meshSize = new Vector2(540, 270);

    public Vector2 CountryUVMin => _countryUVMin;
    public Vector2 CountryUVMax => _countryUVMax;
    public Vector2 MeshSize => _meshSize;

    public override void _Ready()
    {
        GD.Print("[MapView] Initializing map...");
        _currentMapMode = InitialMapMode;

        if (MapMesh == null)
        {
            MapMesh = GetNodeOrNull<MeshInstance3D>("WorldMap")
                   ?? GetNodeOrNull<MeshInstance3D>("CountryMap")
                   ?? GetNodeOrNull<MeshInstance3D>("StateMap");
        }

        if (MapMesh != null)
        {
            _mapMaterial = (ShaderMaterial)MapMesh.GetActiveMaterial(0);
            if (_mapMaterial == null)
                _mapMaterial = (ShaderMaterial)MapMesh.Mesh.SurfaceGetMaterial(0);

            if (_mapMaterial != null)
            {
                // Load world data texture for shader
                string dataFolder = ProjectSettings.GlobalizePath("res://Data");
                MapTextureService.LoadWorldDataTexture(dataFolder);

                // Generate lookup textures
                MapTextureService.SetupShaderLookups(_mapMaterial);

                // Extract ID map image for click detection
                var tex = _mapMaterial.GetShaderParameter("id_map").As<Texture2D>();
                if (tex != null)
                {
                    _idMapImage = tex.GetImage();
                    _idMapSize = new Vector2(_idMapImage.GetWidth(), _idMapImage.GetHeight());
                    MapTextureService.ScanBoundsFromImage(_idMapImage);
                }

                // Extract height map image for terrain generation
                var heightTex = _mapMaterial.GetShaderParameter("height_map").As<Texture2D>();
                if (heightTex != null)
                {
                    HeightMapImage = heightTex.GetImage();
                    GD.Print($"[MapView] Height map loaded: {HeightMapImage.GetWidth()}x{HeightMapImage.GetHeight()}");
                }

                // Extract water map image for terrain generation
                var waterTex = _mapMaterial.GetShaderParameter("water_map").As<Texture2D>();
                if (waterTex != null)
                {
                    WaterMapImage = waterTex.GetImage();
                    GD.Print($"[MapView] Water map loaded: {WaterMapImage.GetWidth()}x{WaterMapImage.GetHeight()}");
                }

                GD.Print("[MapView] Lookup textures and ID map ready.");
            }
        }

        // Check if we need to build a national/micro view
        var portal = GetNodeOrNull<PLVSVLTRA.Autoload.PortalManager>("/root/PortalManager");
        if (portal != null)
        {
            int countryIdx = portal.PendingCountryIdx != -1 ? portal.PendingCountryIdx : portal.ActiveCountryIdx;

            if (portal.CurrentLevel == PLVSVLTRA.Autoload.PortalManager.ZoomLevel.National && countryIdx != -1)
            {
                MapBuilder.BuildNationalView(this, countryIdx);
                portal.PendingCountryIdx = -1;
            }
            else if (portal.CurrentLevel == PLVSVLTRA.Autoload.PortalManager.ZoomLevel.Micro && countryIdx != -1)
            {
                int stateIdx = portal.PendingStateIdx != -1 ? portal.PendingStateIdx : portal.ActiveStateIdx;
                MapBuilder.BuildMicroView(this, countryIdx, stateIdx);
                portal.PendingCountryIdx = -1;
                portal.PendingStateIdx = -1;
            }
        }
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
        if (camera == null) return;

        Vector3 from = camera.ProjectRayOrigin(mousePos);
        Vector3 to = from + camera.ProjectRayNormal(mousePos) * 1000;

        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var result = spaceState.IntersectRay(query);

        if (result.Count > 0)
        {
            Vector3 hitPos = (Vector3)result["position"];
            Vector3 localPos = MapMesh.ToLocal(hitPos);

            float halfW = _meshSize.X / 2f;
            float halfH = _meshSize.Y / 2f;

            float localU = (localPos.X + halfW) / _meshSize.X;
            float localV = (localPos.Z + halfH) / _meshSize.Y;

            if (localU >= 0 && localU <= 1 && localV >= 0 && localV <= 1)
            {
                Vector2 worldUV = LocalToWorldUV(new Vector2(localU, localV));
                ProcessNodeAtUV(worldUV);
            }
        }
    }

    private Vector2 LocalToWorldUV(Vector2 localUV)
    {
        return _countryUVMin + localUV * (_countryUVMax - _countryUVMin);
    }

    private void ProcessNodeAtUV(Vector2 uv)
    {
        if (_idMapImage == null) return;

        int px = Mathf.Clamp((int)(uv.X * _idMapSize.X), 0, (int)_idMapSize.X - 1);
        int py = Mathf.Clamp((int)(uv.Y * _idMapSize.Y), 0, (int)_idMapSize.Y - 1);

        Color idCol = _idMapImage.GetPixel(px, py);
        int nodeId = (int)Mathf.Round(idCol.R * 255f) +
                     ((int)Mathf.Round(idCol.G * 255f) * 256) +
                     ((int)Mathf.Round(idCol.B * 255f) * 65536);

        int arrayIdx = nodeId - 1;
        if (arrayIdx >= 0 && arrayIdx < MapDataService.Nodes.Length)
        {
            var node = MapDataService.Nodes[arrayIdx];
            string countryId = MapDataService.CountryCatalog[node.CountryIdx];
            string stateId = MapDataService.StateCatalog[node.StateIdx];

            GD.Print($"[MapView] Click on Node {nodeId} | Country: {countryId} | State: {stateId}");
            HighlightSelection(node.CountryIdx, node.StateIdx);
        }
    }

    private void HighlightSelection(int cIdx, int sIdx)
    {
        if (_mapMaterial == null) return;

        _mapMaterial.SetShaderParameter("selected_country_idx", cIdx);
        _mapMaterial.SetShaderParameter("selected_state_idx", sIdx);

        Color col = MapTextureService.GetCountryColor(cIdx);
        _mapMaterial.SetShaderParameter("selection_color", new Vector3(col.R, col.G, col.B));
    }

    /// <summary>
    /// Gets the country and state at the exact screen center.
    /// </summary>
    public (int countryIdx, int stateIdx) GetTargetAtScreenCenter()
    {
        Vector2 worldUV = GetWorldUVAtScreenCenter();
        if (worldUV.X < 0) return (-1, -1);
        return GetNodeInfoAtWorldUV(worldUV);
    }

    /// <summary>
    /// Finds the nearest country to screen center (spiral search if center is water).
    /// </summary>
    public int GetNearestCountryToScreenCenter()
    {
        var (countryIdx, _) = GetTargetAtScreenCenter();
        if (countryIdx != -1) return countryIdx;

        Vector2 centerUV = GetWorldUVAtScreenCenter();
        if (centerUV.X < 0 || _idMapImage == null) return -1;

        int cx = (int)(centerUV.X * _idMapSize.X);
        int cy = (int)(centerUV.Y * _idMapSize.Y);
        int maxW = (int)_idMapSize.X - 1;
        int maxH = (int)_idMapSize.Y - 1;

        int[] radii = { 3, 8, 15, 25, 40, 60, 90, 130 };
        foreach (int r in radii)
        {
            for (int angle = 0; angle < 16; angle++)
            {
                float a = angle * Mathf.Pi / 8f;
                int sx = Mathf.Clamp(cx + (int)(Mathf.Cos(a) * r), 0, maxW);
                int sy = Mathf.Clamp(cy + (int)(Mathf.Sin(a) * r), 0, maxH);

                Color idCol = _idMapImage.GetPixel(sx, sy);
                int nodeId = (int)Mathf.Round(idCol.R * 255f) +
                             ((int)Mathf.Round(idCol.G * 255f) * 256) +
                             ((int)Mathf.Round(idCol.B * 255f) * 65536);

                int idx = nodeId - 1;
                if (idx >= 0 && idx < MapDataService.Nodes.Length)
                {
                    return MapDataService.Nodes[idx].CountryIdx;
                }
            }
        }
        return -1;
    }

    private Vector2 GetWorldUVAtScreenCenter()
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var center = viewportSize / 2f;

        var camera = GetViewport().GetCamera3D();
        if (camera == null) return new Vector2(-1, -1);

        Vector3 from = camera.ProjectRayOrigin(center);
        Vector3 to = from + camera.ProjectRayNormal(center) * 1000;

        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var result = spaceState.IntersectRay(query);

        if (result.Count > 0)
        {
            Vector3 hitPos = (Vector3)result["position"];
            Vector3 localPos = MapMesh.ToLocal(hitPos);

            float halfW = _meshSize.X / 2f;
            float halfH = _meshSize.Y / 2f;

            float localU = (localPos.X + halfW) / _meshSize.X;
            float localV = (localPos.Z + halfH) / _meshSize.Y;

            if (localU >= 0 && localU <= 1 && localV >= 0 && localV <= 1)
                return LocalToWorldUV(new Vector2(localU, localV));
        }
        return new Vector2(-1, -1);
    }

    private (int countryIdx, int stateIdx) GetNodeInfoAtWorldUV(Vector2 worldUV)
    {
        if (_idMapImage == null) return (-1, -1);

        int px = Mathf.Clamp((int)(worldUV.X * _idMapSize.X), 0, (int)_idMapSize.X - 1);
        int py = Mathf.Clamp((int)(worldUV.Y * _idMapSize.Y), 0, (int)_idMapSize.Y - 1);

        Color idCol = _idMapImage.GetPixel(px, py);
        int nodeId = (int)Mathf.Round(idCol.R * 255f) +
                     ((int)Mathf.Round(idCol.G * 255f) * 256) +
                     ((int)Mathf.Round(idCol.B * 255f) * 65536);

        int idx = nodeId - 1;
        if (idx >= 0 && idx < MapDataService.Nodes.Length)
        {
            var node = MapDataService.Nodes[idx];
            return (node.CountryIdx, node.StateIdx);
        }
        return (-1, -1);
    }

    // ── Public API for MapBuilder ──────────────────────────────────────────

    public ShaderMaterial GetMapMaterial() => _mapMaterial;

    public void SetUVWindow(Vector2 uvMin, Vector2 uvMax)
    {
        _countryUVMin = uvMin;
        _countryUVMax = uvMax;
    }

    public void SetMeshSize(Vector2 size) => _meshSize = size;

    public void SetMapMode(int mode)
    {
        _currentMapMode = mode;
        if (_mapMaterial != null)
        {
            _mapMaterial.SetShaderParameter("map_mode", mode);
            _mapMaterial.SetShaderParameter("selected_country_idx", -1);
            _mapMaterial.SetShaderParameter("selected_state_idx", -1);
        }
    }
}
