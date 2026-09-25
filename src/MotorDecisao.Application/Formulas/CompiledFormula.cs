using MotorDecisao.Application.Formulas.Parsing;
using MotorDecisao.Application.Sources;

namespace MotorDecisao.Application.Formulas;

/// <summary>
/// A parsed, ready-to-run formula. Compiling once and evaluating many times is
/// the whole point: this is the "compiled artifact" the published-flow cache
/// holds, so a decision never re-parses expressions on the hot path.
/// </summary>
public sealed class CompiledFormula
{
    private readonly FormulaNode _root;

    internal CompiledFormula(
        FormulaNode root,
        IReadOnlySet<string> referencedFields,
        IReadOnlyList<ExternalRef> externalRefs,
        IReadOnlySet<string> referencedVariables,
        IReadOnlyList<PolicyRef> policyRefs)
    {
        _root = root;
        ReferencedFields = referencedFields;
        ExternalReferences = externalRefs;
        ReferencedVariables = referencedVariables;
        PolicyReferences = policyRefs;
    }

    /// <summary>
    /// The distinct field names this formula reads. Useful for validating a policy
    /// (all referenced fields are provided) and for building input schemas.
    /// </summary>
    public IReadOnlySet<string> ReferencedFields { get; }

    /// <summary>
    /// The distinct external references (<c>[Fonte;Produto;Dado]</c>) this formula
    /// uses. The execution layer pre-fetches these before evaluating.
    /// </summary>
    public IReadOnlyList<ExternalRef> ExternalReferences { get; }

    /// <summary>
    /// The distinct variable references (<c>{nome}</c>) this formula uses. Used to
    /// order variable resolution and to detect cycles.
    /// </summary>
    public IReadOnlySet<string> ReferencedVariables { get; }

    /// <summary>
    /// As referências a outras políticas (<c>(Política;Categoria;Variável)</c>)
    /// que esta fórmula usa. A camada de execução as resolve sob demanda.
    /// </summary>
    public IReadOnlyList<PolicyRef> PolicyReferences { get; }

    /// <summary>Evaluates the formula against the given context.</summary>
    public FormulaValue Evaluate(IFormulaContext context) => Evaluator.Evaluate(_root, context);

    /// <summary>
    /// Evaluates and also returns the full step-by-step resolution tree (every
    /// sub-expression → value), for the detailed execution log.
    /// </summary>
    public (FormulaValue Value, IReadOnlyList<EvalStep> Steps) EvaluateTraced(IFormulaContext context)
        => TracingEvaluator.Evaluate(_root, context);
}

/// <summary>
/// Collects the distinct field names and external references in an AST, ignoring
/// identifiers used as function names.
/// </summary>
internal sealed class FieldCollector : IFormulaNodeVisitor<bool>
{
    private readonly HashSet<string> _fields = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ExternalRef> _external = new();
    private readonly HashSet<string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PolicyRef> _policies = new();

    public static (IReadOnlySet<string> Fields, IReadOnlyList<ExternalRef> External, IReadOnlySet<string> Variables, IReadOnlyList<PolicyRef> Policies) Collect(FormulaNode node)
    {
        var collector = new FieldCollector();
        node.Accept(collector);
        return (collector._fields, collector._external, collector._variables, collector._policies);
    }

    public bool VisitLiteral(LiteralNode node) => true;

    public bool VisitField(FieldNode node)
    {
        _fields.Add(node.Name);
        return true;
    }

    public bool VisitUnary(UnaryNode node) => node.Operand.Accept(this);

    public bool VisitBinary(BinaryNode node)
    {
        node.Left.Accept(this);
        node.Right.Accept(this);
        return true;
    }

    public bool VisitFunction(FunctionNode node)
    {
        foreach (var arg in node.Arguments)
        {
            arg.Accept(this);
        }
        return true;
    }

    public bool VisitExternalRef(ExternalRefNode node)
    {
        var reference = new ExternalRef(node.Source, node.Product, node.Datum);
        if (!_external.Contains(reference))
        {
            _external.Add(reference);
        }
        return true;
    }

    public bool VisitVariableRef(VariableRefNode node)
    {
        _variables.Add(node.Name);
        return true;
    }

    public bool VisitPolicyRef(PolicyRefNode node)
    {
        var reference = new PolicyRef(node.Policy, node.Category, node.Variable);
        if (!_policies.Contains(reference))
        {
            _policies.Add(reference);
        }
        return true;
    }
}
