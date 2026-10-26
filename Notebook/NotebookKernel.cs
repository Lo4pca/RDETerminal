using System;
using System.Threading.Tasks;
using RDETerminal.Adapters;
using RDETerminal.Domain.Abstractions;
using RDETerminal.Scripting;
using RDETerminal.Scripting.HotReload;

namespace RDETerminal.Notebook;

public sealed class NotebookKernel
{
    public RoslynCompletionSession Completion { get; }

    private readonly NotebookSession _session;
    private readonly EditorAdapter _editorAdapter;
    private readonly ScriptGlobals _globals;
    private readonly RoslynScriptHost _host;
    private UserScriptReloadResult _hotReloadResult;

    public NotebookSession Session => _session;

    public UserScriptReloadResult HotReloadResult => _hotReloadResult;

    public NotebookKernel(EditorAdapter adapter, IGameLevelBridge levelBridge)
    {
        _session = new NotebookSession();
        _editorAdapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _globals = new ScriptGlobals(_session, _editorAdapter, levelBridge);
        _host = new RoslynScriptHost(ScriptImports.Create());
        Completion = new RoslynCompletionSession(_host.GetCommittedCells);
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
        // Count before resetting: these are the cells whose declarations are about to be lost.
        int discardedCells = _host.GetCommittedCells().Count;

        _host.Reset();
        _session.ClearRuntimeState();
        _session.WorkingLevel = null;

        _session.AddNotice(discardedCells == 0
            ? "Kernel reset. There was no script state to discard."
            : $"Kernel reset. Script state from {discardedCells} executed cell(s) was discarded; "
              + "working level is cleared.");
    }
}