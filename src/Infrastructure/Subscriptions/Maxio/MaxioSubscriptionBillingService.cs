using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing as the system of record.
/// No subscription state is persisted locally — Maxio is queried on every call — which keeps the flow
/// idempotent across application restarts (there is no local userId-to-subscription mapping to lose).
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly KeyedAsyncLock _subscriberLock;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptions<MaxioSettings> settings,
        KeyedAsyncLock subscriberLock,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _subscriberLock = subscriberLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsInFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new PlanNotFoundException(planHandle ?? string.Empty);

        // 1. Validate the plan against the configured product family (handles are stable; ids are not).
        var products = await _client.ListProductsInFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        var product = products.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && p.ArchivedAt is null);
        if (product is null)
            throw new PlanNotFoundException(planHandle);

        // Serialize per-subscriber so a rapid double-click cannot create duplicate customers/subscriptions.
        using (await _subscriberLock.LockAsync(subscriber.Reference, cancellationToken))
        {
            // 2. Ensure a Maxio customer exists for this subscriber (idempotent on the reference).
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            // 3. If the subscriber already has a live subscription to this plan, return it (no duplicate).
            var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var alreadySubscribed = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, product.Handle, StringComparison.OrdinalIgnoreCase)
                && MapSubscription(s, subscriber).IsLive);
            if (alreadySubscribed is not null)
            {
                _logger.LogInformation(
                    "Subscriber {Reference} already has live subscription {SubscriptionId} to plan {Plan}; returning it.",
                    subscriber.Reference, alreadySubscribed.Id, product.Handle);
                return new SubscriptionResult(MapSubscription(alreadySubscribed, subscriber), alreadyExisted: true);
            }

            // 4. Create the subscription without requiring a stored payment method.
            var created = await _client.CreateSubscriptionAsync(
                new CreateSubscriptionBody
                {
                    ProductHandle = product.Handle!,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = "remittance"
                },
                cancellationToken);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} for subscriber {Reference} on plan {Plan}.",
                created.Id, subscriber.Reference, product.Handle);
            return new SubscriptionResult(MapSubscription(created, subscriber), alreadyExisted: false);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        var customer = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
            return Array.Empty<CustomerSubscription>();

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(s => MapSubscription(s, subscriber))
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
            return existing;

        try
        {
            return await _client.CreateCustomerAsync(
                new CreateCustomerBody
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference
                },
                cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            // A concurrent request may have created the customer first (reference must be unique).
            // Re-look it up rather than failing the subscribe.
            _logger.LogWarning(
                "Create customer for {Reference} failed ({Errors}); re-checking for an existing customer.",
                subscriber.Reference, string.Join("; ", ex.Errors));

            var recovered = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (recovered is not null)
                return recovered;

            throw;
        }
    }

    private SubscriptionPlan MapPlan(MaxioProduct product) => new(
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: product.PriceInCents,
        interval: product.Interval,
        intervalUnit: product.IntervalUnit ?? "month",
        productFamilyHandle: product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle);

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, SubscriberIdentity subscriber) => new(
        id: subscription.Id,
        state: subscription.State ?? "unknown",
        planHandle: subscription.Product?.Handle ?? string.Empty,
        planName: subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
        priceInCents: subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0,
        currentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
        currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        nextBillingAt: subscription.NextAssessmentAt,
        createdAt: subscription.CreatedAt,
        customerReference: subscription.Customer?.Reference ?? subscriber.Reference);
}
