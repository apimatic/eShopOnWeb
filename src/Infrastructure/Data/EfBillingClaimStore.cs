using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class EfBillingClaimStore : IBillingClaimStore
{
    private readonly CatalogContext _dbContext;

    public EfBillingClaimStore(CatalogContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<BillingCustomer?> GetCustomerAsync(string buyerId, CancellationToken cancellationToken = default) =>
        _dbContext.BillingCustomers.SingleOrDefaultAsync(c => c.BuyerId == buyerId, cancellationToken);

    public Task<SubscriptionEnrollment?> GetEnrollmentAsync(string reference, CancellationToken cancellationToken = default) =>
        _dbContext.SubscriptionEnrollments.SingleOrDefaultAsync(e => e.Reference == reference, cancellationToken);

    public async Task<IReadOnlyList<SubscriptionEnrollment>> ListUnsettledEnrollmentsAsync(string buyerId, CancellationToken cancellationToken = default) =>
        await _dbContext.SubscriptionEnrollments
            .Where(e => e.BuyerId == buyerId && e.Status != BillingClaimStatus.Completed)
            .ToListAsync(cancellationToken);

    public async Task<bool> TryClaimAsync<T>(T claim, CancellationToken cancellationToken = default) where T : BillingClaim, IAggregateRoot
    {
        // This context already tracks a row with the same key (it read the claim earlier): the claim is held.
        // Checked up front because a failed Add can leave the rejected entity tracked as Added.
        var primaryKey = _dbContext.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!;
        var keyValues = primaryKey.Properties.Select(p => p.PropertyInfo!.GetValue(claim)).ToArray();
        if (_dbContext.Set<T>().Local.FindEntryUntyped(keyValues) is not null)
        {
            return false;
        }

        _dbContext.Set<T>().Add(claim);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            _dbContext.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryUpdateAsync<T>(T claim, CancellationToken cancellationToken = default) where T : BillingClaim, IAggregateRoot
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    public async Task ReleaseAsync<T>(T claim, CancellationToken cancellationToken = default) where T : BillingClaim, IAggregateRoot
    {
        _dbContext.Set<T>().Remove(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already removed or taken over by another caller; nothing left to release.
            _dbContext.Entry(claim).State = EntityState.Detached;
        }
    }

    private static bool IsDuplicateKey(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => false,
        DbUpdateException { InnerException: { } inner } when inner.GetType().FullName == "Microsoft.Data.SqlClient.SqlException" => IsSqlServerDuplicateKey(inner),
        // EF in-memory provider (UseOnlyInMemoryDatabase): its table rejects a duplicate key from SaveChanges
        // with ArgumentException("An item with the same key has already been added"), proven in ClaimRefusal tests.
        ArgumentException => true,
        _ => false,
    };

    // Kept out of IsDuplicateKey so Microsoft.Data.SqlClient is only loaded when SQL Server is actually in use.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsSqlServerDuplicateKey(Exception inner) =>
        // 2627 = primary key / unique constraint violation, 2601 = unique index violation.
        inner is SqlException { Number: 2627 or 2601 };
}
