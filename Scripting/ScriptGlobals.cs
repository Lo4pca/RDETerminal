using System;
using RDETerminal.Adapters;
using RDETerminal.Domain;
using RDETerminal.Domain.Abstractions;
using RDETerminal.Domain.Core;
using RDETerminal.Notebook;

namespace RDETerminal.Scripting;

public sealed class ScriptGlobals
{
    public readonly NotebookSession session;
    public readonly VarApi vars;
    public readonly EventApi events;
    public readonly Func<EventSet> sel;
    public readonly GameApi game;
    public readonly EditorAdapter editor;
    public object ans;

    public LevelDocument Level
    {
        get => session.WorkingLevel;
        set => session.WorkingLevel = value;
    }

    public ScriptGlobals(
        NotebookSession session,
        EditorAdapter adapter,
        IGameLevelBridge levelBridge)
    {
        this.session = session;
        vars = new VarApi(session);
        events = new EventApi(adapter);
        sel = () => events.Sel();
        game = new GameApi(levelBridge, session);
        ans = null;
        editor=adapter;
    }
}