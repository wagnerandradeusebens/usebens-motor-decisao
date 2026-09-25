using System.Globalization;
using MotorDecisao.Application.PublishedFlows;

namespace MotorDecisao.Application.Formulas;

/// <summary>
/// Lógica de consulta a uma tabela de parâmetros, compartilhada pelo contexto de
/// execução. Implementa a busca exata (PROCV) e por faixa (PROCV.FAIXA), com
/// coerção do valor de retorno ao tipo da coluna e tratamento do valor padrão.
/// </summary>
public static class TableLookup
{
    /// <summary>
    /// Resolve uma consulta contra o conjunto de tabelas semeadas. Regras:
    /// - tabela/coluna inexistente → <c>#NOME?</c>;
    /// - busca exata: casa a coluna-chave da tabela com <paramref name="key"/>;
    /// - busca por faixa: acha a linha com min ≤ valor &lt; max (colunas min/max);
    /// - sem correspondência → valor padrão (coagido) ou <c>#N/D</c>.
    /// </summary>
    public static FormulaValue Resolve(
        IReadOnlyDictionary<string, PublishedTable> tables,
        string tableName,
        string returnColumn,
        FormulaValue key,
        bool byRange)
    {
        if (!tables.TryGetValue(tableName, out var table))
        {
            return FormulaValue.Error(FormulaErrorKind.Name); // tabela desconhecida
        }

        var returnIdx = IndexOfColumn(table, returnColumn);
        if (returnIdx < 0)
        {
            return FormulaValue.Error(FormulaErrorKind.Name); // coluna de retorno desconhecida
        }

        var rowIndex = byRange ? FindRowByRange(table, key) : FindRowByKey(table, key);
        if (rowIndex < 0)
        {
            return DefaultOrNotAvailable(table);
        }

        var raw = ValueAt(table, rowIndex, returnIdx);
        return Coerce(raw, ColumnType(table, returnIdx));
    }

    // --- Busca --------------------------------------------------------------

    private static int FindRowByKey(PublishedTable table, FormulaValue key)
    {
        var keyIdx = IndexOfColumn(table, table.KeyColumn ?? string.Empty);
        if (keyIdx < 0) return -1; // tabela sem coluna-chave definida

        var keyType = ColumnType(table, keyIdx);
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var cell = Coerce(ValueAt(table, r, keyIdx), keyType);
            if (ValuesEqual(cell, key))
            {
                return r;
            }
        }
        return -1;
    }

    private static int FindRowByRange(PublishedTable table, FormulaValue key)
    {
        var minIdx = IndexOfColumn(table, table.MinColumn ?? string.Empty);
        var maxIdx = IndexOfColumn(table, table.MaxColumn ?? string.Empty);
        if (minIdx < 0 && maxIdx < 0) return -1; // sem faixa definida

        var value = FormulaCoercion.ToNumber(key);
        if (value.IsError) return -1;
        var v = value.AsNumber();

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var okMin = true;
            var okMax = true;
            if (minIdx >= 0 && TryNumber(ValueAt(table, r, minIdx), out var min))
            {
                okMin = v >= min;
            }
            if (maxIdx >= 0 && TryNumber(ValueAt(table, r, maxIdx), out var max))
            {
                okMax = v < max; // máximo exclusivo, como na matriz
            }
            if (okMin && okMax)
            {
                return r;
            }
        }
        return -1;
    }

    // --- Auxiliares ---------------------------------------------------------

    private static FormulaValue DefaultOrNotAvailable(PublishedTable table)
        => table.DefaultValue is null
            ? FormulaValue.Error(FormulaErrorKind.NotAvailable)
            : FormulaValue.Text(table.DefaultValue); // padrão devolvido como texto; coerção fica a cargo de quem usa

    private static int IndexOfColumn(PublishedTable table, string name)
    {
        for (var i = 0; i < table.Columns.Count; i++)
        {
            if (string.Equals(table.Columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static string ColumnType(PublishedTable table, int idx)
        => table.Columns[idx].Type;

    private static string ValueAt(PublishedTable table, int row, int col)
        => row < table.Rows.Count && col < table.Rows[row].Count ? table.Rows[row][col] ?? string.Empty : string.Empty;

    private static bool TryNumber(string raw, out decimal value)
        => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    /// <summary>Coage o texto bruto da célula ao tipo declarado da coluna.</summary>
    private static FormulaValue Coerce(string raw, string type)
    {
        switch (type?.Trim().ToLowerInvariant())
        {
            case "number":
                return TryNumber(raw, out var n) ? FormulaValue.Number(n) : FormulaValue.Error(FormulaErrorKind.Value);
            case "boolean":
                var t = raw.Trim().ToLowerInvariant();
                if (t is "verdadeiro" or "true" or "1" or "sim") return FormulaValue.Boolean(true);
                if (t is "falso" or "false" or "0" or "não" or "nao") return FormulaValue.Boolean(false);
                return FormulaValue.Error(FormulaErrorKind.Value);
            case "date":
                return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? FormulaValue.Date(d)
                    : FormulaValue.Error(FormulaErrorKind.Value);
            default:
                return FormulaValue.Text(raw);
        }
    }

    /// <summary>Igualdade tolerante para a busca exata: compara por tipo coagido.</summary>
    private static bool ValuesEqual(FormulaValue cell, FormulaValue key)
    {
        // Se a coluna-chave é numérica, compara numericamente com a chave.
        if (cell.Type == FormulaValueType.Number)
        {
            var k = FormulaCoercion.ToNumber(key);
            return !k.IsError && cell.AsNumber() == k.AsNumber();
        }
        if (cell.Type == FormulaValueType.Boolean)
        {
            var k = FormulaCoercion.ToBoolean(key);
            return !k.IsError && cell.AsBoolean() == k.AsBoolean();
        }
        if (cell.Type == FormulaValueType.Date)
        {
            return key.Type == FormulaValueType.Date && cell.AsDate() == key.AsDate();
        }
        // Texto: comparação case-insensitive com o texto da chave.
        var kt = FormulaCoercion.ToText(key);
        return !kt.IsError && string.Equals(cell.AsText(), kt.AsText(), StringComparison.OrdinalIgnoreCase);
    }
}
