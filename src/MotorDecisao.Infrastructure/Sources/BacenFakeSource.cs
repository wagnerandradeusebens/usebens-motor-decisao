using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Sources;

namespace MotorDecisao.Infrastructure.Sources;

/// <summary>
/// A fake BACEN integration for testing the engine end-to-end before a real
/// integration exists. It mimics the SCR (Sistema de Informações de Crédito):
/// given a CPF, it returns credit-limit history, upcoming-debt figures and an
/// overdue-debt flag drawn from the SFN (Sistema Financeiro Nacional).
///
/// All values are derived deterministically from the <c>cpf</c> field so the same
/// CPF always yields the same result (making decisions reproducible for testing).
/// </summary>
public sealed class BacenFakeSource : IExternalSource
{
    public const string SourceName = "BACEN";
    private const string ProductScr = "SCR";

    // Exposed data (referenced in formulas as [BACEN;SCR;<Nome>]).
    private const string TempoInicioSfn = "TempoInicioSFN";
    private const string LimiteMinimo6m = "LimiteCreditoMinimo6m";
    private const string LimiteAtualMedia6m = "LimiteCreditoAtualMedia6m";
    private const string LimiteMedia3m = "LimiteCreditoMedia3m";
    private const string DividasAVencer3m12m = "DividasAVencer3m12m";
    private const string DividasAVencerMinimo12m = "DividasAVencerMinimo12m";
    private const string FlagDividasVencidas12m = "FlagDividasVencidas12m";

    public SourceDescriptor Descriptor { get; } = new(
        Name: SourceName,
        Description: "Integração fake do BACEN (SCR) para testes. Consulta pelo CPF.",
        Products: new[]
        {
            new SourceProduct(
                Name: ProductScr,
                Description: "Sistema de Informações de Crédito (SCR).",
                Data: new[]
                {
                    new SourceDatum(TempoInicioSfn, "Tempo de início no SFN (em meses)."),
                    new SourceDatum(LimiteMinimo6m, "Limite de crédito - mínimo dos últimos 6 meses (R$)."),
                    new SourceDatum(LimiteAtualMedia6m, "Limite de crédito - atual dividido pela média de 6 meses."),
                    new SourceDatum(LimiteMedia3m, "Limite de crédito - média dos últimos 3 meses (R$)."),
                    new SourceDatum(DividasAVencer3m12m, "Dívidas a vencer - 3 meses dividido por 12 meses."),
                    new SourceDatum(DividasAVencerMinimo12m, "Dívidas a vencer - mínimo dos últimos 12 meses (R$)."),
                    new SourceDatum(FlagDividasVencidas12m, "Indica dívidas vencidas nos últimos 12 meses (VERDADEIRO/FALSO).")
                })
        });

    /// <summary>
    /// Fake availability: deterministic per CPF (same CPF → same answer), up ~85%
    /// of the time, consistent with how the SCR data is generated.
    /// </summary>
    public bool IsAvailable(IFormulaContext context)
    {
        context.TryGetField("cpf", out var cpfValue);
        var cpf = cpfValue.Type == FormulaValueType.Blank ? string.Empty : cpfValue.AsText();
        // Offset the seed so availability is independent of the data draw.
        var rng = new Random(StableSeed(cpf) ^ 0x5F3759DF);
        return rng.NextDouble() < 0.85;
    }

    public Task<FormulaValue> ResolveAsync(
        string product,
        string datum,
        IFormulaContext context,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(product, ProductScr, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(FormulaValue.Error(FormulaErrorKind.Name));
        }

        // Reads cpf from the proposal context and uses it as a stable seed so the
        // same CPF always produces the same SCR snapshot. All figures are drawn
        // from the same seed so a CPF's data is internally consistent.
        context.TryGetField("cpf", out var cpfValue);
        var cpf = cpfValue.Type == FormulaValueType.Blank ? string.Empty : cpfValue.AsText();
        var rng = new Random(StableSeed(cpf));

        var tempoInicioSfn = rng.Next(1, 361);                                     // 1..360 meses
        var limiteMedia6m = Math.Round((decimal)rng.NextDouble() * 50_000m, 2);    // R$ 0..50.000
        var limiteMinimo6m = Math.Round(limiteMedia6m * (decimal)(0.4 + rng.NextDouble() * 0.5), 2); // <= média
        var limiteAtual = Math.Round(limiteMedia6m * (decimal)(0.6 + rng.NextDouble() * 0.8), 2);
        var limiteAtualMedia6m = limiteMedia6m == 0 ? 0m : Math.Round(limiteAtual / limiteMedia6m, 4);
        var limiteMedia3m = Math.Round(limiteMedia6m * (decimal)(0.8 + rng.NextDouble() * 0.4), 2);

        var dividas12m = Math.Round((decimal)rng.NextDouble() * 80_000m, 2);       // R$ 0..80.000
        var dividas3m = Math.Round(dividas12m * (decimal)(0.1 + rng.NextDouble() * 0.4), 2);
        var dividas3m12m = dividas12m == 0 ? 0m : Math.Round(dividas3m / dividas12m, 4);
        var dividasMinimo12m = Math.Round(dividas12m * (decimal)(0.2 + rng.NextDouble() * 0.5), 2);
        var flagVencidas12m = rng.NextDouble() < 0.25;                             // ~25% com dívidas vencidas

        return Task.FromResult(datum switch
        {
            _ when Is(datum, TempoInicioSfn) => FormulaValue.Number(tempoInicioSfn),
            _ when Is(datum, LimiteMinimo6m) => FormulaValue.Number(limiteMinimo6m),
            _ when Is(datum, LimiteAtualMedia6m) => FormulaValue.Number(limiteAtualMedia6m),
            _ when Is(datum, LimiteMedia3m) => FormulaValue.Number(limiteMedia3m),
            _ when Is(datum, DividasAVencer3m12m) => FormulaValue.Number(dividas3m12m),
            _ when Is(datum, DividasAVencerMinimo12m) => FormulaValue.Number(dividasMinimo12m),
            _ when Is(datum, FlagDividasVencidas12m) => FormulaValue.Boolean(flagVencidas12m),
            _ => FormulaValue.Error(FormulaErrorKind.Name)
        });
    }

    private static bool Is(string datum, string name)
        => string.Equals(datum, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Turns a CPF into a stable, non-negative RNG seed. Empty CPFs share a seed,
    /// which is fine for a fake source.
    /// </summary>
    private static int StableSeed(string cpf)
    {
        var digits = new string(cpf.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return 0;
        }

        unchecked
        {
            var hash = 17;
            foreach (var c in digits)
            {
                hash = hash * 31 + c;
            }
            return hash & 0x7FFFFFFF; // keep it non-negative
        }
    }
}
