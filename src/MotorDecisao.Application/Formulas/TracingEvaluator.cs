using MotorDecisao.Application.Formulas.Functions;
using MotorDecisao.Application.Formulas.Parsing;

namespace MotorDecisao.Application.Formulas;

/// <summary>
/// One recorded step of a formula's evaluation: the sub-expression as text and
/// the value it resolved to. Depth allows the UI to render the resolution as a
/// tree/indentation.
/// </summary>
public sealed record EvalStep(int Depth, string Expression, string Value);

/// <summary>
/// Evaluates an AST like <see cref="Evaluator"/> but also records a full,
/// step-by-step resolution tree (every sub-expression → value). Short-circuit
/// functions (SE/E/OU/SEERRO) only record the branches actually evaluated.
///
/// The recorded steps are ordered so that a node's children appear before the
/// node itself (post-order), which reads naturally as "resolve the parts, then
/// combine". Depth mirrors the AST nesting.
/// </summary>
public sealed class TracingEvaluator : IFormulaNodeVisitor<FormulaValue>
{
    private readonly IFormulaContext _context;
    private readonly List<EvalStep> _steps = new();
    private int _depth;

    private TracingEvaluator(IFormulaContext context) => _context = context;

    /// <summary>Evaluates and returns both the value and the ordered resolution steps.</summary>
    public static (FormulaValue Value, IReadOnlyList<EvalStep> Steps) Evaluate(FormulaNode node, IFormulaContext context)
    {
        var ev = new TracingEvaluator(context);
        var value = node.Accept(ev);
        return (value, ev._steps);
    }

    private FormulaValue Record(FormulaNode node, FormulaValue value)
    {
        _steps.Add(new EvalStep(_depth, FormulaTextRenderer.Render(node), value.ToString()));
        return value;
    }

    public FormulaValue VisitLiteral(LiteralNode node) => Record(node, node.Value);

    public FormulaValue VisitField(FieldNode node)
        => Record(node, _context.TryGetField(node.Name, out var v) ? v : FormulaValue.Blank);

    public FormulaValue VisitVariableRef(VariableRefNode node)
        => Record(node, _context.ResolveVariable(node.Name));

    public FormulaValue VisitExternalRef(ExternalRefNode node)
        => Record(node, _context.ResolveExternal(node.Source, node.Product, node.Datum));

    public FormulaValue VisitUnary(UnaryNode node)
    {
        _depth++;
        var operand = node.Operand.Accept(this);
        _depth--;
        // Reuse the non-tracing evaluator's arithmetic by delegating on the value.
        var result = Evaluator.Evaluate(new LiteralNode(node.Operator == UnaryOperator.Negate
            ? Negate(operand) : operand), _context);
        return Record(node, result);
    }

    public FormulaValue VisitBinary(BinaryNode node)
    {
        _depth++;
        var left = node.Left.Accept(this);
        var right = node.Right.Accept(this);
        _depth--;
        // Compute this operator's result via a tiny AST of literals so the exact
        // same semantics as Evaluator are used (no logic duplication).
        var result = Evaluator.Evaluate(
            new BinaryNode(node.Operator, new LiteralNode(left), new LiteralNode(right)), _context);
        return Record(node, result);
    }

    public FormulaValue VisitFunction(FunctionNode node)
    {
        var name = node.Name;

        // Short-circuit logical functions: only trace branches actually taken.
        if (FunctionLibrary.ShortCircuit.Contains(name))
        {
            return Record(node, EvaluateShortCircuit(name, node.Arguments));
        }

        _depth++;
        var args = new FormulaValue[node.Arguments.Count];
        for (var i = 0; i < node.Arguments.Count; i++)
        {
            args[i] = node.Arguments[i].Accept(this);
        }
        _depth--;

        var result = FunctionLibrary.IsKnown(name)
            ? FunctionLibrary.Invoke(name, args)
            : FormulaValue.Error(FormulaErrorKind.Name);
        return Record(node, result);
    }

    private FormulaValue EvaluateShortCircuit(string name, IReadOnlyList<FormulaNode> args)
    {
        _depth++;
        try
        {
            switch (name.ToUpperInvariant())
            {
                case "SE":
                    if (args.Count is < 2 or > 3) return FormulaValue.Error(FormulaErrorKind.Value);
                    var cond = FormulaCoercion.ToBoolean(args[0].Accept(this));
                    if (cond.IsError) return cond;
                    if (cond.AsBoolean()) return args[1].Accept(this);
                    return args.Count == 3 ? args[2].Accept(this) : FormulaValue.Boolean(false);

                case "E":
                    if (args.Count == 0) return FormulaValue.Error(FormulaErrorKind.Value);
                    foreach (var a in args)
                    {
                        var b = FormulaCoercion.ToBoolean(a.Accept(this));
                        if (b.IsError) return b;
                        if (!b.AsBoolean()) return FormulaValue.Boolean(false);
                    }
                    return FormulaValue.Boolean(true);

                case "OU":
                    if (args.Count == 0) return FormulaValue.Error(FormulaErrorKind.Value);
                    foreach (var a in args)
                    {
                        var b = FormulaCoercion.ToBoolean(a.Accept(this));
                        if (b.IsError) return b;
                        if (b.AsBoolean()) return FormulaValue.Boolean(true);
                    }
                    return FormulaValue.Boolean(false);

                case "SEERRO":
                    if (args.Count != 2) return FormulaValue.Error(FormulaErrorKind.Value);
                    var v = args[0].Accept(this);
                    return v.IsError ? args[1].Accept(this) : v;

                default:
                    return FormulaValue.Error(FormulaErrorKind.Name);
            }
        }
        finally
        {
            _depth--;
        }
    }

    private static FormulaValue Negate(FormulaValue v)
    {
        var n = FormulaCoercion.ToNumber(v);
        return n.IsError ? n : FormulaValue.Number(-n.AsNumber());
    }
}
