using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscription flow against Maxio and owns the idempotency guarantees: a given
/// eShopOnWeb user maps to exactly one Maxio customer (keyed by reference), and a repeated subscribe
/// request never produces a second live subscription to the same plan.
/// </summary>
internal sealed class MaxioBillingService : ISubscriptionBillingService
{
    // Maxio subscription states that mean "the shopper is already enrolled in this plan". End-of-life
    // states (canceled, expired, trial_ended, ...) are excluded so a shopper can re-subscribe later.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due", "soft_failure", "paused"
    };

    private readonly IMaxioApiClient _apiClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        IMaxioApiClient apiClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _apiClient = apiClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _apiClient.ListProductsForFamilyAsync(ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .Select(ToPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        BillingSubscriber subscriber,
        string? planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var plan = await ResolvePlanAsync(planHandle, cancellationToken);

        // 1) One Maxio customer per eShopOnWeb user (idempotent lookup-or-create keyed by reference).
        var customerId = await EnsureCustomerAsync(subscriber, cancellationToken);

        // 2) If the shopper already has a live subscription to this plan, return it unchanged.
        var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Subscriber {Reference} already has live subscription {SubscriptionId} to plan {Plan}; returning it.",
                subscriber.UserId, existing.Id, plan.Handle);
            var mapped = ToSubscription(existing);
            mapped.AlreadyExisted = true;
            return mapped;
        }

        // 3) Create it. A deterministic uniqueness token makes a rapid double-click collide server-side.
        var token = DeterministicToken(subscriber.UserId, plan.Handle);
        MaxioSubscription created;
        try
        {
            created = await _apiClient.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customerId,
                    UniquenessToken = token
                },
                cancellationToken);
        }
        catch (MaxioDuplicateSubmissionException)
        {
            // A concurrent request won the race. Prefer returning that winner.
            var winner = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
            if (winner is not null)
            {
                var mapped = ToSubscription(winner);
                mapped.AlreadyExisted = true;
                return mapped;
            }

            // No live subscription exists (e.g. re-subscribing shortly after a cancel reused the token).
            // Retry once with a fresh token so a legitimate new subscription can still be created.
            _logger.LogInformation(
                "Duplicate-submission for subscriber {Reference} on plan {Plan} but no live subscription found; retrying with a fresh token.",
                subscriber.UserId, plan.Handle);
            created = await _apiClient.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customerId,
                    UniquenessToken = Guid.NewGuid().ToString()
                },
                cancellationToken);
        }

        _logger.LogInformation(
            "Created subscription {SubscriptionId} ({State}) for subscriber {Reference} on plan {Plan}.",
            created.Id, created.State, subscriber.UserId, plan.Handle);

        return ToSubscription(created);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForAsync(
        BillingSubscriber subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var customer = await _apiClient.LookupCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
        if (customer is null)
        {
            // No billing customer yet means the shopper has never subscribed.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(ToSubscription)
            .ToList();
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string? planHandle, CancellationToken cancellationToken)
    {
        var plans = await GetAvailablePlansAsync(cancellationToken);
        if (plans.Count == 0)
        {
            throw new BillingException(
                $"No subscription plans are available in product family '{ProductFamilyHandle}'.", 409);
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            // No explicit choice: default to the lowest-priced plan (plans are ordered by price).
            return plans[0];
        }

        var match = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new BillingException(
                $"Unknown plan '{planHandle}'. Choose one of: {string.Join(", ", plans.Select(p => p.Handle))}.", 400);
        }

        return match;
    }

    private async Task<int> EnsureCustomerAsync(BillingSubscriber subscriber, CancellationToken cancellationToken)
    {
        var existing = await _apiClient.LookupCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        try
        {
            var created = await _apiClient.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.UserId
                },
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for subscriber {Reference}.",
                created.Id, subscriber.UserId);
            return created.Id;
        }
        catch (MaxioDuplicateReferenceException)
        {
            // A concurrent request created the customer first; re-read it.
            var raced = await _apiClient.LookupCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced.Id;
            }

            throw new BillingException(
                $"Maxio reported customer reference '{subscriber.UserId}' as taken but it could not be found.", 502);
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Where(s => s.State is not null && LiveStates.Contains(s.State))
            .Where(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private string ProductFamilyHandle =>
        !string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle)
            ? _settings.ProductFamilyHandle
            : throw new BillingException("Maxio is not configured: 'Maxio:ProductFamilyHandle' is required.", 500);

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new(
        productId: product.Id,
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: product.PriceInCents,
        interval: product.Interval,
        intervalUnit: product.IntervalUnit ?? "month");

    private static CustomerSubscription ToSubscription(MaxioSubscription subscription) => new(
        id: subscription.Id,
        state: subscription.State ?? "unknown",
        customerId: subscription.Customer?.Id ?? 0,
        customerReference: subscription.Customer?.Reference,
        planHandle: subscription.Product?.Handle,
        planName: subscription.Product?.Name,
        productPriceInCents: subscription.ProductPriceInCents,
        currentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
        currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        nextBillingAt: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        createdAt: subscription.CreatedAt);

    /// <summary>
    /// Produces a stable uniqueness token for a (user, plan) pair so a rapid double-click is rejected
    /// by Maxio as a duplicate submission instead of creating two subscriptions.
    /// </summary>
    private static string DeterministicToken(string userId, string planHandle)
    {
        var input = $"eshoponweb-subscribe:{userId}:{planHandle}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash).ToString();
    }
}
