using RDETerminal.Adapters;
using RDETerminal.Domain.Abstractions;
using RDETerminal.Notebook;
using UnityEngine;

namespace RDETerminal.UI;

/// <summary>
/// Creates and wires the terminal window. Called once from the Harmony postfix
/// on scnEditor.Start(). Acts as the composition root for the terminal feature.
/// </summary>
public static class TerminalBootstrap
{
    private static TerminalWindow _window;

    public static void Ensure()
    {
        if (_window != null)
        {
            return;
        }

        ReflectionGameEventBridge eventBridge = new();
        EditorAdapter editorAdapter = new(eventBridge);
        IGameLevelBridge levelBridge = new GameLevelBridge(editorAdapter, eventBridge);
        NotebookKernel kernel = new(editorAdapter, levelBridge);

        GameObject go = new("RDETerminal");
        Object.DontDestroyOnLoad(go);
        _window = go.AddComponent<TerminalWindow>();
        _window.Initialize(kernel);
    }
}