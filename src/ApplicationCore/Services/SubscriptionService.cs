using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscribe flow on top of <see cref="ISubscriptionBillingGateway"/>. All state
/// lives in the billing system: eShopOnWeb stores no shopper-to-subscription mapping of its own, so
/// the flow stays correct across restarts and across multiple application instances.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _subscriberLocks = new(StringComparer.Ordinal);

    private readonly ISubscriptionBillingGateway _gateway;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(ISubscriptionBillingGateway gateway, IAppLogger<SubscriptionService> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        _gateway.ListPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle,
        string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // Only plans belonging to the configured product family may be subscribed to; this keeps a
        // caller from enrolling in an arbitrary product that happens to live on the same Maxio site.
        var plan = await _gateway.FindPlanAsync(planHandle.Trim(), cancellationToken)
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        // eShopOnWeb collects no card details, so a plan that demands a payment profile can never be
        // signed here. Say so up front instead of letting the billing system reject the signup.
        if (plan.RequiresPaymentMethod)
        {
            throw new PaymentMethodRequiredException(plan.Handle);
        }

        // Serialize concurrent signups for one shopper within this process. The reference-uniqueness
        // rule enforced by the billing system is what makes the flow safe across processes; this just
        // avoids the wasted round trips a double-click would otherwise cause.
        var gate = _subscriberLocks.GetOrAdd(subscriber.BillingReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);
            var existingSubscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

            string subscriptionReference;
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                subscriptionReference = BuildIdempotencyReference(subscriber, idempotencyKey!);
                var replay = existingSubscriptions.FirstOrDefault(s =>
                    string.Equals(s.Reference, subscriptionReference, StringComparison.Ordinal));
                if (replay is not null)
                {
                    _logger.LogInformation(
                        "Subscribe replay for {Reference}: returning existing subscription {SubscriptionId}.",
                        subscriptionReference, replay.Id);
                    return SubscribeResult.AlreadyExisted(replay, plan);
                }
            }
            else
            {
                var live = existingSubscriptions.FirstOrDefault(s =>
                    s.IsLive && string.Equals(s.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));
                if (live is not null)
                {
                    _logger.LogInformation(
                        "Subscriber {Reference} is already on plan {PlanHandle} (subscription {SubscriptionId}, state {State}).",
                        subscriber.BillingReference, plan.Handle, live.Id, live.State);
                    return SubscribeResult.AlreadyExisted(live, plan);
                }

                subscriptionReference = BuildPlanReference(subscriber, plan.Handle,
                    existingSubscriptions.Select(s => s.Reference));
            }

            var newSubscription = new NewSubscription
            {
                CustomerId = customer.Id,
                PlanHandle = plan.Handle,
                Reference = subscriptionReference
            };

            try
            {
                var created = await _gateway.CreateSubscriptionAsync(newSubscription, cancellationToken);
                _logger.LogInformation(
                    "Created subscription {SubscriptionId} on plan {PlanHandle} for {Reference} (state {State}).",
                    created.Id, plan.Handle, subscriber.BillingReference, created.State);
                return SubscribeResult.NewlyCreated(created, plan);
            }
            catch (BillingReferenceConflictException)
            {
                // Another request beat us to it. The reference is ours and unique, so whatever holds
                // it now is exactly the subscription this call was asking for.
                var winner = await _gateway.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (winner is null)
                {
                    throw;
                }

                _logger.LogWarning(
                    "Concurrent subscribe for {Reference} resolved to existing subscription {SubscriptionId}.",
                    subscriptionReference, winner.Id);
                return SubscribeResult.AlreadyExisted(winner, plan);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));

        var customer = await _gateway.FindCustomerByReferenceAsync(subscriber.BillingReference, cancellationToken);
        if (customer is null)
        {
            // A shopper who has never subscribed has no billing customer, which is not an error.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.OrderByDescending(s => s.CreatedAt).ToList();
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _gateway.FindCustomerByReferenceAsync(subscriber.BillingReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new NewBillingCustomer
        {
            Reference = subscriber.BillingReference,
            Email = subscriber.Email,
            FirstName = subscriber.ResolvedFirstName,
            LastName = subscriber.ResolvedLastName
        };

        try
        {
            var created = await _gateway.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation("Created billing customer {CustomerId} for {Reference}.",
                created.Id, subscriber.BillingReference);
            return created;
        }
        catch (BillingReferenceConflictException)
        {
            // Two requests raced to create the same customer; the loser reads back the winner's.
            var winner = await _gateway.FindCustomerByReferenceAsync(subscriber.BillingReference, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            _logger.LogWarning("Concurrent customer creation for {Reference} resolved to {CustomerId}.",
                subscriber.BillingReference, winner.Id);
            return winner;
        }
    }

    private static string BuildIdempotencyReference(SubscriberIdentity subscriber, string idempotencyKey) =>
        $"{subscriber.BillingReference}:key:{Slug(idempotencyKey)}";

    /// <summary>
    /// Default reference for a signup: stable per shopper and plan, so the very first attempt already
    /// carries a reference the billing system will refuse to hand out twice. A numeric suffix is only
    /// needed when the shopper is re-subscribing to a plan they previously cancelled.
    /// </summary>
    private static string BuildPlanReference(SubscriberIdentity subscriber, string planHandle, IEnumerable<string?> taken)
    {
        var baseReference = $"{subscriber.BillingReference}:{planHandle.ToLowerInvariant()}";
        var used = new HashSet<string>(taken.Where(r => r is not null).Select(r => r!), StringComparer.Ordinal);

        if (!used.Contains(baseReference))
        {
            return baseReference;
        }

        for (var attempt = 2; attempt < 1000; attempt++)
        {
            var candidate = $"{baseReference}:{attempt}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseReference}:{Guid.NewGuid():N}";
    }

    /// <summary>Reduces a caller-supplied token to characters that are safe inside a reference.</summary>
    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? char.ToLowerInvariant(c) : '-');
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "key" : slug.Substring(0, Math.Min(slug.Length, 64));
    }
}
