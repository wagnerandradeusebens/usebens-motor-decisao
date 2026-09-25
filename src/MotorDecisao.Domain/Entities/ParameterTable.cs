using MotorDecisao.Domain.Common;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// Tipo de dado de uma coluna de tabela de parâmetros. Define como o valor
/// (armazenado como texto) é coagido na leitura pela linguagem de fórmulas.
/// </summary>
public enum ParameterColumnType
{
    Number,
    Text,
    Boolean,
    Date
}

/// <summary>
/// Base comum de uma tabela de parâmetros (lookup table): colunas nomeadas +
/// linhas de dados, mais os metadados que dizem COMO consultar a tabela nas
/// fórmulas (coluna-chave para busca exata via <c>PROCV</c>, e colunas min/max
/// para busca por faixa via <c>PROCV.FAIXA</c>) e um valor padrão quando não há
/// correspondência.
///
/// Estrutura e dados ficam em jsonb (<see cref="ColumnsJson"/>/<see cref="RowsJson"/>),
/// no mesmo padrão de <see cref="FlowNode.Config"/>, evitando explosão de tabelas.
/// </summary>
public abstract class ParameterTableBase : Entity
{
    /// <summary>Identificador usado nas fórmulas (<c>PROCV("nome"; ...)</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Rótulo amigável exibido no editor.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Definição das colunas em JSON: uma lista de <c>{ "name", "type" }</c>
    /// (o <c>type</c> mapeia para <see cref="ParameterColumnType"/>).
    /// </summary>
    public string ColumnsJson { get; set; } = "[]";

    /// <summary>
    /// Linhas em JSON: uma matriz de strings <c>[linha][coluna]</c>, na mesma
    /// ordem das colunas. A coerção para o tipo da coluna acontece na leitura.
    /// </summary>
    public string RowsJson { get; set; } = "[]";

    /// <summary>Coluna usada como chave na busca exata (<c>PROCV</c>). Opcional.</summary>
    public string? KeyColumn { get; set; }

    /// <summary>Coluna com o limite inferior da faixa (<c>PROCV.FAIXA</c>). Opcional.</summary>
    public string? MinColumn { get; set; }

    /// <summary>Coluna com o limite superior da faixa (<c>PROCV.FAIXA</c>). Opcional.</summary>
    public string? MaxColumn { get; set; }

    /// <summary>
    /// Valor retornado quando a busca não encontra correspondência. Quando nulo,
    /// a consulta resolve para <c>#N/D</c> (tratado como revisão manual).
    /// </summary>
    public string? DefaultValue { get; set; }
}

/// <summary>
/// Tabela de parâmetros LOCAL — escopada a uma <see cref="FlowVersion"/> (como
/// <see cref="Formula"/>). É versionada e congelada junto na publicação. Quando
/// uma tabela local tem o mesmo <see cref="ParameterTableBase.Name"/> de uma
/// global, a local tem prioridade para aquela política.
/// </summary>
public class ParameterTable : ParameterTableBase
{
    /// <summary>Versão dona desta tabela.</summary>
    public Guid FlowVersionId { get; set; }
    public FlowVersion? FlowVersion { get; set; }
}

/// <summary>
/// Tabela de parâmetros GLOBAL — compartilhada por todas as políticas (como
/// <see cref="GlobalVariable"/>). Sem vínculo com versão; referenciada pelo nome.
/// </summary>
public class GlobalParameterTable : ParameterTableBase
{
}
