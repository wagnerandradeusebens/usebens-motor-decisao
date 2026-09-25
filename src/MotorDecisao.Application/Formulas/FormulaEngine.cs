using MotorDecisao.Application.Formulas.Parsing;

namespace MotorDecisao.Application.Formulas;

/// <summary>
/// Public entry point of the Excel-like formula engine. Compile an expression
/// once into a <see cref="CompiledFormula"/>, then evaluate it against proposal
/// data as many times as needed.
///
/// The language is pt-BR flavoured: the argument separator is <c>;</c>, booleans
/// are <c>VERDADEIRO</c>/<c>FALSO</c>, and function names are Portuguese
/// (<c>SE</c>, <c>E</c>, <c>OU</c>, <c>SOMA</c>, <c>ARRED</c>, <c>DATADIF</c>, ...).
/// </summary>
public static class FormulaEngine
{
    /// <summary>
    /// Parses and compiles an expression. Throws <see cref="FormulaException"/>
    /// with a Portuguese message and source position on a syntax error.
    /// </summary>
    public static CompiledFormula Compile(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new FormulaException("Fórmula vazia.", 0);
        }

        var ast = Parser.Parse(expression);
        var (fields, external, variables, policies) = FieldCollector.Collect(ast);
        return new CompiledFormula(ast, fields, external, variables, policies);
    }

    /// <summary>
    /// Attempts to compile an expression, returning <c>false</c> and the error
    /// instead of throwing. Handy for validating user input in the editor.
    /// </summary>
    public static bool TryCompile(string expression, out CompiledFormula? formula, out FormulaException? error)
    {
        try
        {
            formula = Compile(expression);
            error = null;
            return true;
        }
        catch (FormulaException ex)
        {
            formula = null;
            error = ex;
            return false;
        }
    }

    /// <summary>Convenience: compile and evaluate in one call.</summary>
    public static FormulaValue Evaluate(string expression, IFormulaContext context)
        => Compile(expression).Evaluate(context);
}
