using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// All SDK calls are bounded by a total call budget, converted to <see cref="BillingException"/>
/// at this boundary, and writes are reconciled after transport failures so a retried
/// subscribe never produces a duplicate.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FamilyIdCacheDuration = TimeSpan.FromHours(1);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        MaxioOptions options,
        IMemoryCache cache,
        IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var familyId = await ResolveProductFamilyIdAsync(ct);
            var products = await Bounded(c => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: c), ct);

            return products
                .Select(p => p.Product)
                .Where(p => p is not null && !string.IsNullOrEmpty(p.Handle))
                .Select(p => new SubscriptionPlan
                {
                    Handle = p.Handle!,
                    Name = p.Name ?? p.Handle!,
                    PriceInCents = p.PriceInCents ?? 0,
                    Interval = p.Interval ?? 1,
                    IntervalUnit = p.IntervalUnit?.Value ?? string.Empty
                })
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw ConvertListProductsError(ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw ConvertRawError(ex, "list subscription plans");
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw ProviderUnreachable("list subscription plans", ex);
        }
    }

    public async Task<CustomerSubscription> SubscribeAsync(string userId, string email, string productHandle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException("A product handle is required.", HttpStatusCode.BadRequest);
        }

        try
        {
            var customerId = await EnsureCustomerAsync(userId, email, ct);

            // Dedupe: an existing live subscription to the same plan is returned, never duplicated.
            var existing = await FindLiveSubscriptionAsync(customerId, productHandle, ct);
            if (existing is not null)
            {
                return existing;
            }

            var response = await Bounded(c => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customerId,
                        Reference = $"eshop-{userId}-{productHandle}",
                        // Invoice-based collection: the plans require no card, so signup must not
                        // demand a payment method on file for the first balance.
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                ct: c), ct);

            var subscription = response.Subscription
                ?? throw new BillingException("The billing provider returned no subscription.");
            return Map(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw ConvertCreateSubscriptionError(ex);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw ConvertCreateCustomerError(ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw ConvertRawError(ex, "subscribe");
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            // A transport failure on a write means the request may have reached Maxio.
            // Reconcile against provider state before reporting failure.
            _logger.LogWarning($"Transport failure during subscribe for '{productHandle}'; reconciling against provider state. {ex.Message}");
            var settled = await FindLiveSubscriptionSafeAsync(userId, productHandle);
            if (settled is not null)
            {
                return settled;
            }

            throw ProviderUnreachable("subscribe", ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var customerId = await FindCustomerIdByReferenceAsync(userId, ct);
            if (customerId is null)
            {
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(customerId.Value, ct);
            return subscriptions.Select(Map).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw ConvertRawError(ex, "list subscriptions");
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw ProviderUnreachable("list subscriptions", ex);
        }
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        var handle = _options.ProductFamilyHandle!;
        var cacheKey = $"maxio:product-family-id:{handle}";

        if (_cache.TryGetValue(cacheKey, out int cachedId))
        {
            return cachedId;
        }

        var families = await Bounded(c => _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: c), ct);

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f?.Handle == handle);

        if (match?.Id is not int familyId)
        {
            throw new BillingException(
                $"The configured Maxio product family '{handle}' was not found. Check Maxio:ProductFamilyHandle.");
        }

        _cache.Set(cacheKey, familyId, FamilyIdCacheDuration);
        return familyId;
    }

    private async Task<int> EnsureCustomerAsync(string userId, string email, CancellationToken ct)
    {
        var existingId = await FindCustomerIdByReferenceAsync(userId, ct);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var localPart = email.Split('@')[0];
        try
        {
            var created = await Bounded(c => _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = localPart,
                        LastName = "Customer",
                        Email = email,
                        Reference = userId
                    }
                },
                ct: c), ct);

            return created.Customer.Id
                ?? throw new BillingException("The billing provider returned no customer id.");
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            // 422 — the provider enforces one customer per reference, so this is either a lost
            // create race (a concurrent request created it first) or a genuine validation error.
            // Re-read: if the customer now exists, the race was lost benignly.
            var winnerId = await FindCustomerIdByReferenceAsync(userId, ct);
            if (winnerId is not null)
            {
                return winnerId.Value;
            }

            throw ConvertCreateCustomerError(ex);
        }
    }

    private async Task<int?> FindCustomerIdByReferenceAsync(string userId, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(c => _client.Customers.ReadCustomerByReference(userId, ct: c), ct);
            return response.Customer.Id
                ?? throw new BillingException("The billing provider returned no customer id.");
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        return subscriptions
            .Where(s => s.Product?.Handle == productHandle && IsLive(s.State))
            .Select(Map)
            .FirstOrDefault();
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionSafeAsync(string userId, string productHandle)
    {
        try
        {
            var customerId = await FindCustomerIdByReferenceAsync(userId, CancellationToken.None);
            if (customerId is null)
            {
                return null;
            }

            return await FindLiveSubscriptionAsync(customerId.Value, productHandle, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not BillingException)
        {
            _logger.LogWarning($"Reconciliation after transport failure itself failed: {ex.Message}");
            return null;
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var responses = await Bounded(c => _client.Customers.ListCustomerSubscriptions(customerId, ct: c), ct);
        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    private static bool IsLive(SubscriptionState? state)
        => state == SubscriptionState.Active || state == SubscriptionState.Trialing;

    private static CustomerSubscription Map(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        State = subscription.State?.Value ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken ct)
        => ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    private BillingException ConvertRawError(SdkException<RawError> ex, string operation)
    {
        _logger.LogWarning($"Maxio {operation} failed with HTTP {(int)ex.Error.StatusCode}: {ex.Error.ReadAsString()}");
        return new BillingException(
            $"The billing provider rejected the request to {operation} (HTTP {(int)ex.Error.StatusCode}).",
            ex.Error.StatusCode,
            ex);
    }

    private BillingException ConvertCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            // The typed 422 payload is a mismatched shared model (SDK-known issue), so the
            // authoritative detail is the raw body — logged, never leaked to the caller verbatim.
            var detail = ex.Error.TryGetRawError(out var raw) ? raw.ReadAsString() : "validation failed";
            _logger.LogWarning($"Maxio create-customer rejected with 422: {detail}");
            return new BillingException("The billing provider rejected the customer details.", HttpStatusCode.UnprocessableEntity, ex);
        }

        if (ex.Error.TryGetRawError(out var fallback))
        {
            _logger.LogWarning($"Maxio create-customer failed with HTTP {(int)fallback.StatusCode}: {fallback.ReadAsString()}");
            return new BillingException(
                $"The billing provider rejected the request (HTTP {(int)fallback.StatusCode}).",
                fallback.StatusCode,
                ex);
        }

        return new BillingException("The billing provider rejected the request.", null, ex);
    }

    private BillingException ConvertCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errorList))
        {
            var messages = string.Join("; ", errorList.Errors);
            _logger.LogWarning($"Maxio create-subscription rejected with 422: {messages}");
            return new BillingException(
                $"The billing provider rejected the subscription: {messages}",
                HttpStatusCode.UnprocessableEntity,
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            _logger.LogWarning($"Maxio create-subscription failed with HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}");
            return new BillingException(
                $"The billing provider rejected the subscription (HTTP {(int)raw.StatusCode}).",
                raw.StatusCode,
                ex);
        }

        return new BillingException("The billing provider rejected the subscription.", null, ex);
    }

    private BillingException ConvertListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            _logger.LogWarning($"Maxio list-products failed: {message}");
            return new BillingException("The configured product family was not found at the billing provider.", HttpStatusCode.NotFound, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            _logger.LogWarning($"Maxio list-products failed with HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}");
            return new BillingException(
                $"The billing provider rejected the request (HTTP {(int)raw.StatusCode}).",
                raw.StatusCode,
                ex);
        }

        return new BillingException("The billing provider rejected the request.", null, ex);
    }

    private static BillingException UnprocessableResponse(JsonException ex)
        => new("The billing provider returned a response that could not be processed.", null, ex);

    private BillingException ProviderUnreachable(string operation, Exception ex)
    {
        _logger.LogWarning($"Maxio {operation}: provider unreachable or timed out. {ex.Message}");
        return new BillingException(
            $"The billing provider could not be reached while trying to {operation}. The outcome is unknown; retrying is safe and will not create duplicates.",
            null,
            ex);
    }
}
