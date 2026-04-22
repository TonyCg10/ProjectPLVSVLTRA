using Godot;
using System;

public partial class CameraManager : Camera3D 
{
	public enum ZoomLevel { International, National, Micro }
	public enum PortalType { None, International, National, Micro }

	[ExportGroup("Movimiento")]
	[Export] public float BaseMoveSpeed = 30.0f;
	[Export] public float BasePanSpeed = 1.5f;
	[Export] public float Acceleration = 10.0f;

	[ExportGroup("Rotación Libre")]
	[Export] public float YawSensitivity = 0.2f;   // Eje Y (Izquierda/Derecha)
	[Export] public float PitchSensitivity = 0.2f; // Eje X (Arriba/Abajo)
	[Export] public float MinPitch = -85.0f; // Límite mirando hacia abajo
	[Export] public float MaxPitch = -10.0f; // Límite mirando al horizonte

	[ExportGroup("Zoom")]
	[Export] public float ZoomSpeed = 10.0f;
	[Export] public float MinHeight = 10.0f;  
	[Export] public float MaxHeight = 100.0f; 
	[Export] public float ZoomFovWarp = 5.0f; // Cuánto se deforma el FOV al viajar

	[ExportGroup("Portal Thresholds")]
	[Export] public PortalType InPortalTarget = PortalType.None;
	[Export] public float InPortalHeight = 25.0f;
	[Export] public PortalType OutPortalTarget = PortalType.None;
	[Export] public float OutPortalHeight = 95.0f;
	[Export] public float ResistanceRange = 10.0f;
	[Export] public float MinZoomMultiplier = 0.2f;

	[ExportGroup("Límites y Mundo Infinito (Wrap)")]
	[Export] public float MapWidth = 540.0f; 
	[Export] public Vector2 LimitZ = new Vector2(-100f, 100f);

	public System.Action<ZoomLevel> OnZoomPortalTriggered;
	public ZoomLevel CurrentZoomLevel = ZoomLevel.International;

	private Vector3 _targetPosition;
	private Vector3 _targetRotation;
	private float _zoomTravelVelocity = 0.0f;

	public override void _Ready()
	{
		_targetPosition = GlobalPosition;
		_targetRotation = RotationDegrees;

		// Registrar la cámara en el sistema de portales si existe
		var portalManager = GetTree().Root.FindChild("PortalManager", true, false) as ProjectPLVSVLTRA.Core.PortalManager;
		if (portalManager != null)
		{
			// Sincronizamos el nivel actual de la cámara con el del Manager para evitar triggers falsos al cargar
			CurrentZoomLevel = portalManager.CurrentLevel;
			portalManager.RegisterCamera(this);
		}
		else
		{
			GD.Print("[CameraManager] No se encontró PortalManager en la raíz. (¿Olvidaste añadirlo como Autoload?)");
		}
	}

	public override void _Process(double delta)
	{
		float fDelta = (float)delta;

		// 1. Calcular a qué altura estamos (para mantener la velocidad dinámica)
		float heightRatio = Mathf.InverseLerp(MinHeight, MaxHeight, _targetPosition.Y);
		float speedMultiplier = Mathf.Lerp(0.5f, 3.0f, heightRatio);

		HandleKeyboardInput(fDelta, speedMultiplier);

		// 2. EFECTO CINTA DE CORRER (Seamless Wrap en el Eje X)
		float halfWidth = MapWidth / 2.0f;

		// Si cruzamos el borde derecho del mapa central
		if (_targetPosition.X > halfWidth)
		{
			_targetPosition.X -= MapWidth;
			
			// Mover la cámara real instantáneamente
			Vector3 instantPos = GlobalPosition;
			instantPos.X -= MapWidth;
			GlobalPosition = instantPos;
		}
		// Si cruzamos el borde izquierdo del mapa central
		else if (_targetPosition.X < -halfWidth)
		{
			_targetPosition.X += MapWidth;
			
			Vector3 instantPos = GlobalPosition;
			instantPos.X += MapWidth;
			GlobalPosition = instantPos;
		}

		// 3. APLICAR LÍMITES SÓLO A Z e Y
		_targetPosition.Z = Mathf.Clamp(_targetPosition.Z, LimitZ.X, LimitZ.Y);
		_targetPosition.Y = Mathf.Clamp(_targetPosition.Y, MinHeight, MaxHeight);

		// 4. Suavizado (Lerp) de Posición y Rotación
		GlobalPosition = GlobalPosition.Lerp(_targetPosition, Acceleration * fDelta);
		
		Vector3 currentRot = RotationDegrees;
		currentRot.X = Mathf.Lerp(currentRot.X, _targetRotation.X, Acceleration * fDelta);
		currentRot.Y = Mathf.Lerp(currentRot.Y, _targetRotation.Y, Acceleration * fDelta);
		RotationDegrees = currentRot; 

		// 5. Efecto Visual de Tensión y Viaje (FOV)
		float resIn = GetZoomResistance(false); 
		float resOut = GetZoomResistance(true);
		float minRes = Mathf.Min(resIn, resOut);
		
		// FOV Base
		float targetFov = 75.0f;

		// Efecto 1: Tensión en el portal (Estrechar FOV)
		if (minRes < 1.0f)
		{
			float tension = 1.0f - minRes; 
			targetFov = Mathf.Lerp(75.0f, 60.0f, tension);
		}
		
		// Efecto 2: Viaje (FOV Warp por velocidad de zoom)
		float zoomDelta = Mathf.Abs(GlobalPosition.Y - _targetPosition.Y);
		_zoomTravelVelocity = Mathf.Lerp(_zoomTravelVelocity, zoomDelta, 5.0f * fDelta);
		targetFov += _zoomTravelVelocity * ZoomFovWarp;

		Fov = Mathf.Lerp(Fov, targetFov, 5.0f * fDelta);

		// 6. Indicador de Portal en el Suelo
		UpdateGroundIndicator(minRes);

		// 7. Detectar Portales de Zoom
		CheckZoomPortals();
	}

	private MeshInstance3D _groundIndicator;
	private void UpdateGroundIndicator(float resistance)
	{
		if (InPortalTarget == PortalType.None) return;

		if (_groundIndicator == null)
		{
			// Crear un círculo procedural si no existe
			var torus = new TorusMesh {
				InnerRadius = 4.5f,
				OuterRadius = 5.0f,
				Rings = 32,
				RingSegments = 3
			};
			_groundIndicator = new MeshInstance3D {
				Mesh = torus,
				TopLevel = true, // Posición global independiente
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
			};
			var mat = new StandardMaterial3D {
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				AlbedoColor = new Color(1, 1, 1, 0),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				NoDepthTest = true
			};
			_groundIndicator.MaterialOverride = mat;
			AddChild(_groundIndicator);
		}

		// Posicionar el círculo justo debajo de la cámara en el suelo
		Vector3 groundPos = GlobalPosition;
		groundPos.Y = 0.1f; // Un pelín por encima del suelo
		_groundIndicator.GlobalPosition = groundPos;

		// Visibilidad basada en la resistencia (solo cuando bajamos)
		float tension = 1.0f - resistance;
		var matAlpha = _groundIndicator.MaterialOverride as StandardMaterial3D;
		if (matAlpha != null)
		{
			// Solo mostramos si estamos bajando hacia un portal IN
			bool showing = resistance < 1.0f && _targetPosition.Y < GlobalPosition.Y;
			float targetAlpha = showing ? (tension * 0.8f) : 0.0f;
			matAlpha.AlbedoColor = new Color(1, 1, 1, targetAlpha);
		}
	}

	private void CheckZoomPortals()
	{
		// 1. Check IN (Hacia abajo)
		if (InPortalTarget != PortalType.None && _targetPosition.Y <= InPortalHeight)
		{
			TriggerPortal((ZoomLevel)((int)InPortalTarget - 1));
		}
		// 2. Check OUT (Hacia arriba)
		else if (OutPortalTarget != PortalType.None && _targetPosition.Y >= OutPortalHeight)
		{
			TriggerPortal((ZoomLevel)((int)OutPortalTarget - 1));
		}
	}

	private void TriggerPortal(ZoomLevel target)
	{
		if (target != CurrentZoomLevel)
		{
			CurrentZoomLevel = target;
			OnZoomPortalTriggered?.Invoke(CurrentZoomLevel);
		}
	}

	public override void _Input(InputEvent @event)
	{
		// Rotación con Click Derecho (Ahora mueve X e Y)
		if (@event is InputEventMouseMotion mouseMotion && Input.IsMouseButtonPressed(MouseButton.Right))
		{
			_targetRotation.Y -= mouseMotion.Relative.X * YawSensitivity;
			_targetRotation.X -= mouseMotion.Relative.Y * PitchSensitivity;
			
			// Limitamos la X para no dar vueltas de campana
			_targetRotation.X = Mathf.Clamp(_targetRotation.X, MinPitch, MaxPitch);
		}

		// Desplazamiento (Pan) con Click Izquierdo
		if (@event is InputEventMouseMotion panMotion && Input.IsMouseButtonPressed(MouseButton.Left))
		{
			float heightRatio = Mathf.InverseLerp(MinHeight, MaxHeight, _targetPosition.Y);
			float currentPanSpeed = BasePanSpeed * Mathf.Lerp(0.5f, 3.0f, heightRatio);

			Vector3 panDir = -GlobalTransform.Basis.X * panMotion.Relative.X + GlobalTransform.Basis.Y * panMotion.Relative.Y;
			panDir.Y = 0; 
			_targetPosition += panDir * (currentPanSpeed / 100.0f);
		}

		// Zoom con la Rueda
		if (@event is InputEventMouseButton mouseButton)
		{
			bool isWheelUp = mouseButton.ButtonIndex == MouseButton.WheelUp;
			bool isWheelDown = mouseButton.ButtonIndex == MouseButton.WheelDown;

			if (isWheelUp || isWheelDown)
			{
				Vector3 zoomDir = GlobalTransform.Basis.Z;
				float currentResist = GetZoomResistance(isWheelUp);
				float direction = isWheelUp ? -1.0f : 1.0f;

				_targetPosition += zoomDir * direction * ZoomSpeed * currentResist;
				
				GD.Print($"[Camera] Zoom {(isWheelUp ? "IN" : "OUT")}. Height: {_targetPosition.Y:F2}. Resistance: {currentResist:F2}");
			}
		}
	}

	private float GetZoomResistance(bool zoomingOut)
	{
		float y = _targetPosition.Y;
		float multiplier = 1.0f;

		// Solo frenamos cerca del portal que estamos intentando cruzar
		float targetT = zoomingOut ? OutPortalHeight : InPortalHeight;
		PortalType targetP = zoomingOut ? OutPortalTarget : InPortalTarget;

		if (targetP != PortalType.None)
		{
			float dist = Mathf.Abs(y - targetT);
			if (dist < ResistanceRange)
			{
				float factor = dist / ResistanceRange;
				multiplier = Mathf.Lerp(MinZoomMultiplier, 1.0f, factor);
			}
		}

		return multiplier;
	}

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
