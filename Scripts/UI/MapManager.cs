using Godot;
using System;
using System.Collections.Generic;
using Engine.Models;

public partial class MapManager : Node
{
	private MeshInstance3D _provincia1Mesh;
	private MeshInstance3D _provincia2Mesh;

	private Dictionary<string, MeshInstance3D> _provinceMeshes = new();
	private GameManager _gameManager;

	public override void _Ready()
	{
		// Obtenemos las mallas usando las rutas indicadas por el usuario
		_provincia1Mesh = GetNode<MeshInstance3D>("../Provincia_1");
		_provincia2Mesh = GetNode<MeshInstance3D>("../Provincia_2");

		// Mapeamos los IDs del motor ("tarsis" y "elysium") a los objetos visuales de Godot
		_provinceMeshes["tarsis"] = _provincia1Mesh;
		_provinceMeshes["elysium"] = _provincia2Mesh;

		// Buscamos el GameManager para acceder a los datos de la simulación
		_gameManager = GetParent().GetNode<GameManager>("GameManager");
		
		GD.Print("MapManager: Conectado a provincias visuales.");
	}

	public override void _Process(double delta)
	{
		if (_gameManager?.Motor?.Context == null) return;

		// Aquí puedes añadir lógica visual. Por ejemplo, detectar clics o cambiar colores.
		// Por ahora, solo nos aseguramos de que la conexión existe.
	}

	public Province GetProvinceAtMesh(MeshInstance3D mesh)
	{
		foreach (var entry in _provinceMeshes)
		{
			if (entry.Value == mesh)
			{
				return _gameManager.Motor.Context.Provinces.Find(p => p.Id == entry.Key);
			}
		}
		return null;
	}
}
