using Godot;
using Engine.Services;
using System.Collections.Generic;
using ProjectPLVSVLTRA.Core;

namespace ProjectPLVSVLTRA.UI.Map;

public partial class MapController : StaticBody3D
{
    private const float WorldMapWidth = 540f;
    private const float WorldMapHeight = 270f;
    private const float NationalMeshWidth = 420f;
    private const float TargetScreenFill = 0.72f;

    [Export] public MeshInstance3D IdMapMesh;
    private ShaderMaterial _mapMaterial;
    private Image _idMapImage;
    private Vector2 _idMapSize;
    private int _currentMapMode = 3;
    private int _selectedCountryIdx = -1;
    private int _selectedStateIdx = -1;

    // Dimensiones dinámicas del mesh actual
    private Vector2 _meshSize = new Vector2(WorldMapWidth, WorldMapHeight);
    private Vector2 _countryUVMin = Vector2.Zero;
    private Vector2 _countryUVMax = Vector2.One;

    public override void _Ready()
    {
        GD.Print("[MapController] Inicializando mapa...");

        if (IdMapMesh == null)
            IdMapMesh = GetNode<MeshInstance3D>("GlobalMap");

        if (IdMapMesh != null)
        {
            _mapMaterial = (ShaderMaterial)IdMapMesh.GetActiveMaterial(0);
            if (_mapMaterial == null) 
                _mapMaterial = (ShaderMaterial)IdMapMesh.Mesh.SurfaceGetMaterial(0);

            if (_mapMaterial != null)
            {
                DataService.GenerateLookupTextures(_mapMaterial);
                
                var tex = _mapMaterial.GetShaderParameter("id_map").As<Texture2D>();
                if (tex != null)
                {
                    _idMapImage = tex.GetImage();
                    _idMapSize = new Vector2(_idMapImage.GetWidth(), _idMapImage.GetHeight());
                    DataService.ScanCountryBounds(_idMapImage);
                }
                
                GD.Print("[MapController] Texturas de consulta e imagen de IDs preparadas.");
            }
        }

        ConnectUI();

        int initialCountryIdx = PortalManager.PendingCountryIdx != -1
            ? PortalManager.PendingCountryIdx
            : PortalManager.ActiveCountryIdx;

        if (initialCountryIdx != -1)
        {
            BuildNationalMap(initialCountryIdx);
            PortalManager.PendingCountryIdx = -1;
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
            Vector3 localPos = IdMapMesh.ToLocal(hitPos);
            
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

        int px = (int)(uv.X * _idMapSize.X);
        int py = (int)(uv.Y * _idMapSize.Y);
        
        px = Mathf.Clamp(px, 0, (int)_idMapSize.X - 1);
        py = Mathf.Clamp(py, 0, (int)_idMapSize.Y - 1);

        Color idCol = _idMapImage.GetPixel(px, py);
        
        int nodeId = (int)Mathf.Round(idCol.R * 255f) + 
                     ((int)Mathf.Round(idCol.G * 255f) * 256) + 
                     ((int)Mathf.Round(idCol.B * 255f) * 65536);
        
        int arrayIdx = nodeId - 1;

        if (arrayIdx >= 0 && arrayIdx < DataService.Nodes.Length)
        {
            var node = DataService.Nodes[arrayIdx];
            string countryId = DataService.CountryCatalog[node.CountryIdx];
            string stateId   = DataService.StateCatalog[node.StateIdx];

            GD.Print($"[Map] Click en Nodo {nodeId} | País: {countryId} | Estado: {stateId}");
            HighlightSelection(node.CountryIdx, node.StateIdx);
        }
    }

    private void HighlightSelection(int cIdx, int sIdx)
    {
        if (_mapMaterial == null) return;

        _mapMaterial.SetShaderParameter("selected_country_idx", cIdx);
        _mapMaterial.SetShaderParameter("selected_state_idx", sIdx);
        
        Color rawCol = (_currentMapMode == 1) ? DataService.CountryPalette[cIdx] : DataService.StatePalette[sIdx];
        
        float r = Mathf.Floor(rawCol.R * 255f) / 255f;
        float g = Mathf.Floor(rawCol.G * 255f) / 255f;
        float b = Mathf.Floor(rawCol.B * 255f) / 255f;

        _mapMaterial.SetShaderParameter("selection_color", new Vector3(r, g, b));
    }

    private void ConnectUI()
    {
        var btnNodos   = GetNodeOrNull<Button>("UI/HBox/BtnNodos");
        var btnPaises  = GetNodeOrNull<Button>("UI/HBox/BtnPaises");
        var btnEstados = GetNodeOrNull<Button>("UI/HBox/BtnEstados");
        var btnNatural = GetNodeOrNull<Button>("UI/HBox/BtnNatural");

        if (btnNodos != null)   btnNodos.Pressed += () => SetMapMode(0);
        if (btnPaises != null)  btnPaises.Pressed += () => SetMapMode(1);
        if (btnEstados != null) btnEstados.Pressed += () => SetMapMode(2);
        if (btnNatural != null) btnNatural.Pressed += () => SetMapMode(3);
    }

    public void SetMapMode(int mode)
    {
        _currentMapMode = mode;
        if (_mapMaterial != null)
        {
            _mapMaterial.SetShaderParameter("map_mode", mode);
            _mapMaterial.SetShaderParameter("selected_country_idx", -1);
            _mapMaterial.SetShaderParameter("selected_state_idx", -1);
            GD.Print($"[MapController] Modo de mapa cambiado a: {mode}");
        }
    }

    // =========================================
    // DETECCIÓN DE PAÍS EN PANTALLA
    // =========================================

    /// <summary>
    /// Obtiene el país y estado en el centro exacto de la pantalla.
    /// </summary>
    public (int countryIdx, int stateIdx) GetTargetAtScreenCenter()
    {
        Vector2 worldUV = GetWorldUVAtScreenCenter();
        if (worldUV.X < 0) return (-1, -1);
        return GetNodeInfoAtWorldUV(worldUV);
    }

    /// <summary>
    /// Busca el país más cercano al centro de la pantalla.
    /// Si el centro es agua, busca en espiral hasta encontrar tierra.
    /// </summary>
    public int GetNearestCountryToScreenCenter()
    {
        // 1. Intentar centro exacto
        var (countryIdx, _) = GetTargetAtScreenCenter();
        if (countryIdx != -1) return countryIdx;

        // 2. Obtener UV del centro para buscar alrededor
        Vector2 centerUV = GetWorldUVAtScreenCenter();
        if (centerUV.X < 0 || _idMapImage == null) return -1;

        int cx = (int)(centerUV.X * _idMapSize.X);
        int cy = (int)(centerUV.Y * _idMapSize.Y);
        int maxW = (int)_idMapSize.X - 1;
        int maxH = (int)_idMapSize.Y - 1;

        // 3. Buscar en espiral expandiente (8 direcciones × radios crecientes)
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
                if (idx >= 0 && idx < DataService.Nodes.Length)
                {
                    int foundCountry = DataService.Nodes[idx].CountryIdx;
                    GD.Print($"[MapController] País cercano encontrado a radio {r}: {DataService.CountryCatalog[foundCountry]}");
                    return foundCountry;
                }
            }
        }

        GD.Print("[MapController] No se encontró ningún país cercano al centro.");
        return -1;
    }

    /// <summary>
    /// Obtiene la UV del mapa mundial en el punto central de la pantalla via raycast.
    /// </summary>
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
            Vector3 localPos = IdMapMesh.ToLocal(hitPos);

            float halfW = _meshSize.X / 2f;
            float halfH = _meshSize.Y / 2f;

            float localU = (localPos.X + halfW) / _meshSize.X;
            float localV = (localPos.Z + halfH) / _meshSize.Y;

            if (localU >= 0 && localU <= 1 && localV >= 0 && localV <= 1)
            {
                return LocalToWorldUV(new Vector2(localU, localV));
            }
        }

        return new Vector2(-1, -1);
    }

    /// <summary>
    /// Dado un UV en el espacio del mapa mundial, retorna el país y estado.
    /// </summary>
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
        if (idx >= 0 && idx < DataService.Nodes.Length)
        {
            var node = DataService.Nodes[idx];
            return (node.CountryIdx, node.StateIdx);
        }

        return (-1, -1);
    }

    // =========================================
    // CONSTRUCCIÓN DEL MAPA NACIONAL
    // =========================================
    public void BuildNationalMap(int countryIdx)
    {
        if (countryIdx < 0 || countryIdx >= DataService.CountryCatalog.Length) return;

        Rect2 bounds = DataService.GetCountryBounds(countryIdx);
        if (bounds.Size.Length() == 0) return;

        _countryUVMin = bounds.Position;
        _countryUVMax = bounds.Position + bounds.Size;

        // Seguridad: Si los bounds son inválidos, usar el mundo completo
        if (bounds.Size.Length() < 0.0001f)
        {
            _countryUVMin = Vector2.Zero;
            _countryUVMax = Vector2.One;
        }

        // 1. Margen dinámico: los países pequeños necesitan un margen UV mínimo 
        // para que se vea suficiente océano y contexto alrededor.
        float minMarginUV = 0.03f; // ~3% del mapa mundial como mínimo absoluto
        Vector2 marginVec = new Vector2(
            Mathf.Max(bounds.Size.X * 0.35f, minMarginUV),
            Mathf.Max(bounds.Size.Y * 0.35f, minMarginUV)
        );

        _countryUVMin = new Vector2(
            Mathf.Max(0f, _countryUVMin.X - marginVec.X),
            Mathf.Max(0f, _countryUVMin.Y - marginVec.Y)
        );
        _countryUVMax = new Vector2(
            Mathf.Min(1f, _countryUVMax.X + marginVec.X),
            Mathf.Min(1f, _countryUVMax.Y + marginVec.Y)
        );

        float worldWidth = (_countryUVMax.X - _countryUVMin.X) * WorldMapWidth;
        float worldHeight = (_countryUVMax.Y - _countryUVMin.Y) * WorldMapHeight;
        float aspect = worldWidth / Mathf.Max(worldHeight, 0.01f);

        // 2. Tamaño del mesh fijo (400) para evitar que la cámara colapse.
        // Cuba y Brasil ocuparán el mismo espacio en pantalla, pero Cuba
        // representará una zona mucho más pequeña del mundo real.
        float nationalWidth = NationalMeshWidth;
        float nationalHeight = nationalWidth / Mathf.Max(aspect, 0.01f);
        _meshSize = new Vector2(nationalWidth, nationalHeight);

        // 3. Crear terrain mesh o usar el existente
        if (IdMapMesh.Mesh is PlaneMesh pMesh)
        {
            pMesh.Size = _meshSize;
        }
        IdMapMesh.Position = new Vector3(0, 0.05f, 0);

        // Posicionar clones del mapa infinito
        var mapL = GetNodeOrNull<MeshInstance3D>("GlobalMapLeft");
        var mapR = GetNodeOrNull<MeshInstance3D>("GlobalMapRight");
        if (mapL != null) mapL.Position = new Vector3(-_meshSize.X, IdMapMesh.Position.Y, 0);
        if (mapR != null) mapR.Position = new Vector3(_meshSize.X, IdMapMesh.Position.Y, 0);

        // 4. Collision shape
        var collisionShape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        if (collisionShape != null)
        {
            var box = new BoxShape3D();
            box.Size = new Vector3(_meshSize.X, 1f, _meshSize.Y);
            collisionShape.Shape = box;
            collisionShape.Position = new Vector3(0, 0.5f, 0);
        }

        // 5. Configurar Water Meshes
        var ocean = GetNodeOrNull<MeshInstance3D>("Ocean");
        if (ocean != null && ocean.Mesh is PlaneMesh oceanMesh)
        {
            oceanMesh.Size = _meshSize; // Comparte el tamaño del mapa
            ocean.Position = new Vector3(0, 0.0f, 0); // Debajo del mapa
        }
        var oceanL = GetNodeOrNull<MeshInstance3D>("OceanLeft");
        var oceanR = GetNodeOrNull<MeshInstance3D>("OceanRight");
        if (oceanL != null && ocean != null) oceanL.Position = new Vector3(-_meshSize.X, ocean.Position.Y, 0);
        if (oceanR != null && ocean != null) oceanR.Position = new Vector3(_meshSize.X, ocean.Position.Y, 0);

        // 6. Configurar Paper y Table dinámicamente
        var paper = GetNodeOrNull<MeshInstance3D>("Paper");
        if (paper != null && paper.Mesh is PlaneMesh paperMesh)
        {
            paperMesh.Size = new Vector2(_meshSize.X * 3f + 100f, _meshSize.Y + 100f);
        }
        
        var table = GetNodeOrNull<MeshInstance3D>("Table");
        if (table != null && table.Mesh is PlaneMesh tableMesh)
        {
            tableMesh.Size = new Vector2(_meshSize.X * 3f + 500f, _meshSize.Y + 500f);
        }

        // 7. Configurar shader del terreno
        if (_mapMaterial != null)
        {
            _mapMaterial.SetShaderParameter("country_uv_min", _countryUVMin);
            _mapMaterial.SetShaderParameter("country_uv_max", _countryUVMax);

            float uvSpan = Mathf.Max(_countryUVMax.X - _countryUVMin.X, _countryUVMax.Y - _countryUVMin.Y);
            float normalizedSpan = Mathf.Max(uvSpan / TargetScreenFill, 0.01f);
            float zoomFactor = 1.0f / normalizedSpan;
            float dynamicDetailScale = Mathf.Clamp(zoomFactor * 48.0f, 180.0f, 1600.0f);
            _mapMaterial.SetShaderParameter("detail_scale", dynamicDetailScale);
            _mapMaterial.SetShaderParameter("focus_radius", 0.43f);
            _mapMaterial.SetShaderParameter("focus_feather", 0.15f);
            _mapMaterial.SetShaderParameter("overview_blur_px", 2.5f);
            _mapMaterial.SetShaderParameter("overview_dim", 0.52f);
            _mapMaterial.SetShaderParameter("fog_strength", 0.62f);
            _mapMaterial.SetShaderParameter("brightness_mult", 1.85f);

            Color col = DataService.CountryPalette[countryIdx];
            _mapMaterial.SetShaderParameter("selection_color", new Vector3(col.R, col.G, col.B));
        }

        // 8. Aplicar LOD (Level of Detail) a los clones laterales
        ApplyLODToClones();

        // 8. Cámara
        var camera = GetViewport().GetCamera3D() as CameraManager;
        if (camera != null)
        {
            // IMPORTANTÍSIMO: Actualizar la distancia de envoltura infinita
            camera.MapWidth = _meshSize.X;

            // Altura de cámara proporcional al tamaño del país, pero con límites sanos
            float camHeight = Mathf.Max(_meshSize.X, _meshSize.Y) * 0.78f;
            camHeight = Mathf.Clamp(camHeight, 38f, 280f);

            camera.GlobalPosition = new Vector3(0, camHeight, _meshSize.Y * 0.1f);
            camera.LookAt(Vector3.Zero);
            camera.SetTargetState(camera.GlobalPosition, camera.RotationDegrees);
        }
    }

    private void ApplyLODToClones()
    {
        var mapL = GetNodeOrNull<MeshInstance3D>("GlobalMapLeft");
        var mapR = GetNodeOrNull<MeshInstance3D>("GlobalMapRight");

        if (mapL == null || mapR == null) return;

        // Crear un mesh de baja resolución para los clones que están lejos
        var lowResMesh = new PlaneMesh
        {
            Size = _meshSize,
            SubdivideWidth = 128,
            SubdivideDepth = 128
        };
        lowResMesh.SurfaceSetMaterial(0, _mapMaterial);

        mapL.Mesh = lowResMesh;
        mapR.Mesh = lowResMesh;
        
        GD.Print("[MapController] LOD aplicado a los clones laterales (128x128).");
    }
}
