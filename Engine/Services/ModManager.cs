using System.Text.Json;
using Engine.Models;

namespace Engine.Services;

/// <summary>
/// Gestiona el descubrimiento, validación, ordenamiento y carga de los mods.
/// </summary>
public static class ModManager
{
    private static readonly List<ModInfo> _loadedMods = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<ModInfo> LoadedMods => _loadedMods;

    /// <summary>
    /// Escanea la carpeta, resuelve dependencias, y carga la metadata y el scripting de todos los mods válidos.
    /// Esto DEBE llamarse después de GameRegistry.LoadBase, pero antes de crear el mundo.
    /// </summary>
    public static void Initialize(string modsFolder)
    {
        _loadedMods.Clear();

        if (!Directory.Exists(modsFolder))
        {
            Directory.CreateDirectory(modsFolder);
            return; // No hay mods
        }

        var discoveredMods = new Dictionary<string, ModInfo>();

        // 1. Descubrir mods
        foreach (var dir in Directory.GetDirectories(modsFolder))
        {
            var infoPath = Path.Combine(dir, "mod_info.json");
            if (File.Exists(infoPath))
            {
                try
                {
                    var modInfo = JsonSerializer.Deserialize<ModInfo>(File.ReadAllText(infoPath), JsonOpts);
                    if (modInfo != null && !string.IsNullOrWhiteSpace(modInfo.Id))
                    {
                        modInfo.FolderPath = dir;
                        discoveredMods[modInfo.Id] = modInfo;
                    }
                }
                catch (Exception ex)
                {
                    GameLogger.Error("ModManager", $"Error leyendo {infoPath}: {ex.Message}");
                }
            }
        }

        // 2. Resolver dependencias (Topological Sort simple)
        var sortedMods = ResolveDependencies(discoveredMods);

        // 3. Cargar mods en orden
        foreach (var mod in sortedMods)
        {
            LoadModData(mod);
            LoadModScripts(mod);
            _loadedMods.Add(mod);
            GameLogger.Info("ModManager", $"Mod cargado: {mod.Name} v{mod.Version} ({mod.Id})");
        }
    }

    private static void LoadModData(ModInfo mod)
    {
        string defsFolder = Path.Combine(mod.FolderPath, "data", "definitions");
        GameRegistry.LoadMod(defsFolder);

        string locFolder = Path.Combine(mod.FolderPath, "localization");
        Loc.RegisterModFolder(locFolder);
    }

    private static void LoadModScripts(ModInfo mod)
    {
        string controlLua = Path.Combine(mod.FolderPath, "control.lua");
        if (File.Exists(controlLua))
        {
            try
            {
                string scriptContent = File.ReadAllText(controlLua);
                ScriptingService.ExecuteScript(scriptContent, mod.Id);
            }
            catch (Exception ex)
            {
                GameLogger.Error("ModManager", $"Error ejecutando script Lua de {mod.Id}: {ex.Message}");
            }
        }
    }

    private static List<ModInfo> ResolveDependencies(Dictionary<string, ModInfo> mods)
    {
        var sorted = new List<ModInfo>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        void Visit(ModInfo mod)
        {
            if (visiting.Contains(mod.Id))
            {
                GameLogger.Error("ModManager", $"Dependencia circular detectada involucrando el mod {mod.Id}. Saltando.");
                return;
            }
            if (!visited.Contains(mod.Id))
            {
                visiting.Add(mod.Id);
                foreach (var dep in mod.Dependencies)
                {
                    if (mods.TryGetValue(dep, out var depMod))
                    {
                        Visit(depMod);
                    }
                    else
                    {
                        GameLogger.Warning("ModManager", $"Falta la dependencia '{dep}' para el mod '{mod.Id}'. El mod podría no funcionar.");
                    }
                }
                visiting.Remove(mod.Id);
                visited.Add(mod.Id);
                sorted.Add(mod);
            }
        }

        foreach (var mod in mods.Values)
        {
            Visit(mod);
        }

        return sorted;
    }
}
