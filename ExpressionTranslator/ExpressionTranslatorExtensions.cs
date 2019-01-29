using System;
using System.Linq.Expressions;

namespace ExpressionDebugger
{
    public static class ExpressionTranslatorExtensions
    {
        /// <summary>
        /// Generate script text
        /// </summary>
        /// <param name="node">Expression</param>
        /// <returns>Script text</returns>
        public static string ToScript(this Expression node, ExpressionDefinitions definitions = null)
        {
            var translator = new ExpressionTranslator(definitions);
            return translator.Translate(node);
        }
    }
}
