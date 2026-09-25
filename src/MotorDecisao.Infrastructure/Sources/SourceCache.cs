using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Sources;
using MotorDecisao.Domain.Entities;
using MotorDecisao.Infrastructure.Persistence;

namespace MotorDecisao.Infrastructure.Sources;

/// <summary>
/// Cache de respostas de fontes em banco (tabela <c>source_cache</c>),
/// compartilhado entre políticas e execuções. Uma linha por (fonte, produto,
/// chave de negócio); o payload guarda a resposta inteira do produto — um mapa
/// <c>dado → { type, value }</c> em JSON — para reconstruir os <see cref="FormulaValue"/>.
///
/// É um singleton (o catálogo/executor também são), então abre um escopo de DI
/// curto por operação para usar o <see cref="MotorDecisaoDbContext"/> (scoped)
/// sem prendê-lo (evita captive dependency).
/// </summary>
public sealed class SourceCache : ISourceCache
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SourceCache(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<IReadOnlyDictionary<string, FormulaValue>?> TryGetAsync(
        string source,
        string product,
        string businessKey,
        int ttlHours,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MotorDecisaoDbContext>();

        var entry = await db.SourceCacheEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.Source == source
                  && e.Product == product
                  && e.BusinessKey == businessKey,
                cancellationToken);

        if (entry is null)
        {
            return null;
        }

        // TTL: 0 (ou negativo) = permanente; senão, entrada expirada = miss.
        if (ttlHours > 0 && entry.FetchedAt < DateTime.UtcNow.AddHours(-ttlHours))
        {
            return null;
        }

        return DeserializePayload(entry.Payload);
    }

    public async Task SetAsync(
        string source,
        string product,
        string businessKey,
        IReadOnlyDictionary<string, FormulaValue> data,
        CancellationToken cancellationToken = default)
    {
        var payload = SerializePayload(data);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MotorDecisaoDbContext>();

        var entry = await db.SourceCacheEntries.FirstOrDefaultAsync(
            e => e.Source == source
              && e.Product == product
              && e.BusinessKey == businessKey,
            cancellationToken);

        if (entry is null)
        {
            db.SourceCacheEntries.Add(new SourceCacheEntry
            {
                Source = source,
                Product = product,
                BusinessKey = businessKey,
                Payload = payload,
                FetchedAt = DateTime.UtcNow,
            });
        }
        else
        {
            entry.Payload = payload;
            entry.FetchedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // --- (De)serialização do payload -------------------------------------

    /// <summary>Forma serializada de um valor: tipo + texto, para reconstrução.</summary>
    private sealed record SerializedValue(string Type, string Text);

    private static string SerializePayload(IReadOnlyDictionary<string, FormulaValue> data)
    {
        var map = new Dictionary<string, SerializedValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in data)
        {
            var (type, text) = Serialize(kvp.Value);
            map[kvp.Key] = new SerializedValue(type, text);
        }
        return JsonSerializer.Serialize(map);
    }

    private static IReadOnlyDictionary<string, FormulaValue> DeserializePayload(string payload)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, SerializedValue>>(payload)
                  ?? new Dictionary<string, SerializedValue>();
        var result = new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in map)
        {
            result[kvp.Key] = Deserialize(kvp.Value.Type, kvp.Value.Text);
        }
        return result;
    }

    private static (string Type, string Text) Serialize(FormulaValue value) => value.Type switch
    {
        FormulaValueType.Number => ("Number", value.AsNumber().ToString(CultureInfo.InvariantCulture)),
        FormulaValueType.Boolean => ("Boolean", value.AsBoolean() ? "true" : "false"),
        FormulaValueType.Date => ("Date", value.AsDate().ToString("O", CultureInfo.InvariantCulture)),
        _ => ("Text", value.AsText()),
    };

    private static FormulaValue Deserialize(string type, string text) => type switch
    {
        "Number" => FormulaValue.Number(decimal.Parse(text, CultureInfo.InvariantCulture)),
        "Boolean" => FormulaValue.Boolean(text == "true"),
        "Date" => FormulaValue.Date(DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
        _ => FormulaValue.Text(text),
    };
}
