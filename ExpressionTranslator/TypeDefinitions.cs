using System;
using System.Collections.Generic;

namespace ExpressionDebugger
{
    public class TypeDefinitions
    {
        public string? Namespace { get; set; }
        public string? TypeName { get; set; }
        public bool IsStatic { get; set; }
        public bool IsInternal { get; set; }
        public IEnumerable<Type>? Implements { get; set; }
    }
}