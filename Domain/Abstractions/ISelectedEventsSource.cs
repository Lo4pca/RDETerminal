namespace RDETerminal.Domain.Abstractions;

/// <summary>
/// Provides the currently selected level events from the editor.
/// Implemented by <see cref="Adapters.EditorAdapter"/>.
/// </summary>
public interface ISelectedEventsSource
{
    EventSet GetSelectedEvents();
}