using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements the subscription billing capability on top of Maxio Advanced Billing.
/// Responsible for orchestration and idempotency; wire-level concerns live in
/// <see cref="IMaxioApiClient"/>.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// Collection method used when enrolling. "remittance" (invoice billing) activates the
    /// subscription without requiring a stored payment profile, which matches the seeded
    /// plans (payment method not required) and avoids card capture / 3-DS.
    /// </summary>
    private const string CardlessCollectionMethod = "remittance";

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        IMaxioApiClient client,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new PlanNotFoundException(planHandle ?? string.Empty);
        }

        // Validate the requested plan belongs to the configured family. This both gives a
        // clean 404 for bad handles and prevents subscribing to arbitrary site products.
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new PlanNotFoundException(planHandle);
        }

        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

        // Idempotency: if the shopper already has a live subscription to this plan, return it
        // instead of creating a duplicate (guards against double-clicks / retries).
        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Shopper '{0}' already enrolled in plan '{1}' (subscription {2}); returning existing.",
                subscriber.Reference, plan.Handle, existing.Id);
            return new SubscriptionResult(MapSubscription(existing), alreadyEnrolled: true);
        }

        var created = await _client.CreateSubscriptionAsync(
            new CreateSubscriptionDto
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = CardlessCollectionMethod,
            },
            cancellationToken);

        _logger.LogInformation(
            "Created subscription {0} for shopper '{1}' on plan '{2}'.",
            created.Id, subscriber.Reference, plan.Handle);

        return new SubscriptionResult(MapSubscription(created), alreadyEnrolled: false);
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var customer = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            // No Maxio customer yet means the shopper has never subscribed.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    /// <summary>
    /// Returns the shopper's Maxio customer, creating it if absent. Idempotent by
    /// <see cref="SubscriberIdentity.Reference"/>: concurrent creates that lose the race are
    /// recovered by re-reading the customer.
    /// </summary>
    private async Task<CustomerDto> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _client.CreateCustomerAsync(
                new CreateCustomerDto
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference,
                },
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsUnprocessable)
        {
            // Most likely a concurrent create won the race (reference must be unique).
            // Re-read; if the customer now exists, use it. Otherwise surface the error.
            var recovered = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (recovered is not null)
            {
                _logger.LogInformation(
                    "Recovered existing Maxio customer for reference '{0}' after a concurrent create.",
                    subscriber.Reference);
                return recovered;
            }

            throw;
        }
    }

    private async Task<SubscriptionDto?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            SubscriptionStates.IsLive(s.State) &&
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private SubscriptionPlan MapPlan(ProductDto product) => new(
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: checked((int)product.PriceInCents),
        interval: product.Interval,
        intervalUnit: product.IntervalUnit ?? string.Empty,
        requiresPaymentMethod: product.RequireCreditCard,
        productFamilyHandle: product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle);

    private static CustomerSubscription MapSubscription(SubscriptionDto subscription) => new(
        id: subscription.Id,
        state: subscription.State ?? "unknown",
        planHandle: subscription.Product?.Handle ?? string.Empty,
        planName: subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
        priceInCents: checked((int)subscription.ProductPriceInCents),
        currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        nextBillingAt: subscription.NextAssessmentAt,
        activatedAt: subscription.ActivatedAt,
        paymentCollectionMethod: subscription.PaymentCollectionMethod ?? string.Empty);
}
