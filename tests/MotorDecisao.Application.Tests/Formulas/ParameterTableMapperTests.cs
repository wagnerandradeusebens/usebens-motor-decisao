using MotorDecisao.Application.PublishedFlows;
using MotorDecisao.Domain.Entities;
using Xunit;

namespace MotorDecisao.Application.Tests.Formulas;

/// <summary>
/// Testes de mapeamento (jsonb → PublishedTable) e merge local+global (local
/// vence), que alimentam o snapshot congelado.
/// </summary>
public class ParameterTableMapperTests
{
    [Fact]
    public void ToPublished_deserializes_columns_and_rows()
    {
        var entity = new GlobalParameterTable
        {
            Name = "taxa_uf",
            Label = "Taxa por UF",
            ColumnsJson = "[{\"name\":\"uf\",\"type\":\"Text\"},{\"name\":\"taxa\",\"type\":\"Number\"}]",
            RowsJson = "[[\"SP\",\"1.5\"],[\"RJ\",\"1.8\"]]",
            KeyColumn = "uf",
            DefaultValue = "0",
        };

        var published = ParameterTableMapper.ToPublished(entity);

        Assert.Equal("taxa_uf", published.Name);
        Assert.Equal(2, published.Columns.Count);
        Assert.Equal("uf", published.Columns[0].Name);
        Assert.Equal("Number", published.Columns[1].Type);
        Assert.Equal(2, published.Rows.Count);
        Assert.Equal("RJ", published.Rows[1][0]);
        Assert.Equal("uf", published.KeyColumn);
        Assert.Equal("0", published.DefaultValue);
    }

    [Fact]
    public void ToPublished_tolerates_empty_json()
    {
        var entity = new ParameterTable { Name = "vazia", ColumnsJson = "", RowsJson = "" };
        var published = ParameterTableMapper.ToPublished(entity);
        Assert.Empty(published.Columns);
        Assert.Empty(published.Rows);
    }

    [Fact]
    public void Merge_local_wins_over_global_with_same_name()
    {
        var global = new[]
        {
            new PublishedTable("taxa_uf", Array.Empty<PublishedTableColumn>(),
                Array.Empty<IReadOnlyList<string>>(), null, null, null, "global"),
            new PublishedTable("outra", Array.Empty<PublishedTableColumn>(),
                Array.Empty<IReadOnlyList<string>>(), null, null, null, "global"),
        };
        var local = new[]
        {
            new PublishedTable("taxa_uf", Array.Empty<PublishedTableColumn>(),
                Array.Empty<IReadOnlyList<string>>(), null, null, null, "local"),
        };

        var merged = ParameterTableMapper.Merge(global, local);

        // taxa_uf vem da local; "outra" (global) permanece.
        Assert.Equal(2, merged.Count);
        Assert.Equal("local", merged.Single(t => t.Name == "taxa_uf").DefaultValue);
        Assert.Contains(merged, t => t.Name == "outra");
    }

    [Fact]
    public void Merge_name_match_is_case_insensitive()
    {
        var global = new[]
        {
            new PublishedTable("TAXA_UF", Array.Empty<PublishedTableColumn>(),
                Array.Empty<IReadOnlyList<string>>(), null, null, null, "global"),
        };
        var local = new[]
        {
            new PublishedTable("taxa_uf", Array.Empty<PublishedTableColumn>(),
                Array.Empty<IReadOnlyList<string>>(), null, null, null, "local"),
        };

        var merged = ParameterTableMapper.Merge(global, local);

        Assert.Single(merged);
        Assert.Equal("local", merged[0].DefaultValue);
    }
}
