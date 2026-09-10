using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the eShopOnWeb subscribe flow against Maxio: lists plans from the configured product family,
/// ensures a single Maxio customer per shopper, and enrolls them idempotently. Maxio is the system of record;
/// no billing state is persisted locally.
/// </summary>
internal sealed class MaxioBillingService : IMaxioBillingService
{
    // Invoice-based collection so a subscription activates without a stored payment method (the eShop plans
    // require no card). See Collection-Method in the spec.
    private const string RemittanceCollection = "remittance";

    // Maxio products in this integration are priced in the site's default currency (USD in the sandbox).
    // Subscriptions echo their own currency, which we prefer when present.
    private const string DefaultCurrency = "USD";

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(IMaxioApiClient client, IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // Only allow subscribing to plans that belong to the configured product family. This both gives the
        // caller a clear error for an unknown plan and prevents enrolling in arbitrary products.
        var plans = await GetPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MaxioBillingException(
                $"'{planHandle}' is not an available subscription plan.", upstreamStatusCode: 404);
        }

        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

        // Idempotency: if the shopper already has a live subscription to this plan, return it rather than
        // creating a duplicate (guards against double-clicks / retries).
        var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var alreadySubscribed = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !SubscriptionStates.Inactive.Contains(s.State ?? string.Empty));

        if (alreadySubscribed is not null)
        {
            _logger.LogInformation(
                "Shopper {Reference} already subscribed to {Plan} (subscription {SubscriptionId}); returning existing.",
                subscriber.Reference, planHandle, alreadySubscribed.Id);
            return new SubscribeResult(MapSubscription(alreadySubscribed), alreadyExisted: true);
        }

        var created = await _client.CreateSubscriptionAsync(new CreateSubscriptionBody
        {
            ProductHandle = planHandle,
            CustomerId = customer.Id,
            PaymentCollectionMethod = RemittanceCollection,
            Reference = BuildSubscriptionReference(subscriber.Reference, planHandle),
        }, cancellationToken);

        _logger.LogInformation(
            "Created subscription {SubscriptionId} to {Plan} for shopper {Reference} (Maxio customer {CustomerId}).",
            created.Id, planHandle, subscriber.Reference, customer.Id);

        return new SubscribeResult(MapSubscription(created), alreadyExisted: false);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));

        var customer = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            // No Maxio customer yet means the shopper has never subscribed.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    /// <summary>
    /// Returns the shopper's Maxio customer, creating it if absent. Idempotent even under concurrent
    /// first-time subscribes: Maxio enforces a unique customer <c>reference</c>, so a create that loses a
    /// race returns 422 and we re-resolve the now-existing customer.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _client.CreateCustomerAsync(new CreateCustomerBody
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference,
            }, cancellationToken);
        }
        catch (MaxioBillingException ex) when (ex.UpstreamStatusCode == 422)
        {
            // Either the reference was taken by a concurrent create, or the payload was rejected. Re-resolve;
            // if a customer now exists for this reference, use it. Otherwise the failure was genuine.
            var afterRace = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (afterRace is not null)
            {
                _logger.LogInformation(
                    "Customer for {Reference} already existed (concurrent create); using existing.",
                    subscriber.Reference);
                return afterRace;
            }

            throw;
        }
    }

    private static string BuildSubscriptionReference(string subscriberReference, string planHandle)
        => $"eshop-sub:{subscriberReference}:{planHandle}";

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new(
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: product.PriceInCents,
        currency: DefaultCurrency,
        interval: product.Interval,
        intervalUnit: product.IntervalUnit ?? string.Empty);

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : product?.PriceInCents ?? 0;

        return new CustomerSubscription(
            id: subscription.Id,
            state: subscription.State ?? "unknown",
            planHandle: product?.Handle ?? string.Empty,
            planName: product?.Name ?? product?.Handle ?? string.Empty,
            priceInCents: priceInCents,
            currency: string.IsNullOrWhiteSpace(subscription.Currency) ? DefaultCurrency : subscription.Currency!,
            intervalUnit: product?.IntervalUnit ?? string.Empty,
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            nextAssessmentAt: subscription.NextAssessmentAt,
            createdAt: subscription.CreatedAt,
            reference: subscription.Reference);
    }
}
