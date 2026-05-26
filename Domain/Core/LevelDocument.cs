using System.Collections.Generic;
using System.Linq;

namespace RDETerminal.Domain.Core;

public sealed class LevelDocument
{
    public List<LevelEventSnapshot> Events { get; }

    public LevelDocument()
    {
        Events = [];
    }

    public LevelDocument(IEnumerable<LevelEventSnapshot> events)
    {
        Events = events != null ? [.. events] : [];
    }

    public LevelDocument Clone()
    {
        return new LevelDocument(Events.Select(e => e.Clone()));
    }
}
