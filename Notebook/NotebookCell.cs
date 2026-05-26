using System;

namespace RDETerminal.Notebook;

public sealed class NotebookCell(string code)
{
    public string Code { get; private set; } = code;
    public DateTime CreatedAt { get; private set; } = DateTime.Now;
    public NotebookCellResult Result { get; set; }
}