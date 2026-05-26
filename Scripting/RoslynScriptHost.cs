using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using RDETerminal.Notebook;

namespace RDETerminal.Scripting;

public sealed class RoslynScriptHost(ScriptOptions options)
{
    private readonly ScriptOptions _options = options;
    private ScriptState<object> _state;

    public async Task<NotebookCellResult> RunAsync(string code, ScriptGlobals globals)
    {
        try
        {
            if (_state == null)
            {
                _state = await CSharpScript.RunAsync(code, _options, globals, typeof(ScriptGlobals));
            }
            else
            {
                _state = await _state.ContinueWithAsync(code, _options);
            }

            globals.ans = _state.ReturnValue;

            return new NotebookCellResult
            {
                Success = true,
                Output = globals.session.ConsumePrintedOutput(),
                ReturnValue = _state.ReturnValue
            };
        }
        catch (CompilationErrorException ex)
        {
            return new NotebookCellResult
            {
                Success = false,
                Error = string.Join(Environment.NewLine, ex.Diagnostics),
                ReturnValue = null
            };
        }
        catch (Exception ex)
        {
            return new NotebookCellResult
            {
                Success = false,
                Error = ex.ToString(),
                ReturnValue = null
            };
        }
    }

    public void Reset()
    {
        _state = null;
    }
}