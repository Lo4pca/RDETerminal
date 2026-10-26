namespace RDETerminal.Notebook;

/// <summary>
/// A system message shown in the transcript between cells (for example
/// "kernel reset"). Unlike a <see cref="NotebookCell"/> it has no code or result.
/// </summary>
public sealed class NotebookNotice(int afterCellCount, string message)
{
    /// <summary>
    /// How many cells existed when the notice was raised; the notice is shown
    /// after that many cells.
    /// </summary>
    public int AfterCellCount { get; } = afterCellCount;

    public string Message { get; } = message;
}