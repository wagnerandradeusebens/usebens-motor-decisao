namespace MotorDecisao.Application.Formulas.Parsing;

/// <summary>Binary operators supported by the language.</summary>
public enum BinaryOperator
{
    Add, Subtract, Multiply, Divide, Power, Concat,
    Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual
}

/// <summary>Unary operators supported by the language.</summary>
public enum UnaryOperator
{
    Negate,   // -x
    Plus      // +x (no-op, kept for fidelity)
}

/// <summary>
/// Base type of the abstract syntax tree. The AST is the "compiled" form of a
/// formula: parse once, evaluate many times. It is walked by the evaluator and
/// can be held in the published-flow cache.
/// </summary>
public abstract class FormulaNode
{
    /// <summary>Accepts a visitor; used by the evaluator and field collector.</summary>
    public abstract T Accept<T>(IFormulaNodeVisitor<T> visitor);
}

public sealed class LiteralNode : FormulaNode
{
    public FormulaValue Value { get; }
    public LiteralNode(FormulaValue value) => Value = value;
    public override T Accept<T>(IFormulaNodeVisitor<T> visitor) => visitor.VisitLiteral(this);
}

public sealed class FieldNode : FormulaNode
{
    public string Name { get; }
    public FieldNode(string name) => Name = name;
    public override T Accept<T>(IFormulaNodeVisitor<T> visitor) => visitor.VisitField(this);
}

public sealed class UnaryNode : FormulaNode
{
    public UnaryOperator Operator { get; }
    public FormulaNode Operand { get; }
    public UnaryNode(UnaryOperator op, FormulaNode operand)
    {
        Operator = op;
        Operand = operand;
    }
    public override T Accept<T>(IFormulaNodeVisitor<T> visitor) => visitor.VisitUnary(this);
}

public sealed class BinaryNode : FormulaNode
{
    public BinaryOperator Operator { get; }
    public FormulaNode Left { get; }
    public FormulaNode Right { get; }
    public BinaryNode(BinaryOperator op, FormulaNode left, FormulaNode right)
    {
        Operator = op;
        Left = left;
        Right = right;
    }
    public override T Accept<T>(IFormulaNodeVisitor<T> visitor) => visitor.VisitBinary(this);
}

public sealed class FunctionNode : FormulaNode
{
    public string Name { get; }
    public IReadOnlyList<FormulaNode> Arguments { get; }
    public FunctionNode(string name, IReadOnlyList<FormulaNode> arguments)
    {
        Name = name;
        Arguments = arguments;
    }
    public override T Accept<T>(IFormulaNodeVisitor<T> visitor) => visitor.VisitFunction(this);
}

/// <summary>
/// An external data reference written as <c>[Fonte;Produto;Dado]</c>, e.g.
/// <c>[SERASA;Score;Pontuacao]</c>. Resolved against the registered source
/// catalog at evaluation time (via the context).
/// </summary>
public sealed class ExternalRefNode : FormulaNode
{
    public string Source { get; }
    public string Product { get; }
    public string Datum { get; }
    public ExternalRefNode(string source, string product, string datum)
    {
        Source = source;
        Product = product;
        Datum = datum;
    }
    public override T Accept<T>(IFormulaNodeVisitor<T> visitor) => visitor.VisitExternalRef(this);
}

/// <summary>
/// A reference to a named variable written as <c>{nome}</c>. Variables are the
/// user-created named formulas; they are resolved (in dependency order) into the
/// context before the referencing formula is evaluated.
/// </summary>
public sealed class VariableRefNode : FormulaNode
{
    public string Name { get; }
    public VariableRefNode(string name) => Name = name;
    public override T Accept<T>(IFormulaNodeVisitor<T> visitor) => visitor.VisitVariableRef(this);
}

/// <summary>
/// Referência a OUTRA política, escrita como <c>(Política;Categoria;Variável)</c>.
/// Categoria ∈ {Pontos, Limite, Resposta, Variaveis}. Quando avaliada, a política
/// alvo é executada (sob demanda) e o valor pedido é extraído do resultado. O
/// campo <see cref="Variable"/> só é usado quando a categoria é "Variaveis".
/// </summary>
public sealed class PolicyRefNode : FormulaNode
{
    public string Policy { get; }
    public string Category { get; }
    public string Variable { get; }
    public PolicyRefNode(string policy, string category, string variable)
    {
        Policy = policy;
        Category = category;
        Variable = variable;
    }
    public override T Accept<T>(IFormulaNodeVisitor<T> visitor) => visitor.VisitPolicyRef(this);
}

/// <summary>Visitor over the AST.</summary>
public interface IFormulaNodeVisitor<out T>
{
    T VisitLiteral(LiteralNode node);
    T VisitField(FieldNode node);
    T VisitUnary(UnaryNode node);
    T VisitBinary(BinaryNode node);
    T VisitFunction(FunctionNode node);
    T VisitExternalRef(ExternalRefNode node);
    T VisitVariableRef(VariableRefNode node);
    T VisitPolicyRef(PolicyRefNode node);
}
