using RDETerminal.Adapters;

namespace RDETerminal.Domain;

public sealed class EventApi(EditorAdapter adapter)
{
    private readonly EditorAdapter _adapter = adapter;

    public EventSet Sel()
    {
        return _adapter.GetSelectedEvents();
    }
}