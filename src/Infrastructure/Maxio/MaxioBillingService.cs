using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing integration: plans catalog, idempotent customer provisioning
/// and idempotent subscription enrollment. Maxio is the system of record — no local
/// persistence of the user-to-customer mapping is required; both mappings are keyed by
/// deterministic Maxio references derived from the eShopOnWeb user id.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    private const string FamilyIdCacheKeyPrefix = "Maxio:ProductFamilyId:";
    private static readonly TimeSpan FamilyIdCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int PlansPageSize = 50;
    private const int MaxPlanPages = 20;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly SemaphoreSlim _familyIdLock = new(1, 1);

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        IMemoryCache cache,
        ILogger<MaxioBillingService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_settings.ApiKey) ||
            string.IsNullOrWhiteSpace(_settings.Subdomain) ||
            string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioBillingException(
                MaxioBillingFailureKind.NotConfigured,
                "Maxio billing is not configured. Provide Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle via user secrets or environment variables.");
        }
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var familyId = await ResolveFamilyIdAsync(cancellationToken);

        var plans = new List<MaxioPlan>();
        try
        {
            var page = 1;
            while (page <= MaxPlanPages)
            {
                var current = page;
                var products = await BoundedAsync(token => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: current,
                    perPage: PlansPageSize,
                    ct: token), cancellationToken);

                foreach (var productResponse in products)
                {
                    var product = productResponse?.Product;
                    if (product is null)
                    {
                        continue;
                    }
                    plans.Add(new MaxioPlan(
                        product.Handle ?? string.Empty,
                        product.Name ?? product.Handle ?? string.Empty,
                        CentsToPrice(product.PriceInCents),
                        product.Interval ?? 1,
                        product.IntervalUnit?.Value ?? IntervalUnit.Month.Value));
                }

                if (products.Count < PlansPageSize)
                {
                    break;
                }
                page++;
            }
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse("Failed to list the subscription plans from Maxio.", ex);
        }

        return plans;
    }

    public async Task<MaxioSubscription> SubscribeAsync(MaxioSubscriber subscriber, string planHandle, CancellationToken cancellationToken)
    {
        if (subscriber is null || string.IsNullOrWhiteSpace(subscriber.CustomerReference))
        {
            throw new MaxioBillingException(MaxioBillingFailureKind.Validation, "A customer reference is required to subscribe.");
        }
        if (string.IsNullOrWhiteSpace(subscriber.Email))
        {
            throw new MaxioBillingException(MaxioBillingFailureKind.Validation, "An email address is required to subscribe.");
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioBillingException(MaxioBillingFailureKind.Validation, "A plan handle is required to subscribe.");
        }

        var customerId = await EnsureCustomerAsync(subscriber, cancellationToken);

        var subscriptionReference = SubscriptionReference(subscriber.CustomerReference, planHandle);

        var existing = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return MapSubscription(existing, subscriber.CustomerReference, alreadySubscribed: true);
        }

        try
        {
            var response = await BoundedAsync(token => _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customerId,
                        Reference = subscriptionReference,
                        // The hero flow subscribes without card capture / 3-DS: remittance is
                        // the cardless collection method on Relationship Invoicing. A site
                        // configured for automatic card billing still rejects cardless
                        // signup, and that rejection surfaces as a validation error.
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                }, ct: token), cancellationToken);

            if (response?.Subscription is null)
            {
                throw new MaxioBillingException(
                    MaxioBillingFailureKind.Provider,
                    "Maxio did not return the subscription that was created.");
            }
            return MapSubscription(response, subscriber.CustomerReference, alreadySubscribed: false);
        }
        catch (JsonException ex)
        {
            // The response body (success or error) no longer matched its model, so the
            // outcome is unreadable: the create may have gone through. Reconcile by
            // reference — a hit settles it as an idempotent replay; a miss is a provider
            // failure, never an absence.
            var reconciled = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
            {
                return MapSubscription(reconciled, subscriber.CustomerReference, alreadySubscribed: true);
            }
            _logger.LogError(ex, "Maxio returned a subscription response that could not be processed.");
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Provider,
                "The billing provider returned a response that could not be processed.",
                innerException: ex);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                // 422 — either a concurrent create with the same reference (double-click race)
                // or a genuine validation rejection. Re-resolve by reference; only a miss is a rejection.
                var raced = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (raced is not null)
                {
                    return MapSubscription(raced, subscriber.CustomerReference, alreadySubscribed: true);
                }
                throw new MaxioBillingException(
                    MaxioBillingFailureKind.Validation,
                    "Maxio rejected the subscription request.",
                    (int)HttpStatusCode.UnprocessableEntity,
                    errorList.Errors?.ToList(),
                    ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("Maxio rejected the subscription request.", raw, ex);
            }
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Provider,
                "Maxio rejected the subscription request.",
                innerException: ex);
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(string customerReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new MaxioBillingException(MaxioBillingFailureKind.Validation, "A customer reference is required.");
        }

        var customerId = await TryReadCustomerIdByReferenceAsync(customerReference, cancellationToken);
        if (customerId is null)
        {
            // The user has never been provisioned in Maxio, so they hold no subscriptions.
            // Reads never provision.
            return Array.Empty<MaxioSubscription>();
        }

        try
        {
            var responses = await BoundedAsync(
                token => _client.Customers.ListCustomerSubscriptions(customerId.Value, token), cancellationToken);

            var subscriptions = new List<MaxioSubscription>();
            foreach (var response in responses)
            {
                if (response?.Subscription is null)
                {
                    continue;
                }
                subscriptions.Add(MapSubscription(response, customerReference, alreadySubscribed: false));
            }
            return subscriptions;
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse("Failed to list the customer's subscriptions from Maxio.", ex);
        }
    }

    /// <summary>
    /// Idempotently resolves the Maxio customer id for a reference: look up by reference
    /// first; on a 404 create; on a create 422 (double-click race) re-look up instead of
    /// trusting the 422 body.
    /// </summary>
    private async Task<int> EnsureCustomerAsync(MaxioSubscriber subscriber, CancellationToken cancellationToken)
    {
        var found = await TryReadCustomerIdByReferenceAsync(subscriber.CustomerReference, cancellationToken);
        if (found is not null)
        {
            return found.Value;
        }

        try
        {
            var response = await BoundedAsync(token => _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = subscriber.FirstName,
                        LastName = subscriber.LastName,
                        Email = subscriber.Email,
                        Reference = subscriber.CustomerReference
                    }
                }, ct: token), cancellationToken);

            if (response?.Customer?.Id is null)
            {
                throw new MaxioBillingException(
                    MaxioBillingFailureKind.Provider,
                    "Maxio did not return the customer that was provisioned.");
            }
            return response.Customer.Id.Value;
        }
        catch (JsonException ex)
        {
            // The response body (success or error) no longer matched its model, so the
            // outcome is unreadable: the create may have gone through. Re-resolve the
            // reference — a hit settles it as an idempotent replay; a miss is a provider
            // failure, never an absence.
            var raced = await TryReadCustomerIdByReferenceAsync(subscriber.CustomerReference, cancellationToken);
            if (raced is not null)
            {
                return raced.Value;
            }
            _logger.LogError(ex, "Maxio returned a customer response that could not be processed.");
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Provider,
                "The billing provider returned a response that could not be processed.",
                innerException: ex);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A create failure is most likely a concurrent create with the same reference
            // (double-click race). The generated 422 payload shape is unreliable, so the
            // conflict is settled by re-resolving the reference — never by parsing the body.
            var raced = await TryReadCustomerIdByReferenceAsync(subscriber.CustomerReference, cancellationToken);
            if (raced is not null)
            {
                return raced.Value;
            }
            if (ex.Error.TryGetRawError(out var raw) && (int)raw.StatusCode >= 500)
            {
                throw MapRawError("Maxio failed to provision the customer.", raw, ex);
            }
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Validation,
                "Maxio rejected the customer creation.",
                (int)HttpStatusCode.UnprocessableEntity,
                innerException: ex);
        }
    }

    private async Task<int?> TryReadCustomerIdByReferenceAsync(string customerReference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                token => _client.Customers.ReadCustomerByReference(customerReference, token), cancellationToken);
            return response?.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (JsonException ex)
        {
            // An unreadable read is an unknown outcome — provider failure, never an absence.
            throw UnreadableResponse("Failed to resolve the Maxio customer.", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError("Failed to resolve the Maxio customer.", ex.Error, ex);
        }
    }

    private async Task<SubscriptionResponse?> TryFindSubscriptionAsync(string subscriptionReference, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedAsync(
                token => _client.Subscriptions.FindSubscription(reference: subscriptionReference, ct: token), cancellationToken);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                // 404 — no subscription with that reference.
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("Failed to look up the subscription in Maxio.", raw, ex);
            }
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Provider,
                "Failed to look up the subscription in Maxio.",
                innerException: ex);
        }
        catch (JsonException ex)
        {
            // An unreadable read is an unknown outcome — provider failure, never an absence.
            throw UnreadableResponse("Failed to look up the subscription in Maxio.", ex);
        }
    }

    /// <summary>
    /// Resolves the numeric id of the configured product family by handle and caches it.
    /// </summary>
    private async Task<int> ResolveFamilyIdAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"{FamilyIdCacheKeyPrefix}{_settings.ProductFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out int cachedId))
        {
            return cachedId;
        }

        await _familyIdLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cachedId))
            {
                return cachedId;
            }

            IReadOnlyList<ProductFamilyResponse> families;
            try
            {
                families = await BoundedAsync(token => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: token), cancellationToken);
            }
            catch (JsonException ex)
            {
                throw UnreadableResponse("Failed to resolve the Maxio product family.", ex);
            }

            var family = families
                .Select(f => f.ProductFamily)
                .FirstOrDefault(pf => pf is not null &&
                    string.Equals(pf.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

            if (family?.Id is null)
            {
                throw new MaxioBillingException(
                    MaxioBillingFailureKind.NotFound,
                    $"No Maxio product family with handle '{_settings.ProductFamilyHandle}' was found.",
                    (int)HttpStatusCode.NotFound);
            }

            _cache.Set(cacheKey, family.Id.Value, FamilyIdCacheDuration);
            return family.Id.Value;
        }
        finally
        {
            _familyIdLock.Release();
        }
    }

    /// <summary>
    /// Runs one SDK call under a total time budget, and normalizes transport failures and
    /// unreadable provider bodies into the integration's own error type.
    /// </summary>
    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogError(ex, "Maxio is unreachable.");
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Unavailable,
                "The billing provider could not be reached. Please try again later.",
                innerException: ex);
        }
    }

    /// <summary>
    /// An unreadable read response is an unknown outcome — provider failure, never an absence.
    /// </summary>
    private MaxioBillingException UnreadableResponse(string callerSafeMessage, JsonException ex)
    {
        _logger.LogError(ex, "Maxio returned a response that could not be processed.");
        return new MaxioBillingException(
            MaxioBillingFailureKind.Provider,
            "The billing provider returned a response that could not be processed.",
            innerException: ex);
    }

    private static MaxioBillingException MapRawError(string callerSafeMessage, RawError raw, Exception innerException)
    {
        var status = (int)raw.StatusCode;
        var kind = status >= 400 && status < 500
            ? MaxioBillingFailureKind.Validation
            : MaxioBillingFailureKind.Provider;
        return new MaxioBillingException(kind, callerSafeMessage, status, null, innerException);
    }

    private static MaxioSubscription MapSubscription(SubscriptionResponse response, string customerReference, bool alreadySubscribed)
    {
        var subscription = response.Subscription
            ?? throw new MaxioBillingException(
                MaxioBillingFailureKind.Provider,
                "Maxio returned a subscription payload that was empty.");

        var product = subscription.Product;
        var priceCents = subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0;
        var nextBilling = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt;

        return new MaxioSubscription(
            subscription.Id ?? 0,
            product?.Handle ?? string.Empty,
            product?.Name ?? product?.Handle ?? string.Empty,
            CentsToPrice(priceCents),
            subscription.State?.Value ?? string.Empty,
            nextBilling,
            customerReference,
            alreadySubscribed);
    }

    private static string SubscriptionReference(string customerReference, string planHandle) =>
        $"{customerReference}-sub-{planHandle}";

    private static decimal CentsToPrice(long? cents) => (cents ?? 0) / 100m;
}