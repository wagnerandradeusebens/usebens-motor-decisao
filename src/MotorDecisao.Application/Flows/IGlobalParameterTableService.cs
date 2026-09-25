using MotorDecisao.Application.Common;

namespace MotorDecisao.Application.Flows;

/// <summary>
/// Uma tabela de parâmetros GLOBAL (compartilhada entre políticas), como o editor
/// e a API a enxergam. Colunas e linhas viajam estruturadas; o serviço serializa
/// para jsonb ao persistir.
/// </summary>
public sealed record GlobalParameterTableDto(
    Guid Id,
    string Name,
    string Label,
    IReadOnlyList<GraphTableColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? KeyColumn,
    string? MinColumn,
    string? MaxColumn,
    string? DefaultValue,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Corpo para criar/atualizar uma tabela de parâmetros global.</summary>
public sealed record GlobalParameterTableInput(
    string Name,
    string Label,
    IReadOnlyList<GraphTableColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? KeyColumn,
    string? MinColumn,
    string? MaxColumn,
    string? DefaultValue);

/// <summary>
/// CRUD de tabelas de parâmetros globais, referenciadas nas fórmulas por
/// <c>PROCV("nome"; ...)</c>. Uma tabela local de mesmo nome tem prioridade para
/// aquela política.
/// </summary>
public interface IGlobalParameterTableService
{
    Task<IReadOnlyList<GlobalParameterTableDto>> ListAsync(CancellationToken ct = default);
    Task<OperationResult<GlobalParameterTableDto>> CreateAsync(GlobalParameterTableInput input, CancellationToken ct = default);
    Task<OperationResult<GlobalParameterTableDto>> UpdateAsync(Guid id, GlobalParameterTableInput input, CancellationToken ct = default);
    Task<OperationResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
}
