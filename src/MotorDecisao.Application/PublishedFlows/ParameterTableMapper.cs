using System.Text.Json;
using MotorDecisao.Domain.Entities;

namespace MotorDecisao.Application.PublishedFlows;

/// <summary>
/// Converte entidades de tabela de parâmetros (com colunas/linhas em jsonb) para
/// o <see cref="PublishedTable"/> do snapshot, e mescla tabelas locais + globais
/// com precedência local (mesma regra das variáveis: local de mesmo nome vence).
/// </summary>
public static class ParameterTableMapper
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Mapeia uma entidade (local ou global) para o snapshot.</summary>
    public static PublishedTable ToPublished(ParameterTableBase table)
    {
        var columns = Deserialize<List<ColumnDto>>(table.ColumnsJson)
            .Select(c => new PublishedTableColumn(c.Name ?? string.Empty, c.Type ?? "Text"))
            .ToList();

        var rows = Deserialize<List<List<string?>>>(table.RowsJson)
            .Select(r => (IReadOnlyList<string>)r.Select(v => v ?? string.Empty).ToList())
            .ToList();

        return new PublishedTable(
            Name: table.Name,
            Columns: columns,
            Rows: rows,
            KeyColumn: table.KeyColumn,
            MinColumn: table.MinColumn,
            MaxColumn: table.MaxColumn,
            DefaultValue: table.DefaultValue);
    }

    /// <summary>Mescla globais + locais; local de mesmo nome (case-insensitive) vence.</summary>
    public static IReadOnlyList<PublishedTable> Merge(
        IEnumerable<PublishedTable> globals,
        IEnumerable<PublishedTable> locals)
    {
        var localList = locals.ToList();
        var localNames = new HashSet<string>(localList.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);

        var merged = new List<PublishedTable>();
        foreach (var g in globals)
        {
            if (!localNames.Contains(g.Name))
            {
                merged.Add(g);
            }
        }
        merged.AddRange(localList);
        return merged;
    }

    private static T Deserialize<T>(string? json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    private sealed class ColumnDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
    }
}
