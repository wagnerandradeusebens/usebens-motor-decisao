using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotorDecisao.Application.PublishedFlows;

/// <summary>
/// Um membro do bundle congelado: identifica uma política e a versão exata que
/// foi congelada. Usado no manifesto (MembersJson) para o histórico ("vinculado
/// a") e para travar a edição das versões congeladas.
/// </summary>
public sealed record BundleMember(Guid FlowId, Guid FlowVersionId, string Name);

/// <summary>
/// Conteúdo desserializado de um <c>PublishedBundle</c>: os snapshots congelados
/// (por flowId) e os membros. Reconstrói o conjunto para execução e consultas.
/// </summary>
public sealed record BundleContent(
    IReadOnlyDictionary<Guid, PublishedFlowSnapshot> Snapshots,
    IReadOnlyList<BundleMember> Members);

/// <summary>
/// (De)serialização do bundle para/de jsonb. Usa as mesmas convenções do cache
/// (enums como string), garantindo que o snapshot congelado desserialize igual
/// ao que a execução espera.
/// </summary>
public static class BundleJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string SerializeSnapshots(IReadOnlyDictionary<Guid, PublishedFlowSnapshot> snapshots)
        => JsonSerializer.Serialize(snapshots, Options);

    public static string SerializeMembers(IReadOnlyList<BundleMember> members)
        => JsonSerializer.Serialize(members, Options);

    public static IReadOnlyDictionary<Guid, PublishedFlowSnapshot> DeserializeSnapshots(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<Guid, PublishedFlowSnapshot>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<Guid, PublishedFlowSnapshot>>(json, Options)
                ?? new Dictionary<Guid, PublishedFlowSnapshot>();
        }
        catch
        {
            return new Dictionary<Guid, PublishedFlowSnapshot>();
        }
    }

    public static IReadOnlyList<BundleMember> DeserializeMembers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<BundleMember>();
        try
        {
            return JsonSerializer.Deserialize<List<BundleMember>>(json, Options) ?? new List<BundleMember>();
        }
        catch
        {
            return Array.Empty<BundleMember>();
        }
    }
}
