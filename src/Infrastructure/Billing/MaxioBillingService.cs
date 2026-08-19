using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing adapter. Every HTTP interaction matches an operation in
/// <c>maxio-spec/openapi.yaml</c> (listProductsForProductFamily, readCustomerByReference,
/// createCustomer, listCustomerSubscriptions, findSubscription, createSubscription).
/// </summary>
public sealed class MaxioBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "paused",
        "unpaid",
        "awaiting_signup"
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly string? _hostingRegion;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeGates = new(StringComparer.Ordinal);

    public MaxioBillingService(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger)
        : this(httpClient, options, logger, hostingRegion: null)
    {
    }

    internal MaxioBillingService(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger,
        string? hostingRegion)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _hostingRegion = hostingRegion;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        try
        {
            var familyHandle = _options.ProductFamilyHandle;
            var products = await ListProductsForProductFamilyAsync(familyHandle, cancellationToken);
            return products
                .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
                .Select(MapPlan)
                .ToList();
        }
        catch (MaxioApiException ex)
        {
            throw ToBillingException(ex, "Unable to list subscription plans.");
        }
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        SubscribeToPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(request.CustomerReference))
        {
            throw new BillingException("A customer reference is required to subscribe.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            throw new BillingException("A product handle is required to subscribe.", 400);
        }

        await EnsureProductBelongsToConfiguredFamilyAsync(request.ProductHandle, cancellationToken);

        var gate = _subscribeGates.GetOrAdd(
            $"{request.CustomerReference}:{request.ProductHandle}",
            _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, request.ProductHandle, request.CustomerReference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for customer {CustomerReference} on plan {ProductHandle}.",
                    existing.Id,
                    request.CustomerReference,
                    request.ProductHandle);
                return MapSubscription(existing);
            }

            var created = await CreateSubscriptionIdempotentAsync(
                customer.Id,
                request.CustomerReference,
                request.ProductHandle,
                cancellationToken);

            return MapSubscription(created);
        }
        catch (MaxioApiException ex)
        {
            throw ToBillingException(ex, "Unable to create the subscription.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForCustomerAsync(
        string customerReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new BillingException("A customer reference is required.", 400);
        }

        EnsureConfigured();

        try
        {
            var customer = await ReadCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return subscriptions.Select(MapSubscription).ToList();
        }
        catch (MaxioApiException ex)
        {
            throw ToBillingException(ex, "Unable to list subscriptions.");
        }
    }

    private async Task<Customer> EnsureCustomerAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerByReferenceAsync(request.CustomerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await CreateCustomerAsync(request, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await ReadCustomerByReferenceAsync(request.CustomerReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw ToBillingException(ex, "Unable to create a billing customer.");
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        var liveForPlan = subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
            && IsLive(s.State));

        if (liveForPlan is not null)
        {
            return liveForPlan;
        }

        var byReference = await FindSubscriptionByReferenceAsync(
            BuildSubscriptionReference(customerReference, productHandle),
            cancellationToken);

        if (byReference is not null && IsLive(byReference.State)
            && string.Equals(byReference.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
        {
            return byReference;
        }

        return null;
    }

    private async Task<Subscription> CreateSubscriptionIdempotentAsync(
        int customerId,
        string customerReference,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var reference = BuildSubscriptionReference(customerReference, productHandle);
        try
        {
            return await CreateSubscriptionAsync(customerId, productHandle, reference, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var byReference = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (byReference is not null)
            {
                return byReference;
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            var liveForPlan = subscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
                && IsLive(s.State));
            if (liveForPlan is not null)
            {
                return liveForPlan;
            }

            if (LooksLikeDuplicateReference(ex))
            {
                var uniqueReference = $"{reference}:{Guid.NewGuid():N}";
                return await CreateSubscriptionAsync(customerId, productHandle, uniqueReference, cancellationToken);
            }

            throw ToBillingException(ex, "Unable to create the subscription.");
        }
    }

    private async Task EnsureProductBelongsToConfiguredFamilyAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingException($"Unknown subscription plan '{productHandle}'.", 400);
        }
    }

    // GET /product_families/{product_family_id}/products.json  (listProductsForProductFamily)
    private async Task<List<Product>> ListProductsForProductFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var products = new List<Product>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var path = $"product_families/{EncodeFamilyId(familyHandle)}/products.json?page={page}&per_page={perPage}";
            var wrappers = await SendAsync<List<ProductResponse>>(HttpMethod.Get, path, content: null, cancellationToken);
            var pageItems = wrappers?
                .Select(w => w.Product)
                .Where(p => p is not null)
                .Cast<Product>()
                .ToList() ?? new List<Product>();

            products.AddRange(pageItems);
            if (pageItems.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    // GET /customers/lookup.json  (readCustomerByReference)
    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var response = await SendAsync<CustomerResponse>(HttpMethod.Get, path, content: null, cancellationToken);
            return response?.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // POST /customers.json  (createCustomer)
    private async Task<Customer> CreateCustomerAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomerPayload
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.CustomerReference
            }
        };

        var response = await SendAsync<CustomerResponse>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (response?.Customer is null)
        {
            throw new BillingException("Billing provider returned an empty customer payload.", 502);
        }

        return response.Customer;
    }

    // GET /customers/{customer_id}/subscriptions.json  (listCustomerSubscriptions)
    private async Task<List<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var wrappers = await SendAsync<List<SubscriptionResponse>>(HttpMethod.Get, path, content: null, cancellationToken);
        return wrappers?
            .Select(w => w.Subscription)
            .Where(s => s is not null)
            .Cast<Subscription>()
            .ToList() ?? new List<Subscription>();
    }

    // GET /subscriptions/lookup.json  (findSubscription)
    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var response = await SendAsync<SubscriptionResponse>(HttpMethod.Get, path, content: null, cancellationToken);
            return response?.Subscription;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // POST /subscriptions.json  (createSubscription)
    private async Task<Subscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionPayload
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                // Plans are seeded with payment method not required; remittance collects later.
                PaymentCollectionMethod = "remittance"
            }
        };

        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (response?.Subscription is null)
        {
            throw new BillingException("Billing provider returned an empty subscription payload.", 502);
        }

        return response.Subscription;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? content,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        HttpRequestException? lastNetworkError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            if (content is not null)
            {
                var json = JsonSerializer.Serialize(content, MaxioJson.SerializerOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                lastNetworkError = ex;
                _logger.LogWarning(ex, "Maxio request {Method} {Path} failed at the transport layer (attempt {Attempt}).", method, relativePath, attempt);
                if (attempt == maxAttempts || method != HttpMethod.Get)
                {
                    throw new BillingException("The billing provider is unreachable.", 503, ex);
                }

                await DelayBeforeRetryAsync(attempt, retryAfter: null, cancellationToken);
                continue;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new BillingException("The billing provider timed out.", 504, ex);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (IsTransient(response.StatusCode) && attempt < maxAttempts && (method == HttpMethod.Get || response.StatusCode == HttpStatusCode.TooManyRequests))
                {
                    _logger.LogWarning(
                        "Maxio request {Method} {Path} returned {StatusCode} (attempt {Attempt}).",
                        method,
                        relativePath,
                        (int)response.StatusCode,
                        attempt);
                    await DelayBeforeRetryAsync(attempt, response.Headers.RetryAfter?.Delta, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new MaxioApiException(
                        response.StatusCode,
                        body,
                        $"Maxio {(int)response.StatusCode} for {method} {relativePath}.");
                }

                if (string.IsNullOrWhiteSpace(body) || body == "null")
                {
                    return default;
                }

                try
                {
                    return JsonSerializer.Deserialize<T>(body, MaxioJson.SerializerOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize Maxio response for {Method} {Path}.", method, relativePath);
                    throw new BillingException("The billing provider returned an unexpected payload.", 502, ex);
                }
            }
        }

        throw new BillingException("The billing provider is unreachable.", 503, lastNetworkError);
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new BillingException("Maxio billing is not configured.", 503);
        }

        if (_httpClient.BaseAddress is null)
        {
            var baseUrl = _options.ResolveApiBaseUrl(_hostingRegion);
            _httpClient.BaseAddress = ToHttpClientBaseAddress(baseUrl);
        }
    }

    internal static Uri ToHttpClientBaseAddress(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        if (!trimmed.EndsWith('/'))
        {
            trimmed += "/";
        }

        return new Uri(trimmed, UriKind.Absolute);
    }

    private static string EncodeFamilyId(string familyHandle) =>
        $"handle:{Uri.EscapeDataString(familyHandle)}";

    private static string BuildSubscriptionReference(string customerReference, string productHandle) =>
        $"{customerReference}:{productHandle}";

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveSubscriptionStates.Contains(state);

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests
        || statusCode == HttpStatusCode.RequestTimeout
        || (int)statusCode >= 500;

    private static bool LooksLikeDuplicateReference(MaxioApiException ex)
    {
        if (string.IsNullOrWhiteSpace(ex.ResponseBody))
        {
            return false;
        }

        return ex.ResponseBody.Contains("reference", StringComparison.OrdinalIgnoreCase)
               && (ex.ResponseBody.Contains("taken", StringComparison.OrdinalIgnoreCase)
                   || ex.ResponseBody.Contains("already", StringComparison.OrdinalIgnoreCase)
                   || ex.ResponseBody.Contains("unique", StringComparison.OrdinalIgnoreCase));
    }

    private BillingException ToBillingException(MaxioApiException ex, string fallback)
    {
        var detail = TryReadErrorSummary(ex.ResponseBody);
        var statusCode = ex.StatusCode switch
        {
            HttpStatusCode.UnprocessableEntity => 400,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Unauthorized => 503,
            HttpStatusCode.Forbidden => 503,
            HttpStatusCode.TooManyRequests => 429,
            _ => 502
        };

        _logger.LogWarning(ex, "Maxio API error {StatusCode}: {Detail}", (int)ex.StatusCode, detail);
        return new BillingException(string.IsNullOrWhiteSpace(detail) ? fallback : detail, statusCode, ex);
    }

    private static string? TryReadErrorSummary(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ErrorListResponse>(responseBody, MaxioJson.SerializerOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return string.Join(" ", parsed.Errors);
            }
        }
        catch (JsonException)
        {
            // Fall through to a truncated raw body.
        }

        return responseBody.Length <= 300 ? responseBody : responseBody[..300];
    }

    private static async Task DelayBeforeRetryAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        var delay = retryAfter ?? TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
        if (delay > TimeSpan.FromSeconds(10))
        {
            delay = TimeSpan.FromSeconds(10);
        }

        await Task.Delay(delay, cancellationToken);
    }

    private static SubscriptionPlan MapPlan(Product product) =>
        new()
        {
            Handle = product.Handle!,
            Name = product.Name ?? product.Handle!,
            Description = product.Description,
            Price = CentsToDecimal(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? "month"
        };

    private static CustomerSubscription MapSubscription(Subscription subscription) =>
        new()
        {
            Id = subscription.Id,
            State = subscription.State ?? "unknown",
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? subscription.Product?.Handle ?? "Unknown plan",
            Price = CentsToDecimal(subscription.ProductPriceInCents != 0
                ? subscription.ProductPriceInCents
                : subscription.Product?.PriceInCents ?? 0),
            NextBillingAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };

    private static decimal CentsToDecimal(long cents) => cents / 100m;
}
