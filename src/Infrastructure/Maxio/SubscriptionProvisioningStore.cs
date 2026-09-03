using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed record ProvisioningClaim(bool Acquired, string? LeaseToken);

internal sealed class SubscriptionProvisioningStore
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private readonly CatalogContext _context;

    public SubscriptionProvisioningStore(CatalogContext context)
    {
        _context = context;
    }

    public async Task<ProvisioningClaim> TryAcquireAsync(
        string userReference,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await _context.SubscriptionProvisioningIntents
            .SingleOrDefaultAsync(
                x => x.UserReference == userReference && x.ProductHandle == productHandle,
                cancellationToken);

        if (existing is null)
        {
            var leaseToken = Guid.NewGuid().ToString("D");
            var intent = new SubscriptionProvisioningIntent
            {
                UserReference = userReference,
                ProductHandle = productHandle,
                SubscriptionReference = subscriptionReference,
                LeaseToken = leaseToken,
                LeaseExpiresAt = now.Add(LeaseDuration),
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            };

            _context.SubscriptionProvisioningIntents.Add(intent);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return new ProvisioningClaim(true, leaseToken);
            }
            catch (DbUpdateException)
            {
                _context.ChangeTracker.Clear();
                var winnerExists = await _context.SubscriptionProvisioningIntents.AnyAsync(
                    x => x.UserReference == userReference && x.ProductHandle == productHandle,
                    cancellationToken);
                if (!winnerExists)
                {
                    throw;
                }

                return new ProvisioningClaim(false, null);
            }
        }

        if (existing.MaxioSubscriptionId.HasValue || existing.LeaseExpiresAt > now)
        {
            return new ProvisioningClaim(false, null);
        }

        var replacementToken = Guid.NewGuid().ToString("D");
        existing.LeaseToken = replacementToken;
        existing.LeaseExpiresAt = now.Add(LeaseDuration);
        existing.UpdatedAt = now;
        existing.Version++;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new ProvisioningClaim(true, replacementToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            return new ProvisioningClaim(false, null);
        }
    }

    public async Task MarkCompletedAsync(
        string subscriptionReference,
        string leaseToken,
        int maxioSubscriptionId,
        CancellationToken cancellationToken)
    {
        var intent = await _context.SubscriptionProvisioningIntents.SingleAsync(
            x => x.SubscriptionReference == subscriptionReference,
            cancellationToken);

        if (!string.Equals(intent.LeaseToken, leaseToken, StringComparison.Ordinal))
        {
            return;
        }

        intent.MaxioSubscriptionId = maxioSubscriptionId;
        intent.LeaseExpiresAt = DateTimeOffset.MaxValue;
        intent.UpdatedAt = DateTimeOffset.UtcNow;
        intent.Version++;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(
        string subscriptionReference,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        var intent = await _context.SubscriptionProvisioningIntents.SingleOrDefaultAsync(
            x => x.SubscriptionReference == subscriptionReference,
            cancellationToken);

        if (intent is null || !string.Equals(intent.LeaseToken, leaseToken, StringComparison.Ordinal))
        {
            return;
        }

        intent.LeaseExpiresAt = DateTimeOffset.MinValue;
        intent.UpdatedAt = DateTimeOffset.UtcNow;
        intent.Version++;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
