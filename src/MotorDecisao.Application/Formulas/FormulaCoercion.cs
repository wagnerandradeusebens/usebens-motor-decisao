using System.Globalization;

namespace MotorDecisao.Application.Formulas;

/// <summary>
/// Excel-like type coercions used by the evaluator and the function library.
/// Each returns either a coerced value or a <see cref="FormulaValue.Error"/> so
/// callers can propagate failures uniformly.
/// </summary>
public static class FormulaCoercion
{
    /// <summary>
    /// Coerces a value to a number, following spreadsheet rules: booleans become
    /// 1/0, blanks become 0, numeric text is parsed (accepting both '.' and ','
    /// as the decimal separator for pt-BR friendliness). Non-numeric text yields
    /// <c>#VALOR!</c>.
    /// </summary>
    public static FormulaValue ToNumber(FormulaValue value)
    {
        switch (value.Type)
        {
            case FormulaValueType.Number:
                return value;
            case FormulaValueType.Boolean:
                return FormulaValue.Number(value.AsBoolean() ? 1m : 0m);
            case FormulaValueType.Blank:
                return FormulaValue.Number(0m);
            case FormulaValueType.Date:
                // Serial-like: days since 0001-01-01 is enough for arithmetic/diffs.
                return FormulaValue.Number(value.AsDate().Ticks / (decimal)TimeSpan.TicksPerDay);
            case FormulaValueType.Text:
                return TryParseNumber(value.AsText(), out var n)
                    ? FormulaValue.Number(n)
                    : FormulaValue.Error(FormulaErrorKind.Value);
            case FormulaValueType.Error:
                return value;
            default:
                return FormulaValue.Error(FormulaErrorKind.Value);
        }
    }

    /// <summary>
    /// Coerces a value to a boolean: numbers are true when non-zero, text
    /// "VERDADEIRO"/"FALSO" (case-insensitive) map accordingly, blanks are false.
    /// </summary>
    public static FormulaValue ToBoolean(FormulaValue value)
    {
        switch (value.Type)
        {
            case FormulaValueType.Boolean:
                return value;
            case FormulaValueType.Number:
                return FormulaValue.Boolean(value.AsNumber() != 0m);
            case FormulaValueType.Blank:
                return FormulaValue.Boolean(false);
            case FormulaValueType.Text:
                var t = value.AsText().Trim();
                if (string.Equals(t, "VERDADEIRO", StringComparison.OrdinalIgnoreCase)) return FormulaValue.Boolean(true);
                if (string.Equals(t, "FALSO", StringComparison.OrdinalIgnoreCase)) return FormulaValue.Boolean(false);
                return FormulaValue.Error(FormulaErrorKind.Value);
            case FormulaValueType.Error:
                return value;
            default:
                return FormulaValue.Error(FormulaErrorKind.Value);
        }
    }

    /// <summary>Coerces any value to its text representation.</summary>
    public static FormulaValue ToText(FormulaValue value)
    {
        return value.Type == FormulaValueType.Error ? value : FormulaValue.Text(value.ToString());
    }

    /// <summary>
    /// Parses a numeric literal accepting either '.' or ',' as the decimal mark.
    /// A value containing both is treated using '.' as decimal and ',' as a
    /// thousands separator (e.g. "1,234.56").
    /// </summary>
    public static bool TryParseNumber(string raw, out decimal result)
    {
        var s = raw.Trim();
        if (s.Length == 0)
        {
            result = 0m;
            return false;
        }

        var hasDot = s.Contains('.');
        var hasComma = s.Contains(',');

        if (hasDot && hasComma)
        {
            // Ambiguous: assume '.' decimal, ',' thousands -> strip commas.
            s = s.Replace(",", string.Empty);
        }
        else if (hasComma)
        {
            // pt-BR decimal comma -> normalize to '.'.
            s = s.Replace(',', '.');
        }

        return decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out result);
    }
}
