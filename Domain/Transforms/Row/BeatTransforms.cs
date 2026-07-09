using System;
using System.Collections.Generic;
using RDETerminal.Domain.Core;

namespace RDETerminal.Domain.Transforms.Row;

public static class BeatTransforms
{
    public static LevelDocument TransferBeatsFrom(
        LevelDocument level,
        int fromRow,
        int toRow,
        int startBar,
        float startBeat)
    {
        if (level == null) throw new ArgumentNullException(nameof(level));

        var newEvents = new List<LevelEventSnapshot>(level.Events.Count);

        foreach (var evt in level.Events)
        {
            if (evt.GetInt(EventFieldNames.Row)==fromRow&&evt.GetInt(EventFieldNames.Bar)>=startBar&&evt.GetFloat(EventFieldNames.Beat)>=startBeat)
            {
                evt.MarkForDelete();
                var modified = evt.Clone();
                modified.MarkForCreate("Rows");
                modified.Set(EventFieldNames.Row, toRow);
                newEvents.Add(modified);
            }
            newEvents.Add(evt);
        }

        return new LevelDocument(newEvents);
    }
}