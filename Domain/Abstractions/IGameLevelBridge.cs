using RDETerminal.Adapters;
using RDETerminal.Domain.Core;

namespace RDETerminal.Domain.Abstractions;

public interface IGameLevelBridge
{
    LevelDocument CaptureLevel();
    LevelApplyResult ApplyLevel(LevelDocument level);
}