using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscription-billing flows against Maxio and maps Maxio's REST DTOs to the
/// application's domain models. Owns the idempotency guarantees: one Maxio customer per user
/// (keyed by the user's stable reference) and no duplicate live subscription to a plan.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Non-terminal subscription states. A user in any of these is considered already subscribed,
    // so a repeat subscribe request returns the existing subscription instead of creating a new one.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "assessing", "past_due", "soft_failure", "paused",
    };

    private readonly MaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioClient client, IOptions<MaxioSettings> settings, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _settings.ProductFamilyHandle
            ?? throw new SubscriptionBillingException("Maxio product family is not configured.");

        var products = await _client.ListProductsForFamilyAsync(familyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionBillingException("A plan handle is required to subscribe.", 400);
        }

        var (customer, customerCreated) = await EnsureCustomerAsync(subscriber, cancellationToken);

        // Idempotency: if the user already has a live subscription to this plan, return it as-is.
        var existing = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Customer {CustomerId} already subscribed to plan {PlanHandle} (subscription {SubscriptionId}); returning existing.",
                customer.Id, planHandle, existing.Id);
            return new SubscribeResult { Subscription = Map(existing), AlreadySubscribed = true, CustomerCreated = customerCreated };
        }

        try
        {
            var uniquenessToken = Guid.NewGuid().ToString("N");
            var created = await _client.CreateSubscriptionAsync(customer.Id, planHandle, uniquenessToken, cancellationToken);
            _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
                created.Id, customer.Id, planHandle);
            return new SubscribeResult { Subscription = Map(created), AlreadySubscribed = false, CustomerCreated = customerCreated };
        }
        catch (SubscriptionBillingException ex) when (ex.StatusCode == 409)
        {
            // A concurrent duplicate request won the race. Re-read and return the surviving subscription.
            _logger.LogWarning("Duplicate subscription request detected for customer {CustomerId} on plan {PlanHandle}; returning existing.",
                customer.Id, planHandle);
            var winner = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
            if (winner is not null)
            {
                return new SubscribeResult { Subscription = Map(winner), AlreadySubscribed = true, CustomerCreated = customerCreated };
            }

            throw;
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var customer = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(Map)
            .ToList();
    }

    /// <summary>Ensures a single Maxio customer exists for the user (idempotent by reference).</summary>
    private async Task<(CustomerDto Customer, bool Created)> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        try
        {
            var created = await _client.CreateCustomerAsync(new CustomerAttributesDto
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference,
            }, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", created.Id, subscriber.Reference);
            return (created, true);
        }
        catch (SubscriptionBillingException ex) when (ex.StatusCode == 422)
        {
            // The reference must be unique. A concurrent request may have created the customer
            // in the meantime, in which case re-looking up recovers it.
            var afterRace = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (afterRace is not null)
            {
                return (afterRace, false);
            }

            throw;
        }
    }

    private async Task<SubscriptionDto?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.Ordinal)
            && s.State is not null
            && LiveStates.Contains(s.State));
    }

    private static SubscriptionPlan MapPlan(ProductDto product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        FormattedPrice = FormatMoney(product.PriceInCents),
        Currency = "USD",
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
    };

    private static CustomerSubscription Map(SubscriptionDto subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        FormattedPrice = FormatMoney(subscription.ProductPriceInCents),
        Currency = "USD",
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        CurrentPeriodStartsAt = subscription.CurrentPeriodStartedAt,
        NextBillingAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
    };

    private static string FormatMoney(long cents)
    {
        var amount = (decimal)cents / 100m;
        return "$" + amount.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
