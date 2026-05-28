using System;
using System.Collections;
using System.Collections.Generic;

namespace RDETerminal.Domain.Core;

/// <summary>
/// Produces deep copies of values stored in a <see cref="LevelEventSnapshot"/>.
/// </summary>
/// <remarks>
/// <para><b>Array contract:</b> Arrays are intentionally cloned as
/// <see cref="List{T}">List&lt;object&gt;</see>, not as typed arrays.
/// This avoids the complexity of preserving generic array types across the
/// untyped snapshot dictionary.</para>
/// <para>When these values are written back to a game object via
/// <c>ReflectionGameEventBridge.Apply</c>, the <c>TryConvertToArray</c>
/// method reconstructs the correct typed array from the list.
/// The two methods form a matched pair — changes to either side must
/// preserve this round-trip.</para>
/// </remarks>
public static class SnapshotValueCloner
{
    /// <summary>
    /// Deep-clones <paramref name="value"/> for safe storage in a snapshot.
    /// Value types and strings are returned as-is (they are already immutable).
    /// Arrays are stored as <see cref="List{T}">List&lt;object&gt;</see> —
    /// see class remarks for the round-trip contract.
    /// </summary>
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