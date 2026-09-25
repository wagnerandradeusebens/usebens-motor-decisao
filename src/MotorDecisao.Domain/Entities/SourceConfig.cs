using MotorDecisao.Domain.Common;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// Parâmetros de execução de uma fonte externa, editáveis na tela de Fontes.
/// A fonte em si é código (integração), mas seu comportamento operacional
/// (tentativas, timeout, validade do cache) é configurável e persistido aqui,
/// chaveado pelo nome da fonte.
/// </summary>
public class SourceConfig : Entity
{
    /// <summary>Nome da fonte (ex.: SERASA, BACEN). Único.</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>Máximo de tentativas ao consultar a fonte (>= 1).</summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>Tempo máximo de espera por tentativa, em segundos (> 0).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Validade do cache em horas. 0 (ou negativo) = cache permanente (nunca
    /// expira). Acima disso, uma entrada mais antiga que o TTL é ignorada e a
    /// fonte é reconsultada.
    /// </summary>
    public int CacheTtlHours { get; set; } = 0;
}
