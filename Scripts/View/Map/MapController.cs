using Godot;
using Engine.Services;
using System.Collections.Generic;
using ProjectPLVSVLTRA.Core;

namespace ProjectPLVSVLTRA.UI.Map;

public partial class MapController : StaticBody3D
{
    [Export] public MeshInstance3D IdMapMesh;
    private ShaderMaterial _mapMaterial;
    private Image _idMapImage;
    private Vector2 _idMapSize;
    private int _currentMapMode = 3; // Default to Natural/Satellite
    private int _selectedCountryIdx = -1;
    private int _selectedStateIdx = -1;

    public override void _Ready()
    {
        GD.Print("[MapController] Inicializando mapa líquido...");

        // 1. Cargamos el motor y los datos
        DataService.LoadFullWorld(".");

        // 2. Obtener el material del Mesh de IDs
        if (IdMapMesh == null)
            IdMapMesh = GetNode<MeshInstance3D>("GlobalMap");

        if (IdMapMesh != null)
        {
            _mapMaterial = (ShaderMaterial)IdMapMesh.GetActiveMaterial(0);
            if (_mapMaterial == null) 
                _mapMaterial = (ShaderMaterial)IdMapMesh.Mesh.SurfaceGetMaterial(0);

            if (_mapMaterial != null)
            {
                // 3. Generar e inyectar las texturas
                DataService.GenerateLookupTextures(_mapMaterial);
                
                // Cargar imagen de IDs para lectura de clicks
                var tex = _mapMaterial.GetShaderParameter("id_map").As<Texture2D>();
                if (tex != null)
                {
                    _idMapImage = tex.GetImage();
                    _idMapSize = new Vector2(_idMapImage.GetWidth(), _idMapImage.GetHeight());
                    
                    // Escanear límites para el sistema de enfoque
                    DataService.ScanCountryBounds(_idMapImage);
                }
                
                GD.Print("[MapController] Texturas de consulta e imagen de IDs preparadas.");
            }
        }

        // 4. Conectar botones de la UI si existen
        ConnectUI();

        // 5. Aplicar enfoque si venimos de un portal
        if (PortalManager.PendingCountryIdx != -1)
        {
            FocusCameraOnCountry(PortalManager.PendingCountryIdx);
            PortalManager.PendingCountryIdx = -1; // Limpiar
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

        // Raycast para encontrar el punto en el mapa
        Vector3 from = camera.ProjectRayOrigin(mousePos);
        Vector3 to = from + camera.ProjectRayNormal(mousePos) * 1000;

        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var result = spaceState.IntersectRay(query);

        if (result.Count > 0)
        {
            Vector3 hitPos = (Vector3)result["position"];
            
            // Convertir posición 3D a UV relativa al MeshInstance3D
            // Usamos el IdMapMesh para ToLocal para ser precisos
            Vector3 localPos = IdMapMesh.ToLocal(hitPos);
            
            // El PlaneMesh tiene un tamaño de (540, 270)
            float halfW = 540f / 2f;
            float halfH = 270f / 2f;
            
            float u = (localPos.X + halfW) / 540f;
            float v = (localPos.Z + halfH) / 270f;
            
            if (u >= 0 && u <= 1 && v >= 0 && v <= 1)
            {
                ProcessNodeAtUV(new Vector2(u, v));
            }
        }
    }

    private void ProcessNodeAtUV(Vector2 uv)
    {
        if (_idMapImage == null) return;

        int px = (int)(uv.X * _idMapSize.X);
        int py = (int)(uv.Y * _idMapSize.Y);
        
        px = Mathf.Clamp(px, 0, (int)_idMapSize.X - 1);
        py = Mathf.Clamp(py, 0, (int)_idMapSize.Y - 1);

        Color idCol = _idMapImage.GetPixel(px, py);
        
        // Reconstruir ID (mismo algoritmo que el shader)
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

            // Resaltar en el shader
            HighlightSelection(node.CountryIdx, node.StateIdx);
        }
    }

    private void HighlightSelection(int cIdx, int sIdx)
    {
        if (_mapMaterial == null) return;

        _mapMaterial.SetShaderParameter("selected_country_idx", cIdx);
        _mapMaterial.SetShaderParameter("selected_state_idx", sIdx);
        
        // Seleccionamos el color base
        Color rawCol = (_currentMapMode == 1) ? DataService.CountryPalette[cIdx] : DataService.StatePalette[sIdx];
        
        // IMPORTANTE: Truncamos el color al mismo valor de 8 bits que tiene la textura
        // para que la comparación en el shader sea exacta.
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
            // Limpiar selección al cambiar de modo
            _mapMaterial.SetShaderParameter("selected_country_idx", -1);
            _mapMaterial.SetShaderParameter("selected_state_idx", -1);
            GD.Print($"[MapController] Modo de mapa cambiado a: {mode}");
        }
    }

    public (int countryIdx, int stateIdx) GetTargetAtScreenCenter()
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var center = viewportSize / 2f;
        
        var camera = GetViewport().GetCamera3D();
        if (camera == null) return (-1, -1);

        Vector3 from = camera.ProjectRayOrigin(center);
        Vector3 to = from + camera.ProjectRayNormal(center) * 1000;

        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var result = spaceState.IntersectRay(query);

        if (result.Count > 0)
        {
            Vector3 hitPos = (Vector3)result["position"];
            Vector3 localPos = ToLocal(hitPos);
            
            float u = (localPos.X + 270f) / 540f; // 540 / 2 = 270
            float v = (localPos.Z + 135f) / 270f; // 270 / 2 = 135
            
            if (u >= 0 && u <= 1 && v >= 0 && v <= 1)
            {
                int px = (int)(u * _idMapSize.X);
                int py = (int)(v * _idMapSize.Y);
                Color idCol = _idMapImage.GetPixel(Mathf.Clamp(px, 0, (int)_idMapSize.X - 1), 
                                                   Mathf.Clamp(py, 0, (int)_idMapSize.Y - 1));
                
                int nodeId = (int)Mathf.Round(idCol.R * 255f) + 
                             ((int)Mathf.Round(idCol.G * 255f) * 256) + 
                             ((int)Mathf.Round(idCol.B * 255f) * 65536);
                
                int idx = nodeId - 1;
                if (idx >= 0 && idx < DataService.Nodes.Length)
                {
                    var node = DataService.Nodes[idx];
                    return (node.CountryIdx, node.StateIdx);
                }
            }
        }
        return (-1, -1);
    }

    public void FocusCameraOnCountry(int countryIdx)
    {
        if (countryIdx == -1) return;
        
        Rect2 bounds = DataService.GetCountryBounds(countryIdx);
        if (bounds.Size.Length() == 0) return;

        // 1. Calcular Centro 3D
        Vector2 centerUV = bounds.GetCenter();
        float x3D = (centerUV.X * 540f) - (540f / 2f);
        float z3D = (centerUV.Y * 270f) - (270f / 2f);
        Vector3 targetCenter = new Vector3(x3D, 0, z3D);

        // 2. Calcular Altura Ideal (Zoom)
        // Queremos que el país ocupe aprox el 70% de la pantalla
        float maxDim = Math.Max(bounds.Size.X * 540f, bounds.Size.Y * 270f);
        // Ajustamos el clamp para que nunca toque el portal de salida (140.0)
        float targetHeight = Mathf.Clamp(maxDim * 1.2f, 25.0f, 110.0f);

        // 3. Aplicar a la cámara
        var camera = GetViewport().GetCamera3D() as CameraManager;
        if (camera != null)
        {
            camera.GlobalPosition = new Vector3(targetCenter.X, targetHeight, targetCenter.Z + (targetHeight * 0.5f));
            camera.LookAt(targetCenter);
            camera.SetTargetState(camera.GlobalPosition, camera.RotationDegrees);
            GD.Print($"[MapController] Enfoque en {DataService.CountryCatalog[countryIdx]}: Altura {targetHeight}");
        }
    }
}
