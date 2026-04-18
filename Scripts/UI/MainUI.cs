using Godot;
using System;
using System.Linq;
using Engine.Services;
using Engine.Models;

public partial class MainUI : Control
{
    private Label _dateLabel = null!;
    private Label _popLabel = null!;
    private Label _statusLabel = null!;
    private GameManager _gameManager = null!;

    public override void _Ready()
    {
        _dateLabel = GetNode<Label>("MarginContainer/VBoxContainer/DateLabel");
        _popLabel = GetNode<Label>("MarginContainer/VBoxContainer/PopLabel");
        _statusLabel = GetNode<Label>("MarginContainer/VBoxContainer/StatusLabel");
        
        // Asumiendo que GameManager está en la escena principal
        _gameManager = GetParent().GetNode<GameManager>("GameManager");
    }

    public override void _Process(double delta)
    {
        if (_gameManager?.Motor?.Context == null) return;

        var context = _gameManager.Motor.Context;
        
        // Fecha
        _dateLabel.Text = $"📅 {GameCalendar.DisplayDate(context.CurrentDate)}";
        
        // Población
        _popLabel.Text = $"👥 Población: {context.WorldPopulation:N0}";
        
        // Mensaje de estado
        string status = _gameManager.Motor.StatusMessage;
        _statusLabel.Text = string.IsNullOrEmpty(status) ? "Simulación activa" : status;
    }
}
