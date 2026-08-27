using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionBillingService(
    AppIdentityDbContext identityDbContext,
    IMaxioGateway maxio,
    SubscriptionKeyLock keyLock) : ISubscriptionBillingService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ContenderWait = TimeSpan.FromSeconds(25);

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
        maxio.ListPlansAsync(cancellationToken);

    public async Task<SubscriptionDto> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        productHandle = productHandle?.Trim() ?? string.Empty;
        if (productHandle.Length is 0 or > 255)
        {
            throw new MaxioIntegrationException(400, "invalid_product_handle", "A valid productHandle is required.");
        }

        var plan = await maxio.GetPlanAsync(productHandle, cancellationToken);
        var canonicalHandle = plan.Handle.ToLowerInvariant();
        var key = $"{user.Id}\n{canonicalHandle}";
        using var localReservation = await keyLock.AcquireAsync(key, cancellationToken);

        var reference = CreateSubscriptionReference(user.Id, canonicalHandle);
        var link = await GetOrCreateLinkAsync(user.Id, canonicalHandle, reference, cancellationToken);
        var customer = await maxio.EnsureCustomerAsync(user, cancellationToken);

        var reconciled = await ReconcileAsync(reference, customer.Id, canonicalHandle, cancellationToken);
        if (reconciled is not null)
        {
            await MarkSucceededAsync(link.Id, reconciled.Id, cancellationToken);
            return reconciled;
        }

        if (link.Status == MaxioSubscriptionLinkStatus.OutcomeUnknown)
        {
            throw OutcomeUnknown();
        }

        if (link.Status == MaxioSubscriptionLinkStatus.Rejected)
        {
            throw new MaxioIntegrationException(409, "subscription_previously_rejected", "This subscription request was previously rejected.");
        }

        var leaseOwner = Guid.NewGuid().ToString("N");
        if (!await TryAcquireLeaseAsync(link.Id, leaseOwner, cancellationToken))
        {
            var contenderResult = await WaitForContenderAsync(
                link.Id,
                reference,
                customer.Id,
                canonicalHandle,
                cancellationToken);
            if (contenderResult is not null)
            {
                return contenderResult;
            }

            throw new MaxioIntegrationException(409, "subscription_in_progress", "A subscription request is already in progress.");
        }

        try
        {
            var created = await maxio.CreateSubscriptionAsync(
                customer.Reference,
                plan.Handle,
                reference,
                cancellationToken);
            await MarkSucceededAsync(link.Id, created.Id, cancellationToken);
            return created;
        }
        catch (MaxioAmbiguousWriteException)
        {
            var afterFailure = await ReconcileAsync(reference, customer.Id, canonicalHandle, cancellationToken);
            if (afterFailure is not null)
            {
                await MarkSucceededAsync(link.Id, afterFailure.Id, cancellationToken);
                return afterFailure;
            }

            await MarkOutcomeUnknownAsync(link.Id, cancellationToken);
            throw OutcomeUnknown();
        }
        catch (MaxioIntegrationException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            await MarkRejectedAsync(link.Id, cancellationToken);
            throw;
        }
        catch
        {
            await ReleaseLeaseAsync(link.Id, leaseOwner, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(
        BillingUser user,
        CancellationToken cancellationToken)
    {
        var customer = await maxio.FindCustomerAsync(user.Id, cancellationToken);
        return customer is null
            ? Array.Empty<SubscriptionDto>()
            : await maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioSubscriptionLink> GetOrCreateLinkAsync(
        string userId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await identityDbContext.MaxioSubscriptionLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                link => link.UserId == userId && link.ProductHandle == productHandle,
                cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var created = new MaxioSubscriptionLink
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductHandle = productHandle,
            SubscriptionReference = reference,
            Status = MaxioSubscriptionLinkStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        identityDbContext.MaxioSubscriptionLinks.Add(created);
        try
        {
            await identityDbContext.SaveChangesAsync(cancellationToken);
            identityDbContext.Entry(created).State = EntityState.Detached;
            return created;
        }
        catch (DbUpdateException)
        {
            identityDbContext.Entry(created).State = EntityState.Detached;
            return await identityDbContext.MaxioSubscriptionLinks
                .AsNoTracking()
                .SingleAsync(
                    link => link.UserId == userId && link.ProductHandle == productHandle,
                    cancellationToken);
        }
    }

    private async Task<SubscriptionDto?> ReconcileAsync(
        string reference,
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var byReference = await maxio.FindSubscriptionAsync(reference, cancellationToken);
        if (byReference is not null)
        {
            return byReference;
        }

        var subscriptions = await maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> TryAcquireLeaseAsync(Guid linkId, string leaseOwner, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(LeaseDuration);
        if (identityDbContext.Database.IsRelational())
        {
            var updated = await identityDbContext.MaxioSubscriptionLinks
                .Where(link =>
                    link.Id == linkId &&
                    link.Status == MaxioSubscriptionLinkStatus.Pending &&
                    (link.LeaseExpiresAt == null || link.LeaseExpiresAt < now))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(link => link.LeaseOwner, leaseOwner)
                        .SetProperty(link => link.LeaseExpiresAt, expiresAt)
                        .SetProperty(link => link.UpdatedAt, now),
                    cancellationToken);
            return updated == 1;
        }

        var link = await identityDbContext.MaxioSubscriptionLinks.SingleAsync(item => item.Id == linkId, cancellationToken);
        if (link.Status != MaxioSubscriptionLinkStatus.Pending || link.LeaseExpiresAt >= now)
        {
            return false;
        }

        link.LeaseOwner = leaseOwner;
        link.LeaseExpiresAt = expiresAt;
        link.UpdatedAt = now;
        await identityDbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<SubscriptionDto?> WaitForContenderAsync(
        Guid linkId,
        string reference,
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(ContenderWait);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            var link = await identityDbContext.MaxioSubscriptionLinks
                .AsNoTracking()
                .SingleAsync(item => item.Id == linkId, cancellationToken);
            if (link.Status == MaxioSubscriptionLinkStatus.OutcomeUnknown)
            {
                throw OutcomeUnknown();
            }

            if (link.Status == MaxioSubscriptionLinkStatus.Rejected)
            {
                throw new MaxioIntegrationException(409, "subscription_previously_rejected", "This subscription request was rejected.");
            }

            if (link.Status == MaxioSubscriptionLinkStatus.Succeeded)
            {
                return await ReconcileAsync(reference, customerId, productHandle, cancellationToken);
            }
        }

        return await ReconcileAsync(reference, customerId, productHandle, cancellationToken);
    }

    private Task MarkSucceededAsync(Guid linkId, int? subscriptionId, CancellationToken cancellationToken) =>
        UpdateLinkAsync(linkId, MaxioSubscriptionLinkStatus.Succeeded, subscriptionId, cancellationToken);

    private Task MarkOutcomeUnknownAsync(Guid linkId, CancellationToken cancellationToken) =>
        UpdateLinkAsync(linkId, MaxioSubscriptionLinkStatus.OutcomeUnknown, null, cancellationToken);

    private Task MarkRejectedAsync(Guid linkId, CancellationToken cancellationToken) =>
        UpdateLinkAsync(linkId, MaxioSubscriptionLinkStatus.Rejected, null, cancellationToken);

    private async Task UpdateLinkAsync(
        Guid linkId,
        MaxioSubscriptionLinkStatus status,
        int? subscriptionId,
        CancellationToken cancellationToken)
    {
        var link = await identityDbContext.MaxioSubscriptionLinks.SingleAsync(item => item.Id == linkId, cancellationToken);
        link.Status = status;
        link.MaxioSubscriptionId = subscriptionId;
        link.LeaseOwner = null;
        link.LeaseExpiresAt = null;
        link.UpdatedAt = DateTimeOffset.UtcNow;
        await identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseLeaseAsync(Guid linkId, string leaseOwner, CancellationToken cancellationToken)
    {
        var link = await identityDbContext.MaxioSubscriptionLinks.SingleAsync(item => item.Id == linkId, cancellationToken);
        if (link.LeaseOwner == leaseOwner && link.Status == MaxioSubscriptionLinkStatus.Pending)
        {
            link.LeaseOwner = null;
            link.LeaseExpiresAt = null;
            link.UpdatedAt = DateTimeOffset.UtcNow;
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static string CreateSubscriptionReference(string userId, string productHandle)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{productHandle}"));
        return $"eshop-{Convert.ToHexString(hash)[..40].ToLowerInvariant()}";
    }

    private static MaxioIntegrationException OutcomeUnknown() =>
        new(503, "subscription_outcome_unknown", "The subscription may have been created, but Maxio has not confirmed it yet. Retry this same request to reconcile it; no second subscription will be created.");
}
