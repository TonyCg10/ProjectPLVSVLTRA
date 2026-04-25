using Godot;
using Engine.Services;

namespace PLVSVLTRA.UI;

/// <summary>
/// HUD overlay: date, population, map mode buttons, and portal indicator.
/// Scene-agnostic — reads from GameManager singleton.
/// </summary>
public partial class HUD : Control
{
    private Label _dateLabel;
    private Label _popLabel;
    private Label _statusLabel;
    private HBoxContainer _mapModeBtns;
    private MeshInstance3D _portalIndicator;
    private bool _indicatorSetUp = false;

    public override void _Ready()
    {
        _dateLabel = GetNodeOrNull<Label>("MarginContainer/VBoxContainer/DateLabel");
        _popLabel = GetNodeOrNull<Label>("MarginContainer/VBoxContainer/PopLabel");
        _statusLabel = GetNodeOrNull<Label>("MarginContainer/VBoxContainer/StatusLabel");

        // Build map mode buttons dynamically
        BuildMapModeButtons();

        // Build the 3D portal target indicator
        BuildPortalIndicator();
    }

    public override void _Process(double delta)
    {
        var gm = PLVSVLTRA.Autoload.GameManager.Instance;
        if (gm?.Motor?.Context != null)
        {
            var ctx = gm.Motor.Context;
            if (_dateLabel != null)
                _dateLabel.Text = $"\ud83d\udcc5 {GameCalendar.DisplayDate(ctx.CurrentDate)}";
            if (_popLabel != null)
                _popLabel.Text = $"\ud83d\udc65 Population: {ctx.WorldPopulation:N0}";
            if (_statusLabel != null)
            {
                string status = gm.Motor.StatusMessage;
                _statusLabel.Text = string.IsNullOrEmpty(status) ? "" : status;
            }
        }

        UpdatePortalIndicator();
    }

    private void BuildMapModeButtons()
    {
        var marginContainer = GetNodeOrNull("MarginContainer");
        if (marginContainer == null) return;

        // Bottom-center container for map mode buttons
        var bottomPanel = new HBoxContainer
        {
            Name = "MapModePanel",
            LayoutMode = 1,
            AnchorsPreset = (int)LayoutPreset.CenterBottom,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 1.0f,
            AnchorTop = 1.0f,
            OffsetLeft = -160,
            OffsetRight = 160,
            OffsetTop = -60,
            OffsetBottom = -15
        };

        bottomPanel.AddThemeConstantOverride("separation", 8);

        string[] labels = { "Countries", "States", "Natural" };
        int[] modes = { 1, 2, 3 };
        Color[] colors = {
            new Color(0.1f, 0.9f, 1.0f),   // Cyan
            new Color(0.9f, 0.3f, 1.0f),   // Magenta
            new Color(0.4f, 1.0f, 0.5f)    // Green
        };

        for (int i = 0; i < labels.Length; i++)
        {
            var btn = new Button
            {
                Text = labels[i],
                CustomMinimumSize = new Vector2(100, 35),
                FocusMode = FocusModeEnum.None
            };

            // Style the button
            var styleNormal = new StyleBoxFlat
            {
                BgColor = new Color(0.05f, 0.05f, 0.08f, 0.85f),
                BorderColor = colors[i],
                BorderWidthLeft = 2,
                BorderWidthRight = 2,
                BorderWidthTop = 2,
                BorderWidthBottom = 2,
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6,
                ContentMarginLeft = 12,
                ContentMarginRight = 12,
                ContentMarginTop = 6,
                ContentMarginBottom = 6
            };

            var styleHover = (StyleBoxFlat)styleNormal.Duplicate();
            styleHover.BgColor = new Color(colors[i].R * 0.15f, colors[i].G * 0.15f, colors[i].B * 0.15f, 0.9f);
            styleHover.BorderWidthLeft = 3;
            styleHover.BorderWidthRight = 3;
            styleHover.BorderWidthTop = 3;
            styleHover.BorderWidthBottom = 3;

            var stylePressed = (StyleBoxFlat)styleNormal.Duplicate();
            stylePressed.BgColor = new Color(colors[i].R * 0.25f, colors[i].G * 0.25f, colors[i].B * 0.25f, 0.95f);

            btn.AddThemeStyleboxOverride("normal", styleNormal);
            btn.AddThemeStyleboxOverride("hover", styleHover);
            btn.AddThemeStyleboxOverride("pressed", stylePressed);
            btn.AddThemeColorOverride("font_color", colors[i]);
            btn.AddThemeColorOverride("font_hover_color", Colors.White);

            int mode = modes[i];
            btn.Pressed += () => SetMapMode(mode);

            bottomPanel.AddChild(btn);
        }

        AddChild(bottomPanel);
        _mapModeBtns = bottomPanel;
    }

    private void SetMapMode(int mode)
    {
        // Find the MapView in the scene tree
        var mapView = GetTree().Root.GetNodeOrNull<PLVSVLTRA.Map.MapView>(GetTree().CurrentScene.GetPath());
        mapView ??= GetTree().CurrentScene as PLVSVLTRA.Map.MapView;

        if (mapView != null)
        {
            mapView.SetMapMode(mode);
            GD.Print($"[HUD] Map mode set to {mode}");
        }
    }

    private void BuildPortalIndicator()
    {
        // Create a 3D ring indicator for portal target
        var viewport = GetViewport();
        if (viewport == null) return;

        // We'll create the 3D indicator as a child of the scene root
        CallDeferred(nameof(DeferredBuildIndicator));
    }

    private void DeferredBuildIndicator()
    {
        var sceneRoot = GetTree().CurrentScene;
        if (sceneRoot == null) return;

        // Check if indicator already exists
        _portalIndicator = sceneRoot.GetNodeOrNull<MeshInstance3D>("PortalIndicator");
        if (_portalIndicator != null) { _indicatorSetUp = true; return; }

        // Create a torus/ring mesh as the portal target indicator
        var torusMesh = new TorusMesh
        {
            InnerRadius = 3.0f,
            OuterRadius = 5.0f,
            Rings = 32,
            RingSegments = 16
        };

        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.1f, 0.9f, 1.0f, 0.6f),
            EmissionEnabled = true,
            Emission = new Color(0.1f, 0.9f, 1.0f),
            EmissionEnergyMultiplier = 2.0f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = true
        };
        torusMesh.SurfaceSetMaterial(0, mat);

        _portalIndicator = new MeshInstance3D
        {
            Name = "PortalIndicator",
            Mesh = torusMesh,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };

        sceneRoot.AddChild(_portalIndicator);
        _indicatorSetUp = true;
    }

    private void UpdatePortalIndicator()
    {
        if (!_indicatorSetUp || _portalIndicator == null) return;

        var camera = GetViewport()?.GetCamera3D() as PLVSVLTRA.Camera.StrategyCamera;
        if (camera == null) { _portalIndicator.Visible = false; return; }

        // Only show indicator when near the IN portal threshold
        bool nearPortal = camera.InPortalTarget > 0 &&
                          camera.GlobalPosition.Y <= camera.InPortalHeight + camera.ResistanceRange * 1.5f;

        _portalIndicator.Visible = nearPortal;

        if (nearPortal)
        {
            // Position at screen center on the map surface
            var viewportSize = GetViewport().GetVisibleRect().Size;
            var center = viewportSize / 2f;

            Vector3 from = camera.ProjectRayOrigin(center);
            Vector3 to = from + camera.ProjectRayNormal(center) * 1000;

            var spaceState = camera.GetWorld3D().DirectSpaceState;
            var query = PhysicsRayQueryParameters3D.Create(from, to);
            var result = spaceState.IntersectRay(query);

            if (result.Count > 0)
            {
                Vector3 hitPos = (Vector3)result["position"];
                _portalIndicator.GlobalPosition = hitPos + Vector3.Up * 0.5f;

                // Pulsing rotation animation
                float time = (float)Time.GetTicksMsec() / 1000.0f;
                _portalIndicator.RotationDegrees = new Vector3(90, time * 45f, 0);

                // Scale based on proximity to portal
                float dist = camera.GlobalPosition.Y - camera.InPortalHeight;
                float proximity = 1.0f - Mathf.Clamp(dist / camera.ResistanceRange, 0, 1);
                float scale = Mathf.Lerp(0.5f, 1.5f, proximity);
                _portalIndicator.Scale = Vector3.One * scale;

                // Glow intensifies near threshold
                var mat = _portalIndicator.Mesh.SurfaceGetMaterial(0) as StandardMaterial3D;
                if (mat != null)
                {
                    float alpha = Mathf.Lerp(0.2f, 0.8f, proximity);
                    mat.AlbedoColor = new Color(0.1f, 0.9f, 1.0f, alpha);
                    mat.EmissionEnergyMultiplier = Mathf.Lerp(1.0f, 4.0f, proximity);
                }
            }
        }
    }
}
