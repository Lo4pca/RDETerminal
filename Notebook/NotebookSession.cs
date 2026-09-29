using System.Collections.Generic;
using RDETerminal.Domain.Core;

namespace RDETerminal.Notebook;

public sealed class NotebookSession
{
    private readonly List<string> _printed = [];
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

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void ClearRuntimeState()
    {
        _printed.Clear();
    }
}