using System;
using System.Collections.Generic;

namespace RDETerminal.Notebook;

public sealed class CommandHistory
{
    private readonly List<string> _items = [];
    private int _cursor = -1;

    public int Count => _items.Count;

    /// <summary>
    /// True while the user has stepped back into history and has not yet
    /// returned to the "new input" position past the newest entry.
    /// </summary>
    public bool IsBrowsing => _cursor >= 0 && _cursor < _items.Count;

    public void Add(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (_items.Count > 0 && string.Equals(_items[_items.Count - 1], command, StringComparison.Ordinal))
        {
            _cursor = _items.Count;
            return;
        }

        _items.Add(command);
        _cursor = _items.Count;
    }

    public string MovePrevious()
    {
        if (_items.Count == 0)
        {
            return null;
        }

        if (_cursor > 0)
        {
            _cursor--;
        }
        else
        {
            _cursor = 0;
        }

        return _items[_cursor];
    }

    public string MoveNext()
    {
        if (_items.Count == 0)
        {
            return null;
        }

        if (_cursor < _items.Count - 1)
        {
            _cursor++;
            return _items[_cursor];
        }

        _cursor = _items.Count;
        return string.Empty;
    }

    public void ResetCursor()
    {
        _cursor = _items.Count;
    }
}