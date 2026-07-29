using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin typed HttpClient wrapper over the Maxio Advanced Billing REST API. The <see cref="HttpClient"/>
/// is expected to be pre-configured (base address + HTTP Basic auth header) by DI. Each method maps to
/// exactly one confirmed Maxio endpoint and translates non-success responses into
/// <see cref="MaxioApiException"/> (except lookups, where a 404 is a normal "not found" and returns null).
/// </summary>
public class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>GET /product_families.json</summary>
    internal async Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("product_families.json", cancellationToken);
        var envelopes = await ReadResultAsync<List<MaxioProductFamilyEnvelope>>(response, "ListProductFamilies", cancellationToken);
        return envelopes?.Select(e => e.ProductFamily).Where(f => f is not null).Select(f => f!).ToList()
               ?? new List<MaxioProductFamily>();
    }

    /// <summary>GET /product_families/{familyId}/products.json</summary>
    internal async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(int familyId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"product_families/{familyId}/products.json?per_page=200", cancellationToken);
        var envelopes = await ReadResultAsync<List<MaxioProductEnvelope>>(response, "ListProductsForProductFamily", cancellationToken);
        return envelopes?.Select(e => e.Product).Where(p => p is not null).Select(p => p!).ToList()
               ?? new List<MaxioProduct>();
    }

    /// <summary>GET /customers/lookup.json?reference={reference} — returns null on 404 (no such customer).</summary>
    internal async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        string encoded = Uri.EscapeDataString(reference);
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={encoded}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadResultAsync<MaxioCustomerEnvelope>(response, "LookupCustomerByReference", cancellationToken);
        return envelope?.Customer;
    }

    /// <summary>POST /customers.json</summary>
    internal async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes, CancellationToken cancellationToken)
    {
        var body = new MaxioCreateCustomerRequest { Customer = attributes };
        using var response = await _httpClient.PostAsJsonAsync("customers.json", body, JsonOptions, cancellationToken);
        var envelope = await ReadResultAsync<MaxioCustomerEnvelope>(response, "CreateCustomer", cancellationToken);

        return envelope?.Customer
               ?? throw new MaxioApiException(response.StatusCode, "CreateCustomer",
                   new[] { "Maxio returned an empty customer body." }, null);
    }

    /// <summary>POST /subscriptions.json</summary>
    internal async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionAttributes attributes, CancellationToken cancellationToken)
    {
        var body = new MaxioCreateSubscriptionRequest { Subscription = attributes };
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, JsonOptions, cancellationToken);
        var envelope = await ReadResultAsync<MaxioSubscriptionEnvelope>(response, "CreateSubscription", cancellationToken);

        return envelope?.Subscription
               ?? throw new MaxioApiException(response.StatusCode, "CreateSubscription",
                   new[] { "Maxio returned an empty subscription body." }, null);
    }

    /// <summary>GET /customers/{customerId}/subscriptions.json</summary>
    internal async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json?per_page=200", cancellationToken);
        var envelopes = await ReadResultAsync<List<MaxioSubscriptionEnvelope>>(response, "ListCustomerSubscriptions", cancellationToken);
        return envelopes?.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList()
               ?? new List<MaxioSubscription>();
    }

    /// <summary>
    /// Reads a successful JSON body, or throws <see cref="MaxioApiException"/> with parsed error detail
    /// for any non-success status.
    /// </summary>
    private async Task<T?> ReadResultAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            // A body is always expected for the endpoints we call, but be defensive about empty payloads.
            if (response.Content.Headers.ContentLength == 0)
            {
                return default;
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }

        string rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        IReadOnlyList<string> errors = ParseErrors(rawBody);

        _logger.LogWarning("Maxio {Operation} failed: {Status} {Body}", operation, (int)response.StatusCode, rawBody);
        throw new MaxioApiException(response.StatusCode, operation, errors, rawBody);
    }

    private static IReadOnlyList<string> ParseErrors(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return Array.Empty<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(rawBody, JsonOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return parsed.Errors;
            }
        }
        catch (JsonException)
        {
            // Not the { "errors": [...] } shape; fall through and surface the raw body instead.
        }

        return Array.Empty<string>();
    }
}
