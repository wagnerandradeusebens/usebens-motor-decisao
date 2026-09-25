using System.Globalization;
using System.Text;

namespace MotorDecisao.Application.Formulas.Parsing;

/// <summary>
/// Renders an AST node back to its source-like text (e.g. <c>'renda' * 0.8</c>).
/// Used by the tracing evaluator to label each resolution step.
/// </summary>
public sealed class FormulaTextRenderer : IFormulaNodeVisitor<string>
{
    private static readonly FormulaTextRenderer Instance = new();

    public static string Render(FormulaNode node) => node.Accept(Instance);

    public string VisitLiteral(LiteralNode node) => node.Value.Type switch
    {
        FormulaValueType.Text => $"\"{node.Value.AsText()}\"",
        FormulaValueType.Number => node.Value.AsNumber().ToString(CultureInfo.InvariantCulture),
        _ => node.Value.ToString()
    };

    public string VisitField(FieldNode node) => $"'{node.Name}'";

    public string VisitVariableRef(VariableRefNode node) => $"{{{node.Name}}}";

    public string VisitExternalRef(ExternalRefNode node) => $"[{node.Source};{node.Product};{node.Datum}]";

    public string VisitPolicyRef(PolicyRefNode node) => string.IsNullOrEmpty(node.Variable)
        ? $"$[{node.Policy};{node.Category}]"
        : $"$[{node.Policy};{node.Category};{node.Variable}]";

    public string VisitUnary(UnaryNode node)
    {
        var op = node.Operator == UnaryOperator.Negate ? "-" : "+";
        return $"{op}{node.Operand.Accept(this)}";
    }

    public string VisitBinary(BinaryNode node)
    {
        var op = node.Operator switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Power => "^",
            BinaryOperator.Concat => "&",
            BinaryOperator.Equal => "=",
            BinaryOperator.NotEqual => "<>",
            BinaryOperator.Less => "<",
            BinaryOperator.LessOrEqual => "<=",
            BinaryOperator.Greater => ">",
            BinaryOperator.GreaterOrEqual => ">=",
            _ => "?"
        };
        return $"{node.Left.Accept(this)} {op} {node.Right.Accept(this)}";
    }

    public string VisitFunction(FunctionNode node)
    {
        var sb = new StringBuilder(node.Name).Append('(');
        for (var i = 0; i < node.Arguments.Count; i++)
        {
            if (i > 0) sb.Append("; ");
            sb.Append(node.Arguments[i].Accept(this));
        }
        return sb.Append(')').ToString();
    }
}
