using Microsoft.EntityFrameworkCore;
using MotorDecisao.Application.Common;
using MotorDecisao.Application.Execution;
using MotorDecisao.Application.Flows;
using MotorDecisao.Application.PublishedFlows;
using MotorDecisao.Application.Sources;
using MotorDecisao.Domain.Enums;
using MotorDecisao.Infrastructure.Persistence;

namespace MotorDecisao.Infrastructure.Flows;

/// <summary>
/// Deriva o schema de entrada de uma versão: campos declarados + campos-chave
/// obrigatórios das fontes usadas (na própria política e nas referenciadas, em
/// cascata). Os campos de fonte são derivados (não persistidos), então nunca
/// divergem das fórmulas: refletem sempre as fontes efetivamente referenciadas.
/// </summary>
public sealed class PolicyInputSchemaService : IPolicyInputSchemaService
{
    private readonly MotorDecisaoDbContext _db;
    private readonly IPublishedFlowLoader _loader;
    private readonly ISourceCatalog _sources;

    public PolicyInputSchemaService(
        MotorDecisaoDbContext db, IPublishedFlowLoader loader, ISourceCatalog sources)
    {
        _db = db;
        _loader = loader;
        _sources = sources;
    }

    public async Task<OperationResult<PolicyInputSchema>> GetByVersionAsync(
        Guid flowId, Guid versionId, CancellationToken ct = default)
    {
        var root = await _loader.LoadByVersionAsync(flowId, versionId, ct);
        if (root is null)
        {
            return OperationResult<PolicyInputSchema>.NotFound(
                $"Versão {versionId} não encontrada no fluxo {flowId}.");
        }

        var declared = await LoadDeclaredFieldsAsync(versionId, ct);
        var schema = await BuildAsync(root, declared, ct);
        return OperationResult<PolicyInputSchema>.Ok(schema);
    }

    public async Task<PolicyInputSchema?> GetPublishedAsync(Guid flowId, CancellationToken ct = default)
    {
        var root = await _loader.LoadAsync(flowId, ct);
        if (root is null)
        {
            return null;
        }

        var declared = await LoadDeclaredFieldsAsync(root.FlowVersionId, ct);
        return await BuildAsync(root, declared, ct);
    }

    /// <summary>Campos declarados manualmente da versão (na ordem).</summary>
    private async Task<List<InputSchemaField>> LoadDeclaredFieldsAsync(Guid versionId, CancellationToken ct)
    {
        var fields = await _db.InputFields
            .AsNoTracking()
            .Where(f => f.FlowVersionId == versionId)
            .OrderBy(f => f.Order)
            .Select(f => new { f.Name, f.Label, f.Type, f.Required, f.Description, f.Example, f.Group })
            .ToListAsync(ct);

        return fields
            .Select(f => new InputSchemaField(
                f.Name, f.Label, f.Type, f.Required, InputFieldOrigin.Manual, Array.Empty<string>())
            {
                Description = f.Description,
                Example = f.Example,
                Group = f.Group,
            })
            .ToList();
    }

    /// <summary>
    /// Monta o schema: percorre a política raiz e as referenciadas (BFS, mesma
    /// cascata do congelamento), junta as fontes usadas, mapeia cada
    /// (Fonte/Produto) para seu campo-chave e mescla com os campos declarados.
    /// </summary>
    private async Task<PolicyInputSchema> BuildAsync(
        PublishedFlowSnapshot root, List<InputSchemaField> declared, CancellationToken ct)
    {
        // Campo-chave (lower) -> conjunto de "Fonte/Produto" que o exigem.
        var sourceKeys = new Dictionary<string, (string KeyField, SortedSet<string> Sources)>(StringComparer.OrdinalIgnoreCase);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.FlowName };
        var queue = new Queue<PublishedFlowSnapshot>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var snap = queue.Dequeue();

            CompiledFlow compiled;
            try
            {
                compiled = CompiledFlow.Compile(snap);
            }
            catch (FlowCompilationException)
            {
                // Grafo inválido nesta versão: ignora suas fontes/refs e segue.
                continue;
            }

            foreach (var reference in compiled.ExternalReferences)
            {
                var keyField = KeyFieldFor(reference.Source, reference.Product);
                if (string.IsNullOrWhiteSpace(keyField)) continue;

                if (!sourceKeys.TryGetValue(keyField, out var entry))
                {
                    entry = (keyField, new SortedSet<string>(StringComparer.OrdinalIgnoreCase));
                    sourceKeys[keyField] = entry;
                }
                entry.Sources.Add($"{reference.Source}/{reference.Product}");
            }

            foreach (var name in compiled.ReferencedPolicies)
            {
                if (!visited.Add(name)) continue;
                var sub = await _loader.LoadLatestByNameAsync(name, ct);
                if (sub is not null)
                {
                    queue.Enqueue(sub);
                }
            }
        }

        // Índice dos declarados por nome (case-insensitive) para mesclar.
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < declared.Count; i++)
        {
            byName[declared[i].Name] = i;
        }

        var result = new List<InputSchemaField>(declared);
        foreach (var (keyField, info) in sourceKeys)
        {
            var sourcesList = info.Sources.ToList();
            if (byName.TryGetValue(keyField, out var idx))
            {
                // Já existe um campo declarado com esse nome: promove a obrigatório
                // e marca como exigido pelas fontes (origem passa a Source).
                var d = result[idx];
                // Preserva Description/Example (init props) ao recriar o registro.
                result[idx] = (d with
                {
                    Required = true,
                    Origin = InputFieldOrigin.Source,
                    RequiredBySources = sourcesList,
                }) with { Description = d.Description, Example = d.Example, Group = d.Group };
            }
            else
            {
                // Campo derivado novo: obrigatório, tipo texto por padrão.
                result.Add(new InputSchemaField(
                    keyField, keyField, InputFieldType.Text, true, InputFieldOrigin.Source, sourcesList));
            }
        }

        return new PolicyInputSchema(root.FlowId, root.FlowVersionId, result)
        {
            ExampleRequestJson = BuildExampleJson(result),
        };
    }

    /// <summary>
    /// Monta o JSON de exemplo do POST /decisions: usa o valor de exemplo do campo
    /// quando definido, senão um placeholder por tipo.
    /// </summary>
    private static string BuildExampleJson(IReadOnlyList<InputSchemaField> fields)
    {
        // Aninha por grupo: campos com Group entram num objeto {grupo: {...}};
        // campos sem grupo ficam na raiz de "fields". Preserva a ordem de aparição
        // dos grupos e dos campos.
        var fieldsMap = new Dictionary<string, object>();
        var groupObjects = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in fields)
        {
            var value = ExampleValue(f);
            var group = f.Group?.Trim();
            if (string.IsNullOrEmpty(group))
            {
                fieldsMap[f.Name] = value;
                continue;
            }

            if (!groupObjects.TryGetValue(group, out var obj))
            {
                obj = new Dictionary<string, object>();
                groupObjects[group] = obj;
                fieldsMap[group] = obj; // insere o objeto do grupo na ordem de 1ª aparição
            }
            // Dentro do objeto do grupo usa o nome-folha (sem o prefixo "grupo.").
            var leaf = f.Name.StartsWith(group + ".", StringComparison.OrdinalIgnoreCase)
                ? f.Name[(group.Length + 1)..]
                : f.Name;
            obj[leaf] = value;
        }

        var payload = new
        {
            proposalReference = "PROP-123",
            fields = fieldsMap,
        };

        return System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    /// <summary>Valor de exemplo tipado para um campo (usa Example quando há).</summary>
    private static object ExampleValue(InputSchemaField f)
    {
        var ex = f.Example?.Trim();
        switch (f.Type)
        {
            case InputFieldType.Number:
                if (!string.IsNullOrEmpty(ex) && decimal.TryParse(ex,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var n)) return n;
                return 0;
            case InputFieldType.Boolean:
                if (!string.IsNullOrEmpty(ex)) return ex.Equals("true", StringComparison.OrdinalIgnoreCase) || ex == "1";
                return true;
            case InputFieldType.Date:
                return string.IsNullOrEmpty(ex) ? "2025-01-31" : ex;
            default:
                return string.IsNullOrEmpty(ex) ? "exemplo" : ex;
        }
    }

    /// <summary>Campo-chave de um produto no catálogo; vazio se desconhecido.</summary>
    private string KeyFieldFor(string source, string product)
    {
        var descriptor = _sources.List()
            .FirstOrDefault(s => string.Equals(s.Name, source, StringComparison.OrdinalIgnoreCase));
        var prod = descriptor?.Products
            .FirstOrDefault(p => string.Equals(p.Name, product, StringComparison.OrdinalIgnoreCase));
        return prod?.KeyField?.Trim() ?? string.Empty;
    }
}
