using Godot;
using System;
using Engine.Services;

namespace PLVSVLTRA.Autoload;

/// <summary>
/// Autoload that manages scene transitions between zoom levels.
/// Stores camera state per level, handles fade transitions.
/// Decoupled from specific camera/map implementations — communicates via signals.
/// </summary>
public partial class PortalManager : Node
{
    public enum ZoomLevel { International = 0, National = 1, Micro = 2 }

    public static PortalManager Instance { get; private set; }

    // Scene paths for each zoom level
    private static readonly string[] ScenePaths = {
        "res://Scenes/International.tscn",
        "res://Scenes/National.tscn",
        "res://Scenes/Micro.tscn"
    };

    // Persistent state across scene transitions
    public ZoomLevel CurrentLevel { get; private set; } = ZoomLevel.International;
    public int ActiveCountryIdx { get; set; } = -1;
    public int ActiveStateIdx { get; set; } = -1;
    public int PendingCountryIdx { get; set; } = -1;
    public int PendingStateIdx { get; set; } = -1;

    // Camera state persistence per level
    private struct CameraState
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public bool IsValid;
    }

    private readonly CameraState[] _savedStates = new CameraState[3];

    // Transition settings
    [Export] public float FadeDuration { get; set; } = 0.4f;

    // Signal emitted when a transition completes
    [Signal] public delegate void LevelChangedEventHandler(int newLevel);

    public override void _Ready()
    {
        Instance = this;

        // Initialize default camera states
        _savedStates[0] = new CameraState { Position = new Vector3(0, 300, 0), Rotation = new Vector3(-60, 0, 0), IsValid = false };
        _savedStates[1] = new CameraState { Position = new Vector3(0, 40, 0), Rotation = new Vector3(-60, 0, 0), IsValid = false };
        _savedStates[2] = new CameraState { Position = new Vector3(0, 15, 0), Rotation = new Vector3(-60, 0, 0), IsValid = false };

        GD.Print("[PortalManager] Portal system ready.");
    }

    /// <summary>
    /// Save the current camera state for the active level.
    /// Called by StrategyCamera before a transition.
    /// </summary>
    public void SaveCameraState(Vector3 position, Vector3 rotation)
    {
        int idx = (int)CurrentLevel;
        _savedStates[idx] = new CameraState { Position = position, Rotation = rotation, IsValid = true };
    }

    /// <summary>
    /// Retrieve saved camera state for a given level.
    /// Returns default position if no state was saved.
    /// </summary>
    public (Vector3 position, Vector3 rotation, bool isValid) GetSavedCameraState(ZoomLevel level)
    {
        var state = _savedStates[(int)level];
        return (state.Position, state.Rotation, state.IsValid);
    }

    /// <summary>
    /// Transition to a new zoom level. Called by StrategyCamera when portal threshold is crossed.
    /// </summary>
    /// <param name="targetLevel">The zoom level to transition to.</param>
    /// <param name="countryIdx">Country index (for National transitions). -1 to auto-detect.</param>
    /// <param name="stateIdx">State index (for Micro transitions). -1 to auto-detect.</param>
    private bool _isTransitioning = false;

    /// <summary>
    /// Transition to a new zoom level. Called by StrategyCamera when portal threshold is crossed.
    /// </summary>
    public async void TransitionTo(ZoomLevel targetLevel, int countryIdx = -1, int stateIdx = -1)
    {
        if (targetLevel == CurrentLevel || _isTransitioning) return;
        _isTransitioning = true;

        GD.Print($"[PortalManager] Transition: {CurrentLevel} -> {targetLevel}");

        // Fade out (handled by shader portal_fade if available)
        await ToSignal(GetTree().CreateTimer(FadeDuration), "timeout");

        var oldLevel = CurrentLevel;
        CurrentLevel = targetLevel;

        // Resolve country/state for zoom-in transitions
        if (targetLevel > oldLevel) // Zooming IN
        {
            if (countryIdx >= 0)
            {
                ActiveCountryIdx = countryIdx;
                PendingCountryIdx = countryIdx;
            }
            else if (PendingCountryIdx >= 0)
            {
                ActiveCountryIdx = PendingCountryIdx;
            }

            if (stateIdx >= 0)
            {
                ActiveStateIdx = stateIdx;
                PendingStateIdx = stateIdx;
            }
            else if (PendingStateIdx >= 0)
            {
                ActiveStateIdx = PendingStateIdx;
            }

            // Set entry height for the new level (coming from above)
            float entryHeight = targetLevel == ZoomLevel.National ? 200f : 40f;
            _savedStates[(int)targetLevel] = new CameraState
            {
                Position = new Vector3(0, entryHeight, 0),
                Rotation = new Vector3(-60, 0, 0),
                IsValid = true
            };
        }
        else // Zooming OUT
        {
            // Set entry height (coming from below)
            float entryHeight = targetLevel == ZoomLevel.International ? 200f : 30f;

            // Only override if no previous state was saved
            if (!_savedStates[(int)targetLevel].IsValid)
            {
                _savedStates[(int)targetLevel] = new CameraState
                {
                    Position = new Vector3(0, entryHeight, 0),
                    Rotation = new Vector3(-60, 0, 0),
                    IsValid = true
                };
            }
        }

        // Change scene
        GetTree().ChangeSceneToFile(ScenePaths[(int)targetLevel]);
        EmitSignal(SignalName.LevelChanged, (int)targetLevel);

        // Reset guard after scene load completes (next frame)
        await ToSignal(GetTree(), "process_frame");
        _isTransitioning = false;
    }

    /// <summary>
    /// Called by MapView to provide the detected country/state at screen center
    /// before a portal transition.
    /// </summary>
    public void SetDetectedTarget(int countryIdx, int stateIdx)
    {
        if (countryIdx >= 0) PendingCountryIdx = countryIdx;
        if (stateIdx >= 0) PendingStateIdx = stateIdx;
    }
}
