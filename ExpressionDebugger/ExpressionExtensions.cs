using ExpressionDebugger;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime;
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
        public static string ToScript(this Expression node, ExpressionDefinitions definitions = null)
        {
            var translator = new ExpressionTranslator(definitions);
            return translator.Translate(node);
        }

        /// <summary>
        /// Compile with debugging info injected
        /// </summary>
        /// <typeparam name="T">Type of lambda expression</typeparam>
        /// <param name="node">Lambda expression</param>
        /// <param name="options">Compilation options</param>
        /// <returns>Generated method</returns>
        public static T CompileWithDebugInfo<T>(this Expression<T> node, ExpressionCompilationOptions options = null)
        {
            return (T)(object)CompileWithDebugInfo((LambdaExpression)node, options);
        }

        public static Delegate CompileWithDebugInfo(this LambdaExpression node, ExpressionCompilationOptions options = null)
        {
            try
            {
                if (options == null)
                    options = new ExpressionCompilationOptions();
                if (options.DefaultDefinitions == null)
                    options.DefaultDefinitions = new ExpressionDefinitions { IsStatic = true };
                var definitions = options.DefaultDefinitions;
                if (definitions.TypeName == null)
                    definitions.TypeName = "Program";
                var translator = new ExpressionTranslator(definitions);
                var script = translator.Translate(node);
                var compiler = new ExpressionCompiler(options);
                compiler.AddFile(script, Path.ChangeExtension(Path.GetRandomFileName(), ".cs"));
                var references = translator.TypeNames.Select(it => it.Key.Assembly).ToHashSet();
                if (options.References != null)
                    references.UnionWith(options.References);
                references.Add(typeof(object).Assembly);
                references.Add(Assembly.Load(new AssemblyName("System.Runtime")));
                references.Add(Assembly.Load(new AssemblyName("System.Collections")));
                if (translator.HasDynamic)
                    references.Add(Assembly.Load(new AssemblyName("Microsoft.CSharp")));

                var assembly = compiler.CreateAssembly(references);
                var typeName = definitions.Namespace == null
                    ? definitions.TypeName
                    : (definitions.Namespace + "." + definitions.TypeName);
                var type = assembly.GetType(typeName);
                var method = type.GetMethod(definitions.MethodName ?? "Main");
                var obj = definitions.IsStatic ? null : Activator.CreateInstance(type);
                foreach (var kvp in translator.Constants)
                {
                    var field = type.GetField(kvp.Value);
                    field.SetValue(obj, kvp.Key);
                }
                return method.CreateDelegate(node.Type, obj);
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
                return node.Compile();
            }
        }
    }
}
