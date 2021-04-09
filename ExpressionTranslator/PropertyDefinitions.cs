using System;

namespace ExpressionDebugger
{
    public class PropertyDefinitions
    {
        public Type Type { get; set; }
        public string Name { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsInitOnly { get; set; }
    }
}
