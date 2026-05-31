using RDETerminal.Domain.Abstractions;

namespace RDETerminal.Domain;

public sealed class EventApi(ISelectedEventsSource source)
{
    private readonly ISelectedEventsSource _source = source;

    public EventSet Sel()
    {
        return _source.GetSelectedEvents();
    }
}