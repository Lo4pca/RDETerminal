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
    private readonly Func<int, float> _offsetAt;

    private TimeSequence(Func<int, float> offsetAt, int? count)
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
    /// Returns a view of this sequence with the first <paramref name="count" />
    /// elements discarded. Offsets are not rebased; the first remaining element
    /// keeps its original elapsed-time offset.
    /// </summary>
    public TimeSequence Skip(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (count == 0)
            return this;

        int? remaining = Count.HasValue
            ? Math.Max(0, Count.Value - count)
            : null;

        return new TimeSequence(
            index => _offsetAt(checked(index + count)),
            remaining);
    }

    /// <summary>
    /// Returns a lazy view rebased so that the item at <paramref name="index" />
    /// becomes the new sequence origin. Earlier items are discarded and the
    /// selected item's elapsed-time offset becomes 0.
    /// </summary>
    public TimeSequence Rebase(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (Count.HasValue && index >= Count.Value)
            throw new ArgumentOutOfRangeException(
                nameof(index),
                $"Time sequence contains only {Count.Value} offsets; index {index} was requested.");

        int? remaining = Count.HasValue
            ? Count.Value - index
            : null;

        var origin = new Lazy<float>(() => GetOffset(index));

        return new TimeSequence(
            itemIndex =>
            {
                float originOffset = origin.Value;
                if (itemIndex == 0)
                    return 0f;

                return GetOffset(checked(index + itemIndex)) - originOffset;
            },
            remaining);
    }

    /// <summary>
    /// Gets the elapsed-time offset, in seconds, for the item at
    /// <paramref name="index" />.
    /// </summary>
    public float GetOffset(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (Count.HasValue && index >= Count.Value)
        {
            throw new InvalidOperationException(
                $"Time sequence contains only {Count.Value} offsets; index {index} was requested.");
        }

        float offset = _offsetAt(index);
        if (float.IsNaN(offset) || float.IsInfinity(offset))
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
    public static TimeSequence FromOffsets(IEnumerable<float> offsets)
    {
        if (offsets == null) throw new ArgumentNullException(nameof(offsets));

        var values = offsets.ToArray();
        return new TimeSequence(index => values[index], values.Length);
    }

    /// <summary>
    /// Creates a finite sequence from elapsed-time offsets in seconds.
    /// </summary>
    public static TimeSequence FromOffsets(params float[] offsets)
    {
        if (offsets == null) throw new ArgumentNullException(nameof(offsets));
        return FromOffsets((IEnumerable<float>)offsets);
    }

    /// <summary>
    /// Creates an unbounded sequence from a function of the item index.
    /// The function returns elapsed seconds from the sequence origin.
    /// </summary>
    public static TimeSequence Generate(Func<int, float> offsetAt)
    {
        if (offsetAt == null) throw new ArgumentNullException(nameof(offsetAt));
        return new TimeSequence(offsetAt, null);
    }

    /// <summary>
    /// Creates an unbounded evenly-spaced time sequence:
    /// 0, spacing, 2*spacing, ...
    /// </summary>
    public static TimeSequence ConstantSpacing(float spacing)
    {
        if (spacing < 0d)
            throw new ArgumentOutOfRangeException(nameof(spacing), "spacing must be non-negative.");

        return Generate(index => index * spacing);
    }

    /// <summary>
    /// Creates elapsed-time offsets from intervals between consecutive items.
    /// Example intervals [0.5, 0.75, 1.0] become offsets [0, 0.5, 1.25, 2.25].
    /// </summary>
    public static TimeSequence FromIntervals(IEnumerable<float> intervals)
    {
        if (intervals == null) throw new ArgumentNullException(nameof(intervals));

        var offsets = new List<float> { 0f };
        float current = 0f;

        foreach (float interval in intervals)
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