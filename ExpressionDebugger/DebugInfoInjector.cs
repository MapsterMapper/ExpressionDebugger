using System;
using System.Collections.Generic;
using System.Diagnostics;
#if !NETSTANDARD1_3
using System.Dynamic;
#endif
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace ExpressionDebugger
{
    public class DebugInfoInjector : ExpressionVisitor, IDisposable
    {
        private const int Tabsize = 4;
        private readonly SymbolDocumentInfo _document;
        private TextWriter _writer;
        private int _line = 1;
        private int _column = 1;
        private int _indentLevel;

        private Dictionary<string, int> _counter;
        private Dictionary<object, int> _ids;
        private Dictionary<ParameterExpression, ParameterExpression> _params;
        private TextWriter _appendWriter;
        private HashSet<LambdaExpression> _visitedLambda; 

        public DebugInfoInjector(string filename)
        {
            _document = Expression.SymbolDocument(filename);
            var fs = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(fs);
        }

        public DebugInfoInjector(TextWriter writer)
        {
            _document = null;
            _writer = writer;
        }

        public Expression Inject(Expression node)
        {
            var lambda = node as LambdaExpression;
            var result = node.NodeType == ExpressionType.Lambda 
                ? VisitLambda(lambda, LambdaType.Main) 
                : Visit(node);

            FlushAppendWriter();
            return result;
        }

        void FlushAppendWriter()
        {
            if (_appendWriter != null)
                _writer.Write(_appendWriter);
            _appendWriter = null;
        }

        public void Dispose()
        {
            _writer.Dispose();
        }

        static int GetPrecedence(ExpressionType nodeType)
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

        static bool ShouldGroup(Expression node, ExpressionType parentNodeType, bool isRightNode)
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

        void Write(string text)
        {
            _writer.Write(text);
            _column += text.Length;
        }

        Expression Visit(string open, Expression node, params string[] end)
        {
            Write(open);
            var result = Visit(node);
            Write(end);
            return result;
        }

        void Write(params string[] texts)
        {
            foreach (var text in texts)
            {
                Write(text);
            }
        }

        void WriteLine()
        {
            _writer.WriteLine();
            _line += 1;
            _column = 1;

            var spaceCount = _indentLevel*Tabsize;
            _writer.Write(new string(' ', spaceCount));
            _column += spaceCount;
        }

        void WriteNextLine(string text)
        {
            WriteLine();
            Write(text);
        }

        Expression VisitNextLine(string open, Expression node, params string[] end)
        {
            WriteLine();
            Write(open);
            var result = Visit(node);
            Write(end);
            return result;
        }

        void WriteNextLine(params string[] texts)
        {
            WriteLine();
            foreach (var text in texts)
            {
                Write(text);
            }
        }

        void Indent(bool inline = false)
        {
            if (!inline)
                WriteLine();
            Write("{");
            _indentLevel++;
        }

        void Outdent()
        {
            _indentLevel--;
            WriteNextLine("}");
        }

        Expression VisitGroup(Expression node, ExpressionType parentNodeType, bool isRightNode = false)
        {
            Expression result;
            if (!IsInline(node))
            {
                Indent(true);
                result = VisitMultiline(node, true);
                Outdent();
            }
            else if (ShouldGroup(node, parentNodeType, isRightNode))
                result = Visit("(", node, ")");
            else
                result = Visit(node);
            return result;
        }

        static string Translate(ExpressionType nodeType)
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
                case ExpressionType.Power: return "**";
                case ExpressionType.PowerAssign: return "**=";
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
            else
            {
                left = VisitGroup(node.Left, node.NodeType);
                Write(" ", Translate(node.NodeType), " ");
                right = VisitGroup(node.Right, node.NodeType, true);
            }

            return node.Update(left, node.Conversion, right);
        }

        static string Translate(Type type)
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
#if !NETSTANDARD1_3
            if (typeof(IDynamicMetaObjectProvider).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
                return "dynamic";
#endif

            if (type.IsArray)
            {
                var rank = type.GetArrayRank();
                return Translate(type.GetElementType()) + "[" + new string(',', rank - 1) + "]";
            }

            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
                return Translate(underlyingType) + "?";

            if (type.GetTypeInfo().IsGenericType)
            {
                var name = type.Name;
                var index = name.IndexOf('`');
                name = index == -1 ? name : name.Substring(0, index);
                if (type.GetTypeInfo().IsGenericTypeDefinition)
                {
                    var typeArgs = type.GetGenericArguments();
                    return name + "<" + new string(',', typeArgs.Length - 1) + ">";
                }
                return name + "<" + string.Join(", ", type.GetGenericArguments().Select(Translate)) + ">";
            }

            return type.Name;
        }

        Tuple<int, int> GetPosition()
        {
            return Tuple.Create(_line, _column);
        }

        DebugInfoExpression CreateDebugInfo(Tuple<int, int> position)
        {
            return _document == null
                ? null
                : Expression.DebugInfo(_document, position.Item1, position.Item2, _line, _column);
        }

        static bool IsInline(Expression node)
        {
            switch (node.NodeType)
            {
                case ExpressionType.Conditional:
                    var condExpr = (ConditionalExpression) node;
                    return condExpr.Type != typeof (void) && IsInline(condExpr.IfTrue) && IsInline(condExpr.IfFalse);

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

        Expression VisitMultiline(Expression node, bool shouldReturn)
        {
            switch (node.NodeType)
            {
                case ExpressionType.Block:
                    return VisitBlock((BlockExpression) node, shouldReturn);

                case ExpressionType.Conditional:
                    return VisitConditional((ConditionalExpression)node, shouldReturn);

                case ExpressionType.Try:
                    return VisitTry((TryExpression) node, shouldReturn);

                case ExpressionType.Switch:
                    return VisitSwitch((SwitchExpression) node, shouldReturn);

                //case ExpressionType.DebugInfo:
                //case ExpressionType.Goto:
                //case ExpressionType.Loop:
                default:
                    return Visit(node);
            }
        }

        Expression VisitBody(Expression node, bool shouldReturn = false)
        {
            if (node.NodeType == ExpressionType.Block)
                return VisitBlock((BlockExpression) node, shouldReturn);

            if (node.NodeType == ExpressionType.Default && node.Type == typeof(void))
                return node;

            var lines = VisitBlockBody(new List<Expression> {node}, shouldReturn);
            return Expression.Block(lines);
        }

        IEnumerable<Expression> VisitBlockBody(IList<Expression> exprs, bool shouldReturn)
        {
            var lines = new List<Expression>();
            var last = exprs.Count - 1;
            for (int i = 0; i < exprs.Count; i++)
            {
                var expr = exprs[i];
                var isInline = IsInline(expr);
                if (isInline || i > 0)
                    WriteLine();
                
                var position = GetPosition();

                Expression next;
                if (isInline)
                {
                    if (shouldReturn && i == last)
                        Write("return ");
                    next = Visit(expr);
                    Write(";");
                    var symbol = CreateDebugInfo(position);
                    if (symbol != null)
                        lines.Add(symbol);
                }
                else
                {
                    next = VisitMultiline(expr, shouldReturn && i == last);
                }
                lines.Add(next);
            }
            return lines;
        }

        Expression VisitBlock(BlockExpression node, bool shouldReturn)
        {
            var assignedVariables = node.Expressions
                .Where(exp => exp.NodeType == ExpressionType.Assign)
                .Select(exp => ((BinaryExpression)exp).Left)
                .Where(exp => exp.NodeType == ExpressionType.Parameter)
                .Select(exp => (ParameterExpression)exp)
                .ToHashSet();

            var list = new List<ParameterExpression>();
            var hasDeclaration = false;
            foreach (var variable in node.Variables)
            {
                Expression arg;
                if (assignedVariables.Contains(variable))
                    arg = VisitParameter(variable, false);
                else
                {
                    arg = VisitNextLine(Translate(variable.Type) + " ", variable, ";");
                    hasDeclaration = true;
                }
                list.Add((ParameterExpression)arg);
            }
            if (hasDeclaration)
                WriteLine();

            var lines = VisitBlockBody(node.Expressions, shouldReturn && node.Type != typeof (void));
            return Expression.Block(list, lines);
        }

        protected override Expression VisitBlock(BlockExpression node)
        {
            return VisitBlock(node, false);
        }

        CatchBlock VisitCatchBlock(CatchBlock node, bool shouldReturn)
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
            var result = node.Variable != null
                ? Expression.Catch(node.Variable, body, filter)
                : Expression.Catch(node.Test, body, filter);
            return result;
        }

        protected override CatchBlock VisitCatchBlock(CatchBlock node)
        {
            return VisitCatchBlock(node, false);
        }

        Expression VisitConditional(ConditionalExpression node, bool shouldReturn)
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

        Expression VisitConditionalBlock(ConditionalExpression node, bool shouldReturn, bool chain = false)
        {
            if (chain)
                WriteNextLine("else if (");
            else
                WriteNextLine("if (");
            var position = GetPosition();
            Expression test = Visit(node.Test);
            var debug = CreateDebugInfo(position);
            Write(")");
            Indent();
            Expression ifTrue = VisitBody(node.IfTrue, shouldReturn);
            Outdent();
            Expression ifFalse = node.IfFalse;
            if (node.Type == typeof(void) && node.IfFalse.NodeType != ExpressionType.Default)
            {
                if (node.IfFalse.NodeType == ExpressionType.Conditional)
                    ifFalse = VisitConditionalBlock((ConditionalExpression)node.IfFalse, shouldReturn, true);
                else
                {
                    WriteNextLine("else");
                    Indent();
                    ifFalse = VisitBody(node.IfFalse, shouldReturn);
                    Outdent();
                }
            }

            Expression condition = Expression.Condition(test, ifTrue, ifFalse, typeof(void));
            if (debug != null)
                condition = Expression.Block(debug, condition);
            return condition;
        }

        protected override Expression VisitConditional(ConditionalExpression node)
        {
            return VisitConditional(node, false);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            var value = node.Value;

            if (value == null)
                Write("null");
            else if (value is string)
                Write($"\"{value}\"");
            else if (value is char)
                Write($"\'{value}\'");
            else if (value is bool)
                Write(value.ToString().ToLower());
            else if (value is Type t)
                Write($"typeof({Translate(t)})");
            else
            {
                Type type = value.GetType();
                if (type.GetTypeInfo().IsPrimitive || type == typeof(decimal))
                    Write(value.ToString());
                else
                    Write("valueof(", Translate(type), ")");
            }

            return node;
        }

        protected override Expression VisitDefault(DefaultExpression node)
        {
            Write("default(", Translate(node.Type), ")");
            return node;
        }

#if !NETSTANDARD1_3
        static Expression Update(DynamicExpression node, IEnumerable<Expression> args)
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
                return Update(node, new[] {expr}.Concat(args));
            }
            if (node.Binder is SetIndexBinder)
            {
                var expr = VisitGroup(node.Arguments[0], ExpressionType.Index);
                var args = VisitArguments("[", node.Arguments.Skip(1).Take(node.Arguments.Count - 2).ToList(), Visit, "]");
                Write(" = ");
                var value = VisitGroup(node.Arguments[node.Arguments.Count - 1], ExpressionType.Assign);
                return Update(node, new[] {expr}.Concat(args).Concat(new[] {value}));
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

        IList<T> VisitArguments<T>(string open, IList<T> args, Func<T, T> func, string end, bool wrap = false, IList<string> prefix = null) where T : class 
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
                var args = arg != node.Arguments[0] ? new[] {arg}.AsEnumerable() : node.Arguments;
                return node.Update(args);
            }
            else
            {
                var list = VisitArguments("{", node.Arguments, Visit, "}");
                return node.Update(list);
            }
        }

        string GetName(string type, object obj)
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

        string GetName(LabelTarget label)
        {
            return string.IsNullOrEmpty(label.Name) ? GetName("label", label) : label.Name;
        }

        string GetName(ParameterExpression param)
        {
            if (string.IsNullOrEmpty(param.Name))
                return GetName("p", param);
            else if (ReservedWords.Contains(param.Name))
                return "@" + param.Name;
            else
                return param.Name;
        }

        string GetName(LambdaExpression lambda)
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

        Expression VisitMember(Expression instance, Expression node, MemberInfo member)
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
            var obj = node.Indexer != null 
                ? VisitMember(node.Object, node, node.Indexer) 
                : VisitGroup(node.Object, node.NodeType);

            var args = VisitArguments("[", node.Arguments, Visit, "]");

            //TODO:
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

        ParameterExpression VisitParameterDeclaration(ParameterExpression node)
        {
            if (node.Type.IsByRef)
                Write("ref ");
            return (ParameterExpression)Visit(Translate(node.Type) + " ", node);
        }

        Expression VisitLambda(LambdaExpression node, LambdaType type)
        {
            if (type == LambdaType.Main || type == LambdaType.Function)
            {
                var name = type == LambdaType.Main ? "Main" : GetName(node);
                Write(Translate(node.ReturnType), " ", name);
                var args = VisitArguments("(", node.Parameters, VisitParameterDeclaration, ")");
                Indent();
                var body = VisitBody(node.Body, true);

                if (type == LambdaType.Main)
                    FlushAppendWriter();

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
            _writer = _appendWriter;

            WriteLine();
            WriteLine();
            var result = VisitLambda(node, LambdaType.Function);

            //switch back
            _writer = temp;

            return result;
        }

        IList<T> VisitElements<T>(IList<T> list, Func<T, T> func) where T: class 
        {
            var wrap = true;
            if (list.Count == 0)
                wrap = false;
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
                var condExpr = (ConditionalExpression) node.Body;

                if (condExpr.IfFalse is GotoExpression @break && @break.Target == node.BreakLabel)
                {
                    WriteNextLine("while (");
                    var position = GetPosition();
                    var test = Visit(condExpr.Test);
                    var debug = CreateDebugInfo(position);
                    Write(")");
                    Indent();
                    body = VisitBody(condExpr.IfTrue);
                    Outdent();

                    Expression condition = Expression.Condition(test, body, @break, typeof(void));
                    if (debug != null)
                        condition = Expression.Block(debug, condition);
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
            var @new = (NewExpression) Visit(node.NewExpression);
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

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var isExtension = false;
            Expression arg0 = null;

            var obj = node.Object;
            if (obj != null)
                obj = VisitGroup(node.Object, node.NodeType);
            else if (node.Method.GetCustomAttribute<ExtensionAttribute>() != null)
            {
                isExtension = true;
                arg0 = VisitGroup(node.Arguments[0], node.NodeType);
            }
            else if (node.Method.DeclaringType != null)
                Write(Translate(node.Method.DeclaringType));

            if (node.Method.IsSpecialName && node.Method.Name.StartsWith("get_"))
            {
                var attr = node.Method.DeclaringType.GetCustomAttribute<DefaultMemberAttribute>();
                if (attr?.MemberName == node.Method.Name.Substring(4))
                {
                    var keys = VisitArguments("[", node.Arguments, Visit, "]");
                    return node.Update(obj, keys);
                }
            }

            if (node.Method.DeclaringType != null)
                Write(".");
            Write(node.Method.Name);
            var prefix = node.Method.GetParameters()
                .Select(p => p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "");

            if (isExtension)
            {
                var args = VisitArguments("(", node.Arguments.Skip(1).ToList(), Visit, ")", prefix: prefix.Skip(1).ToList());
                var newArgs = new[] {arg0}.Concat(args).ToList();
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
                Write("new ", Translate(node.Type.GetElementType()));
                var args = VisitArguments("[", node.Expressions, Visit, "]");
                return node.Update(args);
            }
            else
            {
                Write("new[]");
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
            "FALSE",
            "TRUE",
        };
#endregion

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return VisitParameter(node, true);
        }

        HashSet<ParameterExpression> _pendingVariables;
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
                    Write(name);
            }
            else
                _pendingVariables.Add(node);

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
            Write($"valueof(IRuntimeVariables, \"{{{node.Variables.Count}}}\"");
            return node;
        }

        Expression VisitSwitch(SwitchExpression node, bool shouldReturn)
        {
            var value = VisitNextLine("switch (", node.SwitchValue, ")");
            Indent();

            var cases = node.Cases.Select(c => VisitSwitchCase(c, shouldReturn)).ToList();
            var @default = node.DefaultBody;
            if (@default != null)
            {
                WriteNextLine("default:");
                _indentLevel++;
                @default = VisitBody(node.DefaultBody, shouldReturn);
                if (!shouldReturn)
                    WriteNextLine("break;");
                _indentLevel--;
            }
            Outdent();
            return node.Update(value, cases, @default);
        }

        protected override Expression VisitSwitch(SwitchExpression node)
        {
            return VisitSwitch(node, false);
        }

        SwitchCase VisitSwitchCase(SwitchCase node, bool shouldReturn)
        {
            var values = node.TestValues.Select(test => VisitNextLine("case ", test, ":")).ToList();
            _indentLevel++;
            var body = VisitBody(node.Body, shouldReturn);
            if (!shouldReturn)
                WriteNextLine("break;");
            _indentLevel--;
            return node.Update(values, body);
        }

        protected override SwitchCase VisitSwitchCase(SwitchCase node)
        {
            return VisitSwitchCase(node, false);
        }

        Expression VisitTry(TryExpression node, bool shouldReturn)
        {
            WriteNextLine("try");
            Indent();
            var body = VisitBody(node.Body, shouldReturn);
            Outdent();
            var handlers = node.Handlers.Select(c => VisitCatchBlock(c, shouldReturn)).ToList();
            var @finally = node.Finally;
            var fault = node.Fault;
            if (node.Finally != null)
            {
                WriteNextLine("finally");
                Indent();
                @finally = VisitBody(node.Finally);
                Outdent();
            }
            else if (node.Fault != null)
            {
                WriteNextLine("fault");
                Indent();
                fault = VisitBody(node.Fault);
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

        Expression VisitUnary(Expression operand, ExpressionType nodeType)
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
                    if (!node.Type.IsAssignableFrom(node.Operand.Type))
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

        static ModuleBuilder mod;
        public Delegate Compile(LambdaExpression node, AssemblyName an = null)
        {
#if NETSTANDARD1_3
            return node.Compile();
#else

            if (mod == null)
            {
                if (an == null)
                {
                    StrongNameKeyPair kp;
                    // Getting this from a resource would be a good idea.
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ExpressionDebugger.mock.keys"))
                    using (var mem = new MemoryStream())
                    {
                        stream.CopyTo(mem);
                        mem.Position = 0;
                        kp = new StrongNameKeyPair(mem.ToArray());
                    }
                    var name = "ExpressionDebugger.Dynamic";
                    an = new AssemblyName(name) { KeyPair = kp };
                }

                var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);

                var daType = typeof(DebuggableAttribute);
                var daCtor = daType.GetConstructor(new[] { typeof(DebuggableAttribute.DebuggingModes) });
                var daBuilder = new CustomAttributeBuilder(daCtor,
                    new object[] {
                        DebuggableAttribute.DebuggingModes.DisableOptimizations |
                        DebuggableAttribute.DebuggingModes.Default
                    });
                asm.SetCustomAttribute(daBuilder);
                mod = asm.DefineDynamicModule(an.Name, true);
            }

            var type = mod.DefineType("T" + Guid.NewGuid().ToString("N"), TypeAttributes.Public | TypeAttributes.Class);
            var meth = type.DefineMethod("Main", MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static);

            var injected = (LambdaExpression)Inject(node);
            var gen = DebugInfoGenerator.CreatePdbGenerator();
            injected.CompileToMethod(meth, gen);

            var newtype = type.CreateType();

            return Delegate.CreateDelegate(node.Type, newtype.GetMethod("Main"));
#endif
        }

        enum LambdaType
        {
            Main,
            Inline,
            Function,
        }
    }
}
