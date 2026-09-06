using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// Maxio is the system of record: this service keeps no local copy of the shopper-to-customer
/// mapping. It instead derives a deterministic <c>reference</c> from the eShopOnWeb user
/// (see <see cref="MaxioReference"/>) and asks Maxio to resolve it, so the integration is correct
/// across restarts and across instances.
/// </remarks>
public sealed class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>The specification caps <c>per_page</c> at 200.</summary>
    private const int PlanPageSize = 200;

    /// <summary>Guards against an endless paging loop if the API ever stops shrinking pages.</summary>
    private const int MaxPlanPages = 20;

    /// <summary>How many reference variants to probe before giving up on finding a free one.</summary>
    private const int MaxReferenceAttempts = 25;

    private readonly IMaxioClient _client;
    private readonly MaxioOptions _options;
    private readonly MaxioSiteMetadataCache _siteMetadata;
    private readonly KeyedAsyncLock _subscribeLocks;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioClient client,
        IOptions<MaxioOptions> options,
        MaxioSiteMetadataCache siteMetadata,
        KeyedAsyncLock subscribeLocks,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _siteMetadata = siteMetadata;
        _subscribeLocks = subscribeLocks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _options.ProductFamilyHandle!;
        var currency = await _siteMetadata.GetCurrencyAsync(_client.ReadSiteAsync, cancellationToken).ConfigureAwait(false);

        var products = new List<MaxioProduct>();
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<MaxioProduct> batch;
            try
            {
                batch = await _client
                    .ListProductsForProductFamilyAsync($"handle:{familyHandle}", page, PlanPageSize, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new BillingConfigurationException(
                    $"Maxio has no product family with handle '{familyHandle}'. " +
                    $"Check '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}'.", ex);
            }

            products.AddRange(batch);

            if (batch.Count < PlanPageSize)
            {
                break;
            }
        }

        var plans = products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MapPlan(p, currency))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "Listed {PlanCount} Maxio subscription plan(s) from product family '{ProductFamilyHandle}'.",
            plans.Count, familyHandle);

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var planHandle = request.PlanHandle?.Trim();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionPlanNotFoundException(string.Empty, "A plan handle is required.");
        }

        var product = await ResolvePlanProductAsync(planHandle, cancellationToken).ConfigureAwait(false);
        var customerReference = CustomerReferenceFor(request.Subscriber);

        // One shopper at a time: this is what keeps a double-clicked subscribe from racing itself.
        using var _ = await _subscribeLocks.AcquireAsync(customerReference, cancellationToken).ConfigureAwait(false);

        var customer = await EnsureCustomerAsync(request.Subscriber, customerReference, cancellationToken)
            .ConfigureAwait(false);

        var existing = (await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(s => s.IsLiveSubscriptionTo(product.Handle));

        if (existing is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerReference} already holds subscription {SubscriptionId} ({State}) to plan '{PlanHandle}'; not creating another.",
                customerReference, existing.Id, existing.State, product.Handle);

            return new SubscribeResult(await MapSubscriptionAsync(existing, cancellationToken).ConfigureAwait(false), true);
        }

        var subscription = await CreateSubscriptionAsync(customer, product, customerReference, cancellationToken)
            .ConfigureAwait(false);

        return new SubscribeResult(await MapSubscriptionAsync(subscription, cancellationToken).ConfigureAwait(false), false);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var customerReference = CustomerReferenceFor(subscriber);
        var customer = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken).ConfigureAwait(false);

        if (customer is null)
        {
            _logger.LogInformation(
                "No Maxio customer exists for reference {CustomerReference}; the user holds no subscriptions.",
                customerReference);

            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);
        var currency = await _siteMetadata.GetCurrencyAsync(_client.ReadSiteAsync, cancellationToken).ConfigureAwait(false);

        return subscriptions
            .Select(s => MapSubscription(s, currency))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(s => s.Id)
            .ToList();
    }

    private string CustomerReferenceFor(SubscriberIdentity subscriber) =>
        MaxioReference.ForCustomer(_options.ReferencePrefix, subscriber.UserKey);

    /// <summary>
    /// Resolves the plan handle to a product and refuses anything outside the configured family,
    /// so a caller cannot subscribe to an arbitrary product on the billing site.
    /// </summary>
    private async Task<MaxioProduct> ResolvePlanProductAsync(string planHandle, CancellationToken cancellationToken)
    {
        var product = await _client.ReadProductByHandleAsync(planHandle, cancellationToken).ConfigureAwait(false);

        if (product is null || string.IsNullOrWhiteSpace(product.Handle))
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        if (product.ArchivedAt is not null)
        {
            throw new SubscriptionPlanNotFoundException(
                planHandle, $"Subscription plan '{planHandle}' is no longer available.");
        }

        if (!string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Rejected subscribe to '{PlanHandle}': it belongs to product family '{ActualFamily}', not '{ExpectedFamily}'.",
                planHandle, product.ProductFamily?.Handle, _options.ProductFamilyHandle);

            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        return product;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscriberIdentity subscriber, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SubscriberNameResolver.Resolve(subscriber);
        var payload = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = string.IsNullOrWhiteSpace(subscriber.Email) ? subscriber.UserKey : subscriber.Email.Trim(),
                Organization = string.IsNullOrWhiteSpace(subscriber.Organization) ? null : subscriber.Organization.Trim(),
                Reference = customerReference
            }
        };

        try
        {
            var created = await _client.CreateCustomerAsync(payload, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {CustomerReference}.", created.Id, customerReference);

            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio enforces uniqueness on the customer reference, so a 422 here most likely means
            // another writer won the race. Re-read before deciding this is a real failure.
            var raced = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken).ConfigureAwait(false);
            if (raced is not null)
            {
                _logger.LogInformation(
                    "Maxio customer {CustomerId} for reference {CustomerReference} was created concurrently; reusing it.",
                    raced.Id, customerReference);

                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCustomer customer, MaxioProduct product, string customerReference, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxReferenceAttempts; attempt++)
        {
            var reference = MaxioReference.ForSubscription(customerReference, product.Handle!, attempt);

            var occupant = await _client.FindSubscriptionAsync(reference, cancellationToken).ConfigureAwait(false);
            if (occupant is not null)
            {
                // A live occupant would already have been returned by the caller's check; if one
                // appears here it was created concurrently, so honour it instead of duplicating.
                if (occupant.IsLiveSubscriptionTo(product.Handle))
                {
                    return occupant;
                }

                // The reference belongs to a canceled or expired subscription: move to the next variant.
                continue;
            }

            var payload = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscription
                {
                    ProductHandle = product.Handle,
                    CustomerId = customer.Id,
                    Reference = reference,
                    PaymentCollectionMethod = _options.PaymentCollectionMethod
                }
            };

            try
            {
                var created = await _client.CreateSubscriptionAsync(payload, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan '{PlanHandle}'.",
                    created.Id, created.State, customer.Id, product.Handle);

                return created;
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity && IsReferenceTaken(ex))
            {
                _logger.LogInformation(
                    "Subscription reference {Reference} was taken concurrently; retrying with the next variant.", reference);
            }
        }

        throw new MaxioApiException(
            "createSubscription",
            HttpStatusCode.Conflict,
            new[] { $"Could not find a free subscription reference for plan '{product.Handle}' after {MaxReferenceAttempts} attempts." });
    }

    private static bool IsReferenceTaken(MaxioApiException exception) =>
        exception.Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
                                  (e.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                                   e.Contains("taken", StringComparison.OrdinalIgnoreCase)));

    private async Task<CustomerSubscription> MapSubscriptionAsync(
        MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        var currency = await _siteMetadata.GetCurrencyAsync(_client.ReadSiteAsync, cancellationToken).ConfigureAwait(false);
        return MapSubscription(subscription, currency);
    }

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, string? fallbackCurrency) =>
        new()
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State ?? "unknown",
            IsLive = MaxioSubscriptionStates.IsLive(subscription.State),
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = ToInt32(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            Currency = string.IsNullOrWhiteSpace(subscription.Currency) ? fallbackCurrency : subscription.Currency,
            Interval = subscription.Product?.Interval,
            IntervalUnit = subscription.Product?.IntervalUnit,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CreatedAt = subscription.CreatedAt,
            BalanceInCents = ToInt32(subscription.BalanceInCents),
            Customer = new BillingCustomer
            {
                Id = subscription.Customer?.Id ?? 0,
                Reference = subscription.Customer?.Reference,
                Email = subscription.Customer?.Email,
                FirstName = subscription.Customer?.FirstName,
                LastName = subscription.Customer?.LastName
            }
        };

    private SubscriptionPlan MapPlan(MaxioProduct product, string? currency) =>
        new()
        {
            Handle = product.Handle!,
            Name = product.Name ?? product.Handle!,
            Description = string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
            PriceInCents = ToInt32(product.PriceInCents) ?? 0,
            Currency = currency,
            Interval = product.Interval ?? 0,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            ProviderProductId = product.Id,
            PricePointId = product.ProductPricePointId ?? product.DefaultProductPricePointId,
            PricePointName = product.ProductPricePointName,
            RequiresPaymentMethod = product.RequireCreditCard ?? false,
            Taxable = product.Taxable ?? false,
            TrialPriceInCents = ToInt32(product.TrialPriceInCents),
            TrialInterval = product.TrialInterval,
            TrialIntervalUnit = product.TrialIntervalUnit,
            ProductFamilyHandle = product.ProductFamily?.Handle ?? _options.ProductFamilyHandle
        };

    private static int? ToInt32(long? value) =>
        value is null ? null : (int)Math.Clamp(value.Value, int.MinValue, int.MaxValue);
}

internal static class MaxioSubscriptionExtensions
{
    /// <summary>True when the subscription is an ongoing enrollment on the given plan handle.</summary>
    public static bool IsLiveSubscriptionTo(this MaxioSubscription subscription, string? planHandle) =>
        MaxioSubscriptionStates.IsLive(subscription.State) &&
        !string.IsNullOrWhiteSpace(planHandle) &&
        string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase);
}
