using System;
using System.Collections.Generic;

namespace RDETerminal.UI;

/// <summary>
/// What kind of change an edit was. Only typing and deleting runs are merged
/// into a single undo step; every other change (a completion, an indent, a
/// paste, history recall, ...) is a step of its own.
/// </summary>
internal enum EditKind
{
    Other,
    Typing,
    Deleting
}

/// <summary>The input's text and selection at one moment.</summary>
internal readonly struct UndoState(string text, int anchor, int focus)
{
    public string Text { get; } = text ?? string.Empty;

    /// <summary>The fixed end of the selection (equals <see cref="Focus"/> when nothing is selected).</summary>
    public int Anchor { get; } = anchor;

    /// <summary>The end of the selection that holds the caret.</summary>
    public int Focus { get; } = focus;

    public bool SameAs(UndoState other) =>
        Anchor == other.Anchor && Focus == other.Focus
        && string.Equals(Text, other.Text, StringComparison.Ordinal);
}

/// <summary>
/// Undo/redo stacks for the terminal's input box (the legacy InputField has
/// none). Each step stores the state before and after the edit, so undoing
/// puts the caret back where it was before the edit, as VS Code does.
/// Plain C#, free of Unity types, so the grouping rules are easy to test.
/// </summary>
/// <remarks>
/// Consecutive single-character typing (or deleting) is merged into one step
/// while it stays contiguous (same text and caret as where the last step left
/// off, so moving the caret starts a new step), happens within
/// <see cref="CoalesceSeconds"/> of the previous one, and, when typing, does not
/// start a new word right after whitespace. Typing "foo bar" therefore undoes
/// as "bar", then "foo ".
/// </remarks>
internal sealed class TerminalUndoHistory
{
    /// <summary>A pause longer than this ends a typing or deleting run.</summary>
    internal const float CoalesceSeconds = 1f;

    private const int MaxEntries = 200;

    private sealed class Entry
    {
        public UndoState Before;
        public UndoState After;
        public EditKind Kind;
        public char LastTyped;
        public float LastTime;
    }

    private readonly List<Entry> _undo = [];
    private readonly List<Entry> _redo = [];

    // After an undo or redo the next edit starts a fresh step instead of merging into an older one.
    private bool _noMergeNext;

    internal bool CanUndo => _undo.Count > 0;

    internal bool CanRedo => _redo.Count > 0;

    /// <summary>Forgets everything (e.g. after a cell was executed and the input cleared).</summary>
    internal void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _noMergeNext = false;
    }

    /// <summary>
    /// Records an edit that changed the text. <paramref name="typed"/> is the
    /// character typed when <paramref name="kind"/> is <see cref="EditKind.Typing"/>.
    /// </summary>
    internal void Record(UndoState before, UndoState after, EditKind kind, char typed, float now)
    {
        _redo.Clear();

        if (_undo.Count > 0 && CanMerge(_undo[_undo.Count - 1], before, kind, typed, now))
        {
            Entry top = _undo[^1];
            top.After = after;
            top.LastTyped = typed;
            top.LastTime = now;
            _noMergeNext = false;
            return;
        }

        _undo.Add(new Entry
        {
            Before = before,
            After = after,
            Kind = kind,
            LastTyped = typed,
            LastTime = now
        });

        if (_undo.Count > MaxEntries)
        {
            _undo.RemoveAt(0);
        }

        _noMergeNext = false;
    }

    /// <summary>
    /// A typed character was absorbed (typing ")" over an auto-inserted ")"), so the
    /// text did not change but the caret moved. Keeps the typing run contiguous.
    /// </summary>
    internal void NoteTypedOver(UndoState after, char typed, float now)
    {
        if (_undo.Count == 0)
        {
            return;
        }

        Entry top = _undo[^1];
        if (top.Kind != EditKind.Typing || _noMergeNext
            || !string.Equals(top.After.Text, after.Text, StringComparison.Ordinal))
        {
            return;
        }

        top.After = after;
        top.LastTyped = typed;
        top.LastTime = now;
    }

    /// <summary>Steps back; <paramref name="state"/> is what the input should show.</summary>
    internal bool TryUndo(out UndoState state)
    {
        state = default;
        if (_undo.Count == 0)
        {
            return false;
        }

        Entry entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);
        _noMergeNext = true;

        state = entry.Before;
        return true;
    }

    /// <summary>Steps forward again; <paramref name="state"/> is what the input should show.</summary>
    internal bool TryRedo(out UndoState state)
    {
        state = default;
        if (_redo.Count == 0)
        {
            return false;
        }

        Entry entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);
        _noMergeNext = true;

        state = entry.After;
        return true;
    }

    private bool CanMerge(Entry top, UndoState before, EditKind kind, char typed, float now)
    {
        if (_noMergeNext || kind == EditKind.Other || top.Kind != kind)
        {
            return false;
        }

        if (now - top.LastTime > CoalesceSeconds)
        {
            return false;
        }

        // Must continue exactly where the last step left off: same text, same caret.
        if (!before.SameAs(top.After))
        {
            return false;
        }

        // A new word after whitespace starts a new step.
        if (kind == EditKind.Typing && char.IsWhiteSpace(top.LastTyped) && !char.IsWhiteSpace(typed))
        {
            return false;
        }

        return true;
    }
}