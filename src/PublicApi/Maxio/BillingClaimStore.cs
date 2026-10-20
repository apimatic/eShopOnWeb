using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Claim persistence shared by the customer and subscription flows. The claim is a store write of its own,
/// made BEFORE the provider call; the store's primary-key refusal is what stops a second caller.
/// </summary>
internal static class BillingClaimStore
{
    /// <summary>
    /// Inserts <paramref name="claim"/>. Returns <c>null</c> when this caller now owns the claim, or the row that
    /// already holds the key when the store refused the insert as a duplicate.
    /// </summary>
    public static async Task<T?> InsertOrGetExistingAsync<T>(DbContext db, T claim, Func<CancellationToken, Task<T?>> loadExisting, CancellationToken ct)
        where T : class
    {
        db.Add(claim);
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (Exception ex) when (IsInsertRefusal(ex))
        {
            // The store refused the claim. Confirm it was a duplicate key by loading the row that holds it.
            db.Entry(claim).State = EntityState.Detached;
            var existing = await loadExisting(ct);
            if (existing is null)
            {
                throw; // not a duplicate: a real store failure
            }
            return existing;
        }
    }

    /// <summary>
    /// What the configured providers raise on a duplicate primary key: SQL Server raises
    /// <see cref="DbUpdateException"/>; the EF in-memory provider (UseOnlyInMemoryDatabase) raises
    /// <see cref="ArgumentException"/> ("An item with the same key has already been added"), pinned by a unit test.
    /// A concurrency failure is not a duplicate. The caller confirms the duplicate by loading the existing row.
    /// </summary>
    private static bool IsInsertRefusal(Exception ex) =>
        (ex is DbUpdateException && ex is not DbUpdateConcurrencyException) || ex is ArgumentException;

    /// <summary>Saves a state change on a claim this caller holds. False when another caller changed it first.</summary>
    public static async Task<bool> TrySaveAsync<T>(DbContext db, T claim, ILogger logger, CancellationToken ct)
        where T : class
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(claim).State = EntityState.Detached;
            logger.LogWarning("A billing claim ({ClaimType}) was changed by another request; keeping the other request's state.", typeof(T).Name);
            return false;
        }
    }

    /// <summary>Releases a claim whose provider call was refused or never made, so a later attempt is not blocked.</summary>
    public static async Task ReleaseAsync<T>(DbContext db, T claim, ILogger logger)
        where T : class
    {
        try
        {
            db.Remove(claim);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(claim).State = EntityState.Detached;
            logger.LogWarning("A billing claim ({ClaimType}) was already changed by another request; not released.", typeof(T).Name);
        }
    }
}
