using Godot;
using System;
using ProjectPLVSVLTRA.UI.Map;
using Engine.Services;

namespace ProjectPLVSVLTRA.Core;

public partial class PortalManager : Node
{
    public static PortalManager? Instance { get; private set; }

    // Persistencia por cada nivel de zoom
    private struct CameraState { public Vector3 Pos; public Vector3 Rot; }
    private CameraState[] _savedLevels = new CameraState[3] {
        new CameraState { Pos = new Vector3(0, 80, 0), Rot = new Vector3(-60, 0, 0) }, // Int
        new CameraState { Pos = new Vector3(0, 40, 0), Rot = new Vector3(-60, 0, 0) }, // Nat
        new CameraState { Pos = new Vector3(0, 15, 0), Rot = new Vector3(-60, 0, 0) }  // Mic
    };

    private float _fadeDuration = 0.4f;
    
    public CameraManager.ZoomLevel CurrentLevel { get; set; } = CameraManager.ZoomLevel.International;

    public static int PendingCountryIdx = -1;
    public static int ActiveCountryIdx = -1;
    public static int PendingStateIdx = -1;

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[PortalManager] Sistema de portales listo.");
    }

    private ShaderMaterial GetMapMaterial()
    {
        var mapController = FindMapController();
        if (mapController != null)
        {
            var mesh = mapController.GetNodeOrNull<MeshInstance3D>("GlobalMap");
            if (mesh != null)
            {
                if (mesh.MaterialOverride is ShaderMaterial matOverride) return matOverride;
                if (mesh.Mesh is PlaneMesh plane && plane.Material is ShaderMaterial planeMat) return planeMat;
            }
        }
        return null;
    }

    public void RegisterCamera(CameraManager camera)
    {
        camera.OnZoomPortalTriggered += HandleZoomPortal;
        
        // Sincronizar el estado del nivel antes de restaurar posición
        camera.CurrentZoomLevel = CurrentLevel;

        // Restaurar la posición específica de este nivel
        var state = _savedLevels[(int)CurrentLevel];
        camera.GlobalPosition = state.Pos;
        camera.RotationDegrees = state.Rot;
        camera.SetTargetState(state.Pos, state.Rot);

        GD.Print($"[PortalManager] Cámara registrada en {CurrentLevel} y restaurada a {state.Pos}.");

        // Fade IN del mapa al entrar a la nueva escena (de negro a visible)
        var mat = GetMapMaterial();
        if (mat != null)
        {
            mat.SetShaderParameter("portal_fade", 1.0f);
            var tween = CreateTween();
            tween.TweenProperty(mat, "shader_parameter/portal_fade", 0.0f, _fadeDuration);
        }
    }

    private async void HandleZoomPortal(CameraManager.ZoomLevel targetLevel)
    {
        if (targetLevel == CurrentLevel) return;

        // Fade OUT del mapa (de visible a negro)
        var mat = GetMapMaterial();
        if (mat != null)
        {
            var tween = CreateTween();
            tween.TweenProperty(mat, "shader_parameter/portal_fade", 1.0f, _fadeDuration);
            await ToSignal(tween, "finished");
        }
        else
        {
            await ToSignal(GetTree().CreateTimer(_fadeDuration), "timeout");
        }

        GD.Print($"[PortalManager] Portal: {CurrentLevel} -> {targetLevel}");

        // 1. Guardar estado del nivel que dejamos
        var camera = GetTree().Root.GetViewport().GetCamera3D() as CameraManager;
        if (camera != null)
        {
            _savedLevels[(int)CurrentLevel] = new CameraState {
                Pos = camera.GlobalPosition,
                Rot = camera.RotationDegrees
            };
        }

        var oldLevel = CurrentLevel;
        CurrentLevel = targetLevel;

        // 2. Ajustar altura de entrada para el nuevo nivel
        // Queremos que al entrar "por arriba" empecemos en el tope del nuevo mapa
        // y al entrar "por abajo" empecemos en la base del nuevo mapa.
        var nextState = _savedLevels[(int)targetLevel];
        
        if (targetLevel > oldLevel) // Zoom IN (Bajamos de nivel)
        {
            // Entramos por el "techo" del nuevo nivel (ej: llegamos a Nacional desde arriba)
            float entryHeight = (targetLevel == CameraManager.ZoomLevel.National) ? 40.0f : 45.0f;
            nextState.Pos.Y = entryHeight;
        }
        else // Zoom OUT (Subimos de nivel)
        {
            // Entramos por el "suelo" del nuevo nivel (ej: llegamos a International desde abajo)
            // IMPORTANTE: Debe estar lejos del umbral de bajada (35.0) para no entrar en bucle
            float entryHeight = (targetLevel == CameraManager.ZoomLevel.International) ? 65.0f : 35.0f;
            nextState.Pos.Y = entryHeight;
        }
        _savedLevels[(int)targetLevel] = nextState;

        if (targetLevel < oldLevel) // Zoom OUT
        {
            if (targetLevel == CameraManager.ZoomLevel.International) TransitionToInternational();
            else TransitionToNational(ActiveCountryIdx);
            return;
        }

        // Zoom IN — Encontrar el MapController para saber qué país está en pantalla
        MapController mapController = FindMapController();

        int cIdx = -1;
        int sIdx = -1;
        if (mapController != null)
        {
            // Para Nacional: buscar país más cercano (funciona incluso sobre agua)
            cIdx = mapController.GetNearestCountryToScreenCenter();
            // Para Micro: necesitamos el estado exacto
            var target = mapController.GetTargetAtScreenCenter();
            sIdx = target.stateIdx;
            
            if (cIdx != -1)
                GD.Print($"[PortalManager] País detectado: {DataService.CountryCatalog[cIdx]} (idx={cIdx})");
            else
                GD.PrintErr("[PortalManager] No se encontró ningún país cercano.");
        }
        else
        {
            GD.PrintErr("[PortalManager] No se encontró MapController en la escena actual.");
        }

        switch (targetLevel)
        {
            case CameraManager.ZoomLevel.National: TransitionToNational(cIdx); break;
            case CameraManager.ZoomLevel.Micro: TransitionToMicro(sIdx); break;
        }
    }

    private void TransitionToInternational()
    {
        GetTree().ChangeSceneToFile("res://Main.tscn");
    }

    private void TransitionToNational(int countryIdx)
    {
        int resolvedCountryIdx = ResolveNationalCountryCandidate(countryIdx);
        if (resolvedCountryIdx != -1)
        {
            ActiveCountryIdx = resolvedCountryIdx;
            if (resolvedCountryIdx < DataService.CountryCatalog.Length)
                GD.Print($"[PortalManager] Entrando a {DataService.CountryCatalog[resolvedCountryIdx]}");
        }
        
        PendingCountryIdx = ActiveCountryIdx;
        GetTree().ChangeSceneToFile("res://Scenes/Map/MapNational.tscn");
    }

    private int ResolveNationalCountryCandidate(int requestedCountryIdx)
    {
        if (requestedCountryIdx >= 0 && requestedCountryIdx < DataService.CountryCatalog.Length)
            return requestedCountryIdx;

        if (ActiveCountryIdx >= 0 && ActiveCountryIdx < DataService.CountryCatalog.Length)
            return ActiveCountryIdx;

        if (DataService.CountryCatalog != null && DataService.CountryCatalog.Length > 0)
            return 0;

        return -1;
    }

    private void TransitionToMicro(int stateIdx)
    {
        if (stateIdx != -1 && stateIdx < DataService.StateCatalog.Length)
            GD.Print($"[PortalManager] Entrando a {DataService.StateCatalog[stateIdx]}");
            
        GetTree().ChangeSceneToFile("res://Scenes/Map/MapMicro.tscn");
    }

    /// <summary>
    /// Busca recursivamente el MapController en el árbol de escena.
    /// </summary>
    private MapController FindMapController()
    {
        return FindNodeOfType<MapController>(GetTree().Root);
    }

    private static T FindNodeOfType<T>(Node root) where T : class
    {
        if (root is T match) return match;
        foreach (var child in root.GetChildren())
        {
            var found = FindNodeOfType<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
