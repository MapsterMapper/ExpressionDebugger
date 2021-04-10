using System;
using System.Collections.Generic;
using System.Reflection;

namespace ExpressionDebugger
{
    public class PropertyDefinitions
    {
        public Type Type { get; set; }
        public string Name { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsInitOnly { get; set; }
        public byte? NullableContext { get; set; }
        public byte[]? Nullable { get; set; }
    }
}
