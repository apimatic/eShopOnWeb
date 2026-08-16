using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the eShopOnWeb subscription flows against Maxio Advanced Billing: listing
/// plans, ensuring a single billing customer per shopper, and enrolling shoppers idempotently.
/// Maps vendor wire models to the framework-neutral domain models in ApplicationCore.
/// </summary>
internal sealed class MaxioBillingService : ISubscriptionBillingService
{
    // Subscription states that are "gone" — a shopper with only these may subscribe again.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended",
    };

    // Serializes ensure-customer + subscribe per shopper so a double-click can never create
    // two customers or two subscriptions within this process.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ReferenceLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(IMaxioApiClient client, MaxioSettings settings, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        _settings.Validate();
        var identifier = $"handle:{_settings.ProductFamilyHandle}";
        var products = await _client.ListProductsForProductFamilyAsync(identifier, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        string? pricePointHandle = null,
        CancellationToken cancellationToken = default)
    {
        if (subscriber is null)
        {
            throw new ArgumentNullException(nameof(subscriber));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException("A plan handle is required to subscribe.");
        }

        _settings.Validate();

        var gate = ReferenceLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerNoLockAsync(subscriber, cancellationToken);

            // Idempotency: if the shopper already has a non-terminal subscription to this plan,
            // return it rather than creating a duplicate.
            var existing = await FindExistingSubscriptionAsync(customer.Id, planHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Shopper {Reference} already subscribed to plan {Plan} (subscription {Id}); returning existing.",
                    subscriber.Reference, planHandle, existing.Id);
                return MapSubscription(existing, alreadyExisted: true);
            }

            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscriptionBody
                {
                    ProductHandle = planHandle,
                    ProductPricePointHandle = string.IsNullOrWhiteSpace(pricePointHandle) ? null : pricePointHandle,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
                        ? null
                        : _settings.PaymentCollectionMethod,
                },
            };

            MaxioSubscription created;
            try
            {
                created = await _client.CreateSubscriptionAsync(request, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.IsValidationError)
            {
                throw new BillingException(
                    $"Could not subscribe to plan '{planHandle}': {string.Join("; ", ex.Errors)}", ex.Errors);
            }

            _logger.LogInformation(
                "Created subscription {Id} (state {State}) for shopper {Reference} on plan {Plan}.",
                created.Id, created.State, subscriber.Reference, planHandle);

            return MapSubscription(created, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        if (subscriber is null)
        {
            throw new ArgumentNullException(nameof(subscriber));
        }

        _settings.Validate();

        var customer = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(s => MapSubscription(s, alreadyExisted: true))
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerNoLockAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerBody
            {
                FirstName = string.IsNullOrWhiteSpace(subscriber.FirstName) ? "eShop" : subscriber.FirstName,
                LastName = string.IsNullOrWhiteSpace(subscriber.LastName) ? "Subscriber" : subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference,
            },
        };

        try
        {
            var created = await _client.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation(
                "Created Maxio customer {Id} for shopper {Reference}.", created.Id, subscriber.Reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsValidationError)
        {
            // A concurrent creator (e.g. a racing request from another process) may have taken
            // the unique reference between our lookup and create. Re-read and use that customer.
            var raced = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw new BillingException(
                $"Could not create billing customer: {string.Join("; ", ex.Errors)}", ex.Errors);
        }
    }

    private async Task<MaxioSubscription?> FindExistingSubscriptionAsync(
        int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !(s.State is not null && TerminalStates.Contains(s.State)));
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        ProductId = product.Id,
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        FormattedPrice = FormatPrice(product.PriceInCents, currency: null),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        ProductFamilyHandle = product.ProductFamily?.Handle,
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, bool alreadyExisted) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        ProductPriceInCents = subscription.ProductPriceInCents,
        FormattedPrice = FormatPrice(subscription.ProductPriceInCents, subscription.Currency),
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CreatedAt = subscription.CreatedAt,
        CustomerReference = subscription.Customer?.Reference,
        CustomerId = subscription.Customer?.Id ?? 0,
        Currency = subscription.Currency,
        AlreadyExisted = alreadyExisted,
    };

    private static string FormatPrice(long cents, string? currency)
    {
        var amount = (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(currency) || string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)
            ? $"${amount}"
            : $"{amount} {currency.ToUpperInvariant()}";
    }
}
