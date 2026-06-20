using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using RDETerminal.Adapters;
using RDETerminal.Domain.Abstractions;
using RDETerminal.Scripting;
using RDETerminal.Scripting.HotReload;

namespace RDETerminal.Notebook;

public sealed class NotebookKernel
{
    public RoslynCompletionSession Completion { get; }

    private readonly NotebookSession _session;
    private readonly ScriptGlobals _globals;
    private readonly RoslynScriptHost _host;
    private UserScriptReloadResult _hotReloadResult;

    public NotebookSession Session => _session;

    public UserScriptReloadResult HotReloadResult => _hotReloadResult;

    public NotebookKernel(EditorAdapter adapter, IGameLevelBridge levelBridge)
    {
        _session = new NotebookSession();
        _globals = new ScriptGlobals(_session, adapter, levelBridge);
        _host = new RoslynScriptHost(ScriptImports.Create());
        Completion = new RoslynCompletionSession(GetMetadataReferences());
    }

    public async Task ApplyReloadResult(UserScriptReloadResult result)
    {
        if (result == null || !result.Success)
        {
            return;
        }

        _hotReloadResult = result;
        _host.ApplyReloadResult(result);
        await Completion.ApplyReloadResult(result).ConfigureAwait(false);
    }

    public Task<NotebookCellResult> ExecuteAsync(string code)
    {
        var cell = new NotebookCell(code);
        _session.Cells.Add(cell);
        return ExecuteInternalAsync(cell, code);
    }

    private async Task<NotebookCellResult> ExecuteInternalAsync(NotebookCell cell, string code)
    {
        NotebookCellResult result = await _host.RunAsync(code, _globals).ConfigureAwait(false);
        cell.Result = result;
        return result;
    }

    public void ResetExecutionState()
    {
        _host.Reset();
        _session.ClearRuntimeState();
        _session.WorkingLevel = null;
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == null || assembly.IsDynamic)
            {
                continue;
            }

            string location;
            try
            {
                location = assembly.Location;
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(location) || !seen.Add(location))
            {
                continue;
            }

            yield return MetadataReference.CreateFromFile(location);
        }
    }
}