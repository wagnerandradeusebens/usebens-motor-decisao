using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Application.Flows;

/// <summary>Summary of a flow and its versions (metadata only, no graph).</summary>
public sealed record FlowSummary(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<FlowVersionSummary> Versions);

public sealed record FlowVersionSummary(
    Guid Id,
    int VersionNumber,
    FlowVersionStatus Status,
    DateTime? PublishedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    /// <summary>
    /// Nomes das políticas (bundles publicados ativos) de OUTRAS políticas que
    /// congelam esta versão como membro — o "Vinculado a" do histórico. Vazio
    /// quando nenhuma outra política referencia a versão.
    /// </summary>
    IReadOnlyList<string> LinkedPolicies);

/// <summary>Input to create a flow (creates version 1 as Draft).</summary>
public sealed record CreateFlowInput(string Name, string? Description);
