using System;
using System.Reflection;

namespace RDETerminal.Adapters;

/// <summary>
/// Low-level reflection helpers used by adapter infrastructure.
/// </summary>
public static class ReflectionUtil
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool TryGetMemberValue(object target, string memberName, out object value)
    {
        value = null;

        if (target == null || string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        Type type = target.GetType();

        PropertyInfo prop = type.GetProperty(memberName, InstanceFlags);
        if (prop != null && prop.GetIndexParameters().Length == 0)
        {
            value = prop.GetValue(target, null);
            return true;
        }

        FieldInfo field = type.GetField(memberName, InstanceFlags);
        if (field != null)
        {
            value = field.GetValue(target);
            return true;
        }

        return false;
    }
}