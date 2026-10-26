using System.Collections.Generic;
using RDETerminal.Domain.Core;

namespace RDETerminal.Notebook;

public sealed class NotebookSession
{
    private readonly List<string> _printed = [];
    private readonly List<NotebookNotice> _notices = [];
    public List<NotebookCell> Cells { get; } = [];

    /// <summary>
    /// System messages (such as "kernel reset") in chronological order. They
    /// survive <see cref="ClearRuntimeState"/> because they are part of the
    /// transcript, not of the runtime state.
    /// </summary>
    public IReadOnlyList<NotebookNotice> Notices => _notices;

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

    // ── Transcript notices ────────────────────────────────────────────────────

    /// <summary>Records a system message at the current position in the transcript.</summary>
    public void AddNotice(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _notices.Add(new NotebookNotice(Cells.Count, message));
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void ClearRuntimeState()
    {
        _printed.Clear();
    }
}