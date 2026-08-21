using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(45);
    private readonly IMaxioBillingGateway _gateway;
    private readonly IBillingLinkStore _store;

    public SubscriptionBillingService(IMaxioBillingGateway gateway, IBillingLinkStore store)
    {
        _gateway = gateway;
        _store = store;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
        _gateway.GetPlansAsync(cancellationToken);

    public async Task<SubscribeOutcome> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "A product handle is required.");
        }

        var gate = UserLocks.GetOrAdd(user.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plans = await _gateway.GetPlansAsync(cancellationToken);
            if (!plans.Any(plan => string.Equals(plan.Handle, productHandle, StringComparison.Ordinal)))
            {
                throw new BillingException(BillingErrorKind.NotFound, "The requested subscription plan is unavailable.");
            }

            var customerReference = CustomerReference(user.UserId);
            var subscriptionReference = SubscriptionReference(user.UserId, productHandle);
            var claim = await _store.ClaimSubscriptionAsync(
                user.UserId,
                productHandle,
                subscriptionReference,
                DateTimeOffset.UtcNow,
                cancellationToken);

            if (claim.Status == BillingClaimStatus.Completed)
            {
                return new SubscribeOutcome(claim.Confirmation, Created: false, InProgress: false);
            }

            if (claim.Status == BillingClaimStatus.InProgress)
            {
                return new SubscribeOutcome(null, Created: false, InProgress: true);
            }

            if (claim.Status == BillingClaimStatus.TerminalFailure)
            {
                throw new BillingException(BillingErrorKind.Conflict, "This plan already has a terminal subscription record.");
            }

            var leaseId = claim.LeaseId!;
            try
            {
                var existing = await _gateway.FindSubscriptionAsync(
                    subscriptionReference,
                    customerReference,
                    productHandle,
                    cancellationToken);
                if (existing != null)
                {
                    await _store.UpsertRecoveredCustomerAsync(user.UserId, customerReference, cancellationToken);
                    await _store.CompleteSubscriptionAsync(user.UserId, productHandle, leaseId, existing, cancellationToken);
                    return new SubscribeOutcome(existing, Created: false, InProgress: false);
                }

                var customerClaim = await _store.ClaimCustomerAsync(
                    user.UserId,
                    customerReference,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                if (customerClaim.Status == BillingClaimStatus.InProgress)
                {
                    return new SubscribeOutcome(null, Created: false, InProgress: true);
                }

                if (customerClaim.Status == BillingClaimStatus.TerminalFailure)
                {
                    throw new BillingException(BillingErrorKind.Conflict, "The billing customer could not be provisioned.");
                }

                if (customerClaim.Status == BillingClaimStatus.Acquired)
                {
                    try
                    {
                        await _gateway.EnsureCustomerAsync(user, customerReference, cancellationToken);
                        await _store.CompleteCustomerAsync(user.UserId, customerClaim.LeaseId!, cancellationToken);
                    }
                    catch (BillingException ex)
                    {
                        await _store.FailCustomerAsync(
                            user.UserId,
                            customerClaim.LeaseId!,
                            IsRetryable(ex),
                            ex.Message,
                            cancellationToken);
                        throw;
                    }
                }

                var created = await _gateway.CreateSubscriptionAsync(
                    subscriptionReference,
                    customerReference,
                    productHandle,
                    cancellationToken);
                await _store.CompleteSubscriptionAsync(user.UserId, productHandle, leaseId, created, cancellationToken);
                return new SubscribeOutcome(created, Created: true, InProgress: false);
            }
            catch (BillingException ex)
            {
                await _store.FailSubscriptionAsync(
                    user.UserId,
                    productHandle,
                    leaseId,
                    IsRetryable(ex),
                    ex.Message,
                    cancellationToken);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionConfirmation>> GetSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken)
    {
        var customerReference = CustomerReference(user.UserId);
        var links = await _store.ListSubscriptionsAsync(user.UserId, cancellationToken);
        var confirmations = new List<SubscriptionConfirmation>();

        if (links.Count == 0)
        {
            var plans = await _gateway.GetPlansAsync(cancellationToken);
            foreach (var plan in plans)
            {
                var recovered = await _gateway.FindSubscriptionAsync(
                    SubscriptionReference(user.UserId, plan.Handle),
                    customerReference,
                    plan.Handle,
                    cancellationToken);
                if (recovered == null)
                {
                    continue;
                }

                await _store.UpsertRecoveredCustomerAsync(user.UserId, customerReference, cancellationToken);
                await _store.UpsertRecoveredSubscriptionAsync(user.UserId, recovered, cancellationToken);
                confirmations.Add(recovered);
            }

            return confirmations;
        }

        foreach (var link in links.Where(link => link.Status == BillingLinkStatus.Completed))
        {
            var refreshed = await _gateway.FindSubscriptionAsync(
                link.SubscriptionReference,
                customerReference,
                link.ProductHandle,
                cancellationToken);
            if (refreshed == null)
            {
                continue;
            }

            await _store.UpsertRecoveredSubscriptionAsync(user.UserId, refreshed, cancellationToken);
            confirmations.Add(refreshed);
        }

        return confirmations;
    }

    public static string CustomerReference(string userId) =>
        "eshop-customer-v1-" + Hash(userId);

    public static string SubscriptionReference(string userId, string productHandle) =>
        "eshop-sub-v1-" + Hash(userId + "\n" + productHandle);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool IsRetryable(BillingException exception) => exception.Kind is
        BillingErrorKind.Throttled or
        BillingErrorKind.Unavailable or
        BillingErrorKind.InvalidProviderResponse;
}
