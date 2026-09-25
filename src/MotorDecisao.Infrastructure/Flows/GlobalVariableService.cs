using Microsoft.EntityFrameworkCore;
using MotorDecisao.Application.Common;
using MotorDecisao.Application.Execution;
using MotorDecisao.Application.Flows;
using MotorDecisao.Application.Formulas;
using MotorDecisao.Domain.Entities;
using MotorDecisao.Infrastructure.Persistence;

namespace MotorDecisao.Infrastructure.Flows;

/// <summary>
/// EF Core implementation of the global-variable CRUD use cases. Each mutation
/// validates that the expression compiles and that the key is unique, then drops
/// every compiled flow from the cache (a global change affects all policies).
/// </summary>
public sealed class GlobalVariableService : IGlobalVariableService
{
    private readonly MotorDecisaoDbContext _db;
    private readonly ICompiledFlowProvider _compiledFlows;

    public GlobalVariableService(MotorDecisaoDbContext db, ICompiledFlowProvider compiledFlows)
    {
        _db = db;
        _compiledFlows = compiledFlows;
    }

    public async Task<IReadOnlyList<GlobalVariableDto>> ListAsync(CancellationToken ct = default)
    {
        var items = await _db.GlobalVariables
            .AsNoTracking()
            .OrderBy(g => g.Key)
            .ToListAsync(ct);

        return items.Select(ToDto).ToList();
    }

    public async Task<OperationResult<GlobalVariableDto>> CreateAsync(GlobalVariableInput input, CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null)
        {
            return OperationResult<GlobalVariableDto>.Invalid(validation);
        }

        var key = input.Key.Trim();
        if (await _db.GlobalVariables.AnyAsync(g => g.Key == key, ct))
        {
            return OperationResult<GlobalVariableDto>.Conflict($"Já existe uma variável global com a chave '{key}'.");
        }

        var entity = new GlobalVariable
        {
            Key = key,
            Label = string.IsNullOrWhiteSpace(input.Label) ? key : input.Label.Trim(),
            Expression = input.Expression.Trim()
        };
        _db.GlobalVariables.Add(entity);
        await _db.SaveChangesAsync(ct);

        _compiledFlows.InvalidateAll();
        return OperationResult<GlobalVariableDto>.Ok(ToDto(entity));
    }

    public async Task<OperationResult<GlobalVariableDto>> UpdateAsync(Guid id, GlobalVariableInput input, CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null)
        {
            return OperationResult<GlobalVariableDto>.Invalid(validation);
        }

        var entity = await _db.GlobalVariables.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (entity is null)
        {
            return OperationResult<GlobalVariableDto>.NotFound($"Variável global {id} não encontrada.");
        }

        var key = input.Key.Trim();
        if (await _db.GlobalVariables.AnyAsync(g => g.Key == key && g.Id != id, ct))
        {
            return OperationResult<GlobalVariableDto>.Conflict($"Já existe uma variável global com a chave '{key}'.");
        }

        entity.Key = key;
        entity.Label = string.IsNullOrWhiteSpace(input.Label) ? key : input.Label.Trim();
        entity.Expression = input.Expression.Trim();
        await _db.SaveChangesAsync(ct);

        _compiledFlows.InvalidateAll();
        return OperationResult<GlobalVariableDto>.Ok(ToDto(entity));
    }

    public async Task<OperationResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.GlobalVariables.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (entity is null)
        {
            return OperationResult<bool>.NotFound($"Variável global {id} não encontrada.");
        }

        _db.GlobalVariables.Remove(entity);
        await _db.SaveChangesAsync(ct);

        _compiledFlows.InvalidateAll();
        return OperationResult<bool>.Ok(true);
    }

    /// <summary>Returns a pt-BR error message, or null if the input is valid.</summary>
    private static string? Validate(GlobalVariableInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Key))
        {
            return "A chave da variável é obrigatória.";
        }
        if (string.IsNullOrWhiteSpace(input.Expression))
        {
            return "A expressão da variável é obrigatória.";
        }
        if (!FormulaEngine.TryCompile(input.Expression, out _, out var error))
        {
            return $"Expressão inválida: {error!.Message}";
        }
        return null;
    }

    private static GlobalVariableDto ToDto(GlobalVariable g)
        => new(g.Id, g.Key, g.Label, g.Expression, g.CreatedAt, g.UpdatedAt);
}
