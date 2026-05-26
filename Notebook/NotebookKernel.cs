using System.Threading.Tasks;
using RDETerminal.Adapters;
using RDETerminal.Domain.Abstractions;
using RDETerminal.Scripting;

namespace RDETerminal.Notebook;

public sealed class NotebookKernel
{
    public RoslynCompletionSession Completion { get; }
    private readonly NotebookSession _session;
    private readonly ScriptGlobals _globals;
    private readonly RoslynScriptHost _host;

    public NotebookSession Session => _session;

    public NotebookKernel(EditorAdapter adapter, IGameLevelBridge levelBridge)
    {
        _session = new NotebookSession();
        _globals = new ScriptGlobals(_session, adapter, levelBridge);
        _host = new RoslynScriptHost(ScriptImports.Create());
        Completion=new RoslynCompletionSession();
    }

    public Task<NotebookCellResult> ExecuteAsync(string code)
    {
        var cell = new NotebookCell(code);
        _session.Cells.Add(cell);
        return ExecuteInternalAsync(cell, code);
    }

    private async Task<NotebookCellResult> ExecuteInternalAsync(NotebookCell cell, string code)
    {
        NotebookCellResult result = await _host.RunAsync(code, _globals);
        cell.Result = result;
        return result;
    }

    public void ResetExecutionState()
    {
        _host.Reset();
        _session.ClearRuntimeState();
        _session.WorkingLevel = null;
    }
}