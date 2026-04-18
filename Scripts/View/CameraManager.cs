using Godot;
using System;

public partial class CameraManager : Camera3D
{
	[Export] public float VelocidadMovimiento = 20.0f;
	[Export] public float VelocidadZoom = 2.0f;
	[Export] public float SensibilidadRotacion = 0.2f;

	private float _zoomObjetivo = 10.0f;
	private Vector3 _posicionObjetivo;

	public override void _Ready()
	{
		_posicionObjetivo = Position;
		_zoomObjetivo = Position.Y; // Usamos la altura como referencia de zoom
	}

	public override void _Process(double delta)
	{
		float d = (float)delta;
		Vector3 direccion = Vector3.Zero;

		// --- MOVIMIENTO WASD ---
		if (Input.IsActionPressed("ui_up"))    direccion.Z -= 1;
		if (Input.IsActionPressed("ui_down"))  direccion.Z += 1;
		if (Input.IsActionPressed("ui_left"))  direccion.X -= 1;
		if (Input.IsActionPressed("ui_right")) direccion.X += 1;

		// Normalizamos para que no vaya más rápido en diagonal
		if (direccion != Vector3.Zero)
		{
			direccion = direccion.Normalized();
			_posicionObjetivo += direccion * VelocidadMovimiento * d;
		}

		// --- SUAVIZADO (Lerp) ---
		// Esto hace que la cámara no se detenga en seco, dándole un toque profesional
		Position = Position.Lerp(_posicionObjetivo, 10 * d);
	}

	public override void _Input(InputEvent @event)
	{
		// --- ZOOM CON RUEDA DEL RATÓN ---
		if (@event is InputEventMouseButton mouseEvent)
		{
			if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
			{
				_posicionObjetivo.Y = Mathf.Clamp(_posicionObjetivo.Y - VelocidadZoom, 2, 30);
				_posicionObjetivo.Z = Mathf.Clamp(_posicionObjetivo.Z - VelocidadZoom, 2, 30);
			}
			if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
			{
				_posicionObjetivo.Y = Mathf.Clamp(_posicionObjetivo.Y + VelocidadZoom, 2, 30);
				_posicionObjetivo.Z = Mathf.Clamp(_posicionObjetivo.Z + VelocidadZoom, 2, 30);
			}
		}
	}
}
