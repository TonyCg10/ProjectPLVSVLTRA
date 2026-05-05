using Godot;
using System;
using Engine.Services;

namespace PLVSVLTRA.Map;

/// <summary>
/// Handles map interaction: overlay shader setup, click detection via Terrain3D collision,
/// ID map queries. Attached to the root node of each map scene.
/// Now works with Terrain3D for geometry and a PoliticalOverlay mesh for visual overlays.
/// </summary>
public partial class MapView : Node3D
{
    [Export] public MeshInstance3D OverlayMesh { get; set; }
    [Export] public int InitialMapMode { get; set; } = 3; // 0=Nodes, 1=Countries, 2=States, 3=Natural

    private ShaderMaterial _overlayMaterial;
    private Image _idMapImage;
    private Vector2 _idMapSize;
    private int _currentMapMode;

    // UV window mapping (full world = 0,0 to 1,1; national = country bounds)
    private Vector2 _countryUVMin = Vector2.Zero;
    private Vector2 _countryUVMax = Vector2.One;
    private Vector2 _meshSize = new Vector2(1024, 512);

    public Vector2 CountryUVMin => _countryUVMin;
    public Vector2 CountryUVMax => _countryUVMax;
    public Vector2 MeshSize => _meshSize;

    public override void _Ready()
    {
        GD.Print("[MapView] Initializing map...");
        _currentMapMode = InitialMapMode;

        // Find overlay mesh
        if (OverlayMesh == null)
        {
            OverlayMesh = GetNodeOrNull<MeshInstance3D>("PoliticalOverlay");
        }

        if (OverlayMesh != null)
        {
            _overlayMaterial = (ShaderMaterial)OverlayMesh.GetActiveMaterial(0);
            if (_overlayMaterial == null && OverlayMesh.Mesh != null)
                _overlayMaterial = (ShaderMaterial)OverlayMesh.Mesh.SurfaceGetMaterial(0);

            if (_overlayMaterial != null)
            {
                // Load world data texture for shader
                string dataFolder = ProjectSettings.GlobalizePath("res://Data");
                MapTextureService.LoadWorldDataTexture(dataFolder);

                // Generate lookup textures
                MapTextureService.SetupShaderLookups(_overlayMaterial);

                // Extract ID map image for click detection
                var tex = _overlayMaterial.GetShaderParameter("id_map").As<Texture2D>();
                if (tex != null)
                {
                    _idMapImage = tex.GetImage();
                    _idMapSize = new Vector2(_idMapImage.GetWidth(), _idMapImage.GetHeight());
                    MapTextureService.ScanBoundsFromImage(_idMapImage);
                }

                GD.Print("[MapView] Overlay textures and ID map ready.");
            }
        }

        // Build the appropriate view based on current zoom level
        var portal = GetNodeOrNull<PLVSVLTRA.Autoload.PortalManager>("/root/PortalManager");
        if (portal != null)
        {
            int countryIdx = portal.PendingCountryIdx != -1 ? portal.PendingCountryIdx : portal.ActiveCountryIdx;

            if (portal.CurrentLevel == PLVSVLTRA.Autoload.PortalManager.ZoomLevel.International)
            {
                MapBuilder.BuildInternationalView(this);
            }
            else if (portal.CurrentLevel == PLVSVLTRA.Autoload.PortalManager.ZoomLevel.National && countryIdx != -1)
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
        else
        {
            // No portal manager — assume International
            MapBuilder.BuildInternationalView(this);
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
        Vector3 to = from + camera.ProjectRayNormal(mousePos) * 5000;

        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var result = spaceState.IntersectRay(query);

        if (result.Count > 0)
        {
            Vector3 hitPos = (Vector3)result["position"];
            // Convert world hit position to UV coordinates
            Vector2 worldUV = WorldPosToUV(hitPos);

            if (worldUV.X >= 0 && worldUV.X <= 1 && worldUV.Y >= 0 && worldUV.Y <= 1)
            {
                ProcessNodeAtUV(worldUV);
            }
        }
    }

    /// <summary>
    /// Converts a world-space hit position to UV coordinates.
    /// For Terrain3D, we compute UV from the terrain bounds.
    /// </summary>
    private Vector2 WorldPosToUV(Vector3 worldPos)
    {
        float halfW = _meshSize.X / 2f;
        float halfH = _meshSize.Y / 2f;

        // Since _meshSize is the full world size (8192x4096), this is exactly the world UV
        float localU = (worldPos.X + halfW) / _meshSize.X;
        float localV = (worldPos.Z + halfH) / _meshSize.Y;

        return new Vector2(localU, localV);
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
        if (_overlayMaterial == null) return;

        _overlayMaterial.SetShaderParameter("selected_country_idx", cIdx);
        _overlayMaterial.SetShaderParameter("selected_state_idx", sIdx);

        Color col = MapTextureService.GetCountryColor(cIdx);
        _overlayMaterial.SetShaderParameter("selection_color", new Vector3(col.R, col.G, col.B));
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
        Vector3 to = from + camera.ProjectRayNormal(center) * 5000;

        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var result = spaceState.IntersectRay(query);

        if (result.Count > 0)
        {
            Vector3 hitPos = (Vector3)result["position"];
            Vector2 worldUV = WorldPosToUV(hitPos);
            if (worldUV.X >= 0 && worldUV.X <= 1 && worldUV.Y >= 0 && worldUV.Y <= 1)
                return worldUV;
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

    public ShaderMaterial GetOverlayMaterial() => _overlayMaterial;

    // Keep old name as alias for compatibility
    public ShaderMaterial GetMapMaterial() => _overlayMaterial;

    public void SetUVWindow(Vector2 uvMin, Vector2 uvMax)
    {
        _countryUVMin = uvMin;
        _countryUVMax = uvMax;
    }

    public void SetMeshSize(Vector2 size) => _meshSize = size;

    public void SetMapMode(int mode)
    {
        _currentMapMode = mode;
        if (_overlayMaterial != null)
        {
            _overlayMaterial.SetShaderParameter("map_mode", mode);
            _overlayMaterial.SetShaderParameter("selected_country_idx", -1);
            _overlayMaterial.SetShaderParameter("selected_state_idx", -1);
        }
    }
}
