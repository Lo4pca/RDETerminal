using System;
using System.Collections;
using System.Collections.Generic;

namespace RDETerminal.Domain.Core;

public static class SnapshotValueCloner
{
    public static object Clone(object value)
    {
        if (value == null)
        {
            return null;
        }

        Type type = value.GetType();

        if (type.IsValueType || value is string)
        {
            return value;
        }

        if (value is Array array)
        {
            var cloned = new List<object>(array.Length);
            foreach (object item in array)
            {
                cloned.Add(Clone(item));
            }
            return cloned;
        }

        if (value is IList list)
        {
            var cloned = new List<object>(list.Count);
            foreach (object item in list)
            {
                cloned.Add(Clone(item));
            }
            return cloned;
        }

        if (value is IDictionary dict)
        {
            var cloned = new Dictionary<object, object>();
            foreach (DictionaryEntry entry in dict)
            {
                cloned[Clone(entry.Key)] = Clone(entry.Value);
            }
            return cloned;
        }

        if (value is IEnumerable enumerable)
        {
            var cloned = new List<object>();
            foreach (object item in enumerable)
            {
                cloned.Add(Clone(item));
            }
            return cloned;
        }

        return value;
    }
}