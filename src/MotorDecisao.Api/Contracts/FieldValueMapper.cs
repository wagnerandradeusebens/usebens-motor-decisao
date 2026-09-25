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
            result[key] = Convert(element);
        }
        return result;
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
