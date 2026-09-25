namespace MotorDecisao.Application.PublishedFlows;

/// <summary>
/// Combines a flow's local variables with the shared global variables into the
/// single list the engine compiles. Local variables take precedence: when a flow
/// defines a variable with the same key as a global one, the local definition
/// wins for that flow (the global is dropped from the merge).
/// </summary>
public static class VariableMerge
{
    /// <summary>
    /// Merges <paramref name="globals"/> and <paramref name="locals"/> with local
    /// precedence. Key comparison is case-insensitive, mirroring how variables are
    /// resolved at evaluation time.
    /// </summary>
    public static IReadOnlyList<PublishedFormula> Merge(
        IReadOnlyList<PublishedFormula> globals,
        IReadOnlyList<PublishedFormula> locals)
    {
        var localKeys = new HashSet<string>(locals.Select(l => l.Key), StringComparer.OrdinalIgnoreCase);

        // Globals first (only those not shadowed by a local), then all locals.
        var merged = new List<PublishedFormula>(globals.Count + locals.Count);
        foreach (var g in globals)
        {
            if (!localKeys.Contains(g.Key))
            {
                merged.Add(g);
            }
        }
        merged.AddRange(locals);
        return merged;
    }
}
