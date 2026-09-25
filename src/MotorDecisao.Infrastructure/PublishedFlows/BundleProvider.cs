using Microsoft.EntityFrameworkCore;
using MotorDecisao.Application.Execution;
using MotorDecisao.Application.PublishedFlows;
using MotorDecisao.Infrastructure.Persistence;

namespace MotorDecisao.Infrastructure.PublishedFlows;

/// <summary>
/// Carrega o bundle congelado ativo de uma política principal do banco e o
/// desserializa em <see cref="BundleContent"/> (snapshots + membros). Retorna
/// null quando a política não tem bundle ativo (fallback para o caminho antigo).
/// </summary>
public sealed class BundleProvider : IBundleProvider
{
    private readonly MotorDecisaoDbContext _db;

    public BundleProvider(MotorDecisaoDbContext db)
    {
        _db = db;
    }

    public async Task<BundleContent?> GetActiveBundleAsync(Guid rootFlowId, CancellationToken cancellationToken = default)
    {
        var bundle = await _db.PublishedBundles
            .AsNoTracking()
            .Where(b => b.RootFlowId == rootFlowId && b.IsActive)
            .OrderByDescending(b => b.PublishedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (bundle is null)
        {
            return null;
        }

        var snapshots = BundleJson.DeserializeSnapshots(bundle.SnapshotsJson);
        var members = BundleJson.DeserializeMembers(bundle.MembersJson);
        return new BundleContent(snapshots, members);
    }
}
