using System.Globalization;
using System.Text.Json;
using MotorDecisao.Application.Formulas;

namespace MotorDecisao.Api.Contracts;

/// <summary>
/// Converts the loosely-typed JSON field map from a decision request into the
/// engine's typed <see cref="FormulaValue"/>s. JSON numbers become decimals,
/// booleans become booleans, and strings that look like ISO dates become dates
/// (otherwise text).
/// </summary>
public static class FieldValueMapper
{
    public static IReadOnlyDictionary<string, FormulaValue> Map(IReadOnlyDictionary<string, JsonElement>? input)
    {
        var result = new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase);
        if (input is null)
        {
            return result;
        }

        foreach (var (key, element) in input)
        {
            Flatten(key, element, result);
        }
        return result;
    }

    /// <summary>
    /// Achata a request: objetos aninhados viram chaves com caminho pontilhado
    /// (<c>grupo.campo</c>), recursivamente. Assim uma request agrupada por assunto
    /// (proponente, operacao, …) é lida pelo motor como campos <c>'proponente.cpf_cnpj'</c>.
    /// Valores escalares (número, texto, bool, data, null) viram o campo daquele
    /// caminho. Arrays não são suportados neste contrato (achatados como texto cru).
    /// </summary>
    private static void Flatten(string path, JsonElement element, Dictionary<string, FormulaValue> acc)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                Flatten($"{path}.{prop.Name}", prop.Value, acc);
            }
            return;
        }
        acc[path] = Convert(element);
    }

    private static FormulaValue Convert(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => FormulaValue.Number(element.GetDecimal()),
        JsonValueKind.True => FormulaValue.Boolean(true),
        JsonValueKind.False => FormulaValue.Boolean(false),
        JsonValueKind.Null => FormulaValue.Blank,
        JsonValueKind.String => ConvertString(element.GetString()!),
        _ => FormulaValue.Text(element.GetRawText())
    };

    private static FormulaValue ConvertString(string s)
    {
        // ISO date (yyyy-MM-dd) -> Date; keep everything else as text.
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            return FormulaValue.Date(date);
        }
        return FormulaValue.Text(s);
    }
}
