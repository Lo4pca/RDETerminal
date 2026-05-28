using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using RDETerminal.Domain.Abstractions;
using RDETerminal.Domain.Core;

namespace RDETerminal.Adapters;

public class ReflectionGameEventBridge : IGameEventBridge
{
    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public;

    private static readonly ConcurrentDictionary<Type, MemberMap> Cache = new();

    public virtual bool CanHandle(Type eventType, string snapshotType)
    {
        return true;
    }

    public virtual LevelEventSnapshot Capture(object gameEvent)
    {
        if (gameEvent == null)
        {
            return null;
        }

        Type runtimeType = gameEvent.GetType();
        var snapshot = new LevelEventSnapshot(CleanTypeName(runtimeType.Name));

        MemberMap map = GetMap(runtimeType);

        foreach (MemberAccessor accessor in map.CaptureOrder)
        {
            if (ShouldSkipBuiltIn(accessor.Name))
            {
                continue;
            }

            if (!accessor.CanRead)
            {
                continue;
            }

            object value = accessor.Get(gameEvent);
            snapshot.LoadCaptured(accessor.Name, value);
        }

        return snapshot;
    }

    public virtual EventApplyResult Apply(LevelEventSnapshot snapshot, object gameEvent)
    {
        if (snapshot == null || gameEvent == null || !snapshot.HasChanges)
        {
            return new EventApplyResult();
        }

        var result = new EventApplyResult
        {
            EventType = snapshot.Type
        };
        MemberMap map = GetMap(gameEvent.GetType());
        var keysToProcess = snapshot.DirtyKeys.ToList();

        foreach (string key in keysToProcess)
        {
            try
            {
                if (ShouldSkipBuiltIn(key))
                {
                    result.SkippedCount++;
                    continue;
                }

                if (!map.ByName.TryGetValue(key, out MemberAccessor accessor))
                {
                    result.SkippedCount++;
                    continue;
                }

                if (!accessor.CanWrite)
                {
                    result.SkippedCount++;
                    continue;
                }

                if (!snapshot.TryGet(key, out object rawValue))
                {
                    result.SkippedCount++;
                    continue;
                }

                if (!TryConvertValue(rawValue, accessor.MemberType, out object converted))
                {
                    result.SkippedCount++;
                    continue;
                }

                accessor.Set(gameEvent, converted);
                snapshot.MarkClean(key);
                result.WrittenCount++;
            }
            catch (Exception ex)
            {
                result.AddFailure(key, ex.Message);
            }
        }
        return result;
    }

    protected static string CleanTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return typeName;
        }

        return typeName.StartsWith("LevelEvent_", StringComparison.OrdinalIgnoreCase)
            ? typeName.Substring("LevelEvent_".Length)
            : typeName;
    }

    private static MemberMap GetMap(Type runtimeType)
    {
        return Cache.GetOrAdd(runtimeType, BuildMap);
    }

    private static MemberMap BuildMap(Type runtimeType)
    {
        var map = new MemberMap();

        foreach (PropertyInfo prop in runtimeType.GetProperties(InstanceFlags))
        {
            if (prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            MethodInfo getter = prop.GetGetMethod(true);
            MethodInfo setter = prop.GetSetMethod(true);

            if (getter == null && setter == null)
            {
                continue;
            }

            if (ShouldSkipBuiltIn(prop.Name))
            {
                continue;
            }

            var accessor = MemberAccessor.CreateProperty(prop, getter != null, setter != null);
            map.Add(accessor);
        }

        foreach (FieldInfo field in runtimeType.GetFields(InstanceFlags))
        {
            if (field.IsInitOnly)
            {
                continue;
            }

            if (ShouldSkipBuiltIn(field.Name))
            {
                continue;
            }

            var accessor = MemberAccessor.CreateField(field);
            map.Add(accessor);
        }

        return map;
    }

    private static bool ShouldSkipBuiltIn(string name)
    {
        return string.Equals(name, "type", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryConvertValue(object rawValue, Type targetType, out object converted)
    {
        converted = null;

        if (rawValue == null)
        {
            if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
            {
                return true;
            }

            return false;
        }

        Type underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType.IsInstanceOfType(rawValue))
        {
            converted = rawValue;
            return true;
        }

        if (underlyingType.IsEnum)
        {
            if (rawValue is string s)
            {
                try
                {
                    converted = Enum.Parse(underlyingType, s, true);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            try
            {
                converted = Enum.ToObject(
                    underlyingType,
                    Convert.ChangeType(rawValue, Enum.GetUnderlyingType(underlyingType), CultureInfo.InvariantCulture));
                return true;
            }
            catch
            {
                return false;
            }
        }

        if (underlyingType == typeof(Vector2))
        {
            return TryConvertToVector2(rawValue, out converted);
        }

        if (TryConvertToTypedList(rawValue, underlyingType, out converted))
        {
            return true;
        }

        if (underlyingType.IsArray)
        {
            return TryConvertToArray(rawValue, underlyingType.GetElementType(), out converted);
        }

        try
        {
            converted = Convert.ChangeType(rawValue, underlyingType, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryConvertToVector2(object rawValue, out object converted)
    {
        converted = null;

        if (rawValue is Vector2 v2)
        {
            converted = v2;
            return true;
        }

        if (rawValue is IList list && list.Count >= 2)
        {
            try
            {
                float x = Convert.ToSingle(list[0], CultureInfo.InvariantCulture);
                float y = Convert.ToSingle(list[1], CultureInfo.InvariantCulture);
                converted = new Vector2(x, y);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryConvertToTypedList(object rawValue, Type targetType, out object converted)
    {
        converted = null;

        if (!targetType.IsGenericType)
        {
            return false;
        }

        Type genericDef = targetType.GetGenericTypeDefinition();
        if (genericDef != typeof(List<>))
        {
            return false;
        }

        if (rawValue is not IEnumerable enumerable)
        {
            return false;
        }

        Type elementType = targetType.GetGenericArguments()[0];
        Type listType = typeof(List<>).MakeGenericType(elementType);
        IList list = (IList)Activator.CreateInstance(listType);

        foreach (object item in enumerable)
        {
            if (!TryConvertValue(item, elementType, out object convertedItem))
            {
                return false;
            }

            list.Add(convertedItem);
        }

        converted = list;
        return true;
    }

    /// <summary>
    /// Converts <paramref name="rawValue"/> to a typed array of <paramref name="elementType"/>.
    /// </summary>
    /// <remarks>
    /// <b>Array contract:</b> <see cref="SnapshotValueCloner.Clone"/> stores arrays as
    /// <c>List&lt;object&gt;</c> to avoid generic type complexity in the snapshot dictionary.
    /// The <c>IEnumerable</c> branch of this method is the matching receiver — it
    /// reconstructs the correctly-typed array from that list.
    /// The two methods form a matched pair; changes to either must preserve the round-trip.
    /// </remarks>
    private static bool TryConvertToArray(object rawValue, Type elementType, out object converted)
    {
        converted = null;

        if (elementType == null)
        {
            return false;
        }

        if (rawValue is Array sourceArray)
        {
            Array targetArray = Array.CreateInstance(elementType, sourceArray.Length);
            for (int i = 0; i < sourceArray.Length; i++)
            {
                if (!TryConvertValue(sourceArray.GetValue(i), elementType, out object convertedItem))
                {
                    return false;
                }

                targetArray.SetValue(convertedItem, i);
            }

            converted = targetArray;
            return true;
        }

        if (rawValue is IEnumerable enumerable)
        {
            var items = new List<object>();
            foreach (object item in enumerable)
            {
                items.Add(item);
            }

            Array targetArray = Array.CreateInstance(elementType, items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                if (!TryConvertValue(items[i], elementType, out object convertedItem))
                {
                    return false;
                }

                targetArray.SetValue(convertedItem, i);
            }

            converted = targetArray;
            return true;
        }

        return false;
    }

    private sealed class MemberMap
    {
        public readonly List<MemberAccessor> CaptureOrder = [];
        public readonly Dictionary<string, MemberAccessor> ByName =
            new(StringComparer.OrdinalIgnoreCase);

        public void Add(MemberAccessor accessor)
        {
            if (accessor == null)
            {
                return;
            }

            if (ByName.ContainsKey(accessor.Name))
            {
                return;
            }

            ByName[accessor.Name] = accessor;
            CaptureOrder.Add(accessor);
        }
    }

    private sealed class MemberAccessor
    {
        public string Name { get; }
        public Type MemberType { get; }
        public bool CanRead { get; }
        public bool CanWrite { get; }

        private readonly Func<object, object> _getter;
        private readonly Action<object, object> _setter;

        private MemberAccessor(
            string name,
            Type memberType,
            bool canRead,
            bool canWrite,
            Func<object, object> getter,
            Action<object, object> setter)
        {
            Name = name;
            MemberType = memberType;
            CanRead = canRead;
            CanWrite = canWrite;
            _getter = getter;
            _setter = setter;
        }

        public object Get(object target)
        {
            return _getter == null ? null : _getter(target);
        }

        public void Set(object target, object value)
        {
            _setter?.Invoke(target, value);
        }

        public static MemberAccessor CreateProperty(PropertyInfo prop, bool canRead, bool canWrite)
        {
            var objParam = Expression.Parameter(typeof(object), "obj");
            var valParam = Expression.Parameter(typeof(object), "val");
            var castObj = Expression.Convert(objParam, prop.DeclaringType);

            Func<object, object> getter = null;
            Action<object, object> setter = null;

            if (canRead)
            {
                Expression propExpr = Expression.Property(castObj, prop);
                Expression boxExpr = Expression.Convert(propExpr, typeof(object));
                getter = Expression.Lambda<Func<object, object>>(boxExpr, objParam).Compile();
            }

            if (canWrite)
            {
                MethodInfo setMethod = prop.GetSetMethod(true);
                if (setMethod != null)
                {
                    Expression convertedVal = Expression.Convert(valParam, prop.PropertyType);
                    Expression call = Expression.Call(castObj, setMethod, convertedVal);
                    setter = Expression.Lambda<Action<object, object>>(call, objParam, valParam).Compile();
                }
            }

            return new MemberAccessor(prop.Name, prop.PropertyType, canRead, canWrite, getter, setter);
        }

        public static MemberAccessor CreateField(FieldInfo field)
        {
            var objParam = Expression.Parameter(typeof(object), "obj");
            var valParam = Expression.Parameter(typeof(object), "val");
            var castObj = Expression.Convert(objParam, field.DeclaringType);

            Expression fieldExpr = Expression.Field(castObj, field);
            Expression boxExpr = Expression.Convert(fieldExpr, typeof(object));
            Func<object, object> getter = Expression.Lambda<Func<object, object>>(boxExpr, objParam).Compile();

            Action<object, object> setter = null;
            if (!field.IsInitOnly && !field.IsLiteral)
            {
                Expression convertedVal = Expression.Convert(valParam, field.FieldType);
                Expression assign = Expression.Assign(fieldExpr, convertedVal);
                setter = Expression.Lambda<Action<object, object>>(assign, objParam, valParam).Compile();
            }

            return new MemberAccessor(field.Name, field.FieldType, true, setter != null, getter, setter);
        }
    }
}