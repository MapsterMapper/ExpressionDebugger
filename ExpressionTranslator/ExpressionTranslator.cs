using System;
using System.Collections.Generic;
#if !NETSTANDARD1_3
using System.Dynamic;
#endif
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ExpressionDebugger
{
    public class ExpressionTranslator : ExpressionVisitor
    {
        private const int Tabsize = 4;
        private StringWriter _writer;
        private int _indentLevel;
        private StringWriter _appendWriter;

        private Dictionary<Type, string> _typeNames;
        private HashSet<string> _usings;
        private Dictionary<object, string> _constants;

        public Dictionary<object, string> Constants => _constants ?? (_constants = new Dictionary<object, string>());
        public Dictionary<Type, string> TypeNames => _typeNames ?? (_typeNames = new Dictionary<Type, string>());

        public bool HasDynamic { get; private set; }
        public ExpressionDefinitions Definitions { get; }
        public Expression Expression { get; private set; }

        private ExpressionTranslator(ExpressionDefinitions definitions = null)
        {
            Definitions = definitions;
            _writer = new StringWriter();
            ResetIndentLevel();
        }

        private void ResetIndentLevel()
        {
            _indentLevel = 0;
            if (Definitions?.TypeName != null)
            {
                _indentLevel++;
                if (Definitions.Namespace != null)
                    _indentLevel++;
            }
        }

        public static ExpressionTranslator Create(Expression node, ExpressionDefinitions definitions = null)
        {
            var translator = new ExpressionTranslator(definitions);
            if (node.NodeType == ExpressionType.Lambda)
                translator.VisitLambda((LambdaExpression)node, LambdaType.Main);
            else
                translator.Visit(node);
            translator.Expression = node;
            return translator;
        }

        private static int GetPrecedence(ExpressionType nodeType)
        {
            switch (nodeType)
            {
                // Assignment
                case ExpressionType.AddAssign:
                case ExpressionType.AddAssignChecked:
                case ExpressionType.AndAssign:
                case ExpressionType.Assign:
                case ExpressionType.DivideAssign:
                case ExpressionType.ExclusiveOrAssign:
                case ExpressionType.LeftShiftAssign:
                case ExpressionType.ModuloAssign:
                case ExpressionType.MultiplyAssign:
                case ExpressionType.MultiplyAssignChecked:
                case ExpressionType.OrAssign:
                case ExpressionType.PowerAssign:
                case ExpressionType.Quote:
                case ExpressionType.RightShiftAssign:
                case ExpressionType.SubtractAssign:
                case ExpressionType.SubtractAssignChecked:
                case ExpressionType.Extension:
                    return 1;

                // Conditional
                case ExpressionType.Coalesce:
                case ExpressionType.Conditional:
                    return 2;

                // Conditional OR
                case ExpressionType.OrElse:
                    return 3;

                // Conditional AND
                case ExpressionType.AndAlso:
                    return 4;

                // Logical OR
                case ExpressionType.Or:
                    return 5;

                // Logical XOR
                case ExpressionType.ExclusiveOr:
                    return 6;

                // Logical AND
                case ExpressionType.And:
                    return 7;

                // Equality
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                    return 8;

                // Relational and type testing
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.TypeAs:
                case ExpressionType.TypeEqual:
                case ExpressionType.TypeIs:
                    return 9;

                // Shift
                case ExpressionType.LeftShift:
                case ExpressionType.RightShift:
                    return 10;

                // Additive
                case ExpressionType.Add:
                case ExpressionType.AddChecked:
                case ExpressionType.Decrement:
                case ExpressionType.Increment:
                case ExpressionType.Subtract:
                case ExpressionType.SubtractChecked:
                    return 11;

                // Multiplicative
                case ExpressionType.Divide:
                case ExpressionType.Modulo:
                case ExpressionType.Multiply:
                case ExpressionType.MultiplyChecked:
                    return 12;

                // Unary
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                case ExpressionType.IsFalse:
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked:
                case ExpressionType.Not:
                case ExpressionType.OnesComplement:
                case ExpressionType.PreDecrementAssign:
                case ExpressionType.PreIncrementAssign:
                case ExpressionType.UnaryPlus:
                    return 13;

                // Power
                case ExpressionType.Power:
                    return 14;

                default:
                    return 100;
            }
        }

        private static bool ShouldGroup(Expression node, ExpressionType parentNodeType, bool isRightNode)
        {
            if (node == null)
                return false;

            var nodePrecedence = GetPrecedence(node.NodeType);
            var parentPrecedence = GetPrecedence(parentNodeType);

            if (nodePrecedence != parentPrecedence)
                return nodePrecedence < parentPrecedence;

            switch (parentNodeType)
            {
                //wrap to prevent confusion
                case ExpressionType.Conditional:
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked:
                case ExpressionType.UnaryPlus:
                    return true;

                //1-(1-1) != 1-1-1
                case ExpressionType.Divide:
                case ExpressionType.Modulo:
                case ExpressionType.LeftShift:
                case ExpressionType.Power:
                case ExpressionType.RightShift:
                case ExpressionType.Subtract:
                case ExpressionType.SubtractChecked:
                    return isRightNode;

                default:
                    return false;
            }
        }

        private void Write(string text)
        {
            _writer.Write(text);
        }

        private Expression Visit(string open, Expression node, params string[] end)
        {
            Write(open);
            var result = Visit(node);
            Write(end);
            return result;
        }

        private void Write(params string[] texts)
        {
            foreach (var text in texts)
            {
                Write(text);
            }
        }

        private void WriteLine()
        {
            _writer.WriteLine();

            var spaceCount = _indentLevel * Tabsize;
            _writer.Write(new string(' ', spaceCount));
        }

        private void WriteNextLine(string text)
        {
            WriteLine();
            Write(text);
        }

        private Expression VisitNextLine(string open, Expression node, params string[] end)
        {
            WriteLine();
            Write(open);
            var result = Visit(node);
            Write(end);
            return result;
        }

        private void WriteNextLine(params string[] texts)
        {
            WriteLine();
            foreach (var text in texts)
            {
                Write(text);
            }
        }

        private void Indent(bool inline = false)
        {
            if (!inline)
                WriteLine();
            Write("{");
            _indentLevel++;
        }

        private void Outdent()
        {
            _indentLevel--;
            WriteNextLine("}");
        }

        private Expression VisitGroup(Expression node, ExpressionType parentNodeType, bool isRightNode = false)
        {
            Expression result;
            if (!IsInline(node))
            {
                var func = typeof(Func<>).MakeGenericType(node.Type);
                Write("(new ", Translate(func), "(() => ");
                Indent(true);
                result = VisitMultiline(node, true);
                Outdent();
                Write(")()");
            }
            else if (ShouldGroup(node, parentNodeType, isRightNode))
            {
                result = Visit("(", node, ")");
            }
            else
            {
                result = Visit(node);
            }

            return result;
        }

        private static string Translate(ExpressionType nodeType)
        {
            switch (nodeType)
            {
                case ExpressionType.Add: return "+";
                case ExpressionType.AddChecked: return "+";
                case ExpressionType.AddAssign: return "+=";
                case ExpressionType.AddAssignChecked: return "+=";
                case ExpressionType.And: return "&";
                case ExpressionType.AndAlso: return "&&";
                case ExpressionType.AndAssign: return "&=";
                case ExpressionType.ArrayLength: return ".Length";
                case ExpressionType.Assign: return "=";
                case ExpressionType.Coalesce: return "??";
                case ExpressionType.Decrement: return " - 1";
                case ExpressionType.Divide: return "/";
                case ExpressionType.DivideAssign: return "/=";
                case ExpressionType.Equal: return "==";
                case ExpressionType.ExclusiveOr: return "^";
                case ExpressionType.ExclusiveOrAssign: return "^=";
                case ExpressionType.GreaterThan: return ">";
                case ExpressionType.GreaterThanOrEqual: return ">=";
                case ExpressionType.Increment: return " + 1";
                case ExpressionType.IsFalse: return "!";
                case ExpressionType.IsTrue: return "";
                case ExpressionType.Modulo: return "%";
                case ExpressionType.ModuloAssign: return "%=";
                case ExpressionType.Multiply: return "*";
                case ExpressionType.MultiplyAssign: return "*=";
                case ExpressionType.MultiplyAssignChecked: return "*=";
                case ExpressionType.MultiplyChecked: return "*";
                case ExpressionType.Negate: return "-";
                case ExpressionType.NegateChecked: return "-";
                case ExpressionType.Not: return "!";
                case ExpressionType.LeftShift: return "<<";
                case ExpressionType.LeftShiftAssign: return "<<=";
                case ExpressionType.LessThan: return "<";
                case ExpressionType.LessThanOrEqual: return "<=";
                case ExpressionType.NotEqual: return "!=";
                case ExpressionType.OnesComplement: return "~";
                case ExpressionType.Or: return "|";
                case ExpressionType.OrAssign: return "|=";
                case ExpressionType.OrElse: return "||";
                case ExpressionType.PreDecrementAssign: return "--";
                case ExpressionType.PreIncrementAssign: return "++";
                case ExpressionType.PostDecrementAssign: return "--";
                case ExpressionType.PostIncrementAssign: return "++";
                //case ExpressionType.Power: return "**";
                //case ExpressionType.PowerAssign: return "**=";
                case ExpressionType.RightShift: return ">>";
                case ExpressionType.RightShiftAssign: return ">>=";
                case ExpressionType.Subtract: return "-";
                case ExpressionType.SubtractChecked: return "-";
                case ExpressionType.SubtractAssign: return "-=";
                case ExpressionType.SubtractAssignChecked: return "-=";
                case ExpressionType.Throw: return "throw";
                case ExpressionType.TypeAs: return " as ";
                case ExpressionType.UnaryPlus: return "+";
                case ExpressionType.Unbox: return "";

                default:
                    throw new InvalidOperationException();
            }
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            Expression left, right;
            if (node.NodeType == ExpressionType.ArrayIndex)
            {
                left = VisitGroup(node.Left, node.NodeType);
                right = Visit("[", node.Right, "]");
            }
            else if (node.NodeType == ExpressionType.Power || node.NodeType == ExpressionType.PowerAssign)
            {
                if (node.NodeType == ExpressionType.PowerAssign)
                {
                    left = VisitGroup(node.Left, node.NodeType);
                    Write(" = ");
                }
                Write(Translate(typeof(Math)), ".Pow(");
                left = Visit(node.Left);
                Write(", ");
                right = Visit(node.Right);
                Write(")");
            }
            else
            {
                left = VisitGroup(node.Left, node.NodeType);
                Write(" ", Translate(node.NodeType), " ");
                right = VisitGroup(node.Right, node.NodeType, true);
            }

            return node.Update(left, node.Conversion, right);
        }

        private string Translate(Type type)
        {
            if (type == typeof(bool))
                return "bool";
            if (type == typeof(byte))
                return "byte";
            if (type == typeof(char))
                return "char";
            if (type == typeof(decimal))
                return "decimal";
            if (type == typeof(double))
                return "double";
            if (type == typeof(float))
                return "float";
            if (type == typeof(int))
                return "int";
            if (type == typeof(long))
                return "long";
            if (type == typeof(object))
                return "object";
            if (type == typeof(sbyte))
                return "sbyte";
            if (type == typeof(short))
                return "short";
            if (type == typeof(string))
                return "string";
            if (type == typeof(uint))
                return "uint";
            if (type == typeof(ulong))
                return "ulong";
            if (type == typeof(ushort))
                return "ushort";
            if (type == typeof(void))
                return "void";
            if (type.GetTypeInfo().IsNotPublic)
            {
                HasDynamic = true;
                return "dynamic";
            }
#if !NETSTANDARD1_3
            if (typeof(IDynamicMetaObjectProvider).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
            {
                HasDynamic = true;
                return "dynamic";
            }
#endif

            if (type.IsArray)
            {
                var rank = type.GetArrayRank();
                return Translate(type.GetElementType()) + "[" + new string(',', rank - 1) + "]";
            }

            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
                return Translate(underlyingType) + "?";

            if (_usings == null)
                _usings = new HashSet<string>();
            if (_typeNames == null)
                _typeNames = new Dictionary<Type, string>();
            if (!_typeNames.TryGetValue(type, out var name))
            {
                name = GetTypeName(type);
                var count = _typeNames.Count(kvp => GetTypeName(kvp.Key) == name);
                if (count > 0)
                    name += count + 1;
                else if (!string.IsNullOrEmpty(type.Namespace))
                    _usings.Add(type.Namespace);
                _typeNames.Add(type, name);
            }

            return name;
        }

        private static string GetVarName(string name)
        {
            var index = name.IndexOf('`');
            if (index >= 0)
                name = name.Substring(0, index);
            return name;
        }

        private string GetTypeName(Type type)
        {
            var name = GetSingleTypeName(type);
            if (type.DeclaringType == null)
                return name;

            return GetTypeName(type.DeclaringType) + "." + name;
        }

        private string GetSingleTypeName(Type type)
        {
            if (type.GetTypeInfo().IsGenericType)
            {
                var name = type.Name;
                var index = name.IndexOf('`');
                if (index >= 0)
                    name = name.Substring(0, index);
                if (type.GetTypeInfo().IsGenericTypeDefinition)
                {
                    var typeArgs = type.GetGenericArguments();
                    return name + "<" + new string(',', typeArgs.Length - 1) + ">";
                }
                return name + "<" + string.Join(", ", type.GetGenericArguments().Select(Translate)) + ">";
            }
            else
            {
                return type.Name;
            }
        }

        private static bool IsInline(Expression node)
        {
            switch (node.NodeType)
            {
                case ExpressionType.Conditional:
                    var condExpr = (ConditionalExpression)node;
                    return condExpr.Type != typeof(void) && IsInline(condExpr.IfTrue) && IsInline(condExpr.IfFalse);

                case ExpressionType.Block:
                case ExpressionType.DebugInfo:
                //case ExpressionType.Goto:
                case ExpressionType.Label:
                case ExpressionType.Loop:
                case ExpressionType.Switch:
                //case ExpressionType.Throw:
                case ExpressionType.Try:
                    return false;
                default:
                    return true;
            }
        }

        private Expression VisitMultiline(Expression node, bool shouldReturn)
        {
            switch (node.NodeType)
            {
                case ExpressionType.Block:
                    return VisitBlock((BlockExpression)node, shouldReturn);

                case ExpressionType.Conditional:
                    return VisitConditional((ConditionalExpression)node, shouldReturn);

                case ExpressionType.Try:
                    return VisitTry((TryExpression)node, shouldReturn);

                case ExpressionType.Switch:
                    return VisitSwitch((SwitchExpression)node, shouldReturn);

                //case ExpressionType.DebugInfo:
                //case ExpressionType.Goto:
                //case ExpressionType.Loop:
                default:
                    return Visit(node);
            }
        }

        private Expression VisitBody(Expression node, bool shouldReturn = false)
        {
            if (node.NodeType == ExpressionType.Block)
                return VisitBlock((BlockExpression)node, shouldReturn);

            if (node.NodeType == ExpressionType.Default && node.Type == typeof(void))
                return node;

            var lines = VisitBlockBody(new List<Expression> { node }, shouldReturn);
            return Expression.Block(lines);
        }

        private IEnumerable<Expression> VisitBlockBody(IList<Expression> exprs, bool shouldReturn)
        {
            var lines = new List<Expression>();
            var last = exprs.Count - 1;
            for (int i = 0; i < exprs.Count; i++)
            {
                var expr = exprs[i];
                if (expr.NodeType == ExpressionType.Default && expr.Type == typeof(void))
                    continue;

                var isInline = IsInline(expr);
                if (isInline || i > 0)
                    WriteLine();

                Expression next;
                if (isInline)
                {
                    if (shouldReturn && i == last && expr.NodeType != ExpressionType.Throw)
                        Write("return ");
                    next = Visit(expr);
                    Write(";");
                }
                else
                {
                    next = VisitMultiline(expr, shouldReturn && i == last);
                }
                lines.Add(next);
            }
            return lines;
        }

        private Expression VisitBlock(BlockExpression node, bool shouldReturn)
        {
            var assignedVariables = node.Expressions
                .Where(exp => exp.NodeType == ExpressionType.Assign)
                .Select(exp => ((BinaryExpression)exp).Left)
                .Where(exp => exp.NodeType == ExpressionType.Parameter)
                .Cast<ParameterExpression>()
                .ToHashSet();

            var list = new List<ParameterExpression>();
            var hasDeclaration = false;
            foreach (var variable in node.Variables)
            {
                Expression arg;
                if (assignedVariables.Contains(variable))
                {
                    arg = VisitParameter(variable, false);
                }
                else
                {
                    arg = VisitNextLine(Translate(variable.Type) + " ", variable, ";");
                    hasDeclaration = true;
                }
                list.Add((ParameterExpression)arg);
            }
            if (hasDeclaration)
                WriteLine();

            var lines = VisitBlockBody(node.Expressions, shouldReturn && node.Type != typeof(void));
            return Expression.Block(list, lines);
        }

        protected override Expression VisitBlock(BlockExpression node)
        {
            return VisitBlock(node, false);
        }

        private CatchBlock VisitCatchBlock(CatchBlock node, bool shouldReturn)
        {
            WriteNextLine("catch (", Translate(node.Test));
            if (node.Variable != null)
            {
                Visit(" ", node.Variable);
            }
            Write(")");

            var filter = node.Filter;
            if (filter != null)
            {
                filter = Visit(" when (", filter, ")");
            }
            Indent();
            var body = VisitBody(node.Body, shouldReturn);
            Outdent();
            return node.Variable != null
                ? Expression.Catch(node.Variable, body, filter)
                : Expression.Catch(node.Test, body, filter);
        }

        protected override CatchBlock VisitCatchBlock(CatchBlock node)
        {
            return VisitCatchBlock(node, false);
        }

        private Expression VisitConditional(ConditionalExpression node, bool shouldReturn)
        {
            if (IsInline(node))
            {
                Expression test = VisitGroup(node.Test, node.NodeType);
                Write(" ? ");
                Expression ifTrue = VisitGroup(node.IfTrue, node.NodeType);
                Write(" : ");
                Expression ifFalse = VisitGroup(node.IfFalse, node.NodeType);
                return node.Update(test, ifTrue, ifFalse);
            }
            else
            {
                return VisitConditionalBlock(node, shouldReturn);
            }
        }

        private Expression VisitConditionalBlock(ConditionalExpression node, bool shouldReturn, bool chain = false)
        {
            if (chain)
                WriteNextLine("else if (");
            else
                WriteNextLine("if (");
            Expression test = Visit(node.Test);
            Write(")");
            Indent();
            Expression ifTrue = VisitBody(node.IfTrue, shouldReturn);
            Expression ifFalse = node.IfFalse;
            if (node.IfFalse.NodeType != ExpressionType.Default)
            {
                Outdent();
                if (node.IfFalse.NodeType == ExpressionType.Conditional)
                {
                    ifFalse = VisitConditionalBlock((ConditionalExpression)node.IfFalse, shouldReturn, true);
                }
                else
                {
                    WriteNextLine("else");
                    Indent();
                    ifFalse = VisitBody(node.IfFalse, shouldReturn);
                    Outdent();
                }
            }
            else
            {
                Outdent();
            }

            Expression condition = Expression.Condition(test, ifTrue, ifFalse, typeof(void));
            return CreateBlock(condition);
        }

        protected override Expression VisitConditional(ConditionalExpression node)
        {
            return VisitConditional(node, false);
        }

        private void WriteValue(object value)
        {
            if (value == null)
            {
                Write("null");
            }
            else if (value is string str)
            {
                if (str.IndexOf('\\') >= 0 || str.IndexOf('\n') >= 0 || str.IndexOf('"') >= 0)
                {
                    str = str.Replace(@"""", @"""""");
                    Write($"@\"{str}\"");
                }
                else
                {
                    Write($"\"{str}\"");
                }
            }
            else if (value is char c)
            {
                if (c == '\\')
                    Write(@"'\\'");
                else if (c == '\'')
                    Write(@"'\''");
                else
                    Write($"'{c}'");
            }
            else if (value is bool)
            {
                Write(value.ToString().ToLower());
            }
            else if (value is Type t)
            {
                Write($"typeof({Translate(t)})");
            }
            else if (value is int)
            {
                Write(value.ToString());
            }
            else if (value is double d)
            {
                if (double.IsNaN(d))
                    Write("double.NaN");
                else if (double.IsPositiveInfinity(d))
                    Write("double.PositiveInfinity");
                else if (double.IsNegativeInfinity(d))
                    Write("double.NegativeInfinity");
                else
                    Write(d.ToString(), "d");
            }
            else if (value is float f)
            {
                if (float.IsNaN(f))
                    Write("float.NaN");
                else if (float.IsPositiveInfinity(f))
                    Write("float.PositiveInfinity");
                else if (float.IsNegativeInfinity(f))
                    Write("float.NegativeInfinity");
                else
                    Write(f.ToString(), "f");
            }
            else if (value is decimal || value is long || value is uint || value is ulong)
            {
                Write(value.ToString(), GetLiteral(value.GetType()));
            }
            else if (value is byte || value is sbyte || value is short || value is ushort)
            {
                Write("(", Translate(value.GetType()), ")", value.ToString());
            }
            else if (value.GetType().GetTypeInfo().IsEnum)
            {
                var name = Enum.GetName(value.GetType(), value);
                if (name != null)
                    Write(Translate(value.GetType()), ".", name);
                else
                    Write("(", Translate(value.GetType()), ")", value.ToString());
            }
            else
            {
                Write(GetConstant(value));
            }
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            WriteValue(node.Value);
            return node;
        }

        private static string GetLiteral(Type type)
        {
            if (type == typeof(decimal))
                return "m";
            else if (type == typeof(long))
                return "l";
            else if (type == typeof(uint))
                return "u";
            else if (type == typeof(ulong))
                return "ul";
            else if (type == typeof(double))
                return "d";
            else if (type == typeof(float))
                return "f";
            else
                return "";
        }

        protected override Expression VisitDefault(DefaultExpression node)
        {
            Write("default(", Translate(node.Type), ")");
            return node;
        }

#if !NETSTANDARD1_3
        private static Expression Update(DynamicExpression node, IEnumerable<Expression> args)
        {
            // ReSharper disable PossibleMultipleEnumeration
            return node.Arguments.SequenceEqual(args) ? node : node.Update(args);
            // ReSharper restore PossibleMultipleEnumeration
        }

        protected override Expression VisitDynamic(DynamicExpression node)
        {
            if (node.Binder is ConvertBinder convert)
            {
                Write("(", Translate(convert.Type), ")");
                var expr = VisitGroup(node.Arguments[0], ExpressionType.Convert);
                return Update(node, new[] { expr }.Concat(node.Arguments.Skip(1)));
            }
            if (node.Binder is GetMemberBinder getMember)
            {
                var expr = VisitGroup(node.Arguments[0], ExpressionType.MemberAccess);
                Write(".", getMember.Name);
                return Update(node, new[] { expr }.Concat(node.Arguments.Skip(1)));
            }
            if (node.Binder is SetMemberBinder setMember)
            {
                var expr = VisitGroup(node.Arguments[0], ExpressionType.MemberAccess);
                Write(".", setMember.Name, " = ");
                var value = VisitGroup(node.Arguments[1], ExpressionType.Assign);
                return Update(node, new[] { expr, value }.Concat(node.Arguments.Skip(2)));
            }
            if (node.Binder is DeleteMemberBinder deleteMember)
            {
                var expr = VisitGroup(node.Arguments[0], ExpressionType.MemberAccess);
                Write(".", deleteMember.Name, " = null");
                return Update(node, new[] { expr }.Concat(node.Arguments.Skip(1)));
            }
            if (node.Binder is GetIndexBinder)
            {
                var expr = VisitGroup(node.Arguments[0], ExpressionType.Index);
                var args = VisitArguments("[", node.Arguments.Skip(1).ToList(), Visit, "]");
                return Update(node, new[] { expr }.Concat(args));
            }
            if (node.Binder is SetIndexBinder)
            {
                var expr = VisitGroup(node.Arguments[0], ExpressionType.Index);
                var args = VisitArguments("[", node.Arguments.Skip(1).Take(node.Arguments.Count - 2).ToList(), Visit, "]");
                Write(" = ");
                var value = VisitGroup(node.Arguments[node.Arguments.Count - 1], ExpressionType.Assign);
                return Update(node, new[] { expr }.Concat(args).Concat(new[] { value }));
            }
            if (node.Binder is DeleteIndexBinder)
            {
                var expr = VisitGroup(node.Arguments[0], ExpressionType.Index);
                var args = VisitArguments("[", node.Arguments.Skip(1).ToList(), Visit, "]");
                Write(" = null");
                return Update(node, new[] { expr }.Concat(args));
            }
            if (node.Binder is InvokeMemberBinder invokeMember)
            {
                var expr = VisitGroup(node.Arguments[0], ExpressionType.MemberAccess);
                Write(".", invokeMember.Name);
                var args = VisitArguments("(", node.Arguments.Skip(1).ToList(), Visit, ")");
                return Update(node, new[] { expr }.Concat(args));
            }
            if (node.Binder is InvokeBinder)
            {
                var expr = VisitGroup(node.Arguments[0], ExpressionType.Invoke);
                var args = VisitArguments("(", node.Arguments.Skip(1).ToList(), Visit, ")");
                return Update(node, new[] { expr }.Concat(args));
            }
            if (node.Binder is CreateInstanceBinder)
            {
                Write("new ");
                var expr = VisitGroup(node.Arguments[0], ExpressionType.Invoke);
                var args = VisitArguments("(", node.Arguments.Skip(1).ToList(), Visit, ")");
                return Update(node, new[] { expr }.Concat(args));
            }
            if (node.Binder is UnaryOperationBinder unary)
            {
                var expr = VisitUnary(node.Arguments[0], unary.Operation);
                return Update(node, new[] { expr }.Concat(node.Arguments.Skip(1)));
            }
            if (node.Binder is BinaryOperationBinder binary)
            {
                var left = VisitGroup(node.Arguments[0], node.NodeType);
                Write(" ", Translate(binary.Operation), " ");
                var right = VisitGroup(node.Arguments[1], node.NodeType, true);
                return Update(node, new[] { left, right }.Concat(node.Arguments.Skip(2)));
            }
            Write("dynamic");
            var dynArgs = VisitArguments("(" + Translate(node.Binder.GetType()) + ", ", node.Arguments, Visit, ")");
            return node.Update(dynArgs);
        }
#endif

        private IList<T> VisitArguments<T>(string open, IList<T> args, Func<T, T> func, string end, bool wrap = false, IList<string> prefix = null) where T : class
        {
            Write(open);
            if (wrap)
                _indentLevel++;

            var list = new List<T>();
            var last = args.Count - 1;
            var changed = false;
            for (var i = 0; i < args.Count; i++)
            {
                if (wrap)
                    WriteLine();
                if (prefix != null)
                    Write(prefix[i]);
                var arg = func(args[i]);
                changed |= arg != args[i];
                list.Add(arg);
                if (i != last)
                    Write(wrap ? "," : ", ");
            }
            if (wrap)
            {
                _indentLevel--;
                WriteLine();
            }
            Write(end);
            return changed ? list : args;
        }

        protected override ElementInit VisitElementInit(ElementInit node)
        {
            if (node.Arguments.Count == 1)
            {
                var arg = Visit(node.Arguments[0]);
                var args = arg != node.Arguments[0] ? new[] { arg }.AsEnumerable() : node.Arguments;
                return node.Update(args);
            }
            else
            {
                var list = VisitArguments("{", node.Arguments, Visit, "}");
                return node.Update(list);
            }
        }

        private Dictionary<string, int> _counter;
        private Dictionary<object, int> _ids;
        private string GetName(string type, object obj)
        {
            if (_ids == null)
                _ids = new Dictionary<object, int>();

            if (_ids.TryGetValue(obj, out int id))
                return type + id;

            if (_counter == null)
                _counter = new Dictionary<string, int>();
            _counter.TryGetValue(type, out id);
            id++;
            _counter[type] = id;
            _ids[obj] = id;
            return type + id;
        }

        private string GetConstant(object obj, string typeName = null)
        {
            if (_constants == null)
                _constants = new Dictionary<object, string>();

            if (_constants.TryGetValue(obj, out var name))
                return name;

            if (_counter == null)
                _counter = new Dictionary<string, int>();

            if (typeName == null)
                typeName = GetVarName(obj.GetType().Name);

            _counter.TryGetValue(typeName, out var id);
            id++;
            _counter[typeName] = id;
            name = typeName + id;
            _constants[obj] = name;
            return name;
        }

        private string GetName(LabelTarget label)
        {
            return string.IsNullOrEmpty(label.Name) ? GetName("label", label) : label.Name;
        }

        private string GetName(ParameterExpression param)
        {
            if (string.IsNullOrEmpty(param.Name))
                return GetName("p", param);
            else if (ReservedWords.Contains(param.Name))
                return "@" + param.Name;
            else
                return param.Name;
        }

        private string GetName(LambdaExpression lambda)
        {
            return string.IsNullOrEmpty(lambda.Name) ? GetName("func", lambda) : lambda.Name;
        }

        protected override Expression VisitGoto(GotoExpression node)
        {
            switch (node.Kind)
            {
                case GotoExpressionKind.Goto:
                    Write("goto ", GetName(node.Target));
                    break;
                case GotoExpressionKind.Return:
                    var value = Visit("return ", node.Value);
                    return node.Update(node.Target, value);
                case GotoExpressionKind.Break:
                    Write("break");
                    break;
                case GotoExpressionKind.Continue:
                    Write("continue");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return node;
        }

        private Expression VisitMember(Expression instance, Expression node, MemberInfo member)
        {
            if (instance != null)
            {
                var result = VisitGroup(instance, node.NodeType);
                Write(".", member.Name);
                return result;
            }
            else
            {
                Write(Translate(member.DeclaringType), ".", member.Name);
                return null;
            }
        }

        protected override Expression VisitIndex(IndexExpression node)
        {
            var obj = node.Indexer != null && node.Indexer.DeclaringType.GetCustomAttribute<DefaultMemberAttribute>()?.MemberName != node.Indexer.Name
                ? VisitMember(node.Object, node, node.Indexer)
                : VisitGroup(node.Object, node.NodeType);

            var args = VisitArguments("[", node.Arguments, Visit, "]");

            return node.Update(obj, args);
        }

        protected override Expression VisitInvocation(InvocationExpression node)
        {
            var exp = VisitGroup(node.Expression, node.NodeType);
            var args = VisitArguments("(", node.Arguments, Visit, ")");
            return node.Update(exp, args);
        }

        protected override Expression VisitLabel(LabelExpression node)
        {
            Write(GetName(node.Target), ":");
            return node;
        }

        private ParameterExpression VisitParameterDeclaration(ParameterExpression node)
        {
            if (node.Type.IsByRef)
                Write("ref ");
            return (ParameterExpression)Visit(Translate(node.Type) + " ", node);
        }

        private void WriteModifier(bool isPublic)
        {
            WriteNextLine(isPublic ? "public " : "private ");
            if (Definitions?.IsStatic == true)
                Write("static ");
        }

        private Expression VisitLambda(LambdaExpression node, LambdaType type)
        {
            if (type == LambdaType.Function || type == LambdaType.Main)
            {
                var name = type == LambdaType.Main
                    ? (Definitions?.MethodName ?? "Main")
                    : GetName(node);
                WriteModifier(type == LambdaType.Main);
                Write(Translate(node.ReturnType), " ", name);
                var args = VisitArguments("(", node.Parameters, VisitParameterDeclaration, ")");
                Indent();
                var body = VisitBody(node.Body, true);

                Outdent();

                return Expression.Lambda(body, name, node.TailCall, args);
            }
            else
            {
                IList<ParameterExpression> args;
                if (node.Parameters.Count == 1)
                {
                    args = new List<ParameterExpression>();
                    var arg = VisitParameter(node.Parameters[0]);
                    args.Add((ParameterExpression)arg);
                }
                else
                {
                    args = VisitArguments("(", node.Parameters.ToList(), p => (ParameterExpression)VisitParameter(p), ")");
                }
                Write(" => ");
                var body = VisitGroup(node.Body, ExpressionType.Quote);
                return Expression.Lambda(body, node.Name, node.TailCall, args);
            }
        }

        private HashSet<LambdaExpression> _visitedLambda;
        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            Write(GetName(node));

            if (_visitedLambda == null)
                _visitedLambda = new HashSet<LambdaExpression>();
            if (_visitedLambda.Contains(node))
                return node;
            _visitedLambda.Add(node);

            //switch writer to append writer
            if (_appendWriter == null)
                _appendWriter = new StringWriter();

            var temp = _writer;
            var oldIndent = _indentLevel;
            try
            {
                _writer = _appendWriter;
                ResetIndentLevel();

                WriteLine();
                return VisitLambda(node, LambdaType.Function);
            }
            finally
            {
                //switch back
                _writer = temp;
                _indentLevel = oldIndent;
            }
        }

        private IList<T> VisitElements<T>(IList<T> list, Func<T, T> func) where T : class
        {
            var wrap = true;
            if (list.Count == 0)
            {
                wrap = false;
            }
            else if (list.Count <= 4)
            {
                var init = list[0] as MemberBinding;
                wrap = init != null && list.Count > 1;
            }
            if (wrap)
                WriteLine();
            else
                Write(" ");
            return VisitArguments("{", list, func, "}", wrap);
        }

        protected override Expression VisitListInit(ListInitExpression node)
        {
            var @new = (NewExpression)Visit(node.NewExpression);
            var args = VisitElements(node.Initializers, VisitElementInit);
            return node.Update(@new, args);
        }

        protected override Expression VisitLoop(LoopExpression node)
        {
            Expression body;
            if (node.Body.NodeType == ExpressionType.Conditional)
            {
                var condExpr = (ConditionalExpression)node.Body;

                if (condExpr.IfFalse is GotoExpression @break && @break.Target == node.BreakLabel)
                {
                    WriteNextLine("while (");
                    var test = Visit(condExpr.Test);
                    Write(")");
                    Indent();
                    body = VisitBody(condExpr.IfTrue);
                    Outdent();
                    var outBreak = CreateBlock(@break);

                    Expression condition = Expression.Condition(test, body, outBreak, typeof(void));
                    condition = CreateBlock(condition);
                    return Expression.Loop(
                        condition,
                        node.BreakLabel,
                        node.ContinueLabel);
                }
            }

            WriteNextLine("while (true)");
            Indent();
            body = VisitBody(node.Body);
            Outdent();
            return Expression.Loop(body, node.BreakLabel, node.ContinueLabel);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var expr = VisitMember(node.Expression, node, node.Member);
            return node.Update(expr);
        }

        protected override MemberAssignment VisitMemberAssignment(MemberAssignment node)
        {
            Write(node.Member.Name, " = ");
            var expr = Visit(node.Expression);
            return node.Update(expr);
        }

        protected override Expression VisitMemberInit(MemberInitExpression node)
        {
            var @new = (NewExpression)Visit(node.NewExpression);
            var args = VisitElements(node.Bindings, VisitMemberBinding);
            return node.Update(@new, args);
        }

        protected override MemberListBinding VisitMemberListBinding(MemberListBinding node)
        {
            Write(node.Member.Name, " =");
            var args = VisitElements(node.Initializers, VisitElementInit);
            return node.Update(args);
        }

        protected override MemberMemberBinding VisitMemberMemberBinding(MemberMemberBinding node)
        {
            Write(node.Member.Name, " =");
            var args = VisitElements(node.Bindings, VisitMemberBinding);
            return node.Update(args);
        }

        private static Type GetDelegateType(MethodInfo method)
        {
            if (method.GetParameters().Any(it => it.IsOut || it.ParameterType.IsByRef))
                throw new InvalidOperationException("Cannot handle non-public method");

            if (method.ReturnType == typeof(void))
            {
                switch (method.GetParameters().Length)
                {
                    case 0: return typeof(Action);
                    case 1: return typeof(Action<>);
                    case 2: return typeof(Action<,>);
                    case 3: return typeof(Action<,,>);
                    case 4: return typeof(Action<,,,>);
                    case 5: return typeof(Action<,,,,>);
                    case 6: return typeof(Action<,,,,,>);
                    case 7: return typeof(Action<,,,,,,>);
                    case 8: return typeof(Action<,,,,,,,>);
                    case 9: return typeof(Action<,,,,,,,,>);
                    case 10: return typeof(Action<,,,,,,,,,>);
                    case 11: return typeof(Action<,,,,,,,,,,>);
                    case 12: return typeof(Action<,,,,,,,,,,,>);
                    case 13: return typeof(Action<,,,,,,,,,,,,>);
                    case 14: return typeof(Action<,,,,,,,,,,,,,>);
                    case 15: return typeof(Action<,,,,,,,,,,,,,,>);
                    case 16: return typeof(Action<,,,,,,,,,,,,,,,>);
                    default: throw new InvalidOperationException("Cannot handle non-public method");
                }
            }
            else
            {
                switch (method.GetParameters().Length)
                {
                    case 0: return typeof(Func<>);
                    case 1: return typeof(Func<,>);
                    case 2: return typeof(Func<,,>);
                    case 3: return typeof(Func<,,,>);
                    case 4: return typeof(Func<,,,,>);
                    case 5: return typeof(Func<,,,,,>);
                    case 6: return typeof(Func<,,,,,,>);
                    case 7: return typeof(Func<,,,,,,,>);
                    case 8: return typeof(Func<,,,,,,,,>);
                    case 9: return typeof(Func<,,,,,,,,,>);
                    case 10: return typeof(Func<,,,,,,,,,,>);
                    case 11: return typeof(Func<,,,,,,,,,,,>);
                    case 12: return typeof(Func<,,,,,,,,,,,,>);
                    case 13: return typeof(Func<,,,,,,,,,,,,,>);
                    case 14: return typeof(Func<,,,,,,,,,,,,,,>);
                    case 15: return typeof(Func<,,,,,,,,,,,,,,,>);
                    case 16: return typeof(Func<,,,,,,,,,,,,,,,,>);
                    default: throw new InvalidOperationException("Cannot handle non-public method");
                }
            }
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var isExtension = false;
            var isNotPublic = false;
            Expression arg0 = null;

            var obj = node.Object;
            if (obj != null)
            {
                obj = VisitGroup(node.Object, node.NodeType);
            }
#if !NET40
            else if (!node.Method.IsPublic || node.Method.DeclaringType?.GetTypeInfo().IsNotPublic == true)
            {
                isNotPublic = true;
                var del = GetDelegateType(node.Method);
                if (del.GetTypeInfo().IsGenericTypeDefinition)
                {
                    var types = node.Method.GetParameters().Select(it => it.ParameterType);
                    if (node.Method.ReturnType != typeof(void))
                        types = types.Concat(new[] { node.Method.ReturnType });
                    del = del.MakeGenericType(types.ToArray());
                }
                var func = node.Method.CreateDelegate(del);
                Write(GetConstant(func, GetVarName(node.Method.Name)), ".Invoke");
            }
#endif
            else if (node.Method.GetCustomAttribute<ExtensionAttribute>() != null)
            {
                isExtension = true;
                arg0 = VisitGroup(node.Arguments[0], node.NodeType);
                Translate(node.Method.DeclaringType);   //execute type without print
            }
            else if (node.Method.DeclaringType != null)
            {
                Write(Translate(node.Method.DeclaringType));
            }

            if (node.Method.IsSpecialName && node.Method.Name.StartsWith("get_"))
            {
                var attr = node.Method.DeclaringType.GetCustomAttribute<DefaultMemberAttribute>();
                if (attr?.MemberName == node.Method.Name.Substring(4))
                {
                    var keys = VisitArguments("[", node.Arguments, Visit, "]");
                    return node.Update(obj, keys);
                }
            }

            if (!isNotPublic)
            {
                if (node.Method.DeclaringType != null)
                    Write(".");
                Write(node.Method.Name);
                if (node.Method.IsGenericMethod)
                {
                    var args = string.Join(", ", node.Method.GetGenericArguments().Select(Translate));
                    Write("<", args, ">");
                }
            }
            var prefix = node.Method.GetParameters()
                .Select(p => p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "");

            if (isExtension)
            {
                var args = VisitArguments("(", node.Arguments.Skip(1).ToList(), Visit, ")", prefix: prefix.Skip(1).ToList());
                var newArgs = new[] { arg0 }.Concat(args).ToList();
                return newArgs.SequenceEqual(node.Arguments) ? node : node.Update(obj, newArgs);
            }
            else
            {
                var args = VisitArguments("(", node.Arguments, Visit, ")", prefix: prefix.ToList());
                return node.Update(obj, args);
            }
        }

        protected override Expression VisitNew(NewExpression node)
        {
            Write("new ", Translate(node.Type));
            var args = VisitArguments("(", node.Arguments, Visit, ")");
            return node.Update(args);
        }

        protected override Expression VisitNewArray(NewArrayExpression node)
        {
            if (node.NodeType == ExpressionType.NewArrayBounds)
            {
                var elemType = node.Type.GetElementType();
                var arrayCount = 1;
                while (elemType.IsArray)
                {
                    elemType = elemType.GetElementType();
                    arrayCount++;
                }
                Write("new ", Translate(elemType));
                var args = VisitArguments("[", node.Expressions, Visit, "]");
                for (int i = 1; i < arrayCount; i++)
                    Write("[]");
                return node.Update(args);
            }
            else
            {
                Write("new ", Translate(node.Type));
                var args = VisitElements(node.Expressions, Visit);
                return node.Update(args);
            }
        }

        #region _reservedWords
        private static readonly HashSet<string> ReservedWords = new HashSet<string>
        {
            "abstract",
            "as",
            "base",
            "bool",
            "break",
            "by",
            "byte",
            "case",
            "catch",
            "char",
            "checked",
            "class",
            "const",
            "continue",
            "decimal",
            "default",
            "delegate",
            "descending",
            "do",
            "double",
            "else",
            "enum",
            "event",
            "explicit",
            "extern",
            "finally",
            "fixed",
            "float",
            "for",
            "foreach",
            "from",
            "goto",
            "group",
            "if",
            "implicit",
            "in",
            "int",
            "interface",
            "internal",
            "into",
            "is",
            "lock",
            "long",
            "namespace",
            "new",
            "null",
            "object",
            "operator",
            "orderby",
            "out",
            "override",
            "params",
            "private",
            "protected",
            "public",
            "readonly",
            "ref",
            "return",
            "sbyte",
            "sealed",
            "select",
            "short",
            "sizeof",
            "stackalloc",
            "static",
            "string",
            "struct",
            "switch",
            "this",
            "throw",
            "try",
            "typeof",
            "ulong",
            "unchecked",
            "unit",
            "unsafe",
            "ushort",
            "using",
            "var",
            "virtual",
            "void",
            "volatile",
            "where",
            "while",
            "yield",
            "false",
            "true",
        };
        #endregion

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return VisitParameter(node, true);
        }

        private HashSet<ParameterExpression> _pendingVariables;
        private Dictionary<ParameterExpression, ParameterExpression> _params;
        private Expression VisitParameter(ParameterExpression node, bool write)
        {
            if (_pendingVariables == null)
                _pendingVariables = new HashSet<ParameterExpression>();

            var name = GetName(node);
            if (write)
            {
                if (_pendingVariables.Contains(node))
                {
                    Write(Translate(node.Type), " ", name);
                    _pendingVariables.Remove(node);
                }
                else
                {
                    Write(name);
                }
            }
            else
            {
                _pendingVariables.Add(node);
            }

            if (!string.IsNullOrEmpty(node.Name))
                return node;

            if (_params == null)
                _params = new Dictionary<ParameterExpression, ParameterExpression>();
            if (!_params.TryGetValue(node, out var result))
            {
                result = Expression.Parameter(node.Type, name);
                _params[node] = result;
            }
            return result;
        }

        protected override Expression VisitRuntimeVariables(RuntimeVariablesExpression node)
        {
            Write(GetConstant(node, "RuntimeVariables"));
            return node;
        }

        private Expression VisitSwitch(SwitchExpression node, bool shouldReturn)
        {
            WriteNextLine("switch (");
            var value = Visit(node.SwitchValue);
            Write(")");
            Indent();

            var cases = node.Cases.Select(c => VisitSwitchCase(c, shouldReturn)).ToList();
            var @default = node.DefaultBody;
            if (@default != null)
            {
                WriteNextLine("default:");
                _indentLevel++;
                @default = VisitBody(node.DefaultBody, shouldReturn);
                if (!shouldReturn)
                {
                    WriteLine();
                    Write("break;");
                }
                _indentLevel--;
                Outdent();
                @default = CreateBlock(@default);
            }
            else
            {
                Outdent();
            }

            node = node.Update(value, cases, @default);
            return CreateBlock(node);
        }

        private static BlockExpression CreateBlock(params Expression[] exprs)
        {
            return Expression.Block(exprs.Where(expr => expr != null));
        }

        protected override Expression VisitSwitch(SwitchExpression node)
        {
            return VisitSwitch(node, false);
        }

        private SwitchCase VisitSwitchCase(SwitchCase node, bool shouldReturn)
        {
            var values = node.TestValues.Select(test => VisitNextLine("case ", test, ":")).ToList();
            _indentLevel++;
            var body = VisitBody(node.Body, shouldReturn);
            if (!shouldReturn)
            {
                WriteLine();
                Write("break;");
                body = CreateBlock(body);
            }
            _indentLevel--;
            return node.Update(values, body);
        }

        protected override SwitchCase VisitSwitchCase(SwitchCase node)
        {
            return VisitSwitchCase(node, false);
        }

        private Expression VisitTry(TryExpression node, bool shouldReturn)
        {
            string faultParam = null;
            if (node.Fault != null)
            {
                faultParam = GetName("fault", node);
                WriteNextLine("bool ", faultParam, " = true;");
            }
            WriteNextLine("try");
            Indent();
            var body = VisitBody(node.Body, shouldReturn);
            if (node.Fault != null)
                WriteNextLine(faultParam, " = false;");
            Outdent();
            var handlers = node.Handlers.Select(c => VisitCatchBlock(c, shouldReturn)).ToList();
            var @finally = node.Finally;
            var fault = node.Fault;
            if (node.Finally != null || node.Fault != null)
            {
                WriteNextLine("finally");
                Indent();
                if (node.Finally != null)
                    @finally = VisitBody(node.Finally);
                if (node.Fault != null)
                {
                    WriteNextLine("if (", faultParam, ")");
                    Indent();
                    fault = VisitBody(node.Fault);
                    Outdent();
                }
                Outdent();
            }
            return node.Update(body, handlers, @finally, fault);
        }

        protected override Expression VisitTry(TryExpression node)
        {
            return VisitTry(node, false);
        }

        protected override Expression VisitTypeBinary(TypeBinaryExpression node)
        {
            var expr = VisitGroup(node.Expression, node.NodeType);
            Write(" is ", Translate(node.TypeOperand));
            return node.Update(expr);
        }

        private Expression VisitUnary(Expression operand, ExpressionType nodeType)
        {
            switch (nodeType)
            {
                case ExpressionType.IsFalse:
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked:
                case ExpressionType.Not:
                case ExpressionType.PreDecrementAssign:
                case ExpressionType.PreIncrementAssign:
                case ExpressionType.OnesComplement:
                case ExpressionType.UnaryPlus:
                    Write(Translate(nodeType));
                    break;
            }

            var result = VisitGroup(operand, nodeType);

            switch (nodeType)
            {
                case ExpressionType.ArrayLength:
                case ExpressionType.Decrement:
                case ExpressionType.Increment:
                case ExpressionType.PostDecrementAssign:
                case ExpressionType.PostIncrementAssign:
                    Write(Translate(nodeType));
                    break;
            }
            return result;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            switch (node.NodeType)
            {
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                    //if (!node.Type.IsAssignableFrom(node.Operand.Type))
                    Write("(", Translate(node.Type), ")");
                    break;

                case ExpressionType.Throw:
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                    // ReSharper disable HeuristicUnreachableCode
                    if (node.Operand == null)
                    {
                        Write("throw");
                        return node;
                    }
                    // ReSharper restore HeuristicUnreachableCode
                    Write("throw ");
                    break;
            }

            var operand = node.NodeType == ExpressionType.Quote && node.Operand.NodeType == ExpressionType.Lambda
                ? VisitLambda((LambdaExpression)node.Operand, LambdaType.Inline)
                : VisitUnary(node.Operand, node.NodeType);

            switch (node.NodeType)
            {
                case ExpressionType.TypeAs:
                    Write(" as ", Translate(node.Type));
                    break;
            }
            return node.Update(operand);
        }

        public override string ToString()
        {
            var codeWriter = new StringWriter();
            var temp = _writer;
            var oldIndent = _indentLevel;
            _indentLevel = 0;

            try
            {
                _writer = codeWriter;

                //exercise to update _usings
                var implements = Definitions?.Implements?.OrderBy(it => it.GetTypeInfo().IsInterface ? 1 : 0)
                    .Select(Translate)
                    .ToList();
                var constants = _constants?.OrderBy(it => it.Value)
                    .Select(kvp => $"{Translate(kvp.Key.GetType())} {kvp.Value};")
                    .ToList();

                if (Definitions?.TypeName != null)
                {
                    if (_usings != null)
                    {
                        var namespaces = _usings
                            .OrderBy(it => it == "System" || it.StartsWith("System.") ? 0 : 1)
                            .ThenBy(it => it);
                        foreach (var ns in namespaces)
                        {
                            WriteNextLine("using ", ns, ";");
                        }
                        WriteLine();
                    }
                    if (_typeNames != null)
                    {
                        var names = _typeNames.Where(kvp => GetTypeName(kvp.Key) != kvp.Value)
                            .OrderBy(kvp => kvp.Value.StartsWith("System.") ? 0 : 1)
                            .ThenBy(kvp => kvp.Value);
                        foreach (var name in names)
                        {
                            WriteNextLine("using ", name.Value, " = ", name.Key.FullName, ";");
                        }
                        WriteLine();
                    }
                    if (Definitions?.Namespace != null)
                    {
                        WriteNextLine("namespace ", Definitions.Namespace);
                        Indent();
                    }

                    WriteModifier(true);
                    Write("class ", Definitions.TypeName);
                    if (implements?.Any() == true)
                    {
                        Write(" : ", string.Join(", ", implements));
                    }
                    Indent();
                }
                if (constants != null)
                {
                    foreach (var constant in constants)
                    {
                        WriteModifier(true);
                        Write(constant);
                    }
                    WriteLine();
                }
                _writer.Write(temp);
                if (_appendWriter != null)
                    _writer.Write(_appendWriter);
                if (Definitions?.TypeName != null)
                {
                    Outdent();
                    if (Definitions?.Namespace != null)
                        Outdent();
                }
                return _writer.ToString();
            }
            finally
            {
                _writer = temp;
                _indentLevel = oldIndent;
            }
        }

        private enum LambdaType
        {
            Main,
            Inline,
            Function,
        }
    }
}
