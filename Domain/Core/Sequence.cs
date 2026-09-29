using System;
using System.Collections.Generic;
using System.Linq;

namespace RDETerminal.Domain.Core;

/// <summary>
/// Describes the offset, in beats, for each item in an ordered sequence.
/// Offset 0 means "at the sequence origin".
/// </summary>
public sealed class Sequence
{
    private readonly Func<int, float> _offsetAt;

    private Sequence(Func<int, float> offsetAt, int? count)
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
    /// Gets the offset for the item at <paramref name="index" />.
    /// </summary>
    public float GetOffset(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (Count.HasValue && index >= Count.Value)
            throw new InvalidOperationException(
                $"Sequence contains only {Count.Value} offsets; index {index} was requested.");

        return _offsetAt(index);
    }

    /// <summary>
    /// Creates a finite sequence from absolute offsets.
    /// Example: [0, 2, 3, 5].
    /// </summary>
    public static Sequence FromOffsets(IEnumerable<float> offsets)
    {
        if (offsets == null) throw new ArgumentNullException(nameof(offsets));

        var values = offsets.ToArray();
        return new Sequence(index => values[index], values.Length);
    }

    /// <summary>
    /// Creates a finite sequence from absolute offsets.
    /// </summary>
    public static Sequence FromOffsets(params float[] offsets)
    {
        if (offsets == null) throw new ArgumentNullException(nameof(offsets));
        return FromOffsets((IEnumerable<float>)offsets);
    }

    /// <summary>
    /// Creates an unbounded sequence from a function of the item index.
    /// </summary>
    public static Sequence Generate(Func<int, float> offsetAt)
    {
        if (offsetAt == null) throw new ArgumentNullException(nameof(offsetAt));
        return new Sequence(offsetAt, null);
    }

    /// <summary>
    /// Creates an unbounded evenly-spaced sequence: 0, spacing, 2*spacing, ...
    /// </summary>
    public static Sequence ConstantSpacing(float spacing)
    {
        return Generate(index => index * spacing);
    }

    /// <summary>
    /// Creates absolute offsets from intervals between consecutive items.
    /// Example intervals [2, 1, 2] become offsets [0, 2, 3, 5].
    /// </summary>
    public static Sequence FromIntervals(IEnumerable<float> intervals)
    {
        if (intervals == null) throw new ArgumentNullException(nameof(intervals));

        var offsets = new List<float> { 0f };
        float current = 0f;

        foreach (float interval in intervals)
        {
            current += interval;
            offsets.Add(current);
        }

        return FromOffsets(offsets);
    }
}