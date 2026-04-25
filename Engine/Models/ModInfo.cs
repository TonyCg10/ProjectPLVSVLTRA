namespace Engine.Models;

/// <summary>
/// Contiene los metadatos de un Mod cargados desde su mod_info.json.
/// </summary>
public record ModInfo
{
    public string Id          { get; init; } = "";
    public string Name        { get; init; } = "";
    public string Version     { get; init; } = "1.0.0";
    public string Author      { get; init; } = "Unknown";
    public string Description { get; init; } = "";

    /// <summary>
    /// Lista de IDs de mods de los que este mod depende.
    /// Deben cargarse antes que este mod.
    /// </summary>
    public List<string> Dependencies { get; init; } = new();

    /// <summary>
    /// Ruta absoluta de la carpeta del mod.
    /// Se rellena en tiempo de ejecución.
    /// </summary>
    public string FolderPath { get; set; } = "";
}
