using RDETerminal.Adapters;
using RDETerminal.Domain.Abstractions;
using RDETerminal.Domain.Core;
using RDETerminal.Notebook;

namespace RDETerminal.Scripting;

public sealed class GameApi(IGameLevelBridge levelBridge, NotebookSession session)
{
    private readonly IGameLevelBridge _levelBridge = levelBridge;
    private readonly NotebookSession _session = session;

    public LevelDocument Current
    {
        get => _session.WorkingLevel;
    }

    public LevelDocument Capture()
    {
        var level = _levelBridge.CaptureLevel();
        _session.WorkingLevel = level;
        return level;
    }

    public LevelApplyResult Apply(LevelDocument level = null)
    {
        var doc = level ?? _session.WorkingLevel;
        if (doc == null)
        {
            return null;
        }

        _session.WorkingLevel = doc;
        return _levelBridge.ApplyLevel(doc);
    }

    public LevelDocument Refresh()
    {
        return Capture();
    }
}