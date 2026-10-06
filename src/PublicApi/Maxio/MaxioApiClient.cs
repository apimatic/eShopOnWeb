using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API, built against the
/// authoritative OpenAPI specification in <c>maxio-spec/</c>. Every endpoint,
/// path, query parameter, request/response shape and the Basic auth scheme used
/// here come from that spec.
/// </summary>
public class MaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> options)
    {
        _httpClient = httpClient;
        _ = options.Value;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// <c>GET /customers/lookup.json?reference={reference}</c> — Read Customer by Reference.
    /// Returns <c>null</c> when the customer does not exist (404).
    /// </summary>
    public async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        var wrapper = await ReadResponseAsync<CustomerResponse>(response, cancellationToken);
        return wrapper?.Customer;
    }

    /// <summary>
    /// <c>POST /customers.json</c> — Create Customer.
    /// </summary>
    public async Task<Customer> CreateCustomerAsync(CreateMaxioCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/customers.json", request, _jsonOptions, cancellationToken);
        var wrapper = await ReadResponseAsync<CustomerResponse>(response, cancellationToken);
        return wrapper!.Customer!;
    }

    /// <summary>
    /// <c>GET /products.json</c> — List Products.
    /// </summary>
    public async Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/products.json", cancellationToken);
        var items = await ReadResponseAsync<List<ProductResponse>>(response, cancellationToken);
        var products = new List<Product>(items?.Count ?? 0);
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item.Product != null)
                {
                    products.Add(item.Product);
                }
            }
        }
        return products;
    }

    /// <summary>
    /// <c>POST /subscriptions.json</c> — Create Subscription.
    /// </summary>
    public async Task<Subscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", request, _jsonOptions, cancellationToken);
        var wrapper = await ReadResponseAsync<SubscriptionResponse>(response, cancellationToken);
        return wrapper!.Subscription!;
    }

    /// <summary>
    /// <c>GET /customers/{customer_id}/subscriptions.json</c> — List Customer Subscriptions.
    /// </summary>
    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json", cancellationToken);
        var items = await ReadResponseAsync<List<SubscriptionResponse>>(response, cancellationToken);
        var subscriptions = new List<Subscription>(items?.Count ?? 0);
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item.Subscription != null)
                {
                    subscriptions.Add(item.Subscription);
                }
            }
        }
        return subscriptions;
    }

    /// <summary>
    /// <c>GET /subscriptions/lookup.json?reference={reference}</c> — Find Subscription by reference.
    /// Returns <c>null</c> when the subscription does not exist (404).
    /// </summary>
    public async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        var wrapper = await ReadResponseAsync<SubscriptionResponse>(response, cancellationToken);
        return wrapper?.Subscription;
    }

    private async Task<T?> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = BuildErrorMessage(response.StatusCode, body);
        throw new MaxioApiException(response.StatusCode, message);
    }

    private static string BuildErrorMessage(HttpStatusCode statusCode, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"The Maxio Advanced Billing API returned HTTP {(int)statusCode} ({statusCode}).";
        }

        try
        {
            var error = JsonSerializer.Deserialize<MaxioErrorResponse>(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true
            });
            if (error != null)
            {
                return $"The Maxio Advanced Billing API returned HTTP {(int)statusCode} ({statusCode}): {error.ToMessage()}";
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body.
        }

        return $"The Maxio Advanced Billing API returned HTTP {(int)statusCode} ({statusCode}): {body}";
    }

    private sealed class CustomerResponse
    {
        public Customer? Customer { get; set; }
    }

    private sealed class ProductResponse
    {
        public Product? Product { get; set; }
    }

    private sealed class SubscriptionResponse
    {
        public Subscription? Subscription { get; set; }
    }
}
