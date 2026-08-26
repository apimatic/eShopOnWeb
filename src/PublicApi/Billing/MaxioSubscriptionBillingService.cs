using System;
using System.Collections.Concurrent;
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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Maxio Advanced Billing implementation of the subscription billing service.
/// All SDK failures are translated here into <see cref="MaxioBillingException"/>;
/// SDK exception types never escape this boundary.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FamilyIdCacheDuration = TimeSpan.FromMinutes(5);
    private const string FamilyIdCacheKey = "Maxio.ProductFamilyId";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly CollectionMethod? _collectionMethod;

    // Serializes subscribe attempts per user, so a double-click or concurrent retry
    // passes through list-then-create one at a time instead of racing the create.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new();

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _collectionMethod = ParseCollectionMethod(_options.CollectionMethod);
    }

    private static CollectionMethod? ParseCollectionMethod(string? value)
    {
        // Default remittance: billed by invoice, so subscribing needs no card on file.
        if (string.IsNullOrWhiteSpace(value))
        {
            return CollectionMethod.Remittance;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "automatic" => CollectionMethod.Automatic,
            "remittance" => CollectionMethod.Remittance,
            "prepaid" => CollectionMethod.Prepaid,
            "invoice" => CollectionMethod.Invoice,
            _ => throw new MaxioBillingException((int)HttpStatusCode.InternalServerError,
                $"Invalid Maxio:CollectionMethod '{value}'. Valid values: automatic, remittance, prepaid, invoice.")
        };
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct)
    {
        EnsureConfigured();
        var familyId = await GetProductFamilyIdAsync(ct);

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await Bounded(
                c => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: 1,
                    perPage: 100,
                    ct: c),
                ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw MapListProductsError(ex);
        }
        catch (Exception ex) when (IsCallFailure(ex))
        {
            throw MapCallFailure(ex, "list subscription plans", ct);
        }

        return products
            .Select(p => p.Product)
            .Where(p => p is not null && !string.IsNullOrEmpty(p.Handle))
            .Select(p => new BillingPlan(
                p!.Handle!,
                p.Name ?? string.Empty,
                p.Description,
                p.PriceInCents,
                p.Interval,
                p.IntervalUnit?.Value))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(BillingCustomer customer, string productHandle, CancellationToken ct)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException((int)HttpStatusCode.BadRequest, "A product handle is required.");
        }

        var gate = _subscribeLocks.GetOrAdd(customer.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var customerId = await EnsureCustomerAsync(customer, ct);

            var existing = (await ListCustomerSubscriptionsAsync(customerId, ct))
                .FirstOrDefault(s => IsForProduct(s, productHandle) && IsNonTerminal(s));
            if (existing is not null)
            {
                return new SubscribeResult(Map(existing), Created: false);
            }

            try
            {
                var created = await Bounded(
                    c => _client.Subscriptions.CreateSubscription(
                        new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = productHandle,
                                CustomerId = customerId,
                                Reference = $"{customer.UserId}:{productHandle}",
                                PaymentCollectionMethod = _collectionMethod
                            }
                        },
                        c),
                    ct);

                if (created.Subscription is { } subscription)
                {
                    return new SubscribeResult(Map(subscription), Created: true);
                }

                throw new MaxioBillingException((int)HttpStatusCode.BadGateway,
                    "The billing provider returned an unexpected response while creating the subscription.");
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                // One branch per TryGet* accessor on CreateSubscriptionError; TryGetRawError last.
                if (ex.Error.TryGetErrorListResponse1(out var errorList)) // 422
                {
                    var details = errorList.Errors is { Count: > 0 }
                        ? string.Join("; ", errorList.Errors)
                        : "validation failed";
                    throw new MaxioBillingException((int)HttpStatusCode.UnprocessableEntity,
                        $"The billing provider rejected the subscription: {details}");
                }
                else if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MapRawError(raw, "create the subscription");
                }
                throw new MaxioBillingException((int)HttpStatusCode.BadGateway,
                    "The billing provider returned an unexpected error while creating the subscription.");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                // The write may have reached the provider — reconcile before reporting failure.
                var reconciled = (await ListCustomerSubscriptionsAsync(customerId, ct))
                    .FirstOrDefault(s => IsForProduct(s, productHandle) && IsNonTerminal(s));
                if (reconciled is not null)
                {
                    return new SubscribeResult(Map(reconciled), Created: true);
                }
                throw MapCallFailure(ex, "create the subscription", ct);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken ct)
    {
        EnsureConfigured();

        var customerId = await TryReadCustomerIdAsync(userId, ct);
        if (customerId is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId.Value, ct);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<int> GetProductFamilyIdAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(FamilyIdCacheKey, out int cachedId))
        {
            return cachedId;
        }

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await Bounded(
                c => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: c),
                ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "list product families");
        }
        catch (Exception ex) when (IsCallFailure(ex))
        {
            throw MapCallFailure(ex, "list product families", ct);
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f?.Handle is not null
                && string.Equals(f.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is not int id)
        {
            throw new MaxioBillingException((int)HttpStatusCode.InternalServerError,
                $"The configured billing product family '{_options.ProductFamilyHandle}' was not found.");
        }

        _cache.Set(FamilyIdCacheKey, id, FamilyIdCacheDuration);
        return id;
    }

    private async Task<int> EnsureCustomerAsync(BillingCustomer customer, CancellationToken ct)
    {
        var existingId = await TryReadCustomerIdAsync(customer.UserId, ct);
        if (existingId.HasValue)
        {
            return existingId.Value;
        }

        try
        {
            var created = await Bounded(
                c => _client.Customers.CreateCustomer(
                    new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = customer.FirstName,
                            LastName = customer.LastName,
                            Email = customer.Email,
                            Reference = customer.UserId
                        }
                    },
                    c),
                ct);

            if (created.Customer?.Id is int newId)
            {
                return newId;
            }

            throw new MaxioBillingException((int)HttpStatusCode.BadGateway,
                "The billing provider returned an unexpected response while creating the customer.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // One branch per TryGet* accessor on CreateCustomerError; TryGetRawError last.
            if (ex.Error.TryGetCustomerErrorResponse1(out _)) // 422
            {
                // Customer references are unique server-side: a concurrent create won the race.
                var winnerId = await TryReadCustomerIdAsync(customer.UserId, ct);
                if (winnerId.HasValue)
                {
                    return winnerId.Value;
                }
                throw new MaxioBillingException((int)HttpStatusCode.UnprocessableEntity,
                    "The billing provider rejected the customer record.");
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, "create the customer");
            }
            throw new MaxioBillingException((int)HttpStatusCode.BadGateway,
                "The billing provider returned an unexpected error while creating the customer.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // The write may have reached the provider — reconcile before reporting failure.
            var reconciledId = await TryReadCustomerIdAsync(customer.UserId, ct);
            if (reconciledId.HasValue)
            {
                return reconciledId.Value;
            }
            throw MapCallFailure(ex, "create the customer", ct);
        }
    }

    private async Task<int?> TryReadCustomerIdAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(c => _client.Customers.ReadCustomerByReference(reference, c), ct);
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // genuine miss — the lookup's "create it" signal
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "look up the billing customer");
        }
        catch (Exception ex) when (IsCallFailure(ex))
        {
            throw MapCallFailure(ex, "look up the billing customer", ct);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(c => _client.Customers.ListCustomerSubscriptions(customerId, c), ct);
            return response
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "list subscriptions");
        }
        catch (Exception ex) when (IsCallFailure(ex))
        {
            throw MapCallFailure(ex, "list subscriptions", ct);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.ProductFamilyHandle)
            || (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl)))
        {
            throw new MaxioBillingException((int)HttpStatusCode.InternalServerError,
                "Billing is not configured. Set the Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle configuration values.");
        }
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        // The only whole-call budget: the SDK's Retry.Timeout and HttpClient.Timeout are per-attempt.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static bool IsCallFailure(Exception ex) =>
        ex is HttpRequestException or JsonException or OperationCanceledException;

    private MaxioBillingException MapCallFailure(Exception ex, string operation, CancellationToken ct)
    {
        switch (ex)
        {
            case OperationCanceledException when ct.IsCancellationRequested:
                // The caller hung up — not a provider failure.
                return new MaxioBillingException(499, "The request was cancelled.", ex);
            case OperationCanceledException:
                _logger.LogWarning("Maxio call timed out: {Operation}", operation);
                return new MaxioBillingException((int)HttpStatusCode.BadGateway,
                    $"The billing provider timed out while trying to {operation}.", ex);
            case HttpRequestException:
                _logger.LogWarning(ex, "Maxio call failed (connection): {Operation}", operation);
                return new MaxioBillingException((int)HttpStatusCode.BadGateway,
                    "The billing provider is unreachable.", ex);
            default: // JsonException — a 2xx with a drifted body, or an error body that matched no generated shape
                _logger.LogWarning(ex, "Maxio returned an unreadable response: {Operation}", operation);
                return new MaxioBillingException((int)HttpStatusCode.BadGateway,
                    "The billing provider returned a response that could not be processed.", ex);
        }
    }

    private MaxioBillingException MapRawError(RawError raw, string operation)
    {
        var status = (int)raw.StatusCode;
        string? body = null;
        try
        {
            body = raw.ReadAsString();
        }
        catch (JsonException)
        {
            // Body unreadable — the status alone still carries the signal.
        }

        _logger.LogWarning("Maxio {Operation} failed with HTTP {Status}: {Body}", operation, status, body);

        // Provider 4xx are caller-actionable and are carried through; 5xx collapse to 502.
        return status is >= 400 and < 500
            ? new MaxioBillingException(status, $"The billing provider rejected the request to {operation} (HTTP {status}).")
            : new MaxioBillingException((int)HttpStatusCode.BadGateway, "The billing provider is unavailable.");
    }

    private MaxioBillingException MapListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        // One branch per TryGet* accessor on ListProductsForProductFamilyError; TryGetRawError last.
        if (ex.Error.TryGetString(out var notFoundMessage)) // 404 — family id not found
        {
            // The cached family id may be stale after a catalog re-seed; drop it.
            _cache.Remove(FamilyIdCacheKey);
            _logger.LogWarning("Maxio product family lookup failed: {Message}", notFoundMessage);
            return new MaxioBillingException((int)HttpStatusCode.InternalServerError,
                $"The configured billing product family '{_options.ProductFamilyHandle}' was not found.");
        }
        else if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw, "list subscription plans");
        }
        return new MaxioBillingException((int)HttpStatusCode.BadGateway,
            "The billing provider returned an unexpected error.");
    }

    private static bool IsForProduct(Subscription s, string productHandle) =>
        s.Product?.Handle is not null
        && string.Equals(s.Product.Handle, productHandle, StringComparison.OrdinalIgnoreCase);

    private static bool IsNonTerminal(Subscription s) =>
        s.State == SubscriptionState.Active
        || s.State == SubscriptionState.Trialing
        || s.State == SubscriptionState.AwaitingSignup
        || s.State == SubscriptionState.PastDue
        || s.State == SubscriptionState.OnHold;

    private static BillingSubscription Map(Subscription s) => new(
        s.Id,
        s.Reference,
        s.State?.Value,
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.Product?.Interval,
        s.Product?.IntervalUnit?.Value,
        s.NextAssessmentAt,
        s.CurrentPeriodEndsAt);
}
