using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Application.Flows;

/// <summary>Origem de um campo do schema de entrada da política.</summary>
public enum InputFieldOrigin
{
    /// <summary>Declarado manualmente pelo autor da política.</summary>
    Manual,

    /// <summary>Derivado automaticamente do campo-chave de uma fonte usada.</summary>
    Source,
}

/// <summary>
/// Um campo do schema de entrada (request) de uma política. Combina os campos
/// declarados manualmente com os campos-chave derivados das fontes usadas (na
/// própria política e nas referenciadas). Campos de origem <see cref="InputFieldOrigin.Source"/>
/// são obrigatórios e não podem ser editados/excluídos no editor.
/// </summary>
public sealed record InputSchemaField(
    string Name,
    string Label,
    InputFieldType Type,
    bool Required,
    InputFieldOrigin Origin,
    /// <summary>
    /// Quando derivado de fonte: os produtos (Fonte/Produto) que exigem este campo
    /// como chave. Vazio para campos manuais. Ex.: ["SERASA/Score", "BACEN/SCR"].
    /// </summary>
    IReadOnlyList<string> RequiredBySources)
{
    /// <summary>Descrição do campo (documenta a request).</summary>
    public string? Description { get; init; }

    /// <summary>Valor de exemplo do campo (usado no payload de exemplo).</summary>
    public string? Example { get; init; }

    /// <summary>Grupo/assunto do campo (ex.: proponente, operacao). Vazio = raiz.</summary>
    public string? Group { get; init; }
}

/// <summary>
/// Schema de entrada completo de uma versão de política: o contrato da request de
/// decisão. É a base para exibir os campos no editor, validar a decisão (campos
/// obrigatórios não podem faltar) e documentar a integração.
/// </summary>
public sealed record PolicyInputSchema(
    Guid FlowId,
    Guid FlowVersionId,
    IReadOnlyList<InputSchemaField> Fields)
{
    /// <summary>
    /// Exemplo do payload de <c>POST /flows/{id}/decisions</c> montado a partir dos
    /// campos (usa o valor de exemplo quando há, senão um placeholder por tipo).
    /// </summary>
    public string ExampleRequestJson { get; init; } = "{}";
}
