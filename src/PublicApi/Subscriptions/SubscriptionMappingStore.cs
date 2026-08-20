using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionMappingStore : ISubscriptionMappingStore
{
    private readonly AppIdentityDbContext _dbContext;

    public SubscriptionMappingStore(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SyncAsync(string userId, MaxioCustomer customer,
        IReadOnlyList<MaxioSubscription> subscriptions, CancellationToken cancellationToken)
    {
        try
        {
            await SyncCoreAsync(userId, customer, subscriptions, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request may have inserted the same Maxio link. Re-read and merge once.
            _dbContext.ChangeTracker.Clear();
            await SyncCoreAsync(userId, customer, subscriptions, cancellationToken);
        }
    }

    private async Task SyncCoreAsync(string userId, MaxioCustomer customer,
        IReadOnlyList<MaxioSubscription> subscriptions, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var customerLink = await _dbContext.MaxioCustomerLinks.FindAsync(new object[] { userId }, cancellationToken);
        if (customerLink == null)
        {
            customerLink = new MaxioCustomerLink { UserId = userId };
            _dbContext.MaxioCustomerLinks.Add(customerLink);
        }

        customerLink.MaxioCustomerId = customer.Id;
        customerLink.CustomerReference = customer.Reference ?? string.Empty;
        customerLink.LastSyncedAt = now;

        var ids = subscriptions.Select(subscription => subscription.Id).ToArray();
        var existing = ids.Length == 0
            ? new Dictionary<long, MaxioSubscriptionLink>()
            : await _dbContext.MaxioSubscriptionLinks
                .Where(link => ids.Contains(link.MaxioSubscriptionId))
                .ToDictionaryAsync(link => link.MaxioSubscriptionId, cancellationToken);

        foreach (var subscription in subscriptions)
        {
            if (!existing.TryGetValue(subscription.Id, out var link))
            {
                link = new MaxioSubscriptionLink
                {
                    MaxioSubscriptionId = subscription.Id,
                    UserId = userId
                };
                _dbContext.MaxioSubscriptionLinks.Add(link);
            }

            link.ProductHandle = subscription.Product?.Handle ?? string.Empty;
            link.State = subscription.State;
            link.LastSyncedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
