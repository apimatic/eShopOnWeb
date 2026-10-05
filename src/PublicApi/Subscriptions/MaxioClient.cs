using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Thin HTTP wrapper around the Maxio Advanced Billing (Chargify) REST API covering exactly
/// the operations this integration needs. Every endpoint here was verified against the live
/// Advanced Billing API:
///   - GET  /products.json
///   - GET  /customers/lookup.json?reference={reference}   (404 = no customer matches)
///   - POST /customers.json
///   - POST /subscriptions.json
///   - GET  /subscriptions.json?customer_id={id}
/// </summary>
public sealed class MaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(IHttpClientFactory httpClientFactory, ILogger<MaxioClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetJsonAsync<List<MaxioProductEnvelope>>(
            "GET /products.json",
            "products.json",
            cancellationToken) ?? new List<MaxioProductEnvelope>();

        return products.Select(p => p.Product).Where(p => p is not null).ToList()!;
    }

    /// <returns>The matching customer, or null when no customer with that reference exists
    /// (Maxio responds HTTP 404 for the lookup endpoint in that case).</returns>
    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        var requestUri = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(
            new HttpRequestMessage(HttpMethod.Get, requestUri), "GET /customers/lookup.json", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadEnvelopeAsync<MaxioCustomerEnvelope>(response, "GET /customers/lookup.json", cancellationToken);
        return payload?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        string firstName, string lastName, string email, string reference,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference
            }
        };

        var customer = await SendForJsonAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            payload,
            "POST /customers.json",
            cancellationToken);

        return customer.Customer!;
    }

    /// <summary>
    /// Creates a subscription for an existing Maxio customer. The product is addressed by its
    /// stable API handle and the subscription carries an app-supplied <paramref name="reference"/>
    /// so double submissions can be detected. "remittance" (invoice-style) collection is passed
    /// explicitly so signups work without capturing a payment method.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        int customerId, string productHandle, string reference,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                reference = reference,
                payment_collection_method = "remittance"
            }
        };

        var result = await SendForJsonAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            payload,
            "POST /subscriptions.json",
            cancellationToken);

        return result.Subscription!;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(
        int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await GetJsonAsync<List<MaxioSubscriptionEnvelope>>(
            "GET /subscriptions.json",
            $"subscriptions.json?customer_id={customerId}&per_page=100",
            cancellationToken) ?? new List<MaxioSubscriptionEnvelope>();

        return subscriptions.Select(s => s.Subscription).Where(s => s is not null).ToList()!;
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient(MaxioOptionsRegistration.HttpClientName);

    private async Task<T?> GetJsonAsync<T>(string description, string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, requestUri), description, cancellationToken);
        return await ReadEnvelopeAsync<T>(response, description, cancellationToken);
    }

    private async Task<T> SendForJsonAsync<T>(HttpMethod method, string requestUri, object payload, string description, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(method, requestUri) { Content = JsonContent.Create(payload) };
        using var response = await SendAsync(request, description, cancellationToken);
        return await ReadEnvelopeAsync<T>(response, description, cancellationToken)
            ?? throw new MaxioApiException(description, (int)response.StatusCode, "empty body");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string description, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        _logger.LogInformation("Maxio API request: {Method} {Uri}", request.Method, new Uri(client.BaseAddress!, request.RequestUri!.ToString()).ToString());

        var response = await client.SendAsync(request, cancellationToken);

        _logger.LogInformation("Maxio API response: {Method} {Uri} -> {StatusCode}",
            request.Method, request.RequestUri, (int)response.StatusCode);

        return response;
    }

    private static async Task<T?> ReadEnvelopeAsync<T>(HttpResponseMessage response, string description, CancellationToken cancellationToken)
        where T : class
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var result = JsonSerializer.Deserialize<T>(content, SerializerOptions);
            if (result is null)
            {
                throw new MaxioApiException(description, (int)response.StatusCode, $"unexpected body: {Truncate(content)}");
            }
            return result;
        }

        throw new MaxioApiException(description, (int)response.StatusCode, Truncate(content));
    }

    private static string Truncate(string value) =>
        value.Length <= 512 ? value : value[..512];

    // Envelope types -----------------------------------------------------

    private sealed class MaxioProductEnvelope
    {
        [JsonPropertyName("product")]
        public MaxioProduct Product { get; set; } = new();
    }

    private sealed class MaxioCustomerEnvelope
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private sealed class MaxioSubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }
    }
}