using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, typed HTTP client over the Maxio Advanced Billing (Chargify) REST API. It knows the
/// wire shapes and error conventions but holds no business logic. Base address and HTTP Basic
/// authentication are configured on the injected <see cref="HttpClient"/> in DI.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Lists all product families on the site.</summary>
    public async Task<IReadOnlyList<MaxioProductFamily>> GetProductFamiliesAsync(CancellationToken ct = default)
    {
        var envelopes = await GetAsync<List<ProductFamilyEnvelope>>("product_families.json", ct)
                        ?? new List<ProductFamilyEnvelope>();
        return envelopes.ConvertAll(e => e.ProductFamily);
    }

    /// <summary>Lists the products (plans) belonging to a product family. Requires the numeric family id.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> GetProductsInFamilyAsync(long familyId, CancellationToken ct = default)
    {
        var envelopes = await GetAsync<List<ProductEnvelope>>($"product_families/{familyId}/products.json", ct)
                        ?? new List<ProductEnvelope>();
        return envelopes.ConvertAll(e => e.Product);
    }

    /// <summary>Looks up a customer by its unique reference. Returns null when no such customer exists (404).</summary>
    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await ReadBodyAsync(response, ct);
        await EnsureSuccessAsync(response, body);
        return Deserialize<CustomerEnvelope>(body)?.Customer;
    }

    /// <summary>Creates a customer. Throws <see cref="MaxioApiException"/> on 422 (e.g. duplicate reference).</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(CustomerAttributes attributes, CancellationToken ct = default)
    {
        var request = new CreateCustomerRequest { Customer = attributes };
        var envelope = await PostAsync<CreateCustomerRequest, CustomerEnvelope>("customers.json", request, ct);
        return envelope!.Customer;
    }

    /// <summary>Lists a customer's subscriptions.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken ct = default)
    {
        var envelopes = await GetAsync<List<SubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", ct)
                        ?? new List<SubscriptionEnvelope>();
        return envelopes.ConvertAll(e => e.Subscription);
    }

    /// <summary>Creates a subscription for an existing customer.</summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(SubscriptionAttributes attributes, CancellationToken ct = default)
    {
        var request = new CreateSubscriptionRequest { Subscription = attributes };
        var envelope = await PostAsync<CreateSubscriptionRequest, SubscriptionEnvelope>("subscriptions.json", request, ct);
        return envelope!.Subscription;
    }

    // ---- transport helpers ----

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, ct);
        var body = await ReadBodyAsync(response, ct);
        await EnsureSuccessAsync(response, body);
        return Deserialize<T>(body);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string url, TRequest payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(url, content, ct);
        var body = await ReadBodyAsync(response, ct);
        await EnsureSuccessAsync(response, body);
        return Deserialize<TResponse>(body);
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
        => await response.Content.ReadAsStringAsync(ct);

    private static Task EnsureSuccessAsync(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return Task.CompletedTask;
        }

        var errors = ParseErrors(body);
        throw new MaxioApiException(response.StatusCode, errors, body);
    }

    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(body, JsonOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return parsed.Errors;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body (e.g. an HTML gateway page); fall through.
        }

        return Array.Empty<string>();
    }

    private static T? Deserialize<T>(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }
}
