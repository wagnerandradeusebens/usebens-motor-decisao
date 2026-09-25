namespace MotorDecisao.Application.Formulas;

/// <summary>
/// Supplies the values that formula field references resolve to at evaluation
/// time. In the engine these fields are the proposal's data (age, income, ...)
/// plus any values computed by earlier nodes in the flow.
///
/// Field names are matched case-insensitively, matching business-user
/// expectations. A reference to an unknown field is not an error: it resolves to
/// <see cref="FormulaValue.Blank"/>, mirroring how an empty spreadsheet cell
/// behaves. This keeps policies resilient when an optional field is absent.
/// </summary>
public interface IFormulaContext
{
    /// <summary>
    /// Attempts to resolve a field by name. Returns <c>false</c> when the field is
    /// not present; the evaluator then treats it as blank.
    /// </summary>
    bool TryGetField(string name, out FormulaValue value);

    /// <summary>
    /// Resolves an external reference <c>[Fonte;Produto;Dado]</c>. Because formula
    /// evaluation is synchronous, external values are expected to be pre-fetched
    /// (the execution layer resolves them asynchronously before evaluating) and
    /// looked up here. Returns <c>#N/D</c> when a value was not pre-fetched.
    /// </summary>
    FormulaValue ResolveExternal(string source, string product, string datum);

    /// <summary>
    /// Resolves a variable reference <c>{nome}</c>. Variables are pre-resolved in
    /// dependency order before evaluation. Returns <c>#N/D</c> when the variable
    /// has not been resolved (e.g. unknown name).
    /// </summary>
    FormulaValue ResolveVariable(string name);
}

/// <summary>
/// A simple dictionary-backed context. Convenient for tests and for adapting a
/// proposal payload into the engine.
/// </summary>
public sealed class DictionaryFormulaContext : IFormulaContext
{
    private readonly Dictionary<string, FormulaValue> _fields;
    private readonly Dictionary<string, FormulaValue> _external =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FormulaValue> _variables =
        new(StringComparer.OrdinalIgnoreCase);

    public DictionaryFormulaContext(IEnumerable<KeyValuePair<string, FormulaValue>>? fields = null)
    {
        _fields = new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase);
        if (fields is not null)
        {
            foreach (var kv in fields)
            {
                _fields[kv.Key] = kv.Value;
            }
        }
    }

    public DictionaryFormulaContext Set(string name, FormulaValue value)
    {
        _fields[name] = value;
        return this;
    }

    /// <summary>Stores a pre-fetched external value for <c>[source;product;datum]</c>.</summary>
    public DictionaryFormulaContext SetExternal(string source, string product, string datum, FormulaValue value)
    {
        _external[ExternalKey(source, product, datum)] = value;
        return this;
    }

    public bool TryGetField(string name, out FormulaValue value)
        => _fields.TryGetValue(name, out value);

    public FormulaValue ResolveExternal(string source, string product, string datum)
        => _external.TryGetValue(ExternalKey(source, product, datum), out var v)
            ? v
            : FormulaValue.Error(FormulaErrorKind.NotAvailable);

    /// <summary>Stores a resolved variable value for <c>{name}</c>.</summary>
    public DictionaryFormulaContext SetVariable(string name, FormulaValue value)
    {
        _variables[name] = value;
        return this;
    }

    public FormulaValue ResolveVariable(string name)
        => _variables.TryGetValue(name, out var v)
            ? v
            : FormulaValue.Error(FormulaErrorKind.NotAvailable);

    private static string ExternalKey(string source, string product, string datum)
        => $"{source}\u0001{product}\u0001{datum}";
}
