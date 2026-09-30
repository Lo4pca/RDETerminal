using System;
using System.Collections.Generic;
using RDETerminal.Domain.Core;
using RDETerminal.Domain.Queries;

namespace RDETerminal.Domain.Transforms.Common;

public static class EventTransforms
{
    public static void IncrementBarAndBeats(ref int bar, ref float beat, float num)
    {
        beat += num;
        while (beat >= 9f)
        {
            beat -= 8f;
            bar += 1;
        }
    }

    public static LevelDocument SetEventSpacingStartFrom(
        LevelDocument level,
        int startBar,
        float startBeat,
        float spacing,
        Func<LevelEventSnapshot, bool> filter=null)
    {
        return SetEventSpacingStartFrom(
            level,
            startBar,
            startBeat,
            Sequence.ConstantSpacing(spacing),
            filter);
    }

    /// <summary>
    /// Applies an existing beat-domain sequence from the requested origin.
    /// The sequence offsets are interpreted as global crotchet offsets, and
    /// the destination bar/beat is resolved using the level's CPB map.
    /// </summary>
    public static LevelDocument SetEventSpacingStartFrom(
        LevelDocument level,
        int startBar,
        float startBeat,
        Sequence sequence,
        Func<LevelEventSnapshot, bool> filter=null)
    {
        if (level == null) throw new ArgumentNullException(nameof(level));
        if (sequence == null) throw new ArgumentNullException(nameof(sequence));
        filter??=EventQueries.AllEvents;

        LevelTimingMap timing = LevelTimingMap.FromLevel(level);
        float originAbsoluteBeat = timing.GetAbsoluteBeat(startBar, startBeat);

        var newEvents = new List<LevelEventSnapshot>(level.Events.Count);
        int index = 0;

        foreach (LevelEventSnapshot evt in level.Events)
        {
            if (filter(evt))
            {
                float offset = sequence.GetOffset(index);
                LevelTimingMap.BarBeatPosition target = timing.GetPosition(
                    originAbsoluteBeat + offset);

                LevelEventSnapshot modified = evt.Clone();
                modified.Set(EventFieldNames.Bar, target.Bar);
                modified.Set(EventFieldNames.Beat, target.Beat);

                newEvents.Add(modified);
                index++;
            }
            else
            {
                newEvents.Add(evt);
            }
        }

        return new LevelDocument(newEvents);
    }

    /// <summary>
    /// Converts elapsed-time spacing to beat spacing at the requested origin,
    /// taking all BPM and crotchets-per-bar changes in the level into account.
    /// </summary>
    public static LevelDocument SetEventSpacingStartFrom(
        LevelDocument level,
        int startBar,
        float startBeat,
        TimeSequence sequence,
        Func<LevelEventSnapshot, bool> filter=null)
    {
        if (level == null) throw new ArgumentNullException(nameof(level));
        if (sequence == null) throw new ArgumentNullException(nameof(sequence));
        filter??=EventQueries.AllEvents;

        Sequence beatSequence = LevelTimingMap
            .FromLevel(level)
            .ToBeatSequence(sequence, startBar, startBeat);

        return SetEventSpacingStartFrom(
            level,
            startBar,
            startBeat,
            beatSequence,
            filter);
    }
}