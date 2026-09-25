using System.Text.Json;
using System.Text.Json.Serialization;
using MotorDecisao.Application.PublishedFlows;
using MotorDecisao.Domain.Enums;
using Xunit;

namespace MotorDecisao.Application.Tests.PublishedFlows;

/// <summary>
/// The Redis cache stores <see cref="PublishedFlowSnapshot"/> as JSON, so it must
/// round-trip losslessly with enums written as strings. This guards the cache's
/// serialization contract without needing a live Redis.
/// </summary>
public class SnapshotSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static PublishedFlowSnapshot SampleSnapshot() => new(
        FlowId: Guid.NewGuid(),
        FlowName: "Política Exemplo",
        FlowVersionId: Guid.NewGuid(),
        VersionNumber: 3,
        PublishedAt: new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc),
        Nodes: new[]
        {
            new PublishedNode("start", FlowNodeKind.Start, "Início", "{}", null),
            new PublishedNode("cond", FlowNodeKind.Condition, "Maior de idade", "{\"expression\":\"'idade' >= 18\"}", null),
            new PublishedNode("note", FlowNodeKind.Comment, "anotação", "{\"text\":\"oi\"}", null),
            new PublishedNode("ap", FlowNodeKind.Decision, "Aprovar", "{\"outcome\":\"Approved\"}", null)
        },
        Edges: new[]
        {
            new PublishedEdge("e1", "start", "cond", null, null),
            new PublishedEdge("e2", "cond", "ap", "true", "sim")
        },
        Rulesets: new[]
        {
            new PublishedRuleset(Guid.NewGuid(), "Scorecard", 50m, new[]
            {
                new PublishedRule(Guid.NewGuid(), 1, "Idade", "'idade' >= 18", RuleEffect.Score, 20m, null, "ok"),
                new PublishedRule(Guid.NewGuid(), 2, "Bloqueio", "'restricao' = VERDADEIRO", RuleEffect.Decision, 0m, DecisionOutcome.Denied, "negado")
            })
        },
        Formulas: new[]
        {
            new PublishedFormula("fator", "Fator", "'renda' * 0.3"),
            new PublishedFormula("idade_minima", "Idade mínima", "18")
        });

    [Fact]
    public void Snapshot_round_trips_through_json()
    {
        var original = SampleSnapshot();

        var json = JsonSerializer.Serialize(original, Options);
        var back = JsonSerializer.Deserialize<PublishedFlowSnapshot>(json, Options);

        Assert.NotNull(back);
        Assert.Equal(original.FlowId, back!.FlowId);
        Assert.Equal(original.FlowName, back.FlowName);
        Assert.Equal(original.FlowVersionId, back.FlowVersionId);
        Assert.Equal(original.VersionNumber, back.VersionNumber);
        Assert.Equal(original.PublishedAt, back.PublishedAt);

        Assert.Equal(original.Nodes.Count, back.Nodes.Count);
        Assert.Equal(FlowNodeKind.Comment, back.Nodes[2].Kind);
        Assert.Equal("{\"text\":\"oi\"}", back.Nodes[2].Config);

        Assert.Equal("true", back.Edges[1].SourceHandle);

        var rs = Assert.Single(back.Rulesets);
        Assert.Equal(2, rs.Rules.Count);
        Assert.Equal(RuleEffect.Decision, rs.Rules[1].Effect);
        Assert.Equal(DecisionOutcome.Denied, rs.Rules[1].ForcedOutcome);

        Assert.Equal(2, back.Formulas.Count);
        Assert.Equal("'renda' * 0.3", back.Formulas[0].Expression);
    }

    [Fact]
    public void Enums_are_written_as_strings_not_numbers()
    {
        var json = JsonSerializer.Serialize(SampleSnapshot(), Options);

        // Enum values should appear by name, so the JSON stays readable/stable.
        Assert.Contains("\"Start\"", json);
        Assert.Contains("\"Condition\"", json);
        Assert.Contains("\"Comment\"", json);
        Assert.Contains("\"Denied\"", json);
    }
}
