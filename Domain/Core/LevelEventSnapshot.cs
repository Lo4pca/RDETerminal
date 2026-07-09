using System;
using System.Collections.Generic;

namespace RDETerminal.Domain.Core;

public enum SnapshotAction
{
    Update,
    Create,
    Delete
}

public sealed class LevelEventSnapshot(string type = null)
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _originalValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _dirtyOrder = [];
    private readonly HashSet<string> _dirtyKeys = new(StringComparer.OrdinalIgnoreCase);

    public string Type { get; set; } = type;
    public SnapshotAction Action { get; private set; } = SnapshotAction.Update;
    public string TargetTabName { get; private set; }
    public bool HasChanges => _dirtyOrder.Count > 0;
    public bool IsCreate => Action == SnapshotAction.Create;
    public bool IsDelete => Action == SnapshotAction.Delete;

    public IReadOnlyList<string> DirtyKeys => _dirtyOrder;

    public IReadOnlyDictionary<string, object> Fields => _values;

    public void MarkForUpdate()
    {
        Action = SnapshotAction.Update;
        TargetTabName = null;
    }

    public void MarkForCreate(string targetTabName)
    {
        Action = SnapshotAction.Create;
        TargetTabName = targetTabName;
        //Create类型snaphot调用链：GameLevelBridge.ApplyLevel->EditorAdapter.CreateEventFromSnapshot->ReflectionGameEventBridge.Apply
        //对于新创建的事件来说，不存在_originalValues；若_originalValues不为空，说明该snapshot取自于某个事件，拷贝原始值以保证初始行为
        //因此需要先将snapshot标记为Create再用Set设置属性，否则原始值将覆盖之前设置的内容
        foreach (var pair in _originalValues)
        {
            Set(pair.Key,pair.Value);
        }
        MarkAllDirty();
    }

    public void MarkForDelete()
    {
        Action = SnapshotAction.Delete;
        TargetTabName = null;
    }

    public void LoadCaptured(string key, object value)
    {
        object cloned = SnapshotValueCloner.Clone(value);
        _values[key] = cloned;
        _originalValues[key] = SnapshotValueCloner.Clone(cloned); //保证两个字典引用不同的对象实例
    }

    public void Set(string key, object value)
    {
        object cloned = SnapshotValueCloner.Clone(value);
        _values[key] = cloned;

        if (!_originalValues.TryGetValue(key, out object original))
        {
            MarkDirty(key);
            return;
        }

        if (!SnapshotValueComparer.AreEqual(original, cloned))
        {
            MarkDirty(key);
        }
        else
        {
            MarkClean(key);
        }
    }

    public void MarkDirty(string key)
    {
        if (_dirtyKeys.Add(key))
        {
            _dirtyOrder.Add(key);
        }
    }

    public void MarkClean(string key)
    {
        if (_dirtyKeys.Remove(key))
        {
            _dirtyOrder.Remove(key);
        }
    }

    public void MarkAllDirty()
    {
        foreach (var pair in _values)
        {
            MarkDirty(pair.Key);
        }
    }

    public void ClearDirty()
    {
        _dirtyOrder.Clear();
        _dirtyKeys.Clear();
    }

    public bool IsDirty(string key)
    {
        return _dirtyKeys.Contains(key);
    }

    public IEnumerable<KeyValuePair<string, object>> EnumerateDirtyFields()
    {
        foreach (string key in _dirtyOrder)
        {
            if (_values.TryGetValue(key, out object value))
            {
                yield return new KeyValuePair<string, object>(key, value);
            }
        }
    }

    public bool TryGet(string key, out object value)
    {
        return _values.TryGetValue(key, out value);
    }

    public object Get(string key, object fallback = null)
    {
        return _values.TryGetValue(key, out object value) ? value : fallback;
    }

    public string GetString(string key, string fallback = null)
    {
        if (!_values.TryGetValue(key, out object value) || value == null)
        {
            return fallback;
        }

        return value.ToString();
    }

    public int GetInt(string key, int fallback = 0)
    {
        if (!_values.TryGetValue(key, out object value) || value == null)
        {
            return fallback;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return fallback;
        }
    }

    public float GetFloat(string key, float fallback = 0f)
    {
        if (!_values.TryGetValue(key, out object value) || value == null)
        {
            return fallback;
        }

        try
        {
            return Convert.ToSingle(value);
        }
        catch
        {
            return fallback;
        }
    }

    public LevelEventSnapshot Clone()
    {
        var clone = new LevelEventSnapshot(Type)
        {
            Action = Action,
            TargetTabName = TargetTabName
        };

        foreach (var pair in _values)
        {
            clone._values[pair.Key] = SnapshotValueCloner.Clone(pair.Value);
        }

        foreach (var pair in _originalValues)
        {
            clone._originalValues[pair.Key] = SnapshotValueCloner.Clone(pair.Value);
        }

        foreach (string key in _dirtyOrder)
        {
            clone._dirtyOrder.Add(key);
            clone._dirtyKeys.Add(key);
        }

        return clone;
    }
}
