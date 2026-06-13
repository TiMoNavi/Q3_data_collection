using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace SObasic
{
    public static class SOValueAccessUtility
    {
        private const BindingFlags MemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static bool TryRead(object target, string memberPath, out object value, out string error)
        {
            value = null;
            error = string.Empty;

            if (!TryResolveMemberOwner(target, memberPath, out object owner, out string memberName, out error))
            {
                return false;
            }

            Type ownerType = owner.GetType();
            if (TryGetField(ownerType, memberName, out FieldInfo field))
            {
                value = field.GetValue(owner);
                return true;
            }

            if (TryGetProperty(ownerType, memberName, out PropertyInfo property))
            {
                if (!property.CanRead)
                {
                    error = "Property '" + memberName + "' on " + ownerType.FullName + " is not readable.";
                    return false;
                }

                value = property.GetValue(owner);
                return true;
            }

            error = "Member '" + memberName + "' was not found on " + ownerType.FullName + ".";
            return false;
        }

        public static bool TryWrite(object target, string memberPath, object rawValue, out string error)
        {
            error = string.Empty;

            if (!TryResolveMemberOwner(target, memberPath, out object owner, out string memberName, out error))
            {
                return false;
            }

            Type ownerType = owner.GetType();
            if (TryGetField(ownerType, memberName, out FieldInfo field))
            {
                if (!TryConvertValue(field.FieldType, rawValue, out object converted, out error))
                {
                    return false;
                }

                field.SetValue(owner, converted);
                return true;
            }

            if (TryGetProperty(ownerType, memberName, out PropertyInfo property))
            {
                if (!property.CanWrite)
                {
                    error = "Property '" + memberName + "' on " + ownerType.FullName + " is not writable.";
                    return false;
                }

                if (!TryConvertValue(property.PropertyType, rawValue, out object converted, out error))
                {
                    return false;
                }

                property.SetValue(owner, converted);
                return true;
            }

            error = "Member '" + memberName + "' was not found on " + ownerType.FullName + ".";
            return false;
        }

        public static bool TryGetMemberType(object target, string memberPath, out Type memberType, out string error)
        {
            memberType = null;
            error = string.Empty;

            if (!TryResolveMemberOwner(target, memberPath, out object owner, out string memberName, out error))
            {
                return false;
            }

            Type ownerType = owner.GetType();
            if (TryGetField(ownerType, memberName, out FieldInfo field))
            {
                memberType = field.FieldType;
                return true;
            }

            if (TryGetProperty(ownerType, memberName, out PropertyInfo property))
            {
                memberType = property.PropertyType;
                return true;
            }

            error = "Member '" + memberName + "' was not found on " + ownerType.FullName + ".";
            return false;
        }

        public static bool TryConvertFromSerialized(
            Type targetType,
            string stringValue,
            bool boolValue,
            int intValue,
            long longValue,
            float floatValue,
            Vector2 vector2Value,
            Vector3 vector3Value,
            out object value,
            out string error)
        {
            value = null;
            error = string.Empty;

            if (targetType == typeof(string))
            {
                value = stringValue ?? string.Empty;
                return true;
            }

            if (targetType == typeof(bool))
            {
                value = boolValue;
                return true;
            }

            if (targetType == typeof(int))
            {
                value = intValue;
                return true;
            }

            if (targetType == typeof(long))
            {
                value = longValue;
                return true;
            }

            if (targetType == typeof(float))
            {
                value = floatValue;
                return true;
            }

            if (targetType == typeof(double))
            {
                value = (double)floatValue;
                return true;
            }

            if (targetType == typeof(Vector2))
            {
                value = vector2Value;
                return true;
            }

            if (targetType == typeof(Vector3))
            {
                value = vector3Value;
                return true;
            }

            if (targetType == typeof(Vector2Int))
            {
                value = new Vector2Int(Mathf.RoundToInt(vector2Value.x), Mathf.RoundToInt(vector2Value.y));
                return true;
            }

            if (targetType == typeof(Vector3Int))
            {
                value = new Vector3Int(
                    Mathf.RoundToInt(vector3Value.x),
                    Mathf.RoundToInt(vector3Value.y),
                    Mathf.RoundToInt(vector3Value.z));
                return true;
            }

            if (targetType.IsEnum)
            {
                try
                {
                    value = Enum.Parse(targetType, stringValue, true);
                    return true;
                }
                catch (Exception exception)
                {
                    error = "Could not parse enum " + targetType.Name + " from '" + stringValue + "': " + exception.Message;
                    return false;
                }
            }

            error = "Unsupported serialized SO value type " + targetType.FullName + ".";
            return false;
        }

        public static bool TryConvertValue(Type targetType, object rawValue, out object value, out string error)
        {
            value = null;
            error = string.Empty;

            if (targetType == null)
            {
                error = "Target type is null.";
                return false;
            }

            if (rawValue == null)
            {
                if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                {
                    value = null;
                    return true;
                }

                error = "Cannot assign null to value type " + targetType.FullName + ".";
                return false;
            }

            Type rawType = rawValue.GetType();
            if (targetType.IsAssignableFrom(rawType))
            {
                value = rawValue;
                return true;
            }

            try
            {
                if (targetType.IsEnum)
                {
                    value = rawValue is string enumText
                        ? Enum.Parse(targetType, enumText, true)
                        : Enum.ToObject(targetType, rawValue);
                    return true;
                }

                if (targetType == typeof(Vector2) && rawValue is Vector3 rawVector3For2)
                {
                    value = new Vector2(rawVector3For2.x, rawVector3For2.y);
                    return true;
                }

                if (targetType == typeof(Vector3) && rawValue is Vector2 rawVector2For3)
                {
                    value = new Vector3(rawVector2For3.x, rawVector2For3.y, 0f);
                    return true;
                }

                if (targetType == typeof(Vector2Int) && rawValue is Vector2 rawVector2)
                {
                    value = new Vector2Int(Mathf.RoundToInt(rawVector2.x), Mathf.RoundToInt(rawVector2.y));
                    return true;
                }

                if (targetType == typeof(Vector3Int) && rawValue is Vector3 rawVector3)
                {
                    value = new Vector3Int(
                        Mathf.RoundToInt(rawVector3.x),
                        Mathf.RoundToInt(rawVector3.y),
                        Mathf.RoundToInt(rawVector3.z));
                    return true;
                }

                value = Convert.ChangeType(rawValue, targetType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not convert " + rawType.Name + " to " + targetType.Name + ": " + exception.Message;
                return false;
            }
        }

        public static string FormatValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is UnityEngine.Object unityObject)
            {
                return unityObject == null
                    ? "null"
                    : unityObject.name + " (" + unityObject.GetType().Name + ")";
            }

            if (value is float floatValue)
            {
                return floatValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value is double doubleValue)
            {
                return doubleValue.ToString(CultureInfo.InvariantCulture);
            }

            return value.ToString();
        }

        private static bool TryResolveMemberOwner(
            object target,
            string memberPath,
            out object owner,
            out string memberName,
            out string error)
        {
            owner = target;
            memberName = string.Empty;
            error = string.Empty;

            if (target == null)
            {
                error = "SO value access target is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(memberPath))
            {
                error = "SO value access member path is empty.";
                return false;
            }

            string[] segments = memberPath.Split('.');
            for (int i = 0; i < segments.Length - 1; i++)
            {
                string segment = segments[i];
                if (!TryReadDirectMember(owner, segment, out object next, out error))
                {
                    return false;
                }

                if (next == null)
                {
                    error = "Member path '" + memberPath + "' hit null at '" + segment + "'.";
                    return false;
                }

                owner = next;
            }

            memberName = segments[segments.Length - 1];
            return true;
        }

        private static bool TryReadDirectMember(object owner, string memberName, out object value, out string error)
        {
            value = null;
            error = string.Empty;

            Type ownerType = owner.GetType();
            if (TryGetField(ownerType, memberName, out FieldInfo field))
            {
                value = field.GetValue(owner);
                return true;
            }

            if (TryGetProperty(ownerType, memberName, out PropertyInfo property))
            {
                if (!property.CanRead)
                {
                    error = "Property '" + memberName + "' on " + ownerType.FullName + " is not readable.";
                    return false;
                }

                value = property.GetValue(owner);
                return true;
            }

            error = "Member '" + memberName + "' was not found on " + ownerType.FullName + ".";
            return false;
        }

        private static bool TryGetField(Type type, string name, out FieldInfo field)
        {
            field = type.GetField(name, MemberFlags);
            return field != null;
        }

        private static bool TryGetProperty(Type type, string name, out PropertyInfo property)
        {
            property = type.GetProperty(name, MemberFlags);
            return property != null;
        }
    }
}
