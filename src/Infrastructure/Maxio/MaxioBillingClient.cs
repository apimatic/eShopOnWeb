using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API.
///
/// Per the Billing API documentation:
///  - Authentication is HTTP Basic over TLS: the API key as username and "X" as password.
///  - The US environment base address is https://{site}.chargify.com (an explicit
///    Maxio:BaseUrl override is honored when configured).
///  - Responses are JSON, with resource payloads wrapped in a single root object
///    ("product", "customer", "subscription") and list endpoints returning bare arrays
///    of wrapped resources.
/// </summary>
public class MaxioBillingClient : IMaxioBillingClient
{
    private const int MaxAttempts = 3;
    private const int ProductsPageSize = 200;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<bool>>> SiteArchitectureCache = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var maxioOptions = options.Value;
        _httpClient = httpClient;
        _httpClient.BaseAddress = maxioOptions.ResolveBaseAddress();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{maxioOptions.ApiKey}:X")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(() => NewRequest(HttpMethod.Get, $"products/handle/{Uri.EscapeDataString(handle)}.json"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response);
        return await ReadWrappedAsync<MaxioProduct>(response);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        var result = new List<MaxioProduct>();
        var page = 1;
        while (true)
        {
            var response = await SendAsync(() => NewRequest(HttpMethod.Get, $"products.json?per_page={ProductsPageSize}&page={page}"), cancellationToken);
            await EnsureSuccessAsync(response);
            var items = await ReadListAsync<MaxioProduct>(response);
            result.AddRange(items.Where(p =>
                string.Equals(p.ProductFamily?.Handle, familyHandle, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrEmpty(p.ArchivedAt)));
            if (items.Count < ProductsPageSize) break;
            page++;
        }
        return result;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(() => NewRequest(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response);
        return await ReadWrappedAsync<MaxioCustomer>(response);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreateRequest request, CancellationToken cancellationToken = default)
    {
        var body = new { customer = request };
        var response = await SendAsync(() => NewJsonRequest(HttpMethod.Post, "customers.json", body), cancellationToken);
        await EnsureSuccessAsync(response);
        return (await ReadWrappedAsync<MaxioCustomer>(response))!;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(() => NewRequest(HttpMethod.Get, $"customers/{customerId}/subscriptions.json"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return Array.Empty<MaxioSubscription>();
        await EnsureSuccessAsync(response);
        return await ReadListAsync<MaxioSubscription>(response);
    }

    public async Task<bool> IsRelationshipInvoicingEnabledAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = _httpClient.BaseAddress!.Host;
        return await SiteArchitectureCache.GetOrAdd(cacheKey, _ => new Lazy<Task<bool>>(() => LoadSiteArchitectureAsync())).Value;
    }

    private async Task<bool> LoadSiteArchitectureAsync()
    {
        var response = await SendAsync(() => NewRequest(HttpMethod.Get, "site.json"), CancellationToken.None);
        await EnsureSuccessAsync(response);
        var json = await response.Content.ReadAsStringAsync();
        response.Dispose();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("site", out var site) &&
               site.TryGetProperty("relationship_invoicing_enabled", out var ri) &&
               ri.ValueKind == JsonValueKind.True;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string paymentCollectionMethod, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        // Duplicate prevention per the Billing API docs: supply a uniqueness_token with any
        // POST; a retried request with the same token within 60 minutes is rejected with 409.
        var body = new
        {
            subscription = new MaxioSubscriptionCreateRequest
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = paymentCollectionMethod
            },
            uniqueness_token = uniquenessToken
        };
        var response = await SendAsync(() => NewJsonRequest(HttpMethod.Post, "subscriptions.json", body), cancellationToken);
        await EnsureSuccessAsync(response);
        return (await ReadWrappedAsync<MaxioSubscription>(response))!;
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(() => NewRequest(HttpMethod.Get, $"subscriptions/{subscriptionId}.json"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response);
        return await ReadWrappedAsync<MaxioSubscription>(response);
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string relativeUrl) => new(method, relativeUrl);

    private static HttpRequestMessage NewJsonRequest(HttpMethod method, string relativeUrl, object body)
    {
        var request = new HttpRequestMessage(method, relativeUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        return request;
    }

    /// <summary>
    /// Sends the request produced by <paramref name="requestFactory"/>, retrying transient
    /// failures (network errors, timeouts, 408/429/5xx) with exponential backoff. The factory
    /// is re-invoked for each attempt because a request message cannot be safely reused.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(requestFactory(), cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Threading.Tasks.TaskCanceledException
                                       && !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), cancellationToken);
                    continue;
                }
                throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, $"Request to Maxio Billing API failed after {MaxAttempts} attempts: {ex.Message}", ex.Message);
            }

            var isTransient = response.StatusCode == HttpStatusCode.RequestTimeout
                              || response.StatusCode == (HttpStatusCode)429
                              || (int)response.StatusCode >= 500;
            if (isTransient && attempt < MaxAttempts)
            {
                response.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), cancellationToken);
                continue;
            }
            return response;
        }

        throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, $"Request to Maxio Billing API failed after {MaxAttempts} attempts: {lastError?.Message}");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        response.Dispose();
        throw new MaxioApiException(response.StatusCode, body);
    }

    private static async Task<T?> ReadWrappedAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        response.Dispose();
        using var document = JsonDocument.Parse(json);
        // Single-resource responses are wrapped: {"product": {...}}, {"customer": {...}},
        // {"subscription": {...}}. Unwrap the sole member; otherwise deserialize the root.
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            var first = root.EnumerateObject().FirstOrDefault();
            if (first.Value.ValueKind == JsonValueKind.Object &&
                root.EnumerateObject().Count() == 1)
            {
                root = first.Value;
            }
        }
        return root.Deserialize<T>(JsonOptions);
    }

    private static async Task<List<T>> ReadListAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        response.Dispose();
        using var document = JsonDocument.Parse(json);
        var result = new List<T>();
        if (document.RootElement.ValueKind != JsonValueKind.Array) return result;
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var root = element;
            if (element.ValueKind == JsonValueKind.Object)
            {
                var first = element.EnumerateObject().FirstOrDefault();
                if (first.Value.ValueKind == JsonValueKind.Object &&
                    element.EnumerateObject().Count() == 1)
                {
                    root = first.Value;
                }
            }
            var item = root.Deserialize<T>(JsonOptions);
            if (item is not null) result.Add(item);
        }
        return result;
    }
}