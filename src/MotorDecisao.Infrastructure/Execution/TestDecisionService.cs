using MotorDecisao.Application.Execution;
using MotorDecisao.Application.PublishedFlows;

namespace MotorDecisao.Infrastructure.Execution;

/// <summary>
/// Executa uma decisão de TESTE contra uma versão específica (rascunho incluso),
/// sem publicar e sem persistir. Monta o snapshot da versão na hora (com tabelas
/// e variáveis globais mescladas), compila e roda o executor. Diferente do
/// <see cref="DecisionService"/>, NÃO grava <c>DecisionExecution</c> — o resultado
/// é efêmero, só para o autor conferir o comportamento antes de publicar.
///
/// As políticas referenciadas (<c>$[Política;…]</c>) resolvem pelo provedor por
/// nome (versão mais recente), o mesmo critério que o editor já usa para as
/// subpolíticas — ou seja, o teste reflete o que rodaria hoje.
/// </summary>
public sealed class TestDecisionService : ITestDecisionService
{
    private readonly IPublishedFlowLoader _loader;
    private readonly FlowExecutor _executor;

    public TestDecisionService(IPublishedFlowLoader loader, FlowExecutor executor)
    {
        _loader = loader;
        _executor = executor;
    }

    public async Task<DecisionResult> TestAsync(
        Guid flowId, Guid versionId, DecisionRequest request, CancellationToken cancellationToken = default)
    {
        var snapshot = await _loader.LoadByVersionAsync(flowId, versionId, cancellationToken);
        if (snapshot is null)
        {
            throw new InvalidOperationException(
                $"Versão {versionId} não encontrada no fluxo {flowId}.");
        }

        // Compila o grafo da versão (pode lançar FlowCompilationException, que o
        // endpoint traduz em 400 com a mensagem do erro).
        var flow = CompiledFlow.Compile(snapshot);

        // policyResolver: null → o executor usa o PolicyProvider padrão (resolve
        // subpolíticas pela versão mais recente). Sem persistência.
        return await _executor.ExecuteAsync(flow, request, cancellationToken, policyStack: null, policyResolver: null);
    }
}
