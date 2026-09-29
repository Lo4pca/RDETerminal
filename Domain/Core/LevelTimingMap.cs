using RDETerminal.Domain.Queries;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RDETerminal.Domain.Core;

/// <summary>
/// Converts between the level editor's musical position model and nominal
/// one-timespeed elapsed time.
///
/// The map mirrors the relevant behavior found in the game's LevelBase and
/// Timeline implementations:
///   - crotchets-per-bar changes apply from their bar onward;
///   - BPM changes occur at their bar/beat position;
///   - the first PlaySong BPM establishes the initial BPM when one exists;
///   - otherwise the game's default BPM of 100 is used.
///
/// Unlike the live scrConductor, this class is pure Domain code and can be
/// used while the editor is stopped.
/// </summary>
public sealed class LevelTimingMap
{
    private const float DefaultBpm = 100f;
    private const int DefaultCrotchetsPerBar = 8;

    private readonly CpbSegment[] _cpbSegments;
    private readonly TempoSegment[] _tempoSegments;

    private LevelTimingMap(
        CpbSegment[] cpbSegments,
        TempoSegment[] tempoSegments)
    {
        _cpbSegments = cpbSegments;
        _tempoSegments = tempoSegments;
    }

    /// <summary>
    /// Builds a timing map from a captured level document.
    /// </summary>
    public static LevelTimingMap FromLevel(LevelDocument level)
    {
        if (level == null) throw new ArgumentNullException(nameof(level));

        List<LevelEventSnapshot> events = [.. level.Events.Where(evt => evt != null)];

        CpbSegment[] cpbSegments = BuildCpbSegments(events);
        TempoSegment[] tempoSegments = BuildTempoSegments(events, cpbSegments);

        return new LevelTimingMap(cpbSegments, tempoSegments);
    }

    /// <summary>
    /// Returns the global musical beat coordinate for a bar/beat position.
    /// Bar 1, beat 1 is coordinate 0.
    /// </summary>
    public float GetAbsoluteBeat(int bar, float beat)
    {
        if (bar < 1)
            throw new ArgumentOutOfRangeException(nameof(bar), "bar must be at least 1.");
        if (beat < 1f)
            throw new ArgumentOutOfRangeException(nameof(beat), "beat must be at least 1.");

        CpbSegment segment = FindCpbSegment(bar);

        return segment.StartAbsoluteBeat
             + (bar - segment.StartBar) * segment.CrotchetsPerBar
             + (beat - 1f);
    }

    /// <summary>
    /// Converts a global musical beat coordinate back into bar/beat.
    /// </summary>
    public BarBeatPosition GetPosition(float absoluteBeat)
    {
        if (float.IsNaN(absoluteBeat) || float.IsInfinity(absoluteBeat))
            throw new ArgumentOutOfRangeException(nameof(absoluteBeat));

        if (absoluteBeat < 0f)
            throw new ArgumentOutOfRangeException(
                nameof(absoluteBeat),
                "Positions before bar 1 beat 1 are not representable.");

        CpbSegment segment = FindCpbSegmentForBeat(absoluteBeat);
        float delta = absoluteBeat - segment.StartAbsoluteBeat;

        long barsFromStart = (long)Math.Floor(delta / segment.CrotchetsPerBar);
        float beatRemainder = delta - barsFromStart * segment.CrotchetsPerBar;

        return new BarBeatPosition(
            checked(segment.StartBar + (int)barsFromStart),
            beatRemainder + 1f);
    }

    /// <summary>
    /// Returns elapsed nominal seconds from bar 1 beat 1 to the supplied
    /// absolute beat coordinate.
    /// </summary>
    public float GetTimeAtAbsoluteBeat(float absoluteBeat)
    {
        if (float.IsNaN(absoluteBeat) || float.IsInfinity(absoluteBeat))
            throw new ArgumentOutOfRangeException(nameof(absoluteBeat));
        if (absoluteBeat < 0f)
            throw new ArgumentOutOfRangeException(nameof(absoluteBeat));

        TempoSegment segment = FindTempoSegment(absoluteBeat);
        return segment.StartTime
             + (absoluteBeat - segment.StartAbsoluteBeat) * 60f / segment.Bpm;
    }

    /// <summary>
    /// Returns the absolute beat coordinate reached after the supplied
    /// elapsed nominal seconds from bar 1 beat 1.
    /// </summary>
    public float GetAbsoluteBeatAtTime(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (seconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(seconds));

        TempoSegment segment = FindTempoSegmentForTime(seconds);
        return segment.StartAbsoluteBeat
             + (seconds - segment.StartTime) * segment.Bpm / 60f;
    }

    /// <summary>
    /// Converts a sequence of elapsed-time offsets to beat offsets measured
    /// from the given bar/beat origin.
    /// </summary>
    public Sequence ToBeatSequence(
        TimeSequence sequence,
        int startBar,
        float startBeat)
    {
        if (sequence == null) throw new ArgumentNullException(nameof(sequence));

        float originAbsoluteBeat = GetAbsoluteBeat(startBar, startBeat);
        float originTime = GetTimeAtAbsoluteBeat(originAbsoluteBeat);

        if (sequence.Count.HasValue)
        {
            var offsets = new float[sequence.Count.Value];
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = GetAbsoluteBeatAtTime(originTime + (float)sequence.GetOffset(i))
                           - originAbsoluteBeat;
            }

            return Sequence.FromOffsets(offsets);
        }

        return Sequence.Generate(index =>
            GetAbsoluteBeatAtTime(originTime + (float)sequence.GetOffset(index))
            - originAbsoluteBeat);
    }

    public readonly struct BarBeatPosition(int bar, float beat)
    {
        public int Bar { get; } = bar;
        public float Beat { get; } = beat;

        public override string ToString()
        {
            return $"bar: {Bar}, beat: {Beat.ToString(CultureInfo.InvariantCulture)}";
        }
    }

    private readonly struct CpbEvent(int bar, int crotchetsPerBar, int order)
    {
        public int Bar { get; } = bar;
        public int CrotchetsPerBar { get; } = crotchetsPerBar;
        public int Order { get; } = order;
    }

    private readonly struct CpbSegment(int startBar, float startAbsoluteBeat, int crotchetsPerBar)
    {
        public int StartBar { get; } = startBar;
        public float StartAbsoluteBeat { get; } = startAbsoluteBeat;
        public int CrotchetsPerBar { get; } = crotchetsPerBar;
    }

    private readonly struct TempoEvent(float absoluteBeat, float bpm, int order)
    {
        public float AbsoluteBeat { get; } = absoluteBeat;
        public float Bpm { get; } = bpm;
        public int Order { get; } = order;
    }

    private readonly struct TempoSegment(
        float startAbsoluteBeat,
        float startTime,
        float bpm)
    {
        public float StartAbsoluteBeat { get; } = startAbsoluteBeat;
        public float StartTime { get; } = startTime;
        public float Bpm { get; } = bpm;
    }

    private static CpbSegment[] BuildCpbSegments(
        IReadOnlyList<LevelEventSnapshot> events)
    {
        var byBar = new Dictionary<int, CpbEvent>();

        for (int i = 0; i < events.Count; i++)
        {
            LevelEventSnapshot evt = events[i];
            if (!EventQueries.TypeIs(evt, EventTypeNames.SetCrotchetsPerBar))
                continue;

            int bar = evt.GetInt(EventFieldNames.Bar);
            int cpb = evt.GetInt(EventFieldNames.CrotchetsPerBar);

            if (bar < 1)
                throw new InvalidOperationException("A crotchets-per-bar event has an invalid bar.");
            if (cpb <= 0)
                throw new InvalidOperationException("A crotchets-per-bar event has an invalid crotchetsPerBar value.");

            byBar[bar] = new CpbEvent(bar, cpb, i);
        }

        var changes = new List<CpbEvent>(byBar.Values)
        {
            new(1, DefaultCrotchetsPerBar, -1)
        };
        changes = [.. changes
            .GroupBy(change => change.Bar)
            .Select(group => group.OrderBy(change => change.Order).Last())
            .OrderBy(change => change.Bar)];

        var segments = new List<CpbSegment>(changes.Count);
        int currentBar = changes[0].Bar;
        int currentCpb = changes[0].CrotchetsPerBar;
        float currentAbsoluteBeat = 0f;

        foreach (CpbEvent change in changes)
        {
            if (change.Bar > currentBar)
            {
                currentAbsoluteBeat +=
                    (change.Bar - currentBar) * currentCpb;
            }

            currentBar = change.Bar;
            currentCpb = change.CrotchetsPerBar;

            // Segment coordinate is the beginning of the changed bar.
            if (segments.Count == 0 || segments[^1].StartBar != currentBar)
            {
                segments.Add(new CpbSegment(
                    currentBar,
                    currentAbsoluteBeat,
                    currentCpb));
            }
            else
            {
                // Same-bar duplicates have already been collapsed, so this is
                // defensive only.
                segments[^1] = new CpbSegment(
                    currentBar,
                    currentAbsoluteBeat,
                    currentCpb);
            }
        }

        return [.. segments];
    }

    private static TempoSegment[] BuildTempoSegments(
        IReadOnlyList<LevelEventSnapshot> events,
        IReadOnlyList<CpbSegment> cpbSegments)
    {
        float initialBpm = DefaultBpm;

        for (int i = 0; i < events.Count; i++)
        {
            LevelEventSnapshot evt = events[i];
            if (!EventQueries.TypeIs(evt, EventTypeNames.PlaySong))
                continue;

            float bpm = evt.GetFloat(EventFieldNames.BeatsPerMinute);
            if (bpm > 0f)
            {
                initialBpm = bpm;
                break;
            }
        }

        var changes = new List<TempoEvent>();

        for (int i = 0; i < events.Count; i++)
        {
            LevelEventSnapshot evt = events[i];
            bool isPlaySong = EventQueries.TypeIs(evt, EventTypeNames.PlaySong);
            bool isSetBpm = EventQueries.TypeIs(evt, EventTypeNames.SetBeatsPerMinute);

            if (!isPlaySong && !isSetBpm)
                continue;

            float bpm = evt.GetFloat(EventFieldNames.BeatsPerMinute);
            if (bpm <= 0f)
            {
                throw new InvalidOperationException(
                    $"Tempo event at index {i} has a non-positive BPM: {bpm}.");
            }

            int bar = evt.GetInt(EventFieldNames.Bar);
            float beat = evt.GetFloat(EventFieldNames.Beat);
            float absoluteBeat = GetAbsoluteBeatFromCpb(
                bar,
                beat,
                cpbSegments);

            changes.Add(new TempoEvent(absoluteBeat, bpm, i));
        }

        changes.Sort((left, right) =>
        {
            int byBeat = left.AbsoluteBeat.CompareTo(right.AbsoluteBeat);
            return byBeat != 0 ? byBeat : left.Order.CompareTo(right.Order);
        });

        var segments = new List<TempoSegment>(changes.Count + 1)
        {
            new(0f, 0f, initialBpm)
        };

        float previousBeat = 0f;
        float previousTime = 0f;
        float currentBpm = initialBpm;

        foreach (TempoEvent change in changes)
        {
            if (change.AbsoluteBeat < 0f)
                continue;

            if (change.AbsoluteBeat > previousBeat)
            {
                previousTime +=
                    (change.AbsoluteBeat - previousBeat) * 60f / currentBpm;
                previousBeat = change.AbsoluteBeat;
            }

            currentBpm = change.Bpm;

            if (segments.Count > 0 &&
                Math.Abs(segments[^1].StartAbsoluteBeat - change.AbsoluteBeat) < 1e-9f)
            {
                segments[^1] = new TempoSegment(
                    change.AbsoluteBeat,
                    previousTime,
                    currentBpm);
            }
            else
            {
                segments.Add(new TempoSegment(
                    change.AbsoluteBeat,
                    previousTime,
                    currentBpm));
            }
        }

        return [.. segments];
    }

    private static float GetAbsoluteBeatFromCpb(
        int bar,
        float beat,
        IReadOnlyList<CpbSegment> cpbSegments)
    {
        if (bar < 1)
            throw new InvalidOperationException("Tempo event has an invalid bar.");
        if (beat < 1f)
            throw new InvalidOperationException("Tempo event has an invalid beat.");

        CpbSegment segment = cpbSegments[0];
        for (int i = 1; i < cpbSegments.Count; i++)
        {
            if (cpbSegments[i].StartBar > bar)
                break;

            segment = cpbSegments[i];
        }

        return segment.StartAbsoluteBeat
             + (bar - segment.StartBar) * segment.CrotchetsPerBar
             + beat - 1f;
    }

    private CpbSegment FindCpbSegment(int bar)
    {
        CpbSegment result = _cpbSegments[0];

        for (int i = 1; i < _cpbSegments.Length; i++)
        {
            if (_cpbSegments[i].StartBar > bar)
                break;

            result = _cpbSegments[i];
        }

        return result;
    }

    private CpbSegment FindCpbSegmentForBeat(float absoluteBeat)
    {
        CpbSegment result = _cpbSegments[0];

        for (int i = 1; i < _cpbSegments.Length; i++)
        {
            if (_cpbSegments[i].StartAbsoluteBeat > absoluteBeat + 1e-12f)
                break;

            result = _cpbSegments[i];
        }

        return result;
    }

    private TempoSegment FindTempoSegment(float absoluteBeat)
    {
        TempoSegment result = _tempoSegments[0];

        for (int i = 1; i < _tempoSegments.Length; i++)
        {
            if (_tempoSegments[i].StartAbsoluteBeat > absoluteBeat + 1e-12f)
                break;

            result = _tempoSegments[i];
        }

        return result;
    }

    private TempoSegment FindTempoSegmentForTime(float seconds)
    {
        TempoSegment result = _tempoSegments[0];

        for (int i = 1; i < _tempoSegments.Length; i++)
        {
            if (_tempoSegments[i].StartTime > seconds + 1e-12f)
                break;

            result = _tempoSegments[i];
        }

        return result;
    }
}