using System.IO;
using RDETerminal.Adapters;
using RDETerminal.Domain.Abstractions;
using RDETerminal.Notebook;
using RDETerminal.Scripting.HotReload;
using UnityEngine;

namespace RDETerminal.UI;

/// <summary>
/// Creates and wires the terminal window. Called once from the Harmony postfix
/// on scnEditor.Start(). Acts as the composition root for the terminal feature.
/// </summary>
public static class TerminalBootstrap
{
    private static TerminalWindow _window;
    private static UserScriptCatalog _catalog;
    private static UserScriptWatcher _watcher;
    private static UserScriptCompiler _compiler;

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

        InitializeHotReload(kernel);

        GameObject go = new("RDETerminal");
        Object.DontDestroyOnLoad(go);
        _window = go.AddComponent<TerminalWindow>();
        _window.Initialize(kernel);
    }

    private static void InitializeHotReload(NotebookKernel kernel)
    {
        string assemblyLocation = typeof(TerminalBootstrap).Assembly.Location;
        string rootDir = Path.GetDirectoryName(assemblyLocation) ?? Application.dataPath;
        string scriptsDir = Path.Combine(rootDir, "Scripts");

        _catalog = new UserScriptCatalog(scriptsDir);

        _compiler = new UserScriptCompiler(
            defaultImports:
            [
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "RDLevelEditor",
                "RDETerminal.Domain",
                "RDETerminal.Domain.Core",
                "RDETerminal.Domain.Queries",
                "RDETerminal.Domain.Transforms.Common",
                "RDETerminal.Domain.Transforms.Text",
                "RDETerminal.Scripting"
            ]);

        _watcher = new UserScriptWatcher(scriptsDir, _catalog);
        _watcher.OnReloadRequested += () => ReloadScripts(kernel);

        ReloadScripts(kernel);
    }

    private static async void ReloadScripts(NotebookKernel kernel)
    {
        if (_compiler == null || _catalog == null || kernel == null)
        {
            return;
        }

        UserScriptReloadResult result = _compiler.Compile(_catalog);

        if (result.Success)
        {
            await kernel.ApplyReloadResult(result).ConfigureAwait(false);
            Plugin.LogInfo("[HotReload] user scripts reloaded successfully.");
        }
        else
        {
            Plugin.LogError("[HotReload] user scripts reload failed:");
            if (!string.IsNullOrWhiteSpace(result.ErrorSummary))
            {
                Plugin.LogError(result.ErrorSummary);
            }
        }
    }
}