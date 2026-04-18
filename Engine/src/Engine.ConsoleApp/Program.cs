using Engine;
using Engine.Services;
using Engine.Systems;
using Engine.Interfaces;

// El baseFolder contiene data/, localization/, config.json, logs/, saves/
string baseFolder = AppDomain.CurrentDomain.BaseDirectory;

// 1. Config primero (determina idioma, debug mode, etc.)
Config.Load(baseFolder);

// 2. Mundo (carga localization según config)
var context = DataService.LoadFullWorld(baseFolder);
context.Language = Config.Language;

var systems = new List<ISystem>
{
    new IndustryExpansionSystem(),
    new PopSystem()
};

var engine = new Motor(context, systems, baseFolder);
engine.Start();