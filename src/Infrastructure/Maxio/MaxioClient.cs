using System;
using System.Collections.Generic;
using System.Linq;
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
/// Thin HTTP client for the Maxio Advanced Billing (Chargify) API.
///
/// Contract verified against the Maxio sandbox (see IMaxioClient method docs):
///  - Basic auth with the API key as username and the literal password "x".
///  - Base address https://{subdomain}.chargify.com, or Maxio:BaseUrl verbatim when set.
///  - JSON envelopes: {"product":{...}}, {"customer":{...}}, {"subscription":{...}},
///    lists are arrays of the same envelopes.
///  - Errors are 4xx/5xx with {"errors":["..."]} (or {"error":"..."}).
/// </summary>
public interface IMaxioClient
{
    /// <summary>GET /products.json</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /products/handle/{handle}.json — null when the handle is unknown.</summary>
    Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference={reference} — null when no customer matches.</summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>GET /subscriptions/{id}.json — null when the subscription does not exist.</summary>
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>GET /subscriptions.json?reference={reference}</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    /// <exception cref="MaxioApiException">When Maxio rejects the request (e.g. duplicate reference).</exception>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}

public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var settings = options.Value ?? new MaxioOptions();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set the MAXIO_API_KEY environment variable or the Maxio:ApiKey user secret.");

        string baseAddress;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            baseAddress = settings.BaseUrl;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
                throw new InvalidOperationException(
                    "Neither Maxio:BaseUrl nor Maxio:Subdomain is configured. Set MAXIO_SITE_SUBDOMAIN (or MAXIO_BASE_URL).");
            baseAddress = $"https://{settings.Subdomain}.chargify.com";
        }

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(baseAddress.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "products.json"), cancellationToken);
        var wrappers = await DeserializeAsync<List<ProductEnvelope>>(response, cancellationToken) ?? new List<ProductEnvelope>();
        return wrappers.Select(w => w.Product).Where(p => p is not null).Cast<MaxioProduct>().ToList();
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"products/handle/{Uri.EscapeDataString(handle)}.json"), cancellationToken, notFoundMeansNull: true);
        var envelope = await DeserializeAsync<ProductEnvelope>(response, cancellationToken);
        return envelope?.Product;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}"), cancellationToken, notFoundMeansNull: true);
        var envelope = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"subscriptions/{subscriptionId}.json"), cancellationToken, notFoundMeansNull: true);
        var envelope = await DeserializeAsync<SubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"subscriptions.json?reference={Uri.EscapeDataString(reference)}"), cancellationToken);
        var wrappers = await DeserializeAsync<List<SubscriptionEnvelope>>(response, cancellationToken) ?? new List<SubscriptionEnvelope>();
        return wrappers.Select(w => w.Subscription).Where(s => s is not null).Cast<MaxioSubscription>().ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionPayload
            {
                ProductHandle = request.ProductHandle,
                PaymentCollectionMethod = request.PaymentCollectionMethod,
                Reference = request.Reference,
                CustomerId = request.CustomerId,
                CustomerAttributes = request.CustomerAttributes is null
                    ? null
                    : new CustomerAttributesPayload
                    {
                        FirstName = request.CustomerAttributes.FirstName,
                        LastName = request.CustomerAttributes.LastName,
                        Email = request.CustomerAttributes.Email,
                        Reference = request.CustomerAttributes.Reference
                    }
            }
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var message = new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var response = await SendAsync(message, cancellationToken);
        var envelope = await DeserializeAsync<SubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(200, Array.Empty<string>(), "Maxio returned an empty subscription on create.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken, bool notFoundMeansNull = false)
    {
        var response = await _httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (notFoundMeansNull && (int)response.StatusCode == 404)
                return response;
            throw new MaxioApiException(
                (int)response.StatusCode,
                ParseErrors(body),
                $"Maxio API call {message.Method} {message.RequestUri} failed with HTTP {(int)response.StatusCode}.");
        }
        return response;
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            return null;
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<string>();
        try
        {
            var parsed = JsonSerializer.Deserialize<ErrorEnvelope>(body, JsonOptions);
            if (parsed?.Errors is { Count: > 0 })
                return parsed.Errors;
            if (!string.IsNullOrWhiteSpace(parsed?.Error))
                return new[] { parsed.Error };
        }
        catch (JsonException)
        {
            return new[] { body };
        }
        return new[] { body };
    }

    private sealed class ProductEnvelope
    {
        public MaxioProduct? Product { get; set; }
    }

    private sealed class CustomerEnvelope
    {
        public MaxioCustomer? Customer { get; set; }
    }

    private sealed class SubscriptionEnvelope
    {
        public MaxioSubscription? Subscription { get; set; }
    }

    private sealed class ErrorEnvelope
    {
        public List<string>? Errors { get; set; }
        public string? Error { get; set; }
    }

    private sealed class CreateSubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public CreateSubscriptionPayload Subscription { get; set; } = new();
    }

    private sealed class CreateSubscriptionPayload
    {
        [JsonPropertyName("product_handle")]
        public string ProductHandle { get; set; } = string.Empty;

        [JsonPropertyName("payment_collection_method")]
        public string PaymentCollectionMethod { get; set; } = string.Empty;

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("customer_id")]
        public int? CustomerId { get; set; }

        [JsonPropertyName("customer_attributes")]
        public CustomerAttributesPayload? CustomerAttributes { get; set; }
    }

    private sealed class CustomerAttributesPayload
    {
        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }
    }
}
