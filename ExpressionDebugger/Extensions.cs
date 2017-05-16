using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ExpressionDebugger
{
    internal static class Extensions
    {
        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source)
        {
            return new HashSet<T>(source);
        }

#if NET40
        public static Type GetTypeInfo(this Type type) {
            return type;
        }
#endif

#if NET40 || NETSTANDARD1_3
        public static T GetCustomAttribute<T>(this MemberInfo memberInfo) where T : Attribute
        {
            return (T)memberInfo.GetCustomAttributes(typeof(T), true).SingleOrDefault();
        }

        public static T GetCustomAttribute<T>(this Type type) where T : Attribute
        {
            return (T)type.GetTypeInfo().GetCustomAttributes(typeof(T), true).SingleOrDefault();
        }
#endif
    }
}
