using System;
using System.Collections.Generic;

namespace ExpressionDebugger
{
    public class ExpressionDefinitions
    {
        public string Namespace { get; set; }
        public string TypeName { get; set; }
        public bool IsStatic { get; set; }
        public string MethodName { get; set; }
        public IEnumerable<Type> Implements { get; set; }
    }
}
