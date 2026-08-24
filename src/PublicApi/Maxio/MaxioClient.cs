using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API. Every method maps to an operation in
/// maxio-spec/openapi.yaml (noted on each method); auth is HTTP Basic (API key as username, "x" as
/// password) configured on the HttpClient at registration time.
/// </summary>
public class MaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>listProductsForProductFamily: GET /product_families/{product_family_id}/products.json</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The path parameter accepts the family's handle prefixed with "handle:".
        var path = $"product_families/{Uri.EscapeDataString("handle:" + productFamilyHandle)}/products.json";
        var wrappers = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, body: null, cancellationToken: cancellationToken);
        return (wrappers ?? new List<MaxioProductResponse>())
            .Select(w => w.Product)
            .Where(p => p is not null)
            .Cast<MaxioProduct>()
            .ToList();
    }

    /// <summary>readCustomerByReference: GET /customers/lookup.json?reference=... Returns null when no customer matches (404).</summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Get, path, body: null, cancellationToken: cancellationToken, allowNotFound: true);
        return response?.Customer;
    }

    /// <summary>createCustomer: POST /customers.json</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest { Customer = customer };
        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return response?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, string.Empty, new[] { "Maxio returned an empty customer payload." });
    }

    /// <summary>createSubscription: POST /subscriptions.json</summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return response?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.Created, string.Empty, new[] { "Maxio returned an empty subscription payload." });
    }

    /// <summary>listCustomerSubscriptions: GET /customers/{customer_id}/subscriptions.json</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var wrappers = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", body: null, cancellationToken: cancellationToken);
        return (wrappers ?? new List<MaxioSubscriptionResponse>())
            .Select(w => w.Subscription)
            .Where(s => s is not null)
            .Cast<MaxioSubscription>()
            .ToList();
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Maxio {Method} {Path} failed with {StatusCode}: {Body}", method, path, (int)response.StatusCode, errorBody);
            throw MaxioApiException.Create(response.StatusCode, errorBody);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return System.Text.Json.JsonSerializer.Deserialize<T>(responseBody);
    }
}
