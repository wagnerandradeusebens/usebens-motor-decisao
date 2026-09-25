using Microsoft.EntityFrameworkCore;
using MotorDecisao.Application.Common;
using MotorDecisao.Application.Execution;
using MotorDecisao.Application.Flows;
using MotorDecisao.Domain.Entities;
using MotorDecisao.Infrastructure.Persistence;

namespace MotorDecisao.Infrastructure.Flows;

/// <summary>
/// CRUD de tabelas de parâmetros globais. Cada mutação valida nome único e
/// consistência básica, então invalida todo o cache de fluxos compilados (uma
/// tabela global afeta todas as políticas que a referenciam).
/// </summary>
public sealed class GlobalParameterTableService : IGlobalParameterTableService
{
    private readonly MotorDecisaoDbContext _db;
    private readonly ICompiledFlowProvider _compiledFlows;

    public GlobalParameterTableService(MotorDecisaoDbContext db, ICompiledFlowProvider compiledFlows)
    {
        _db = db;
        _compiledFlows = compiledFlows;
    }

    public async Task<IReadOnlyList<GlobalParameterTableDto>> ListAsync(CancellationToken ct = default)
    {
        var items = await _db.GlobalParameterTables
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return items.Select(ToDto).ToList();
    }

    public async Task<OperationResult<GlobalParameterTableDto>> CreateAsync(GlobalParameterTableInput input, CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null)
        {
            return OperationResult<GlobalParameterTableDto>.Invalid(validation);
        }

        var name = input.Name.Trim();
        if (await _db.GlobalParameterTables.AnyAsync(t => t.Name == name, ct))
        {
            return OperationResult<GlobalParameterTableDto>.Conflict($"Já existe uma tabela global com o nome '{name}'.");
        }

        var entity = new GlobalParameterTable();
        Apply(entity, input);
        _db.GlobalParameterTables.Add(entity);
        await _db.SaveChangesAsync(ct);

        _compiledFlows.InvalidateAll();
        return OperationResult<GlobalParameterTableDto>.Ok(ToDto(entity));
    }

    public async Task<OperationResult<GlobalParameterTableDto>> UpdateAsync(Guid id, GlobalParameterTableInput input, CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null)
        {
            return OperationResult<GlobalParameterTableDto>.Invalid(validation);
        }

        var entity = await _db.GlobalParameterTables.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null)
        {
            return OperationResult<GlobalParameterTableDto>.NotFound($"Tabela global {id} não encontrada.");
        }

        var name = input.Name.Trim();
        if (await _db.GlobalParameterTables.AnyAsync(t => t.Name == name && t.Id != id, ct))
        {
            return OperationResult<GlobalParameterTableDto>.Conflict($"Já existe uma tabela global com o nome '{name}'.");
        }

        Apply(entity, input);
        await _db.SaveChangesAsync(ct);

        _compiledFlows.InvalidateAll();
        return OperationResult<GlobalParameterTableDto>.Ok(ToDto(entity));
    }

    public async Task<OperationResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.GlobalParameterTables.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null)
        {
            return OperationResult<bool>.NotFound($"Tabela global {id} não encontrada.");
        }

        _db.GlobalParameterTables.Remove(entity);
        await _db.SaveChangesAsync(ct);

        _compiledFlows.InvalidateAll();
        return OperationResult<bool>.Ok(true);
    }

    /// <summary>Copia os campos do input para a entidade, serializando colunas/linhas.</summary>
    private static void Apply(GlobalParameterTable entity, GlobalParameterTableInput input)
    {
        var name = input.Name.Trim();
        entity.Name = name;
        entity.Label = string.IsNullOrWhiteSpace(input.Label) ? name : input.Label.Trim();
        entity.ColumnsJson = ParameterTableJson.SerializeColumns(input.Columns);
        entity.RowsJson = ParameterTableJson.SerializeRows(input.Rows);
        entity.KeyColumn = string.IsNullOrWhiteSpace(input.KeyColumn) ? null : input.KeyColumn.Trim();
        entity.MinColumn = string.IsNullOrWhiteSpace(input.MinColumn) ? null : input.MinColumn.Trim();
        entity.MaxColumn = string.IsNullOrWhiteSpace(input.MaxColumn) ? null : input.MaxColumn.Trim();
        entity.DefaultValue = input.DefaultValue;
    }

    /// <summary>Retorna uma mensagem de erro pt-BR, ou null se o input é válido.</summary>
    private static string? Validate(GlobalParameterTableInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return "O nome da tabela é obrigatório.";
        }
        if (input.Columns is null || input.Columns.Count == 0)
        {
            return "A tabela precisa de ao menos uma coluna.";
        }
        // Nome de coluna-chave/min/max, quando informado, deve existir nas colunas.
        var colNames = new HashSet<string>(input.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var (col, label) in new[] { (input.KeyColumn, "chave"), (input.MinColumn, "mínimo"), (input.MaxColumn, "máximo") })
        {
            if (!string.IsNullOrWhiteSpace(col) && !colNames.Contains(col.Trim()))
            {
                return $"A coluna {label} '{col}' não existe na tabela.";
            }
        }
        return null;
    }

    private static GlobalParameterTableDto ToDto(GlobalParameterTable t)
        => new(
            t.Id,
            t.Name,
            t.Label,
            ParameterTableJson.DeserializeColumns(t.ColumnsJson),
            ParameterTableJson.DeserializeRows(t.RowsJson),
            t.KeyColumn,
            t.MinColumn,
            t.MaxColumn,
            t.DefaultValue,
            t.CreatedAt,
            t.UpdatedAt);
}
