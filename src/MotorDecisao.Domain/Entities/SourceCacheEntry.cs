using MotorDecisao.Domain.Common;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// Uma entrada de cache da RESPOSTA de uma consulta a fonte externa, compartilhada
/// entre TODAS as políticas e execuções. Uma linha por (fonte, produto, chave de
/// negócio); o <see cref="Payload"/> guarda TODOS os dados do produto naquele
/// snapshot. A chave de negócio é o valor do campo OBRIGATÓRIO que identifica a
/// consulta do produto (o KeyField: cpf, cnpj, placa, etc.), não necessariamente
/// o CPF.
///
/// Fluxo: ao resolver <c>[Fonte;Produto;Dado]</c>, o motor procura a resposta do
/// produto aqui; se existir (e não expirada), lê o dado do payload; senão consulta
/// a fonte UMA vez (produto inteiro) e grava. Assim, outra referência a qualquer
/// dado do mesmo produto/chave reaproveita a mesma resposta, sem nova chamada.
/// </summary>
public class SourceCacheEntry : Entity
{
    /// <summary>Nome da fonte (ex.: SERASA, BACEN).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Produto da fonte (ex.: Score, SCR).</summary>
    public string Product { get; set; } = string.Empty;

    /// <summary>
    /// Chave de negócio: o valor do campo obrigatório da consulta (KeyField do
    /// produto — ex.: cpf, cnpj, placa), normalizado para lookup estável.
    /// </summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// Resposta inteira do produto, serializada como JSON: um mapa
    /// <c>dado → { type, value }</c> com todos os dados do mesmo snapshot da
    /// consulta. Uma linha por (fonte, produto, chave).
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Momento em que a resposta foi obtida da fonte (base para expiração).</summary>
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
