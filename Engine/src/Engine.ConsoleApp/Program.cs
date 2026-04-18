using Engine;
using Engine.Services;
using Engine.Systems;
using Engine.Interfaces;

string dataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

var context = DataService.LoadFullWorld(dataFolder);

var systems = new List<ISystem>
{
    new PopSystem()
};

var engine = new Motor(context, systems);
engine.Start();