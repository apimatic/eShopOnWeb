using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class EfBillingLinkStore : IBillingLinkStore
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(45);
    private readonly CatalogContext _context;

    public EfBillingLinkStore(CatalogContext context)
    {
        _context = context;
    }

    public async Task<CustomerClaim> ClaimCustomerAsync(
        string userId,
        string reference,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var existing = await _context.MaxioCustomerLinks.SingleOrDefaultAsync(
                link => link.UserId == userId,
                cancellationToken);
            if (existing == null)
            {
                var lease = Guid.NewGuid().ToString("D");
                _context.MaxioCustomerLinks.Add(new MaxioCustomerLink(userId, reference, lease, now + LeaseDuration));
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    return new CustomerClaim(BillingClaimStatus.Acquired, reference, lease);
                }
                catch (DbUpdateException)
                {
                    _context.ChangeTracker.Clear();
                    continue;
                }
            }

            if (!string.Equals(existing.CustomerReference, reference, StringComparison.Ordinal))
            {
                throw new BillingException(BillingErrorKind.Conflict, "The local billing customer identity is inconsistent.");
            }

            if (existing.Status == BillingLinkStatus.Completed)
            {
                return new CustomerClaim(BillingClaimStatus.Completed, reference, null);
            }

            if (existing.Status == BillingLinkStatus.TerminalFailure)
            {
                return new CustomerClaim(BillingClaimStatus.TerminalFailure, reference, null);
            }

            if (existing.Status == BillingLinkStatus.Pending && existing.LeaseExpiresAt > now)
            {
                return new CustomerClaim(BillingClaimStatus.InProgress, reference, null);
            }

            var newLease = Guid.NewGuid().ToString("D");
            existing.Acquire(newLease, now + LeaseDuration);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return new CustomerClaim(BillingClaimStatus.Acquired, reference, newLease);
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
            }
        }

        return new CustomerClaim(BillingClaimStatus.InProgress, reference, null);
    }

    public async Task CompleteCustomerAsync(string userId, string leaseId, CancellationToken cancellationToken)
    {
        var link = await CustomerLeaseAsync(userId, leaseId, cancellationToken);
        link.Complete();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task FailCustomerAsync(
        string userId,
        string leaseId,
        bool retryable,
        string safeError,
        CancellationToken cancellationToken)
    {
        var link = await CustomerLeaseAsync(userId, leaseId, cancellationToken);
        link.Fail(retryable, safeError);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertRecoveredCustomerAsync(string userId, string reference, CancellationToken cancellationToken)
    {
        var link = await _context.MaxioCustomerLinks.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (link == null)
        {
            link = new MaxioCustomerLink(userId, reference, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow);
            _context.MaxioCustomerLinks.Add(link);
        }
        else if (!string.Equals(link.CustomerReference, reference, StringComparison.Ordinal))
        {
            throw new BillingException(BillingErrorKind.Conflict, "The recovered billing customer identity is inconsistent.");
        }

        link.Complete();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<SubscriptionClaim> ClaimSubscriptionAsync(
        string userId,
        string productHandle,
        string reference,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var existing = await _context.MaxioSubscriptionLinks.SingleOrDefaultAsync(
                link => link.UserId == userId && link.ProductHandle == productHandle,
                cancellationToken);
            if (existing == null)
            {
                var lease = Guid.NewGuid().ToString("D");
                _context.MaxioSubscriptionLinks.Add(
                    new MaxioSubscriptionLink(userId, productHandle, reference, lease, now + LeaseDuration));
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    return new SubscriptionClaim(BillingClaimStatus.Acquired, reference, lease, null);
                }
                catch (DbUpdateException)
                {
                    _context.ChangeTracker.Clear();
                    continue;
                }
            }

            if (!string.Equals(existing.SubscriptionReference, reference, StringComparison.Ordinal))
            {
                throw new BillingException(BillingErrorKind.Conflict, "The local subscription identity is inconsistent.");
            }

            if (existing.Status == BillingLinkStatus.Completed)
            {
                return new SubscriptionClaim(BillingClaimStatus.Completed, reference, null, existing.Confirmation());
            }

            if (existing.Status == BillingLinkStatus.TerminalFailure)
            {
                return new SubscriptionClaim(BillingClaimStatus.TerminalFailure, reference, null, null);
            }

            if (existing.Status == BillingLinkStatus.Pending && existing.LeaseExpiresAt > now)
            {
                return new SubscriptionClaim(BillingClaimStatus.InProgress, reference, null, null);
            }

            var newLease = Guid.NewGuid().ToString("D");
            existing.Acquire(newLease, now + LeaseDuration);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return new SubscriptionClaim(BillingClaimStatus.Acquired, reference, newLease, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
            }
        }

        return new SubscriptionClaim(BillingClaimStatus.InProgress, reference, null, null);
    }

    public async Task CompleteSubscriptionAsync(
        string userId,
        string productHandle,
        string leaseId,
        SubscriptionConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        var link = await SubscriptionLeaseAsync(userId, productHandle, leaseId, cancellationToken);
        link.Complete(confirmation);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task FailSubscriptionAsync(
        string userId,
        string productHandle,
        string leaseId,
        bool retryable,
        string safeError,
        CancellationToken cancellationToken)
    {
        var link = await SubscriptionLeaseAsync(userId, productHandle, leaseId, cancellationToken);
        link.Fail(retryable, safeError);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertRecoveredSubscriptionAsync(
        string userId,
        SubscriptionConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        var link = await _context.MaxioSubscriptionLinks.SingleOrDefaultAsync(
            item => item.UserId == userId && item.ProductHandle == confirmation.ProductHandle,
            cancellationToken);
        if (link == null)
        {
            link = new MaxioSubscriptionLink(
                userId,
                confirmation.ProductHandle,
                confirmation.Reference,
                Guid.NewGuid().ToString("D"),
                DateTimeOffset.UtcNow);
            _context.MaxioSubscriptionLinks.Add(link);
        }
        else if (!string.Equals(link.SubscriptionReference, confirmation.Reference, StringComparison.Ordinal))
        {
            throw new BillingException(BillingErrorKind.Conflict, "The recovered subscription identity is inconsistent.");
        }

        link.Complete(confirmation);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscriptionLink>> ListSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken) =>
        await _context.MaxioSubscriptionLinks
            .AsNoTracking()
            .Where(link => link.UserId == userId)
            .OrderBy(link => link.ProductHandle)
            .ToListAsync(cancellationToken);

    private async Task<MaxioCustomerLink> CustomerLeaseAsync(
        string userId,
        string leaseId,
        CancellationToken cancellationToken)
    {
        var link = await _context.MaxioCustomerLinks.SingleAsync(item => item.UserId == userId, cancellationToken);
        if (!string.Equals(link.LeaseId, leaseId, StringComparison.Ordinal))
        {
            throw new BillingException(BillingErrorKind.Conflict, "Billing customer ownership changed during provisioning.");
        }

        return link;
    }

    private async Task<MaxioSubscriptionLink> SubscriptionLeaseAsync(
        string userId,
        string productHandle,
        string leaseId,
        CancellationToken cancellationToken)
    {
        var link = await _context.MaxioSubscriptionLinks.SingleAsync(
            item => item.UserId == userId && item.ProductHandle == productHandle,
            cancellationToken);
        if (!string.Equals(link.LeaseId, leaseId, StringComparison.Ordinal))
        {
            throw new BillingException(BillingErrorKind.Conflict, "Subscription ownership changed during provisioning.");
        }

        return link;
    }
}

