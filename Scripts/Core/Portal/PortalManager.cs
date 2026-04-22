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
    private CanvasLayer _fadeLayer;
    private ColorRect _fadeRect;
    
    public static int PendingCountryIdx = -1;
    public static int PendingStateIdx = -1;

    public override void _Ready()
    {
        Instance = this;
        SetupFadeLayer();
        GD.Print("[PortalManager] Sistema de portales listo.");
    }

    private void SetupFadeLayer()
    {
        _fadeLayer = new CanvasLayer { Layer = 100 };
        _fadeRect = new ColorRect {
            Color = new Color(0, 0, 0, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _fadeRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        
        _fadeLayer.AddChild(_fadeRect);
        AddChild(_fadeLayer);
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

        // Fade OUT al entrar a nueva escena
        var tween = CreateTween();
        tween.TweenProperty(_fadeRect, "color:a", 0.0f, _fadeDuration);
    }

    private async void HandleZoomPortal(CameraManager.ZoomLevel targetLevel)
    {
        if (targetLevel == CurrentLevel) return;

        // Iniciar Fade IN
        var tween = CreateTween();
        tween.TweenProperty(_fadeRect, "color:a", 1.0f, _fadeDuration);
        await ToSignal(tween, "finished");

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
            float entryHeight = (targetLevel == CameraManager.ZoomLevel.National) ? 130.0f : 45.0f;
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
            else TransitionToNational(-1);
            return;
        }

        // Zoom IN
        var mapController = GetTree().Root.FindChild("Map", true, false) as MapController;
        if (mapController == null)
        {
            foreach (var node in GetTree().Root.GetChildren())
            {
                mapController = node.FindChild("*", true, false) as MapController;
                if (mapController != null) break;
            }
        }

        int cIdx = -1;
        int sIdx = -1;
        if (mapController != null)
        {
            var target = mapController.GetTargetAtScreenCenter();
            cIdx = target.countryIdx;
            sIdx = target.stateIdx;
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
        if (countryIdx != -1 && countryIdx < DataService.CountryCatalog.Length)
            GD.Print($"[PortalManager] Entrando a {DataService.CountryCatalog[countryIdx]}");
        
        PendingCountryIdx = countryIdx;
        GetTree().ChangeSceneToFile("res://Scenes/Map/MapNational.tscn");
    }

    private void TransitionToMicro(int stateIdx)
    {
        if (stateIdx != -1 && stateIdx < DataService.StateCatalog.Length)
            GD.Print($"[PortalManager] Entrando a {DataService.StateCatalog[stateIdx]}");
            
        GetTree().ChangeSceneToFile("res://Scenes/Map/MapMicro.tscn");
    }
}
