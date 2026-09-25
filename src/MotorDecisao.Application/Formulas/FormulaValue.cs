using System.Globalization;

namespace MotorDecisao.Application.Formulas;

/// <summary>
/// The kinds of error a formula can produce, mirroring Excel's error values so
/// they are familiar to business users writing rules.
/// </summary>
public enum FormulaErrorKind
{
    /// <summary>Division by zero: <c>#DIV/0!</c>.</summary>
    DivByZero,

    /// <summary>Wrong type / uncoercible value: <c>#VALOR!</c>.</summary>
    Value,

    /// <summary>Unknown function or name: <c>#NOME?</c>.</summary>
    Name,

    /// <summary>Invalid field reference: <c>#REF!</c>.</summary>
    Reference,

    /// <summary>Invalid numeric result (e.g. sqrt of negative): <c>#NUM!</c>.</summary>
    Number,

    /// <summary>Value not available: <c>#N/D</c>.</summary>
    NotAvailable
}

/// <summary>
/// The runtime type tag of a <see cref="FormulaValue"/>.
/// </summary>
public enum FormulaValueType
{
    Number,
    Text,
    Boolean,
    Date,
    Error,
    /// <summary>An empty/blank cell, distinct from an empty string or zero.</summary>
    Blank
}

/// <summary>
/// An immutable, typed value flowing through the formula engine. Numbers use
/// <see cref="decimal"/> for financial precision. Errors are values too, so they
/// can propagate through an expression exactly like Excel.
/// </summary>
public readonly struct FormulaValue : IEquatable<FormulaValue>
{
    public FormulaValueType Type { get; }

    private readonly decimal _number;
    private readonly string? _text;
    private readonly bool _boolean;
    private readonly DateTime _date;
    private readonly FormulaErrorKind _error;

    private FormulaValue(
        FormulaValueType type,
        decimal number = 0m,
        string? text = null,
        bool boolean = false,
        DateTime date = default,
        FormulaErrorKind error = default)
    {
        Type = type;
        _number = number;
        _text = text;
        _boolean = boolean;
        _date = date;
        _error = error;
    }

    // --- Factories --------------------------------------------------------

    public static FormulaValue Number(decimal value) => new(FormulaValueType.Number, number: value);
    public static FormulaValue Text(string value) => new(FormulaValueType.Text, text: value);
    public static FormulaValue Boolean(bool value) => new(FormulaValueType.Boolean, boolean: value);
    public static FormulaValue Date(DateTime value) => new(FormulaValueType.Date, date: value.Date);
    public static FormulaValue Error(FormulaErrorKind kind) => new(FormulaValueType.Error, error: kind);
    public static readonly FormulaValue Blank = new(FormulaValueType.Blank);

    // --- Accessors --------------------------------------------------------

    public bool IsError => Type == FormulaValueType.Error;
    public bool IsBlank => Type == FormulaValueType.Blank;
    public FormulaErrorKind ErrorKind => _error;

    public decimal AsNumber() => _number;
    public string AsText() => _text ?? string.Empty;
    public bool AsBoolean() => _boolean;
    public DateTime AsDate() => _date;

    /// <summary>
    /// Renders the value the way it would display in a cell. Errors render with
    /// their Excel-like token (pt-BR variants).
    /// </summary>
    public override string ToString() => Type switch
    {
        FormulaValueType.Number => _number.ToString(CultureInfo.InvariantCulture),
        FormulaValueType.Text => _text ?? string.Empty,
        FormulaValueType.Boolean => _boolean ? "VERDADEIRO" : "FALSO",
        FormulaValueType.Date => _date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        FormulaValueType.Blank => string.Empty,
        FormulaValueType.Error => _error switch
        {
            FormulaErrorKind.DivByZero => "#DIV/0!",
            FormulaErrorKind.Value => "#VALOR!",
            FormulaErrorKind.Name => "#NOME?",
            FormulaErrorKind.Reference => "#REF!",
            FormulaErrorKind.Number => "#NUM!",
            FormulaErrorKind.NotAvailable => "#N/D",
            _ => "#ERRO!"
        },
        _ => string.Empty
    };

    public bool Equals(FormulaValue other)
    {
        if (Type != other.Type)
        {
            return false;
        }

        return Type switch
        {
            FormulaValueType.Number => _number == other._number,
            FormulaValueType.Text => string.Equals(_text, other._text, StringComparison.Ordinal),
            FormulaValueType.Boolean => _boolean == other._boolean,
            FormulaValueType.Date => _date == other._date,
            FormulaValueType.Error => _error == other._error,
            FormulaValueType.Blank => true,
            _ => false
        };
    }

    public override bool Equals(object? obj) => obj is FormulaValue other && Equals(other);

    public override int GetHashCode() => Type switch
    {
        FormulaValueType.Number => _number.GetHashCode(),
        FormulaValueType.Text => _text?.GetHashCode(StringComparison.Ordinal) ?? 0,
        FormulaValueType.Boolean => _boolean.GetHashCode(),
        FormulaValueType.Date => _date.GetHashCode(),
        FormulaValueType.Error => _error.GetHashCode(),
        _ => 0
    };
}
