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

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API.
/// All payloads use snake_case JSON; timestamps come back as ISO-8601 strings with offsets.
/// See https://docs.maxio.com for the API documentation.
/// </summary>
public class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/site.json", null, cancellationToken);
        await EnsureSuccessAsync(response, "/site.json", cancellationToken);
        var envelope = await ReadAsync<MaxioSiteEnvelope>(response, cancellationToken);
        return envelope?.Site;
    }

    /// <summary>
    /// Lists the products belonging to the configured product family. Handles are stable across
    /// sandbox re-seeds, numeric ids are not, so the family is addressed by handle.
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var path = $"/product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);
        var items = await ReadAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
        return items?
            .Where(i => i.Product is not null)
            .Select(i => i.Product!)
            .ToList() ?? new List<MaxioProduct>();
    }

    /// <summary>Returns the customer with the given reference, or null when no customer matches.</summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, path, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        CancellationToken cancellationToken)
    {
        var body = new CustomerRequest
        {
            Customer = new CustomerFields
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        const string path = "/customers.json";
        using var response = await SendAsync(HttpMethod.Post, path, ToJsonContent(body), cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer ?? throw new MaxioApiException(HttpStatusCode.OK, new[] { "Maxio did not return a customer." }, path);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var path = $"/customers/{customerId}/subscriptions.json";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);
        var items = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return items?
            .Where(i => i.Subscription is not null)
            .Select(i => i.Subscription!)
            .ToList() ?? new List<MaxioSubscription>();
    }

    /// <summary>
    /// Creates a subscription for an existing Maxio customer. The subscription is created with
    /// remittance (invoice) payment collection so no card capture / 3-DS is attempted at signup.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        long customerId,
        string productHandle,
        string paymentCollectionMethod,
        CancellationToken cancellationToken)
    {
        var body = new SubscriptionRequest
        {
            Subscription = new SubscriptionFields
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                PaymentCollectionMethod = paymentCollectionMethod
            }
        };

        const string path = "/subscriptions.json";
        using var response = await SendAsync(HttpMethod.Post, path, ToJsonContent(body), cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription ?? throw new MaxioApiException(HttpStatusCode.OK, new[] { "Maxio did not return a subscription." }, path);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string pathAndQuery, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, pathAndQuery)
        {
            Content = content
        };
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = new List<string>();
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<MaxioErrorsEnvelope>(raw, JsonOptions);
            if (envelope?.Errors is { Count: > 0 })
            {
                errors.AddRange(envelope.Errors);
            }
            else if (!string.IsNullOrWhiteSpace(raw))
            {
                errors.Add(raw);
            }
        }
        catch
        {
            // fall through with the generic message
        }

        if (errors.Count == 0)
        {
            errors.Add($"Unexpected HTTP {(int)response.StatusCode} response from Maxio.");
        }

        throw new MaxioApiException(response.StatusCode, errors, path);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(raw, JsonOptions);
    }

    private static HttpContent ToJsonContent(object body)
    {
        return JsonContent.Create(body, options: JsonOptions);
    }

    private sealed class CustomerFields
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Reference { get; set; }
    }

    private sealed class CustomerRequest
    {
        public CustomerFields? Customer { get; set; }
    }

    private sealed class SubscriptionFields
    {
        public long? CustomerId { get; set; }
        public string? ProductHandle { get; set; }
        public string? PaymentCollectionMethod { get; set; }
    }

    private sealed class SubscriptionRequest
    {
        public SubscriptionFields? Subscription { get; set; }
    }
}
