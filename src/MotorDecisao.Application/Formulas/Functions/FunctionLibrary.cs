using System.Globalization;

namespace MotorDecisao.Application.Formulas.Functions;

/// <summary>
/// The catalogue of Portuguese formula functions. Each function receives its
/// arguments already evaluated to <see cref="FormulaValue"/>s, except for the
/// short-circuiting logical functions which are handled specially by the
/// evaluator (SE, E, OU, SEERRO) so they can avoid evaluating branches/errors.
///
/// Function names are matched case-insensitively. Names use the Excel pt-BR
/// spellings business users know (SE, SOMA, ARRED, ARREDONDAR.PARA.CIMA, ...).
/// </summary>
public static class FunctionLibrary
{
    /// <summary>Function names that require lazy (short-circuit) evaluation.</summary>
    public static readonly IReadOnlySet<string> ShortCircuit =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SE", "E", "OU", "SEERRO" };

    private static readonly Dictionary<string, Func<IReadOnlyList<FormulaValue>, FormulaValue>> Eager =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["NAO"] = Not,
            // Math
            ["ARRED"] = Round,
            ["ABS"] = Abs,
            ["MINIMO"] = Min,
            ["MENOR"] = Min,      // convenience alias
            ["MAXIMO"] = Max,
            ["MAIOR"] = Max,      // convenience alias
            ["SOMA"] = Sum,
            ["ARREDONDAR.PARA.BAIXO"] = RoundDown,
            ["ARREDONDAR.PARA.CIMA"] = RoundUp,
            ["RESTO"] = Mod,
            ["RAIZ"] = Sqrt,
            ["RAIZCUBICA"] = Cbrt,
            ["TRUNCAR"] = Truncate,
            // Text
            ["CONCATENAR"] = Concat,
            ["CONCAT"] = Concat,
            ["NUM.CARACT"] = Len,
            ["MAIUSCULA"] = Upper,
            ["MINUSCULA"] = Lower,
            ["ARRUMAR"] = Trim,
            ["ESQUERDA"] = Left,
            ["DIREITA"] = Right,
            // Date
            ["HOJE"] = Today,
            ["ANO"] = Year,
            ["MES"] = Month,
            ["DIA"] = Day,
            ["DATADIF"] = DateDif,
            // Info
            ["EHNUM"] = IsNumber,
            ["EHBRANCO"] = IsBlank,
        };

    public static bool IsKnown(string name) => ShortCircuit.Contains(name) || Eager.ContainsKey(name);

    /// <summary>
    /// Invokes an eager function by name. Returns <c>#NOME?</c> for unknown names.
    /// Any argument that is an error short-circuits to that error, matching Excel.
    /// </summary>
    public static FormulaValue Invoke(string name, IReadOnlyList<FormulaValue> args)
    {
        foreach (var a in args)
        {
            if (a.IsError) return a;
        }

        return Eager.TryGetValue(name, out var fn)
            ? fn(args)
            : FormulaValue.Error(FormulaErrorKind.Name);
    }

    // --- Logical ----------------------------------------------------------

    private static FormulaValue Not(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 1) return FormulaValue.Error(FormulaErrorKind.Value);
        var b = FormulaCoercion.ToBoolean(a[0]);
        return b.IsError ? b : FormulaValue.Boolean(!b.AsBoolean());
    }

    // --- Math -------------------------------------------------------------

    private static FormulaValue Round(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 2) return FormulaValue.Error(FormulaErrorKind.Value);
        var value = FormulaCoercion.ToNumber(a[0]);
        var digits = FormulaCoercion.ToNumber(a[1]);
        if (value.IsError) return value;
        if (digits.IsError) return digits;

        var d = (int)digits.AsNumber();
        if (d < 0 || d > 28) return FormulaValue.Error(FormulaErrorKind.Number);
        return FormulaValue.Number(Math.Round(value.AsNumber(), d, MidpointRounding.AwayFromZero));
    }

    private static FormulaValue Abs(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 1) return FormulaValue.Error(FormulaErrorKind.Value);
        var n = FormulaCoercion.ToNumber(a[0]);
        return n.IsError ? n : FormulaValue.Number(Math.Abs(n.AsNumber()));
    }

    private static FormulaValue Min(IReadOnlyList<FormulaValue> a) => Aggregate(a, isMin: true);
    private static FormulaValue Max(IReadOnlyList<FormulaValue> a) => Aggregate(a, isMin: false);

    private static FormulaValue Aggregate(IReadOnlyList<FormulaValue> a, bool isMin)
    {
        if (a.Count == 0) return FormulaValue.Error(FormulaErrorKind.Value);
        decimal? acc = null;
        foreach (var v in a)
        {
            var n = FormulaCoercion.ToNumber(v);
            if (n.IsError) return n;
            var x = n.AsNumber();
            acc = acc is null ? x : (isMin ? Math.Min(acc.Value, x) : Math.Max(acc.Value, x));
        }
        return FormulaValue.Number(acc!.Value);
    }

    private static FormulaValue Sum(IReadOnlyList<FormulaValue> a)
    {
        decimal total = 0m;
        foreach (var v in a)
        {
            var n = FormulaCoercion.ToNumber(v);
            if (n.IsError) return n;
            total += n.AsNumber();
        }
        return FormulaValue.Number(total);
    }

    private static FormulaValue RoundDown(IReadOnlyList<FormulaValue> a) => DirectionalRound(a, up: false);
    private static FormulaValue RoundUp(IReadOnlyList<FormulaValue> a) => DirectionalRound(a, up: true);

    private static FormulaValue DirectionalRound(IReadOnlyList<FormulaValue> a, bool up)
    {
        if (a.Count is < 1 or > 2) return FormulaValue.Error(FormulaErrorKind.Value);
        var value = FormulaCoercion.ToNumber(a[0]);
        if (value.IsError) return value;
        var digits = 0;
        if (a.Count == 2)
        {
            var d = FormulaCoercion.ToNumber(a[1]);
            if (d.IsError) return d;
            digits = (int)d.AsNumber();
            if (digits < 0 || digits > 28) return FormulaValue.Error(FormulaErrorKind.Number);
        }

        var factor = (decimal)Math.Pow(10, digits);
        var scaled = value.AsNumber() * factor;
        var result = up ? Math.Ceiling(scaled) : Math.Floor(scaled);
        return FormulaValue.Number(result / factor);
    }

    private static FormulaValue Mod(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 2) return FormulaValue.Error(FormulaErrorKind.Value);
        var x = FormulaCoercion.ToNumber(a[0]);
        var y = FormulaCoercion.ToNumber(a[1]);
        if (x.IsError) return x;
        if (y.IsError) return y;
        if (y.AsNumber() == 0m) return FormulaValue.Error(FormulaErrorKind.DivByZero);
        return FormulaValue.Number(x.AsNumber() % y.AsNumber());
    }

    /// <summary>RAIZ(número): square root. Negative input is #NUM!.</summary>
    private static FormulaValue Sqrt(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 1) return FormulaValue.Error(FormulaErrorKind.Value);
        var n = FormulaCoercion.ToNumber(a[0]);
        if (n.IsError) return n;
        var x = n.AsNumber();
        if (x < 0m) return FormulaValue.Error(FormulaErrorKind.Number);
        return FormulaValue.Number((decimal)Math.Sqrt((double)x));
    }

    /// <summary>RAIZCUBICA(número): cube root. Defined for negative inputs too.</summary>
    private static FormulaValue Cbrt(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 1) return FormulaValue.Error(FormulaErrorKind.Value);
        var n = FormulaCoercion.ToNumber(a[0]);
        if (n.IsError) return n;
        return FormulaValue.Number((decimal)Math.Cbrt((double)n.AsNumber()));
    }

    /// <summary>
    /// TRUNCAR works on numbers or text.
    /// - Number: TRUNCAR(número[; casas]) drops digits beyond <c>casas</c> (default 0)
    ///   without rounding, keeping the sign (e.g. TRUNCAR(3,9)=3, TRUNCAR(-3,9)=-3).
    /// - Text: TRUNCAR(texto; n) keeps the first <c>n</c> characters.
    /// The second argument is required for text and optional for numbers.
    /// </summary>
    private static FormulaValue Truncate(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count is < 1 or > 2) return FormulaValue.Error(FormulaErrorKind.Value);

        // Text truncation when the first argument is genuine text (not a number
        // that merely coerces from a numeric string).
        if (a[0].Type == FormulaValueType.Text)
        {
            if (a.Count != 2) return FormulaValue.Error(FormulaErrorKind.Value);
            var n = FormulaCoercion.ToNumber(a[1]);
            if (n.IsError) return n;
            var count = (int)n.AsNumber();
            if (count < 0) return FormulaValue.Error(FormulaErrorKind.Value);
            var s = a[0].AsText();
            return FormulaValue.Text(count >= s.Length ? s : s[..count]);
        }

        // Numeric truncation.
        var value = FormulaCoercion.ToNumber(a[0]);
        if (value.IsError) return value;
        var digits = 0;
        if (a.Count == 2)
        {
            var d = FormulaCoercion.ToNumber(a[1]);
            if (d.IsError) return d;
            digits = (int)d.AsNumber();
            if (digits < 0 || digits > 28) return FormulaValue.Error(FormulaErrorKind.Number);
        }

        var factor = (decimal)Math.Pow(10, digits);
        var scaled = value.AsNumber() * factor;
        var truncated = Math.Truncate(scaled); // toward zero, no rounding
        return FormulaValue.Number(truncated / factor);
    }

    // --- Text -------------------------------------------------------------

    private static FormulaValue Concat(IReadOnlyList<FormulaValue> a)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var v in a)
        {
            var t = FormulaCoercion.ToText(v);
            if (t.IsError) return t;
            sb.Append(t.AsText());
        }
        return FormulaValue.Text(sb.ToString());
    }

    private static FormulaValue Len(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 1) return FormulaValue.Error(FormulaErrorKind.Value);
        var t = FormulaCoercion.ToText(a[0]);
        return t.IsError ? t : FormulaValue.Number(t.AsText().Length);
    }

    private static FormulaValue Upper(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 1) return FormulaValue.Error(FormulaErrorKind.Value);
        var t = FormulaCoercion.ToText(a[0]);
        return t.IsError ? t : FormulaValue.Text(t.AsText().ToUpperInvariant());
    }

    private static FormulaValue Lower(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 1) return FormulaValue.Error(FormulaErrorKind.Value);
        var t = FormulaCoercion.ToText(a[0]);
        return t.IsError ? t : FormulaValue.Text(t.AsText().ToLowerInvariant());
    }

    private static FormulaValue Trim(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 1) return FormulaValue.Error(FormulaErrorKind.Value);
        var t = FormulaCoercion.ToText(a[0]);
        return t.IsError ? t : FormulaValue.Text(t.AsText().Trim());
    }

    private static FormulaValue Left(IReadOnlyList<FormulaValue> a) => SidePart(a, fromLeft: true);
    private static FormulaValue Right(IReadOnlyList<FormulaValue> a) => SidePart(a, fromLeft: false);

    private static FormulaValue SidePart(IReadOnlyList<FormulaValue> a, bool fromLeft)
    {
        if (a.Count is < 1 or > 2) return FormulaValue.Error(FormulaErrorKind.Value);
        var t = FormulaCoercion.ToText(a[0]);
        if (t.IsError) return t;
        var count = 1;
        if (a.Count == 2)
        {
            var n = FormulaCoercion.ToNumber(a[1]);
            if (n.IsError) return n;
            count = (int)n.AsNumber();
            if (count < 0) return FormulaValue.Error(FormulaErrorKind.Value);
        }
        var s = t.AsText();
        count = Math.Min(count, s.Length);
        return FormulaValue.Text(fromLeft ? s[..count] : s[(s.Length - count)..]);
    }

    // --- Date -------------------------------------------------------------

    private static FormulaValue Today(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 0) return FormulaValue.Error(FormulaErrorKind.Value);
        return FormulaValue.Date(DateTime.UtcNow.Date);
    }

    private static FormulaValue Year(IReadOnlyList<FormulaValue> a) => DatePart(a, d => d.Year);
    private static FormulaValue Month(IReadOnlyList<FormulaValue> a) => DatePart(a, d => d.Month);
    private static FormulaValue Day(IReadOnlyList<FormulaValue> a) => DatePart(a, d => d.Day);

    private static FormulaValue DatePart(IReadOnlyList<FormulaValue> a, Func<DateTime, int> part)
    {
        if (a.Count != 1) return FormulaValue.Error(FormulaErrorKind.Value);
        var d = AsDate(a[0]);
        return d is null ? FormulaValue.Error(FormulaErrorKind.Value) : FormulaValue.Number(part(d.Value));
    }

    /// <summary>
    /// DATADIF(start; end; unit) where unit is "Y"/"M"/"D" (anos/meses/dias).
    /// The go-to for computing age or time-on-book in credit policies.
    /// </summary>
    private static FormulaValue DateDif(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 3) return FormulaValue.Error(FormulaErrorKind.Value);
        var start = AsDate(a[0]);
        var end = AsDate(a[1]);
        var unitVal = FormulaCoercion.ToText(a[2]);
        if (start is null || end is null || unitVal.IsError) return FormulaValue.Error(FormulaErrorKind.Value);
        if (end.Value < start.Value) return FormulaValue.Error(FormulaErrorKind.Number);

        var s = start.Value;
        var e = end.Value;
        var unit = unitVal.AsText().Trim().ToUpperInvariant();
        return unit switch
        {
            "D" => FormulaValue.Number((decimal)(e - s).Days),
            "M" => FormulaValue.Number((e.Year - s.Year) * 12 + (e.Month - s.Month) - (e.Day < s.Day ? 1 : 0)),
            "Y" => FormulaValue.Number(YearsBetween(s, e)),
            _ => FormulaValue.Error(FormulaErrorKind.Value)
        };
    }

    private static int YearsBetween(DateTime s, DateTime e)
    {
        var years = e.Year - s.Year;
        if (e.Month < s.Month || (e.Month == s.Month && e.Day < s.Day)) years--;
        return years;
    }

    private static DateTime? AsDate(FormulaValue v)
    {
        if (v.Type == FormulaValueType.Date) return v.AsDate();
        if (v.Type == FormulaValueType.Text &&
            DateTime.TryParse(v.AsText(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.Date;
        }
        return null;
    }

    // --- Info -------------------------------------------------------------

    private static FormulaValue IsNumber(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 1) return FormulaValue.Error(FormulaErrorKind.Value);
        return FormulaValue.Boolean(a[0].Type == FormulaValueType.Number);
    }

    private static FormulaValue IsBlank(IReadOnlyList<FormulaValue> a)
    {
        if (a.Count != 1) return FormulaValue.Error(FormulaErrorKind.Value);
        return FormulaValue.Boolean(a[0].IsBlank);
    }
}
