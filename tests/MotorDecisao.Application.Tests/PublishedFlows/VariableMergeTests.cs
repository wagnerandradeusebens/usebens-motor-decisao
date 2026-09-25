using MotorDecisao.Application.PublishedFlows;
using Xunit;

namespace MotorDecisao.Application.Tests.PublishedFlows;

/// <summary>
/// Tests the merge of global and local variables. Locals must win on key
/// collisions (case-insensitive); non-colliding globals must remain available.
/// </summary>
public class VariableMergeTests
{
    private static PublishedFormula F(string key, string expr) => new(key, key, expr);

    [Fact]
    public void Local_variable_shadows_global_of_same_key()
    {
        var globals = new[] { F("taxa", "10"), F("teto", "5000") };
        var locals = new[] { F("taxa", "20") };

        var merged = VariableMerge.Merge(globals, locals);

        // "teto" (global, not shadowed) + "taxa" (local wins).
        Assert.Equal(2, merged.Count);
        var taxa = Assert.Single(merged, m => string.Equals(m.Key, "taxa", System.StringComparison.OrdinalIgnoreCase));
        Assert.Equal("20", taxa.Expression);
        Assert.Contains(merged, m => m.Key == "teto" && m.Expression == "5000");
    }

    [Fact]
    public void Shadowing_is_case_insensitive()
    {
        var globals = new[] { F("Taxa", "10") };
        var locals = new[] { F("taxa", "20") };

        var merged = VariableMerge.Merge(globals, locals);

        var only = Assert.Single(merged);
        Assert.Equal("20", only.Expression);
    }

    [Fact]
    public void Globals_and_locals_without_collision_are_all_kept()
    {
        var globals = new[] { F("g1", "1"), F("g2", "2") };
        var locals = new[] { F("l1", "3") };

        var merged = VariableMerge.Merge(globals, locals);

        Assert.Equal(3, merged.Count);
        Assert.Contains(merged, m => m.Key == "g1");
        Assert.Contains(merged, m => m.Key == "g2");
        Assert.Contains(merged, m => m.Key == "l1");
    }

    [Fact]
    public void Empty_inputs_produce_empty_result()
    {
        var merged = VariableMerge.Merge(System.Array.Empty<PublishedFormula>(), System.Array.Empty<PublishedFormula>());
        Assert.Empty(merged);
    }
}
