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

            var keysA = dictA.Keys.Cast<object>().ToList();
            foreach (object key in keysA)
            {
                bool found = false;
                foreach (object keyB in dictB.Keys)
                {
                    if (AreEqual(key, keyB))
                    {
                        if (!AreEqual(dictA[key], dictB[keyB]))
                        {
                            return false;
                        }

                        found = true;
                        break;
                    }
                }

                if (!found)
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