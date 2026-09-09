using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio-backed implementation of the subscription-billing capability. Orchestrates the low-level
/// <see cref="IMaxioClient"/> calls, applies idempotency, and maps Maxio contracts to eShopOnWeb models.
/// </summary>
public sealed class MaxioBillingService : ISubscriptionBillingService
{
    private readonly IMaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly KeyedAsyncLock _subscribeLock;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        IMaxioClient client,
        IOptions<MaxioSettings> settings,
        KeyedAsyncLock subscribeLock,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _subscribeLock = subscribeLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new BillingException(
                "Maxio is not configured: 'Maxio:ProductFamilyHandle' is required to list subscription plans.");
        }

        var products = await _client
            .ListProductsForFamilyAsync($"handle:{_settings.ProductFamilyHandle}", cancellationToken)
            .ConfigureAwait(false);

        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan!)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        BillingUser user, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(user, nameof(user));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // Serialize concurrent subscribe attempts for the same user (e.g. a double-click) so we cannot
        // race two customer-creation / subscription-creation flows against each other.
        using (await _subscribeLock.LockAsync(user.Reference, cancellationToken).ConfigureAwait(false))
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken).ConfigureAwait(false);

            // Idempotency: if the user already has a live subscription to this plan, return it unchanged.
            var existingSubscriptions = await _client
                .ListCustomerSubscriptionsAsync(customer.Id, cancellationToken)
                .ConfigureAwait(false);

            var existing = existingSubscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(MapSubscription!)
                .FirstOrDefault(s => s.IsLive &&
                    string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {Reference} already has a live subscription {SubscriptionId} to plan {PlanHandle}; returning it.",
                    user.Reference, existing.Id, planHandle);
                return new SubscribeResult(existing, alreadyExisted: true);
            }

            var created = await _client.CreateSubscriptionAsync(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscriptionBody
                    {
                        ProductHandle = planHandle,
                        CustomerId = customer.Id,
                        // Invoice-based collection: create the subscription without capturing a card.
                        PaymentCollectionMethod = "remittance",
                    },
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} ({State}) for user {Reference} on plan {PlanHandle}.",
                created.Id, created.State, user.Reference, planHandle);

            return new SubscribeResult(MapSubscription(created), alreadyExisted: false);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(
        BillingUser user, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(user, nameof(user));

        var customer = await _client.LookupCustomerByReferenceAsync(user.Reference, cancellationToken)
            .ConfigureAwait(false);

        // No billing customer yet means the user has never subscribed.
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken)
            .ConfigureAwait(false);

        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(MapSubscription!)
            .ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for the user, creating one if none exists. Idempotent on the user's
    /// stable <see cref="BillingUser.Reference"/>; tolerates a concurrent creation that wins the race by
    /// re-reading the customer after a uniqueness (422) rejection.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(BillingUser user, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(user.Reference, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.CreateCustomerAsync(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomerBody
                    {
                        FirstName = user.FirstName,
                        LastName = user.LastName,
                        Email = user.Email,
                        Reference = user.Reference,
                    },
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Created Maxio customer {CustomerId} for user {Reference}.", created.Id, user.Reference);
            return created;
        }
        catch (BillingException ex) when (ex.UpstreamStatusCode == 422)
        {
            // A concurrent request created the customer first (reference must be unique). Re-read it.
            _logger.LogInformation(
                "Customer creation for {Reference} was rejected as a duplicate; re-reading existing customer.",
                user.Reference);

            var afterRace = await _client.LookupCustomerByReferenceAsync(user.Reference, cancellationToken)
                .ConfigureAwait(false);
            return afterRace ?? throw new BillingException(
                "Maxio rejected customer creation as a duplicate but no matching customer could be found.",
                upstreamStatusCode: ex.UpstreamStatusCode, upstreamBody: ex.UpstreamBody, innerException: ex);
        }
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard,
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
    };
}
