using System.Text.Json;

namespace MotorDecisao.Application.Flows;

/// <summary>
/// (De)serialização das colunas e linhas de uma tabela de parâmetros para/de
/// jsonb. Compartilhado pelos serviços de tabela local e global para garantir o
/// mesmo formato persistido (colunas como <c>[{name,type}]</c>, linhas como
/// matriz de strings).
/// </summary>
public static class ParameterTableJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static string SerializeColumns(IReadOnlyList<GraphTableColumn> columns)
        => JsonSerializer.Serialize(columns.Select(c => new ColumnDto { Name = c.Name, Type = c.Type }));

    public static string SerializeRows(IReadOnlyList<IReadOnlyList<string>> rows)
        => JsonSerializer.Serialize(rows);

    public static IReadOnlyList<GraphTableColumn> DeserializeColumns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<GraphTableColumn>();
        try
        {
            var raw = JsonSerializer.Deserialize<List<ColumnDto>>(json, Options) ?? new();
            return raw.Select(c => new GraphTableColumn(c.Name ?? string.Empty, c.Type ?? "Text")).ToList();
        }
        catch
        {
            return Array.Empty<GraphTableColumn>();
        }
    }

    public static IReadOnlyList<IReadOnlyList<string>> DeserializeRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<IReadOnlyList<string>>();
        try
        {
            var raw = JsonSerializer.Deserialize<List<List<string?>>>(json, Options) ?? new();
            return raw.Select(r => (IReadOnlyList<string>)r.Select(v => v ?? string.Empty).ToList()).ToList();
        }
        catch
        {
            return Array.Empty<IReadOnlyList<string>>();
        }
    }

    private sealed class ColumnDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
    }
}
