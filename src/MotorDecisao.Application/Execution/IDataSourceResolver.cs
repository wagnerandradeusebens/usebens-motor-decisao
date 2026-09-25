using MotorDecisao.Application.Formulas;

namespace MotorDecisao.Application.Execution;

/// <summary>
/// Resolves an external data source (credit bureau, internal API, own database)
/// requested by a <c>DataSource</c> node, returning fields to merge into the
/// evaluation context so later nodes can use them.
///
/// This is the extension point for real integrations. The default implementation
/// is a no-op, so flows with DataSource nodes run today without external calls;
/// concrete resolvers are added in a later phase.
/// </summary>
public interface IDataSourceResolver
{
    /// <summary>
    /// Fetches fields for the given source. Receives the current context so a
    /// resolver can use already-known values (e.g. a document number) as input.
    /// Returns the fields to add/overwrite in the context.
    /// </summary>
    Task<IReadOnlyDictionary<string, FormulaValue>> ResolveAsync(
        DataSourceConfig config,
        IFormulaContext currentContext,
        CancellationToken cancellationToken = default);
}

/// <summary>Default no-op resolver: returns no fields.</summary>
public sealed class NoOpDataSourceResolver : IDataSourceResolver
{
    private static readonly IReadOnlyDictionary<string, FormulaValue> Empty =
        new Dictionary<string, FormulaValue>();

    public Task<IReadOnlyDictionary<string, FormulaValue>> ResolveAsync(
        DataSourceConfig config,
        IFormulaContext currentContext,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Empty);
}
