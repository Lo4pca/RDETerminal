using System;
using RDETerminal.Domain.Core;

namespace RDETerminal.Domain.Queries;

public static class EventQueries
{
    public static bool TypeIs(LevelEventSnapshot evt, string typeName)
    {
        return evt != null && string.Equals(evt.Type, typeName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool BarAtLeast(LevelEventSnapshot evt, int bar)
    {
        return evt != null && evt.GetInt(EventFieldNames.Bar) >= bar;
    }

    public static bool BeatAtMost(LevelEventSnapshot evt, float beat)
    {
        return evt != null && evt.GetFloat(EventFieldNames.Beat) <= beat;
    }
    public static bool IsEventInRowTab(LevelEventSnapshot evt)
    {
        return evt != null && (TypeIs(evt,EventTypeNames.AddOneshotBeat)||
                               TypeIs(evt,EventTypeNames.AddClassicBeat)||
                               TypeIs(evt,EventTypeNames.AddFreeTimeBeat)||
                               TypeIs(evt,EventTypeNames.PulseFreeTimeBeat)||
                               TypeIs(evt,EventTypeNames.SetRowXs));
    }
    public static bool AllEvents(LevelEventSnapshot _)
    {
        return true;
    }
}