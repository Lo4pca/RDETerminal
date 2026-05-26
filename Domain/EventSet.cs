using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RDETerminal.Domain.Core;

namespace RDETerminal.Domain;

public sealed class EventSet(IEnumerable<LevelEventSnapshot> items) : IEnumerable<LevelEventSnapshot>
{
    private readonly List<LevelEventSnapshot> _items = items != null ? [.. items] : [];

    public static readonly EventSet Empty = new([]);

    public int Count
    {
        get { return _items.Count; }
    }

    public bool IsEmpty
    {
        get { return _items.Count == 0; }
    }

    public bool AnyDirty
    {
        get { return _items.Any(x => x.HasChanges); }
    }

    public LevelEventSnapshot this[int index]
    {
        get { return _items[index]; }
    }

    public EventSet Where(Func<LevelEventSnapshot, bool> predicate)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        return new EventSet(_items.Where(predicate));
    }

    public EventSet ForEach(Action<LevelEventSnapshot> action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        foreach (LevelEventSnapshot item in _items)
        {
            action(item);
        }

        return this;
    }

    public EventSet Set(string key, object value)
    {
        foreach (LevelEventSnapshot item in _items)
        {
            item.Set(key, value);
        }

        return this;
    }

    public EventSet MarkDirty(string key)
    {
        foreach (LevelEventSnapshot item in _items)
        {
            item.MarkDirty(key);
        }

        return this;
    }

    public EventSet MarkForCreate(string targetTabName)
    {
        foreach (LevelEventSnapshot item in _items)
        {
            item.MarkForCreate(targetTabName);
        }

        return this;
    }

    public EventSet MarkForUpdate()
    {
        foreach (LevelEventSnapshot item in _items)
        {
            item.MarkForUpdate();
        }

        return this;
    }

    public EventSet MarkForDelete()
    {
        foreach (LevelEventSnapshot item in _items)
        {
            item.MarkForDelete();
        }

        return this;
    }

    public IEnumerator<LevelEventSnapshot> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public string Dump()
    {
        if (_items.Count == 0)
        {
            return "(empty)";
        }

        return string.Join(Environment.NewLine, [.. _items.Select(Describe)]);
    }

    private static string Describe(LevelEventSnapshot item)
    {
        if (item == null)
        {
            return "[null]";
        }

        string type = string.IsNullOrWhiteSpace(item.Type) ? "unknown" : item.Type;
        string id = item.GetString("id");
        string bar = item.GetString("bar");
        string beat = item.GetString("beat");
        string text = item.GetString("text");
        string action = item.Action.ToString();

        List<string> parts = [action, type];

        if (!string.IsNullOrWhiteSpace(id))
        {
            parts.Add("id=" + id);
        }

        if (!string.IsNullOrWhiteSpace(bar))
        {
            parts.Add("bar=" + bar);
        }

        if (!string.IsNullOrWhiteSpace(beat))
        {
            parts.Add("beat=" + beat);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            parts.Add("text=" + text);
        }

        if (!string.IsNullOrWhiteSpace(item.TargetTabName))
        {
            parts.Add("tab=" + item.TargetTabName);
        }

        return string.Join(" ", parts);
    }
}
