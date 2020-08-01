using System;
using System.Collections.Generic;

namespace ExpressionDebugger
{
    public class ExpressionDefinitions : TypeDefinitions
    {
        public string? MethodName { get; set; }
        public bool IsExpression { get; set; }
    }

    public class TypeDefinitions
    {
        public string? Namespace { get; set; }
        public string? TypeName { get; set; }
        public bool IsStatic { get; set; }
        public IEnumerable<Type>? Implements { get; set; }
    }

}
