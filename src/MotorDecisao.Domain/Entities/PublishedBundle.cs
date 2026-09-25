using MotorDecisao.Domain.Common;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// Retrato IMUTÁVEL de uma publicação: congela a política principal e todas as
/// subpolíticas referenciadas (recursivamente), cada uma na sua versão mais
/// recente no instante da publicação. É o que a execução usa — editar/versionar
/// qualquer política depois NÃO altera um bundle já publicado; só republicar a
/// principal gera um novo bundle.
/// </summary>
public class PublishedBundle : Entity
{
    /// <summary>Política principal (a que foi publicada).</summary>
    public Guid RootFlowId { get; set; }

    /// <summary>Versão publicada da principal que originou este bundle.</summary>
    public Guid RootFlowVersionId { get; set; }

    /// <summary>Instante da publicação/congelamento.</summary>
    public DateTime PublishedAt { get; set; }

    /// <summary>
    /// Bundle ativo? Há no máximo um ativo por <see cref="RootFlowId"/> (o da
    /// versão publicada vigente). Publicações anteriores ficam inativas (histórico).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// JSON com o mapa <c>flowId → PublishedFlowSnapshot</c> de TODAS as políticas
    /// congeladas (principal + subs). É a fonte da verdade da execução deste bundle.
    /// </summary>
    public string SnapshotsJson { get; set; } = "{}";

    /// <summary>
    /// JSON com o "manifesto" das políticas do bundle:
    /// <c>[{ flowId, flowVersionId, name }]</c>. Usado para o histórico ("vinculado
    /// a") e para travar a edição das versões congeladas.
    /// </summary>
    public string MembersJson { get; set; } = "[]";
}
