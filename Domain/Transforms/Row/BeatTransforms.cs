using System;
using System.Collections.Generic;
using RDETerminal.Domain.Core;
using RDETerminal.Domain.Queries;

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
            if (EventQueries.IsEventInRowTab(evt)&&evt.GetInt(EventFieldNames.Row)==fromRow&&evt.GetInt(EventFieldNames.Bar)>=startBar&&evt.GetFloat(EventFieldNames.Beat)>=startBeat)
            {
                //According to scnEditor.AddNewEventControl, parent transform which events in "Rows" attached to depends on room id
                //If we only modify Row field, transferring beats that aren't in the same room will error
                //So we choose to delete original events and create new events
                var modified = evt.Clone();
                var deleted = evt.Clone();
                deleted.MarkForDelete();
                modified.MarkForCreate("Rows");
                modified.Set(EventFieldNames.Row, toRow);
                newEvents.Add(modified);
                newEvents.Add(deleted);
            }
            else newEvents.Add(evt);
        }

        return new LevelDocument(newEvents);
    }
}