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
    }
}
