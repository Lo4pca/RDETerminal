using System;
using System.Collections.Generic;
using RDETerminal.Domain.Core;

namespace RDETerminal.Notebook;

public sealed class NotebookSession
{
    private readonly List<string> _printed = [];

    public List<NotebookCell> Cells { get; } = [];

    public Dictionary<string, object> Variables { get; } =
        new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

    public LevelDocument WorkingLevel { get; set; }

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

    public void Set(string name, object value)
    {
        Variables[name] = value;
    }

    public bool TryGet(string name, out object value)
    {
        return Variables.TryGetValue(name, out value);
    }

    public void ClearRuntimeState()
    {
        _printed.Clear();
        Variables.Clear();
    }
}