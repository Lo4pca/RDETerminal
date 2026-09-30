using System;
using System.Collections.Generic;
using System.Linq;

namespace RDETerminal.Domain.Core;

/// <summary>
/// Describes elapsed time offsets, in seconds, for an ordered sequence.
/// Offset 0 means "at the sequence origin".
///
/// This is deliberately independent of tempo, bars, beats, Unity, and the
/// editor. It is the natural representation for sequences captured from
/// physical taps.
/// </summary>
public sealed class TimeSequence
{
    private readonly Func<int, double> _offsetAt;

    private TimeSequence(Func<int, double> offsetAt, int? count)
    {
        _offsetAt = offsetAt ?? throw new ArgumentNullException(nameof(offsetAt));
        Count = count;
    }

    /// <summary>
    /// Number of offsets available when the sequence is finite.
    /// Null means the sequence is unbounded.
    /// </summary>
    public int? Count { get; }

    /// <summary>
    /// Gets the elapsed-time offset, in seconds, for the item at
    /// <paramref name="index" />.
    /// </summary>
    public double GetOffset(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (Count.HasValue && index >= Count.Value)
        {
            throw new InvalidOperationException(
                $"Time sequence contains only {Count.Value} offsets; index {index} was requested.");
        }

        double offset = _offsetAt(index);
        if (double.IsNaN(offset) || double.IsInfinity(offset))
        {
            throw new InvalidOperationException(
                $"Time sequence produced an invalid offset at index {index}: {offset}.");
        }

        return offset;
    }

    /// <summary>
    /// Creates a finite sequence from elapsed-time offsets in seconds.
    /// Example: [0, 0.5, 1.25, 2.0].
    /// </summary>
    public static TimeSequence FromOffsets(IEnumerable<double> offsets)
    {
        if (offsets == null) throw new ArgumentNullException(nameof(offsets));

        var values = offsets.ToArray();
        return new TimeSequence(index => values[index], values.Length);
    }

    /// <summary>
    /// Creates a finite sequence from elapsed-time offsets in seconds.
    /// </summary>
    public static TimeSequence FromOffsets(params double[] offsets)
    {
        if (offsets == null) throw new ArgumentNullException(nameof(offsets));
        return FromOffsets((IEnumerable<double>)offsets);
    }

    /// <summary>
    /// Creates an unbounded sequence from a function of the item index.
    /// The function returns elapsed seconds from the sequence origin.
    /// </summary>
    public static TimeSequence Generate(Func<int, double> offsetAt)
    {
        if (offsetAt == null) throw new ArgumentNullException(nameof(offsetAt));
        return new TimeSequence(offsetAt, null);
    }

    /// <summary>
    /// Creates an unbounded evenly-spaced time sequence:
    /// 0, spacing, 2*spacing, ...
    /// </summary>
    public static TimeSequence ConstantSpacing(double spacing)
    {
        if (spacing < 0d)
            throw new ArgumentOutOfRangeException(nameof(spacing), "spacing must be non-negative.");

        return Generate(index => index * spacing);
    }

    /// <summary>
    /// Creates elapsed-time offsets from intervals between consecutive items.
    /// Example intervals [0.5, 0.75, 1.0] become offsets [0, 0.5, 1.25, 2.25].
    /// </summary>
    public static TimeSequence FromIntervals(IEnumerable<double> intervals)
    {
        if (intervals == null) throw new ArgumentNullException(nameof(intervals));

        var offsets = new List<double> { 0d };
        double current = 0d;

        foreach (double interval in intervals)
        {
            if (interval < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(intervals),
                    "Time intervals must be non-negative.");
            }

            current += interval;
            offsets.Add(current);
        }

        return FromOffsets(offsets);
    }

    /// <summary>
    /// Converts this time sequence to a beat-domain <see cref="Sequence"/>
    /// using the level's timing map and the requested sequence origin.
    /// </summary>
    public Sequence ToSequence(LevelDocument level, int startBar, float startBeat)
    {
        if (level == null) throw new ArgumentNullException(nameof(level));

        return LevelTimingMap.FromLevel(level)
            .ToBeatSequence(this, startBar, startBeat);
    }
}
