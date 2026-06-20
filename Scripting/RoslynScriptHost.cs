using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using RDETerminal.Notebook;
using RDETerminal.Scripting.HotReload;

namespace RDETerminal.Scripting;

public sealed class RoslynScriptHost
{
    private readonly ScriptOptions _baseOptions;
    private ScriptOptions _options;
    private ScriptState<object> _state;
    private readonly object _sync = new();
    private int _version;

    public RoslynScriptHost(ScriptOptions options)
    {
        _baseOptions = options ?? ScriptOptions.Default;
        _options = _baseOptions;
    }

    public void ApplyReloadResult(UserScriptReloadResult result)
    {
        lock (_sync)
        {
            _options = BuildOptions(result);
            _state = null;
            _version++;
            Plugin.LogInfo("[HotReload] session state was reset");
        }
    }

    public async Task<NotebookCellResult> RunAsync(string code, ScriptGlobals globals)
    {
        try
        {
            ScriptOptions options;
            ScriptState<object> state;
            int version;

            lock (_sync)
            {
                options = _options;
                state = _state;
                version = _version;
            }

            ScriptState<object> nextState;
            if (state == null)
            {
                nextState = await CSharpScript.RunAsync(code, options, globals, typeof(ScriptGlobals)).ConfigureAwait(false);
            }
            else
            {
                nextState = await state.ContinueWithAsync(code, options).ConfigureAwait(false);
            }

            lock (_sync)
            {
                if (version == _version)
                {
                    _state = nextState;
                }
            }

            globals.ans = nextState.ReturnValue;

            return new NotebookCellResult
            {
                Success = true,
                Output = globals.session.ConsumePrintedOutput(),
                ReturnValue = nextState.ReturnValue
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
        lock (_sync)
        {
            _state = null;
            _version++;
        }
    }

    private ScriptOptions BuildOptions(UserScriptReloadResult result)
    {
        ScriptOptions options = _baseOptions;

        if (result == null || !result.Success)
        {
            return options;
        }

        if (result.MetadataReference != null)
        {
            options = options.AddReferences(result.MetadataReference);
        }

        if (!result.ExportedNamespaces.IsDefaultOrEmpty)
        {
            options = options.AddImports([.. result.ExportedNamespaces.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)]);
        }

        return options;
    }
}