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
/// <remarks>
/// User script reloading is manual (triggered by the "Reload Scripts" button),
/// not automatic on file save. <c>Assembly.Load(byte[])</c> — the only way to
/// load a compiled user-script assembly under Unity's Mono runtime — cannot be
/// unloaded; every reload permanently grows the process's resident assembly
/// count. Automatic file-watching would silently trigger this cost on every
/// save (including saves from format-on-save tooling). Manual reload puts the
/// user in control of when that cost is paid. See the manual reload report
/// for the full investigation of why this can't be made safe automatically
/// on this runtime.
/// </remarks>
public static class TerminalBootstrap
{
    private static TerminalWindow _window;
    private static UserScriptCatalog _catalog;
    private static UserScriptCompiler _compiler;
    private static NotebookKernel _kernel;

    public static void Ensure()
    {
        if (_window != null)
        {
            return;
        }

        ReflectionGameEventBridge eventBridge = new();
        EditorAdapter editorAdapter = new(eventBridge);
        IGameLevelBridge levelBridge = new GameLevelBridge(editorAdapter, eventBridge);
        _kernel = new NotebookKernel(editorAdapter, levelBridge);

        InitializeUserScripts();

        GameObject go = new("RDETerminal");
        Object.DontDestroyOnLoad(go);
        _window = go.AddComponent<TerminalWindow>();
        _window.Initialize(_kernel);
    }

    /// <summary>
    /// Recompiles every .cs file in the Scripts folder and applies the result
    /// to the running kernel and completion session. Called explicitly by the
    /// "Reload Scripts" button — never automatically. Each call loads a new
    /// assembly into the process that cannot be unloaded; calling this
    /// repeatedly in a single session will grow memory usage over time. This
    /// is an unavoidable platform limitation, not a bug — see the remarks on
    /// this class for the full explanation.
    /// </summary>
    internal static async void ReloadUserScripts()
    {
        if (_compiler == null || _catalog == null || _kernel == null)
        {
            return;
        }

        Plugin.LogInfo("[ScriptReload] reloading user scripts...");

        _catalog.Refresh();
        UserScriptReloadResult result = _compiler.Compile(_catalog);

        if (result.Success)
        {
            await _kernel.ApplyReloadResult(result).ConfigureAwait(false);
            Plugin.LogInfo("[ScriptReload] user scripts reloaded successfully.");
        }
        else
        {
            Plugin.LogError("[ScriptReload] user scripts reload failed:");
            if (!string.IsNullOrWhiteSpace(result.ErrorSummary))
            {
                Plugin.LogError(result.ErrorSummary);
            }
        }
    }

    private static void InitializeUserScripts()
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

        // Initial load happens once at startup, same as before — only the
        // ongoing file-watching behavior is removed.
        ReloadUserScripts();
    }
}