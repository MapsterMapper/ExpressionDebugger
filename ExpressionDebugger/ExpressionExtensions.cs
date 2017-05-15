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
#if NETSTANDARD1_3
            return node.Compile();
#else

            if (filename == null)
                filename = Path.GetTempFileName();
            var assemblyName = "m_" + Guid.NewGuid().ToString("N");
            var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);

            var daType = typeof(DebuggableAttribute);
            var daCtor = daType.GetConstructor(new[] { typeof(DebuggableAttribute.DebuggingModes) });
            var daBuilder = new CustomAttributeBuilder(daCtor, new object[] {
                DebuggableAttribute.DebuggingModes.DisableOptimizations |
                DebuggableAttribute.DebuggingModes.Default });
            asm.SetCustomAttribute(daBuilder);

            var mod = asm.DefineDynamicModule(assemblyName, true);
            var type = mod.DefineType("Program", TypeAttributes.Public | TypeAttributes.Class);
            var meth = type.DefineMethod("Main", MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static);

            LambdaExpression injected;
            using (var injector = new DebugInfoInjector(filename))
            {
                injected = (LambdaExpression)injector.Inject(node);
            }
            var gen = DebugInfoGenerator.CreatePdbGenerator();
            injected.CompileToMethod(meth, gen);

            var newtype = type.CreateType();

            return Delegate.CreateDelegate(node.Type, newtype.GetMethod("Main"));
#endif
        }
    }
}
