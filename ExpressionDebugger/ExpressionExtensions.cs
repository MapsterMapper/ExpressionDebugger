using System;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace ExpressionDebugger
{
    public static class ExpressionExtensions
    {
        public static string ToScript(this Expression node)
        {
            var writer = new StringWriter();
            new DebugInfoInjector(writer).Inject(node);
            return writer.ToString();
        }

        public static T Compile<T>(this Expression<T> node, string filename)
        {
#if NETSTANDARD1_3
            return node.Compile();
#else
            var name = "m_" + Guid.NewGuid().ToString("N");
            var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);

            var daType = typeof(DebuggableAttribute);
            var daCtor = daType.GetConstructor(new[] { typeof(DebuggableAttribute.DebuggingModes) });
            var daBuilder = new CustomAttributeBuilder(daCtor, new object[] {
                DebuggableAttribute.DebuggingModes.DisableOptimizations |
                DebuggableAttribute.DebuggingModes.Default });
            asm.SetCustomAttribute(daBuilder);

            var mod = asm.DefineDynamicModule(name, true);
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

            return (T)(object)Delegate.CreateDelegate(typeof(T), newtype.GetMethod("Main"));
#endif
        }
    }
}
