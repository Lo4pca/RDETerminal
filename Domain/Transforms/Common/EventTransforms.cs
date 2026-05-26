using System;
using System.Collections.Generic;
using RDETerminal.Domain.Core;

namespace RDETerminal.Domain.Transforms.Common;

public static class EventTransforms
{
    public static void IncrementBarAndBeats(ref int bar, ref double beat, double num)
    {
        beat += num;
        while (beat >= 9d)
        {
            beat -= 8d;
            bar += 1;
        }
    }

    public static LevelDocument SetEventSpacingStartFrom(
        LevelDocument level,
        int startBar,
        double startBeat,
        double spacing,
        Func<LevelEventSnapshot, bool> filter)
    {
        if (level == null) throw new ArgumentNullException(nameof(level));
        if (filter == null) throw new ArgumentNullException(nameof(filter));

        var newEvents = new List<LevelEventSnapshot>(level.Events.Count);
        int currBar = startBar;
        double currBeat = startBeat;

        foreach (var evt in level.Events)
        {
            if (filter(evt))
            {
                var modified = evt.Clone();
                modified.Set(EventFieldNames.Bar, currBar);
                modified.Set(EventFieldNames.Beat, currBeat);
                IncrementBarAndBeats(ref currBar, ref currBeat, spacing);
                newEvents.Add(modified);
            }
            else
            {
                newEvents.Add(evt);
            }
        }

        return new LevelDocument(newEvents);
    }
}