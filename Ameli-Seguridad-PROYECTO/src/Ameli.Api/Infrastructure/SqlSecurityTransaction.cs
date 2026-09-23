using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ameli.Api.Infrastructure;

// SQL Server application locks coordinate API instances, not just one process.
// Sorted resources prevent two operations from taking user locks in opposite order.
public sealed class SqlSecurityTransaction(SecurityDbContext db)
{
    public async Task<IDbContextTransaction> BeginAsync(IEnumerable<string> resources, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var resource in resources.Distinct().Order(StringComparer.Ordinal))
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock @Resource={resource},
                        @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
                    IF @result < 0 THROW 51000, 'Security transaction busy', 1;
                    """, ct);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
    public static string User(Guid id) => $"ameli:user:{id:N}";
}
