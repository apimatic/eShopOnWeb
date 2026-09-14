using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements the application's subscription-billing capability on top of
/// <see cref="IMaxioClient"/>. Owns the idempotency rules for the Subscribe flow:
/// one billing customer per eShop user (keyed by reference) and no duplicate active
/// subscription for the same plan.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// States in which a subscription is considered ended, so a shopper may subscribe
    /// to the plan again. Any other state is treated as a live subscription for the
    /// purpose of duplicate prevention.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    // Invoice-based collection so a subscription can activate without a stored payment
    // method (the demo plans do not require a card). Value defined by Collection-Method.yaml.
    private const string RemittanceCollection = "remittance";

    private readonly IMaxioClient _client;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(IMaxioClient client, MaxioOptions options, IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_options.ProductFamilyHandle!, cancellationToken);

        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new UnknownSubscriptionPlanException(planHandle ?? string.Empty);
        }

        // Validate the requested plan belongs to the configured product family. This
        // keeps the endpoint catalog-agnostic and prevents subscribing to arbitrary products.
        var products = await _client.ListProductsForFamilyAsync(_options.ProductFamilyHandle!, cancellationToken);
        var plan = products.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new UnknownSubscriptionPlanException(planHandle);
        }

        // Ensure a single billing customer exists for this eShop user (idempotent).
        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

        // Idempotency: if the customer already has a live subscription to this plan,
        // return it instead of creating a duplicate (e.g. on a double-click).
        var existingSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.Ordinal) && IsLive(s));

        if (existing is not null)
        {
            _logger.LogInformation(
                "Reusing existing {State} subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
                existing.State ?? "unknown", existing.Id, customer.Id, planHandle);
            return MapSubscription(existing, planHandle, customer.Id, alreadyExisted: true);
        }

        // Deterministic, app-owned reference so concurrent subscribe requests (double-click)
        // cannot create two subscriptions: Maxio enforces reference uniqueness per site, so
        // only one create wins and the loser reconciles to the winner below.
        var subscriptionReference = BuildSubscriptionReference(subscriber.Reference, planHandle);

        try
        {
            var created = await _client.CreateSubscriptionAsync(
                new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = RemittanceCollection,
                    Reference = subscriptionReference
                },
                cancellationToken);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {PlanHandle}.",
                created.Id, created.State ?? "unknown", customer.Id, planHandle);

            return MapSubscription(created, planHandle, customer.Id, alreadyExisted: false);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A competing request may have created the subscription first (reference taken).
            // Reconcile to that subscription instead of surfacing an error or duplicating.
            var reconciled = await _client.LookupSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
            {
                _logger.LogInformation(
                    "Reconciled concurrent subscribe to existing subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
                    reconciled.Id, customer.Id, planHandle);
                return MapSubscription(reconciled, planHandle, customer.Id, alreadyExisted: true);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        var customer = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(s => MapSubscription(s, s.Product?.Handle ?? string.Empty, customer.Id, alreadyExisted: false))
            .ToList();
    }

    /// <summary>
    /// Returns the billing customer for the subscriber, creating one if none exists.
    /// The customer <c>reference</c> is unique in Maxio, so concurrent creates are
    /// reconciled by re-reading the customer after a create conflict.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.CreateCustomerAsync(
                new CreateCustomer
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference
                },
                cancellationToken);

            _logger.LogInformation("Created billing customer {CustomerId} for reference {Reference}.",
                created.Id, subscriber.Reference);
            return created;
        }
        catch (MaxioApiException ex)
        {
            // A concurrent request may have created the customer between our lookup and
            // create (the reference must be unique). Re-read and use the winner.
            _logger.LogWarning("Create customer for reference {Reference} failed ({Message}); re-checking for an existing customer.",
                subscriber.Reference, ex.Message);

            var rechecked = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (rechecked is not null)
            {
                return rechecked;
            }

            throw;
        }
    }

    private static bool IsLive(MaxioSubscription subscription)
        => subscription.State is null || !TerminalStates.Contains(subscription.State);

    /// <summary>
    /// Builds a stable, bounded subscription reference from the subscriber reference and
    /// plan handle. Hashing keeps it within Maxio's reference length limit regardless of
    /// how long the source values are, while remaining deterministic for idempotency.
    /// </summary>
    private static string BuildSubscriptionReference(string subscriberReference, string planHandle)
    {
        var input = $"{subscriberReference}|{planHandle}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hex = Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        return $"eshopsub-{hex}";
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product)
        => new(
            productId: product.Id,
            handle: product.Handle!,
            name: product.Name ?? product.Handle!,
            description: string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
            priceInCents: product.PriceInCents,
            interval: product.Interval,
            intervalUnit: product.IntervalUnit ?? string.Empty);

    private static SubscriptionDetails MapSubscription(MaxioSubscription subscription, string planHandle, long customerId, bool alreadyExisted)
        => new(
            id: subscription.Id,
            state: subscription.State ?? "unknown",
            planHandle: subscription.Product?.Handle ?? planHandle,
            planName: subscription.Product?.Name ?? planHandle,
            priceInCents: subscription.ProductPriceInCents != 0
                ? subscription.ProductPriceInCents
                : subscription.Product?.PriceInCents ?? 0,
            interval: subscription.Product?.Interval ?? 0,
            intervalUnit: subscription.Product?.IntervalUnit ?? string.Empty,
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            nextBillingAt: subscription.NextAssessmentAt,
            paymentCollectionMethod: subscription.PaymentCollectionMethod,
            customerId: subscription.Customer?.Id ?? customerId,
            customerReference: subscription.Customer?.Reference,
            alreadyExisted: alreadyExisted);
}
