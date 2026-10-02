using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

    // Invariant: always exactly the cells that produced _state (in order).
    // Only ever replaced wholesale (never mutated), and only under _sync.
    private ImmutableList<string> _committedCells = ImmutableList<string>.Empty;

    public RoslynScriptHost(ScriptOptions options)
    {
        _baseOptions = options ?? ScriptOptions.Default;
        _options = _baseOptions;
    }

    /// <summary>
    /// Code of every cell that has executed successfully since the last reset or
    /// reload, in execution order. These cells are exactly what produced the
    /// current script state, so anything that needs to know what is in scope
    /// right now (e.g. completion) should read this instead of keeping its own
    /// record.
    /// </summary>
    /// <remarks>
    /// The returned list is an immutable snapshot: a new instance is created
    /// whenever the contents change, so callers may use reference equality as a
    /// cheap "has anything changed?" check. Safe to call from any thread.
    /// </remarks>
    public IReadOnlyList<string> GetCommittedCells()
    {
        lock (_sync)
        {
            return _committedCells;
        }
    }

    public void ApplyReloadResult(UserScriptReloadResult result)
    {
        lock (_sync)
        {
            _options = BuildOptions(result);
            _state = null;
            _committedCells = ImmutableList<string>.Empty;
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
            ImmutableList<string> cells;
            int version;

            lock (_sync)
            {
                options = _options;
                state = _state;
                cells = _committedCells;
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

                    // Built from the snapshot taken together with `state` (not from
                    // the live list) so this always describes the chain of cells that
                    // produced `nextState`, even if two cells were in flight at once.
                    _committedCells = cells.Add(code);
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
            _committedCells = ImmutableList<string>.Empty;
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