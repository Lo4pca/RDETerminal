using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace RDETerminal.Domain.Core;

public static class SnapshotValueComparer
{
    public static bool AreEqual(object a, object b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        if (a is string || b is string)
        {
            return Equals(a, b);
        }

        if (a is IDictionary dictA && b is IDictionary dictB)
        {
            if (dictA.Count != dictB.Count)
            {
                return false;
            }

            // Build a lookup over dictB so each key search is O(1) rather
            // than O(n), reducing the overall comparison from O(n²) to O(n).
            var comparer = new ObjectComparer();
            var lookupB = new Dictionary<object, object>(dictB.Count, comparer);
            foreach (DictionaryEntry entry in dictB)
            {
                lookupB[entry.Key] = entry.Value;
            }

            foreach (DictionaryEntry entry in dictA)
            {
                if (!lookupB.TryGetValue(entry.Key, out object valueB))
                {
                    return false;
                }

                if (!AreEqual(entry.Value, valueB))
                {
                    return false;
                }
            }

            return true;
        }

        if (a is IList listA && b is IList listB)
        {
            if (listA.Count != listB.Count)
            {
                return false;
            }

            for (int i = 0; i < listA.Count; i++)
            {
                if (!AreEqual(listA[i], listB[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (a is IEnumerable enumA && b is IEnumerable enumB)
        {
            return enumA.Cast<object>().SequenceEqual(enumB.Cast<object>(), new ObjectComparer());
        }

        return Equals(a, b);
    }

    private sealed class ObjectComparer : IEqualityComparer<object>
    {
        public new bool Equals(object x, object y)
        {
            return AreEqual(x, y);
        }

        public int GetHashCode(object obj)
        {
            return obj?.GetHashCode() ?? 0;
        }
    }
}