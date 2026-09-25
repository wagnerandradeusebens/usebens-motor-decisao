namespace MotorDecisao.Application.Formulas;

/// <summary>
/// Funções da linguagem que consultam tabelas de parâmetros e, por isso, precisam
/// do <see cref="IFormulaContext"/> (diferente das funções puras do
/// <c>FunctionLibrary</c>). São despachadas pelos avaliadores no VisitFunction.
///
/// - <c>PROCV("tabela"; "coluna_retorno"; chave)</c> — busca exata pela coluna-chave.
/// - <c>PROCV.FAIXA("tabela"; "coluna_retorno"; valor)</c> — busca pela faixa (min ≤ v &lt; max).
/// </summary>
public static class TableFunctions
{
    public const string Exact = "PROCV";
    public const string Range = "PROCV.FAIXA";

    public static bool IsTableFunction(string name)
        => string.Equals(name, Exact, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, Range, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Executa PROCV/PROCV.FAIXA. Os 3 argumentos já vêm avaliados: nome da tabela
    /// (texto), coluna de retorno (texto) e a chave/valor de busca. Assinatura
    /// inválida → <c>#VALOR!</c>.
    /// </summary>
    public static FormulaValue Invoke(string name, IReadOnlyList<FormulaValue> args, IFormulaContext context)
    {
        if (args.Count != 3)
        {
            return FormulaValue.Error(FormulaErrorKind.Value);
        }

        var tableName = FormulaCoercion.ToText(args[0]);
        if (tableName.IsError) return tableName;
        var returnColumn = FormulaCoercion.ToText(args[1]);
        if (returnColumn.IsError) return returnColumn;

        var byRange = string.Equals(name, Range, StringComparison.OrdinalIgnoreCase);
        return context.ResolveTable(tableName.AsText(), returnColumn.AsText(), args[2], byRange);
    }
}
