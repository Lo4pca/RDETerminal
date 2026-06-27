using System;
using System.Collections.Generic;
using RDETerminal.Domain.Core;

namespace RDETerminal.Notebook;

public sealed class NotebookSession
{
    private readonly List<string> _printed = [];
    private readonly Dictionary<string, object> _variables =
        new(StringComparer.OrdinalIgnoreCase);

    public List<NotebookCell> Cells { get; } = [];

    public LevelDocument WorkingLevel { get; set; }

    // ── Print buffer ──────────────────────────────────────────────────────────

    public void Print(object value)
    {
        _printed.Add(value == null ? "null" : value.ToString());
    }

    public string ConsumePrintedOutput()
    {
        if (_printed.Count == 0)
        {
            return string.Empty;
        }

        string text = string.Join("\n", _printed);
        _printed.Clear();
        return text;
    }

    // ── Variable store ────────────────────────────────────────────────────────

    /// <summary>Names of all currently stored variables.</summary>
    public IEnumerable<string> VariableNames => _variables.Keys;

    /// <summary>Sets or replaces a variable by name.</summary>
    public void Set(string name, object value)
    {
        _variables[name] = value;
    }

    /// <summary>Tries to retrieve a variable by name.</summary>
    public bool TryGet(string name, out object value)
    {
        return _variables.TryGetValue(name, out value);
    }

    /// <summary>
    /// Returns a variable cast to <typeparamref name="T"/>.
    /// Returns <c>default</c> when the name is not found or the value is null.
    /// Uses <see cref="Convert.ChangeType"/> as a fallback when the stored
    /// type is not directly assignable.
    /// </summary>
    public T Get<T>(string name)
    {
        if (!_variables.TryGetValue(name, out object value) || value == null)
        {
            return default;
        }

        if (value is T t)
        {
            return t;
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void ClearRuntimeState()
    {
        _printed.Clear();
        _variables.Clear();
    }
}