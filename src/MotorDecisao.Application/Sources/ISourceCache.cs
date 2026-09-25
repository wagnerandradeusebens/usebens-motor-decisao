using MotorDecisao.Application.Formulas;

namespace MotorDecisao.Application.Sources;

/// <summary>
/// Cache persistente e compartilhado das RESPOSTAS de consultas a fontes externas,
/// chaveado por (fonte, produto, chave de negócio). Uma linha guarda a resposta
/// inteira do produto (todos os dados do mesmo snapshot). A primeira consulta
/// grava; as seguintes — de qualquer política ou execução — recuperam. Implementado
/// na camada de infraestrutura (banco), abstraído aqui para o catálogo de fontes.
/// </summary>
public interface ISourceCache
{
    /// <summary>
    /// Recupera a resposta cacheada do produto (mapa dado → valor), se existir e
    /// ainda válida. <paramref name="ttlHours"/> define a validade: 0 (ou negativo)
    /// = permanente; acima disso, entradas mais antigas que o TTL são ignoradas
    /// (miss). Retorna null no miss.
    /// </summary>
    Task<IReadOnlyDictionary<string, FormulaValue>?> TryGetAsync(
        string source,
        string product,
        string businessKey,
        int ttlHours,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grava (ou atualiza) a resposta inteira do produto obtida da fonte, para
    /// reuso futuro por qualquer dado daquele produto/chave.
    /// </summary>
    Task SetAsync(
        string source,
        string product,
        string businessKey,
        IReadOnlyDictionary<string, FormulaValue> data,
        CancellationToken cancellationToken = default);
}
