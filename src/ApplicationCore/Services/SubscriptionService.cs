using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscribe flow on top of <see cref="IBillingGateway"/>. All duplicate-suppression
/// policy lives here; the gateway stays a thin, spec-faithful adapter.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingGateway _billingGateway;
    private readonly KeyedAsyncLock _subscribeLock;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingGateway billingGateway, KeyedAsyncLock subscribeLock, IAppLogger<SubscriptionService> logger)
    {
        _billingGateway = billingGateway;
        _subscribeLock = subscribeLock;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingGateway.ListPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request, nameof(request));

        var plan = await ResolvePlanAsync(request.PlanHandle, cancellationToken);
        var userKey = request.Subscriber.UserKey;
        var idempotencyKey = request.IdempotencyKey ?? plan.Handle;

        // Serialise concurrent signups for the same shopper+key so a double-click cannot race past
        // the duplicate checks below and enroll them twice.
        using (await _subscribeLock.LockAsync($"{userKey}|{idempotencyKey}", cancellationToken))
        {
            var customer = await _billingGateway.EnsureCustomerAsync(request.Subscriber, cancellationToken);

            var replay = await _billingGateway.FindSubscriptionAsync(userKey, idempotencyKey, cancellationToken);
            if (replay is not null)
            {
                // An explicit idempotency key means "give me back exactly what that call produced",
                // whatever state it ended up in. Without one, only a still-live subscription counts:
                // a shopper whose subscription was canceled is free to sign up again.
                if (request.IdempotencyKey is not null || replay.IsLive)
                {
                    _logger.LogInformation(
                        "Subscribe replay for {0} on plan {1}: returning existing subscription {2} ({3}).",
                        userKey, plan.Handle, replay.Id, replay.State);
                    return new SubscribeResult(replay, Created: false);
                }
            }

            var existing = (await _billingGateway.ListSubscriptionsAsync(customer.Id, cancellationToken))
                .FirstOrDefault(s => s.IsLive && string.Equals(s.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Customer {0} is already subscribed to plan {1} (subscription {2}, {3}); not creating another.",
                    customer.Id, plan.Handle, existing.Id, existing.State);
                return new SubscribeResult(existing, Created: false);
            }

            // A terminal subscription is still occupying the derived reference, so give the new one a
            // distinct reference rather than colliding with the shopper's billing history.
            var reference = replay is null
                ? idempotencyKey
                : $"{idempotencyKey}:{DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}";

            var subscription = await _billingGateway.CreateSubscriptionAsync(
                customer.Id, plan.Handle, userKey, reference, cancellationToken);

            _logger.LogInformation(
                "Created subscription {0} for customer {1} on plan {2} ({3}).",
                subscription.Id, customer.Id, plan.Handle, subscription.State);

            return new SubscribeResult(subscription, Created: true);
        }
    }

    public async Task<SubscriberSubscriptions> GetSubscriptionsAsync(string userKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userKey, nameof(userKey));

        var customer = await _billingGateway.FindCustomerAsync(userKey, cancellationToken);
        if (customer is null)
        {
            return new SubscriberSubscriptions(null, Array.Empty<CustomerSubscription>());
        }

        var subscriptions = await _billingGateway.ListSubscriptionsAsync(customer.Id, cancellationToken);
        var ordered = subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(s => s.Id)
            .ToList();

        return new SubscriberSubscriptions(customer, ordered);
    }

    /// <summary>
    /// Only plans in the configured product family are subscribable, so resolve the requested handle
    /// against that catalog rather than trusting the caller's string straight through to the provider.
    /// A request that names no plan falls back to the configured default.
    /// </summary>
    private async Task<SubscriptionPlan> ResolvePlanAsync(string? planHandle, CancellationToken cancellationToken)
    {
        var handle = planHandle ?? _billingGateway.DefaultPlanHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new PlanNotSpecifiedException();
        }

        var plans = await _billingGateway.ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new PlanNotFoundException(handle!, _billingGateway.ProductFamilyHandle);
        }

        return plan;
    }
}
