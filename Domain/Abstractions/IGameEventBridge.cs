using RDETerminal.Domain.Core;

namespace RDETerminal.Domain.Abstractions;

public interface IGameEventBridge
{
    LevelEventSnapshot Capture(object gameEvent);
    EventApplyResult Apply(LevelEventSnapshot snapshot, object gameEvent);
}