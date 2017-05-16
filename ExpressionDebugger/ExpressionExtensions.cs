using ExpressionDebugger;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace System.Linq.Expressions
{
    public static class ExpressionExtensions
    {
        /// <summary>
        /// Generate script text
        /// </summary>
        /// <param name="node">Expression</param>
        /// <returns>Script text</returns>
        public static string ToScript(this Expression node)
        {
            var writer = new StringWriter();
            new DebugInfoInjector(writer).Inject(node);
            return writer.ToString();
        }

        /// <summary>
        /// Compile with debugging info injected
        /// </summary>
        /// <typeparam name="T">Type of lambda expression</typeparam>
        /// <param name="node">Lambda expression</param>
        /// <param name="filename">Filename to inject source code. if omit, temp filename will be used</param>
        /// <returns>Generated method</returns>
        public static T CompileWithDebugInfo<T>(this Expression<T> node, string filename = null)
        {
            return (T)(object)CompileWithDebugInfo((LambdaExpression)node, filename);
        }

        public static Delegate CompileWithDebugInfo(this LambdaExpression node, string filename = null)
        {
            using (var injector = new DebugInfoInjector(filename))
            {
                return injector.Compile(node);
            }
        }
    }
}
