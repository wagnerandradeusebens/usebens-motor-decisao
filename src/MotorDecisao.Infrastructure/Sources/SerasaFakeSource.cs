using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Sources;

namespace MotorDecisao.Infrastructure.Sources;

/// <summary>
/// A fake SERASA integration for testing the engine end-to-end before a real
/// bureau integration exists. It exposes the product <c>Score</c> with the datum
/// <c>Pontuacao</c> and returns a random score between 1 and 1000. It reads the
/// proposal's <c>cpf</c> field (used only to demonstrate context access; the
/// score is random regardless of CPF, per the current test requirement).
/// </summary>
public sealed class SerasaFakeSource : IExternalSource
{
    public const string SourceName = "SERASA";

    public SourceDescriptor Descriptor { get; } = new(
        Name: SourceName,
        Description: "Integração fake para testes (score aleatório 1–1000).",
        Products: new[]
        {
            new SourceProduct(
                Name: "Score",
                Description: "Score de crédito.",
                Data: new[] { new SourceDatum("Pontuacao", "Pontuação de 1 a 1000.") },
                KeyField: "cpf")
        });

    /// <summary>Fake availability: up ~90% of the time (random per call).</summary>
    public bool IsAvailable(IFormulaContext context) => Random.Shared.NextDouble() < 0.9;

    public Task<IReadOnlyDictionary<string, FormulaValue>> ResolveProductAsync(
        string product,
        IFormulaContext context,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(product, "Score", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyDictionary<string, FormulaValue>>(
                new Dictionary<string, FormulaValue>());
        }

        // Reads cpf from the proposal context (demonstrates context access).
        _ = context.TryGetField("cpf", out _);

        var score = Random.Shared.Next(1, 1001); // 1..1000 inclusive
        var data = new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Pontuacao"] = FormulaValue.Number(score),
        };
        return Task.FromResult<IReadOnlyDictionary<string, FormulaValue>>(data);
    }
}
