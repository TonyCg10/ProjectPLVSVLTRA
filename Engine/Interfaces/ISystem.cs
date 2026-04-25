using Engine.Models;

namespace Engine.Interfaces;

public interface ISystem
{
    string Name { get; }
    void Update(GameContext context, long currentTick);
}