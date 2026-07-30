using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin typed client over the Maxio Advanced Billing REST API. Authentication (HTTP Basic with
/// the API key as username) and base address are configured on the injected <see cref="HttpClient"/>.
/// Each method maps to a single documented Maxio endpoint.
/// </summary>
internal sealed class MaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public MaxioClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>GET /product_families/{handle}/products.json — plans within a product family.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> GetProductsForFamilyAsync(string familyHandle, CancellationToken ct)
    {
        var uri = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";
        using var response = await _http.GetAsync(uri, ct);
        var envelopes = await ReadAsync<List<MaxioProductEnvelope>>(response, ct) ?? new();

        var products = new List<MaxioProduct>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
                products.Add(envelope.Product);
        }
        return products;
    }

    /// <summary>GET /customers/lookup.json?reference=... — returns null when no customer matches.</summary>
    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        var uri = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _http.GetAsync(uri, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, ct);
        return envelope?.Customer;
    }

    /// <summary>POST /customers.json — creates a customer.</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("customers.json", request, JsonOptions, ct);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, ct);
        return envelope?.Customer
            ?? throw new MaxioApiException(response.StatusCode, Array.Empty<string>(), "Create customer returned an empty body.");
    }

    /// <summary>GET /customers/{id}/subscriptions.json — subscriptions belonging to a customer.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken ct)
    {
        var uri = $"customers/{customerId}/subscriptions.json?per_page=200";
        using var response = await _http.GetAsync(uri, ct);
        var envelopes = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, ct) ?? new();

        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
                subscriptions.Add(envelope.Subscription);
        }
        return subscriptions;
    }

    /// <summary>POST /subscriptions.json — creates a subscription.</summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("subscriptions.json", request, JsonOptions, ct);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, ct);
        return envelope?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, Array.Empty<string>(), "Create subscription returned an empty body.");
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new MaxioApiException(response.StatusCode, ParseErrors(body), body);

        if (string.IsNullOrWhiteSpace(body))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode, new[] { "Unexpected response format from Maxio." }, body)
            {
                Source = ex.Message
            };
        }
    }

    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<string>();

        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(body, JsonOptions);
            if (parsed?.Errors is { Count: > 0 } errors)
                return errors;
        }
        catch (JsonException)
        {
            // errors may be an object map rather than an array; fall back to the raw body below.
        }

        return new[] { body.Trim() };
    }
}
