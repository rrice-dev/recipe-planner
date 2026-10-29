using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RecipePlanner.Api.Data;

// Nutrition facts and store prices are cached in shared tables, one row per ingredient name. Two requests
// looking up the same new name at the same moment both try to add that row, and the database (rightly)
// lets only one in. The loser's row says the same thing as the winner's, so it's simply dropped.
public static class CacheSaving
{
    public static async Task SaveCacheAsync(this AppDbContext db, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (attempt < 3 && IsDuplicate(ex))
            {
                // Forget the rows another request already saved, then save whatever else is left
                foreach (var entry in ex.Entries) entry.State = EntityState.Detached;
            }
        }
    }

    private static bool IsDuplicate(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
