using MotorDecisao.Application.Formulas.Functions;
using MotorDecisao.Application.Formulas.Parsing;

namespace MotorDecisao.Application.Formulas;

/// <summary>
/// Evaluates an AST against an <see cref="IFormulaContext"/>, producing a single
/// typed <see cref="FormulaValue"/>. Semantics follow Excel closely: arithmetic in
/// <see cref="decimal"/>, error propagation, string concatenation with <c>&amp;</c>,
/// and short-circuiting for the logical functions.
/// </summary>
public sealed class Evaluator : IFormulaNodeVisitor<FormulaValue>
{
    private readonly IFormulaContext _context;

    private Evaluator(IFormulaContext context) => _context = context;

    public static FormulaValue Evaluate(FormulaNode node, IFormulaContext context)
        => node.Accept(new Evaluator(context));

    public FormulaValue VisitLiteral(LiteralNode node) => node.Value;

    public FormulaValue VisitField(FieldNode node)
    {
        // Unknown field resolves to blank, like an empty cell.
        return _context.TryGetField(node.Name, out var value) ? value : FormulaValue.Blank;
    }

    public FormulaValue VisitUnary(UnaryNode node)
    {
        var operand = node.Operand.Accept(this);
        if (operand.IsError) return operand;

        var n = FormulaCoercion.ToNumber(operand);
        if (n.IsError) return n;

        return node.Operator switch
        {
            UnaryOperator.Negate => FormulaValue.Number(-n.AsNumber()),
            UnaryOperator.Plus => n,
            _ => FormulaValue.Error(FormulaErrorKind.Value)
        };
    }

    public FormulaValue VisitBinary(BinaryNode node)
    {
        var left = node.Left.Accept(this);
        if (left.IsError) return left;
        var right = node.Right.Accept(this);
        if (right.IsError) return right;

        return node.Operator switch
        {
            BinaryOperator.Add => Arith(left, right, (x, y) => x + y),
            BinaryOperator.Subtract => Arith(left, right, (x, y) => x - y),
            BinaryOperator.Multiply => Arith(left, right, (x, y) => x * y),
            BinaryOperator.Divide => Divide(left, right),
            BinaryOperator.Power => Power(left, right),
            BinaryOperator.Concat => Concat(left, right),
            _ => Compare(node.Operator, left, right)
        };
    }

    public FormulaValue VisitExternalRef(ExternalRefNode node)
    {
        // External values are pre-fetched by the execution layer and read here.
        return _context.ResolveExternal(node.Source, node.Product, node.Datum);
    }

    public FormulaValue VisitVariableRef(VariableRefNode node)
    {
        // Variables are pre-resolved (in dependency order) before evaluation.
        return _context.ResolveVariable(node.Name);
    }

    public FormulaValue VisitPolicyRef(PolicyRefNode node)
    {
        // A política alvo é executada pela camada de execução (sob demanda) e o
        // valor é semeado no contexto; aqui apenas lemos.
        return _context.ResolvePolicy(node.Policy, node.Category, node.Variable);
    }

    public FormulaValue VisitFunction(FunctionNode node)
    {
        var name = node.Name;

        // Short-circuiting logical functions need lazy argument evaluation.
        if (FunctionLibrary.ShortCircuit.Contains(name))
        {
            return EvaluateShortCircuit(name, node.Arguments);
        }

        var args = new FormulaValue[node.Arguments.Count];
        for (var i = 0; i < node.Arguments.Count; i++)
        {
            args[i] = node.Arguments[i].Accept(this);
        }

        // PROCV/PROCV.FAIXA consultam uma tabela de parâmetros no contexto (não são
        // funções "puras"). Tratadas aqui, com o contexto disponível.
        if (TableFunctions.IsTableFunction(name))
        {
            return TableFunctions.Invoke(name, args, _context);
        }

        if (!FunctionLibrary.IsKnown(name))
        {
            return FormulaValue.Error(FormulaErrorKind.Name);
        }

        return FunctionLibrary.Invoke(name, args);
    }

    // --- Short-circuit logical functions ----------------------------------

    private FormulaValue EvaluateShortCircuit(string name, IReadOnlyList<FormulaNode> args)
    {
        switch (name.ToUpperInvariant())
        {
            case "SE":
                // SE(condition; then; else?) — evaluate only the taken branch.
                if (args.Count is < 2 or > 3) return FormulaValue.Error(FormulaErrorKind.Value);
                var cond = FormulaCoercion.ToBoolean(args[0].Accept(this));
                if (cond.IsError) return cond;
                if (cond.AsBoolean()) return args[1].Accept(this);
                return args.Count == 3 ? args[2].Accept(this) : FormulaValue.Boolean(false);

            case "E":
                // AND: stop at the first false.
                if (args.Count == 0) return FormulaValue.Error(FormulaErrorKind.Value);
                foreach (var arg in args)
                {
                    var b = FormulaCoercion.ToBoolean(arg.Accept(this));
                    if (b.IsError) return b;
                    if (!b.AsBoolean()) return FormulaValue.Boolean(false);
                }
                return FormulaValue.Boolean(true);

            case "OU":
                // OR: stop at the first true.
                if (args.Count == 0) return FormulaValue.Error(FormulaErrorKind.Value);
                foreach (var arg in args)
                {
                    var b = FormulaCoercion.ToBoolean(arg.Accept(this));
                    if (b.IsError) return b;
                    if (b.AsBoolean()) return FormulaValue.Boolean(true);
                }
                return FormulaValue.Boolean(false);

            case "SEERRO":
                // SEERRO(value; fallback) — evaluate fallback only if value errors.
                if (args.Count != 2) return FormulaValue.Error(FormulaErrorKind.Value);
                var v = args[0].Accept(this);
                return v.IsError ? args[1].Accept(this) : v;

            default:
                return FormulaValue.Error(FormulaErrorKind.Name);
        }
    }

    // --- Operator helpers -------------------------------------------------

    private static FormulaValue Arith(FormulaValue left, FormulaValue right, Func<decimal, decimal, decimal> op)
    {
        var l = FormulaCoercion.ToNumber(left);
        if (l.IsError) return l;
        var r = FormulaCoercion.ToNumber(right);
        if (r.IsError) return r;
        return FormulaValue.Number(op(l.AsNumber(), r.AsNumber()));
    }

    private static FormulaValue Divide(FormulaValue left, FormulaValue right)
    {
        var l = FormulaCoercion.ToNumber(left);
        if (l.IsError) return l;
        var r = FormulaCoercion.ToNumber(right);
        if (r.IsError) return r;
        if (r.AsNumber() == 0m) return FormulaValue.Error(FormulaErrorKind.DivByZero);
        return FormulaValue.Number(l.AsNumber() / r.AsNumber());
    }

    private static FormulaValue Power(FormulaValue left, FormulaValue right)
    {
        var l = FormulaCoercion.ToNumber(left);
        if (l.IsError) return l;
        var r = FormulaCoercion.ToNumber(right);
        if (r.IsError) return r;

        var result = Math.Pow((double)l.AsNumber(), (double)r.AsNumber());
        if (double.IsNaN(result) || double.IsInfinity(result))
        {
            return FormulaValue.Error(FormulaErrorKind.Number);
        }
        return FormulaValue.Number((decimal)result);
    }

    private static FormulaValue Concat(FormulaValue left, FormulaValue right)
    {
        var l = FormulaCoercion.ToText(left);
        if (l.IsError) return l;
        var r = FormulaCoercion.ToText(right);
        if (r.IsError) return r;
        return FormulaValue.Text(l.AsText() + r.AsText());
    }

    private static FormulaValue Compare(BinaryOperator op, FormulaValue left, FormulaValue right)
    {
        var cmp = CompareValues(left, right);
        if (cmp is null)
        {
            // Only equality/inequality is defined across incompatible types.
            return op switch
            {
                BinaryOperator.Equal => FormulaValue.Boolean(left.Equals(right)),
                BinaryOperator.NotEqual => FormulaValue.Boolean(!left.Equals(right)),
                _ => FormulaValue.Error(FormulaErrorKind.Value)
            };
        }

        var c = cmp.Value;
        return op switch
        {
            BinaryOperator.Equal => FormulaValue.Boolean(c == 0),
            BinaryOperator.NotEqual => FormulaValue.Boolean(c != 0),
            BinaryOperator.Less => FormulaValue.Boolean(c < 0),
            BinaryOperator.LessOrEqual => FormulaValue.Boolean(c <= 0),
            BinaryOperator.Greater => FormulaValue.Boolean(c > 0),
            BinaryOperator.GreaterOrEqual => FormulaValue.Boolean(c >= 0),
            _ => FormulaValue.Error(FormulaErrorKind.Value)
        };
    }

    /// <summary>
    /// Orders two values when they are comparable (both numeric/boolean, both text,
    /// or both dates). Returns null when they are not order-comparable.
    /// </summary>
    private static int? CompareValues(FormulaValue left, FormulaValue right)
    {
        // Dates compare as dates.
        if (left.Type == FormulaValueType.Date && right.Type == FormulaValueType.Date)
        {
            return left.AsDate().CompareTo(right.AsDate());
        }

        // Text compares as text (ordinal, case-insensitive like Excel).
        if (left.Type == FormulaValueType.Text && right.Type == FormulaValueType.Text)
        {
            return string.Compare(left.AsText(), right.AsText(), StringComparison.OrdinalIgnoreCase);
        }

        // Otherwise attempt numeric comparison (numbers, booleans, blanks).
        var l = FormulaCoercion.ToNumber(left);
        var r = FormulaCoercion.ToNumber(right);
        if (!l.IsError && !r.IsError)
        {
            return l.AsNumber().CompareTo(r.AsNumber());
        }

        return null;
    }
}
