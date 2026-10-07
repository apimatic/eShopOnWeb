using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates subscription purchases: keeps the Maxio billing system of
/// record as the source of truth and mirrors the state of each user's
/// subscriptions locally.
///
/// Idempotency: concurrent or repeated subscribe calls for the same
/// user + plan are serialized and collapsed — a duplicate call returns the
/// existing subscription instead of creating a second one in Maxio.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly ISubscriptionBillingProvider _billingProvider;
    private readonly IReadRepository<UserSubscription> _subscriptionReadRepository;
    private readonly IRepository<UserSubscription> _subscriptionWriteRepository;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(ISubscriptionBillingProvider billingProvider,
        IReadRepository<UserSubscription> subscriptionReadRepository,
        IRepository<UserSubscription> subscriptionWriteRepository,
        IAppLogger<SubscriptionService> logger)
    {
        _billingProvider = billingProvider;
        _subscriptionReadRepository = subscriptionReadRepository;
        _subscriptionWriteRepository = subscriptionWriteRepository;
        _logger = logger;
    }

    public Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        return _billingProvider.ListPlansAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserSubscription>> GetSubscriptionsForUserAsync(string userId,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _subscriptionReadRepository.ListAsync(
            new UserSubscriptionsForUserSpec(userId), cancellationToken);

        foreach (var subscription in subscriptions)
        {
            await RefreshFromBillingAsync(subscription, cancellationToken);
        }

        return subscriptions;
    }

    public async Task<UserSubscription> SubscribeAsync(string userId, string email, string fullName,
        string planHandle, CancellationToken cancellationToken)
    {
        var gate = SubscribeLocks.GetOrAdd($"{userId}|{planHandle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plans = await _billingProvider.ListPlansAsync(cancellationToken);
            if (plans.All(p => !string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
            {
                throw new SubscriptionPlanNotFoundException(planHandle);
            }

            var existing = await _subscriptionWriteRepository.FirstOrDefaultAsync(
                new UserSubscriptionForUserAndPlanSpec(userId, planHandle), cancellationToken);

            if (existing is not null)
            {
                await RefreshFromBillingAsync(existing, cancellationToken);
                if (!IsTerminal(existing.State))
                {
                    _logger.LogInformation("Subscribe is idempotent: user {UserId} already holds subscription {SubscriptionId} for plan {PlanHandle}.",
                        userId, existing.BillingSubscriptionId, planHandle);
                    return existing;
                }
            }

            var (firstName, lastName) = SplitFullName(fullName);
            var created = await _billingProvider.SubscribeAsync(userId, email, firstName, lastName,
                planHandle, cancellationToken);

            if (existing is not null)
            {
                // The previous subscription reached a terminal state; point the
                // record at the freshly created billing subscription.
                existing.RebindTo(created.Id, created.PlanPriceInCents, created.State, created.NextBillingAt);
                await _subscriptionWriteRepository.UpdateAsync(existing, cancellationToken);
                await _subscriptionWriteRepository.SaveChangesAsync(cancellationToken);
                return existing;
            }

            var record = new UserSubscription(userId, email, created.CustomerId, created.Id,
                created.PlanHandle, created.PlanName, created.PlanPriceInCents,
                created.State, created.NextBillingAt);
            await _subscriptionWriteRepository.AddAsync(record, cancellationToken);
            await _subscriptionWriteRepository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("User {UserId} subscribed to plan {PlanHandle} (billing subscription {SubscriptionId}).",
                userId, planHandle, created.Id);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RefreshFromBillingAsync(UserSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            var remote = await _billingProvider.GetSubscriptionAsync(subscription.BillingSubscriptionId,
                cancellationToken);
            subscription.Refresh(remote.State, remote.PlanPriceInCents, remote.NextBillingAt);
            await _subscriptionWriteRepository.UpdateAsync(subscription, cancellationToken);
            await _subscriptionWriteRepository.SaveChangesAsync(cancellationToken);
        }
        catch (BillingEntityNotFoundException)
        {
            _logger.LogWarning("Billing subscription {SubscriptionId} no longer exists upstream.",
                subscription.BillingSubscriptionId);
        }
    }

    private static bool IsTerminal(string state) =>
        state is "canceled" or "expired" or "failed_to_pay";

    private static (string firstName, string lastName) SplitFullName(string fullName)
    {
        var parts = (fullName ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("eShop", "Customer"),
            1 => (parts[0], "Customer"),
            _ => (parts[0], string.Join(' ', parts.Skip(1)))
        };
    }
}
