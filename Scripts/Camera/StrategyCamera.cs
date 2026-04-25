using Godot;
using System;

namespace PLVSVLTRA.Camera;

/// <summary>
/// Universal strategy camera for all zoom levels.
/// Handles pan (WASD + mouse drag), zoom (scroll), rotation (right-click).
/// Portal threshold detection and resistance curves.
/// Configurable per-scene via [Export] properties.
/// </summary>
public partial class StrategyCamera : Camera3D
{
    [ExportGroup("Movement")]
    [Export] public float BaseMoveSpeed { get; set; } = 30.0f;
    [Export] public float BasePanSpeed { get; set; } = 1.5f;
    [Export] public float Acceleration { get; set; } = 10.0f;

    [ExportGroup("Rotation")]
    [Export] public float YawSensitivity { get; set; } = 0.2f;
    [Export] public float PitchSensitivity { get; set; } = 0.2f;
    [Export] public float MinPitch { get; set; } = -85.0f;
    [Export] public float MaxPitch { get; set; } = -35.0f;

    [ExportGroup("Zoom")]
    [Export] public float ZoomSpeed { get; set; } = 10.0f;
    [Export] public float MinHeight { get; set; } = 10.0f;
    [Export] public float MaxHeight { get; set; } = 100.0f;
    [Export] public float ZoomFovWarp { get; set; } = 5.0f;

    [ExportGroup("Portal Thresholds")]
    [Export] public int InPortalTarget { get; set; } = 0;  // 0=None, 1=Int, 2=Nat, 3=Mic
    [Export] public float InPortalHeight { get; set; } = 25.0f;
    [Export] public int OutPortalTarget { get; set; } = 0;
    [Export] public float OutPortalHeight { get; set; } = 95.0f;
    [Export] public float ResistanceRange { get; set; } = 10.0f;
    [Export] public float MinZoomMultiplier { get; set; } = 0.2f;
    [Export] public int PortalScrollsRequired { get; set; } = 4;
    [Export] public float PortalActivationWindowSec { get; set; } = 0.7f;
    [Export] public float PortalSnapEpsilon { get; set; } = 0.35f;

    [ExportGroup("World Wrap")]
    [Export] public float MapWidth { get; set; } = 540.0f;
    [Export] public Vector2 LimitZ { get; set; } = new Vector2(-100f, 100f);
    [Export] public bool EnableHorizontalWrap { get; set; } = true;

    // Internal state
    private Vector3 _targetPosition;
    private Vector3 _targetRotation;
    private float _zoomTravelVelocity = 0.0f;
    private int _inPortalCharge = 0;
    private int _outPortalCharge = 0;
    private double _lastPortalChargeTimeSec = -100.0;

    public override void _Ready()
    {
        // Try to restore camera state from PortalManager
        var portal = GetNodeOrNull<PLVSVLTRA.Autoload.PortalManager>("/root/PortalManager");
        if (portal != null)
        {
            var (pos, rot, isValid) = portal.GetSavedCameraState(portal.CurrentLevel);
            if (isValid)
            {
                GlobalPosition = pos;
                RotationDegrees = rot;
            }
        }

        _targetPosition = GlobalPosition;
        _targetRotation = RotationDegrees;
    }

    public override void _Process(double delta)
    {
        float fDelta = (float)delta;

        // 1. Speed scaling based on height
        float heightRatio = Mathf.InverseLerp(MinHeight, MaxHeight, _targetPosition.Y);
        float speedMultiplier = Mathf.Lerp(0.5f, 3.0f, heightRatio);

        HandleKeyboardInput(fDelta, speedMultiplier);

        // 2. Horizontal wrap (seamless infinite scroll on X axis)
        if (EnableHorizontalWrap)
        {
            float halfWidth = MapWidth / 2.0f;
            if (_targetPosition.X > halfWidth)
            {
                _targetPosition.X -= MapWidth;
                var pos = GlobalPosition;
                pos.X -= MapWidth;
                GlobalPosition = pos;
            }
            else if (_targetPosition.X < -halfWidth)
            {
                _targetPosition.X += MapWidth;
                var pos = GlobalPosition;
                pos.X += MapWidth;
                GlobalPosition = pos;
            }
        }

        // 3. Clamp Z and Y limits
        _targetPosition.Z = Mathf.Clamp(_targetPosition.Z, LimitZ.X, LimitZ.Y);
        _targetPosition.Y = Mathf.Clamp(_targetPosition.Y, MinHeight, MaxHeight);

        // 4. Smooth interpolation
        GlobalPosition = GlobalPosition.Lerp(_targetPosition, Acceleration * fDelta);

        Vector3 currentRot = RotationDegrees;
        currentRot.X = Mathf.Lerp(currentRot.X, _targetRotation.X, Acceleration * fDelta);
        currentRot.Y = Mathf.Lerp(currentRot.Y, _targetRotation.Y, Acceleration * fDelta);
        RotationDegrees = currentRot;

        // 5. FOV effects (tension near portal + travel warp)
        float resIn = GetZoomResistance(false);
        float resOut = GetZoomResistance(true);
        float minRes = Mathf.Min(resIn, resOut);

        float targetFov = 75.0f;
        if (minRes < 1.0f)
        {
            float tension = 1.0f - minRes;
            targetFov = Mathf.Lerp(75.0f, 60.0f, tension);
        }

        float zoomDelta = Mathf.Abs(GlobalPosition.Y - _targetPosition.Y);
        _zoomTravelVelocity = Mathf.Lerp(_zoomTravelVelocity, zoomDelta, 5.0f * fDelta);
        targetFov += _zoomTravelVelocity * ZoomFovWarp;

        Fov = Mathf.Lerp(Fov, targetFov, 5.0f * fDelta);
    }

    public override void _Input(InputEvent @event)
    {
        // Right-click rotation
        if (@event is InputEventMouseMotion mouseMotion && Input.IsMouseButtonPressed(MouseButton.Right))
        {
            _targetRotation.Y -= mouseMotion.Relative.X * YawSensitivity;
            _targetRotation.X -= mouseMotion.Relative.Y * PitchSensitivity;
            _targetRotation.X = Mathf.Clamp(_targetRotation.X, MinPitch, MaxPitch);
        }

        // Left-click pan
        if (@event is InputEventMouseMotion panMotion && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            float heightRatio = Mathf.InverseLerp(MinHeight, MaxHeight, _targetPosition.Y);
            float currentPanSpeed = BasePanSpeed * Mathf.Lerp(0.5f, 3.0f, heightRatio);

            Vector3 panDir = -GlobalTransform.Basis.X * panMotion.Relative.X
                           + GlobalTransform.Basis.Y * panMotion.Relative.Y;
            panDir.Y = 0;
            _targetPosition += panDir * (currentPanSpeed / 100.0f);
        }

        // Scroll zoom
        if (@event is InputEventMouseButton mouseButton)
        {
            bool isWheelUp = mouseButton.ButtonIndex == MouseButton.WheelUp;
            bool isWheelDown = mouseButton.ButtonIndex == MouseButton.WheelDown;

            if (isWheelUp || isWheelDown)
            {
                HandleScrollZoom(isWheelDown);
            }
        }
    }

    private void HandleScrollZoom(bool zoomingOut)
    {
        Vector3 zoomDir = GlobalTransform.Basis.Z;
        int targetPortal = zoomingOut ? OutPortalTarget : InPortalTarget;
        float targetHeight = zoomingOut ? OutPortalHeight : InPortalHeight;
        double nowSec = Time.GetTicksMsec() / 1000.0;

        if (targetPortal != 0)
        {
            // Check if at portal threshold
            bool atLimit = zoomingOut
                ? (_targetPosition.Y >= targetHeight - PortalSnapEpsilon)
                : (_targetPosition.Y <= targetHeight + PortalSnapEpsilon);

            if (atLimit)
            {
                if (nowSec - _lastPortalChargeTimeSec > PortalActivationWindowSec)
                    ResetPortalCharge();

                _lastPortalChargeTimeSec = nowSec;
                if (zoomingOut) _outPortalCharge++;
                else _inPortalCharge++;

                int currentCharge = zoomingOut ? _outPortalCharge : _inPortalCharge;

                if (currentCharge >= PortalScrollsRequired)
                {
                    TriggerPortal(zoomingOut);
                }
                return; // Block further zoom movement
            }
        }

        // Apply zoom with resistance
        float currentResist = GetZoomResistance(zoomingOut);
        float direction = zoomingOut ? 1.0f : -1.0f;
        Vector3 step = zoomDir * direction * ZoomSpeed * currentResist;

        // Prevent overshooting the portal threshold
        if (targetPortal != 0 && step.Y != 0.0f)
        {
            float proposedY = _targetPosition.Y + step.Y;
            if (zoomingOut && proposedY > targetHeight)
            {
                float fraction = (targetHeight - _targetPosition.Y) / step.Y;
                step *= fraction;
            }
            else if (!zoomingOut && proposedY < targetHeight)
            {
                float fraction = (targetHeight - _targetPosition.Y) / step.Y;
                step *= fraction;
            }
        }

        _targetPosition += step;

        // Snap to threshold if overshooting
        if (targetPortal != 0)
        {
            if (zoomingOut && _targetPosition.Y > targetHeight) _targetPosition.Y = targetHeight;
            if (!zoomingOut && _targetPosition.Y < targetHeight) _targetPosition.Y = targetHeight;
        }

        ResetPortalCharge();
    }

    private void TriggerPortal(bool zoomingOut)
    {
        ResetPortalCharge();

        var portal = GetNodeOrNull<PLVSVLTRA.Autoload.PortalManager>("/root/PortalManager");
        if (portal == null) return;

        // Save current state before transition
        portal.SaveCameraState(GlobalPosition, RotationDegrees);

        // Determine target zoom level
        int targetPortal = zoomingOut ? OutPortalTarget : InPortalTarget;
        var targetLevel = (PLVSVLTRA.Autoload.PortalManager.ZoomLevel)(targetPortal - 1);

        // For zoom-in transitions, detect the target under screen center
        if (!zoomingOut)
        {
            var mapView = GetTree().CurrentScene as PLVSVLTRA.Map.MapView;
            if (mapView != null)
            {
                // Get both country and state at screen center
                var (countryIdx, stateIdx) = mapView.GetTargetAtScreenCenter();

                // Fallback to spiral search if center is water
                if (countryIdx < 0)
                    countryIdx = mapView.GetNearestCountryToScreenCenter();

                if (countryIdx >= 0)
                {
                    portal.SetDetectedTarget(countryIdx, stateIdx);
                    GD.Print($"[StrategyCamera] Portal target: country {countryIdx}, state {stateIdx}");
                }
                else
                {
                    GD.Print("[StrategyCamera] No country found — portal cancelled");
                    return; // Don't transition if no country detected
                }
            }
        }

        GD.Print($"[StrategyCamera] Portal triggered -> {targetLevel}");
        portal.TransitionTo(targetLevel);
    }

    private void ResetPortalCharge()
    {
        _inPortalCharge = 0;
        _outPortalCharge = 0;
        _lastPortalChargeTimeSec = -100.0;
    }

    private float GetZoomResistance(bool zoomingOut)
    {
        float y = _targetPosition.Y;
        float targetHeight = zoomingOut ? OutPortalHeight : InPortalHeight;
        int targetPortal = zoomingOut ? OutPortalTarget : InPortalTarget;

        if (targetPortal != 0)
        {
            float dist = Mathf.Abs(y - targetHeight);
            if (dist < ResistanceRange)
            {
                float factor = Mathf.Clamp(dist / ResistanceRange, 0.0f, 1.0f);
                float curved = factor * factor;
                return Mathf.Lerp(MinZoomMultiplier, 1.0f, curved);
            }
        }
        return 1.0f;
    }

    /// <summary>
    /// Allows external code to set the camera position/rotation directly.
    /// Used by MapBuilder after constructing a national/micro view.
    /// </summary>
    public void SetTargetState(Vector3 pos, Vector3 rot)
    {
        _targetPosition = pos;
        _targetRotation = rot;
    }

    private void HandleKeyboardInput(float delta, float speedMultiplier)
    {
        Vector3 inputDir = Vector3.Zero;

        if (Input.IsKeyPressed(Key.W)) inputDir.Z -= 1;
        if (Input.IsKeyPressed(Key.S)) inputDir.Z += 1;
        if (Input.IsKeyPressed(Key.A)) inputDir.X -= 1;
        if (Input.IsKeyPressed(Key.D)) inputDir.X += 1;

        if (inputDir != Vector3.Zero)
        {
            inputDir = inputDir.Normalized();
            Vector3 forward = new Vector3(GlobalTransform.Basis.Z.X, 0, GlobalTransform.Basis.Z.Z).Normalized();
            Vector3 right = new Vector3(GlobalTransform.Basis.X.X, 0, GlobalTransform.Basis.X.Z).Normalized();
            _targetPosition += (forward * inputDir.Z + right * inputDir.X) * (BaseMoveSpeed * speedMultiplier) * delta;
        }
    }
}
