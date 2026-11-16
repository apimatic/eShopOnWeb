using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Finix;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class PaymentHelpers
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OwnerLocks = new();

    public static SemaphoreSlim LockForOrder(int orderId)
    {
        return OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
    }

    public static string? OwnerId(ClaimsPrincipal? user)
    {
        return user?.Identity?.Name;
    }

    public static async Task<string> GetOrCreateIdentityAsync(CatalogContext db, FinixClient finix,
        string ownerId, CancellationToken cancellationToken)
    {
        var existing = await db.FinixCustomers
            .FirstOrDefaultAsync(c => c.OwnerId == ownerId, cancellationToken);

        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        var ownerLock = OwnerLocks.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        await ownerLock.WaitAsync(cancellationToken);
        try
        {
            existing = await db.FinixCustomers
                .FirstOrDefaultAsync(c => c.OwnerId == ownerId, cancellationToken);
            if (existing is not null)
            {
                return existing.FinixIdentityId;
            }

            var identity = await finix.CreateIdentityAsync(cancellationToken);
            var customer = new FinixCustomer(ownerId, identity.Id);
            db.FinixCustomers.Add(customer);
            await db.SaveChangesAsync(cancellationToken);
            return identity.Id;
        }
        finally
        {
            ownerLock.Release();
        }
    }


}
