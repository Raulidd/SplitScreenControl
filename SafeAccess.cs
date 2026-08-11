using System;
using System.Reflection;

namespace SplitScreenControl
{
    public static class SafeAccess
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static object GetRaw(object obj, string name)
        {
            if (obj == null) return null;
            Type type = obj.GetType();

            while (type != null)
            {
                FieldInfo f = type.GetField(name, Flags);
                if (f != null) return f.GetValue(obj);

                PropertyInfo p = type.GetProperty(name, Flags);
                if (p != null) return p.GetValue(obj, null);

                type = type.BaseType;
            }
            return null;
        }

        public static double GetNumeric(object obj, string name, double fallback = 0)
        {
            object raw = GetRaw(obj, name);
            if (raw == null) return fallback;

            try
            {
                return Convert.ToDouble(raw);
            }
            catch
            {
                object inner = GetRaw(raw, "Value");
                if (inner != null)
                {
                    try { return Convert.ToDouble(inner); } catch { }
                }
                return fallback;
            }
        }

        public static bool GetBool(object obj, string name, bool fallback = false)
        {
            object raw = GetRaw(obj, name);
            if (raw == null) return fallback;

            try
            {
                return Convert.ToBoolean(raw);
            }
            catch
            {
                object inner = GetRaw(raw, "Value");
                if (inner != null)
                {
                    try { return Convert.ToBoolean(inner); } catch { }
                }
                return fallback;
            }
        }

        public static object GetStaticRaw(string assemblyQualifiedTypeName, string name)
        {
            Type type = Type.GetType(assemblyQualifiedTypeName);
            if (type == null) return null;

            while (type != null)
            {
                FieldInfo f = type.GetField(name, Flags);
                if (f != null && f.IsStatic) return f.GetValue(null);

                PropertyInfo p = type.GetProperty(name, Flags);
                MethodInfo getter = p?.GetGetMethod(true);
                if (getter != null && getter.IsStatic) return p.GetValue(null, null);

                type = type.BaseType;
            }
            return null;
        }
        public static bool SetRaw(object obj, string name, object value)
        {
            if (obj == null) return false;
            Type type = obj.GetType();

            while (type != null)
            {
                FieldInfo f = type.GetField(name, Flags);
                if (f != null) { f.SetValue(obj, value); return true; }

                PropertyInfo p = type.GetProperty(name, Flags);
                if (p != null && p.CanWrite) { p.SetValue(obj, value, null); return true; }

                type = type.BaseType;
            }
            return false;
        }

        public static bool InvokeNoArgMethod(object obj, string name)
        {
            if (obj == null) return false;
            Type type = obj.GetType();

            while (type != null)
            {
                MethodInfo m = type.GetMethod(name, Flags, null, Type.EmptyTypes, null);
                if (m != null) { m.Invoke(obj, null); return true; }

                type = type.BaseType;
            }
            return false;
        }

        public static bool InvokeMethod(object obj, string name, params object[] args)
        {
            if (obj == null) return false;
            Type type = obj.GetType();
            int argCount = args?.Length ?? 0;

            while (type != null)
            {
                foreach (MethodInfo m in type.GetMethods(Flags))
                {
                    if (m.Name != name) continue;
                    ParameterInfo[] parameters = m.GetParameters();
                    if (parameters.Length != argCount) continue;

                    m.Invoke(obj, args);
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }
    }
}
