using MotorDecisao.Application.Common;

namespace MotorDecisao.Application.Flows;

/// <summary>
/// Computa o schema de entrada (contrato da request) de uma versão de política:
/// os campos declarados manualmente somados aos campos-chave OBRIGATÓRIOS
/// derivados das fontes usadas — na própria política e em todas as referenciadas
/// em cascata. É o que o editor exibe, o que valida a decisão e o que documenta a
/// integração.
/// </summary>
public interface IPolicyInputSchemaService
{
    /// <summary>Schema de uma versão específica (por id), qualquer status.</summary>
    Task<OperationResult<PolicyInputSchema>> GetByVersionAsync(
        Guid flowId, Guid versionId, CancellationToken ct = default);

    /// <summary>
    /// Schema da versão PUBLICADA de um fluxo (para validar decisões em produção).
    /// <c>null</c> quando não há versão publicada.
    /// </summary>
    Task<PolicyInputSchema?> GetPublishedAsync(Guid flowId, CancellationToken ct = default);
}
