using System;
using RDETerminal.Domain.Core;

namespace RDETerminal.Domain.Abstractions;

public interface IGameEventBridge
{
    bool CanHandle(Type eventType, string snapshotType);

    LevelEventSnapshot Capture(object gameEvent);

    EventApplyResult Apply(LevelEventSnapshot snapshot, object gameEvent);
}