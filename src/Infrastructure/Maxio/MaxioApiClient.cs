using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin typed client over the Maxio Advanced Billing HTTP API. Every call maps to a path,
/// verb, and payload defined in the OpenAPI spec (maxio-spec/openapi.yaml). Base address and
/// HTTP Basic authentication are configured on the injected <see cref="HttpClient"/> at registration.
/// </summary>
public class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IAppLogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>GET /product_families/{product_family_id}/products.json (id may be "handle:{handle}").</summary>
    internal async Task<IReadOnlyList<ProductWire>> ListProductsForFamilyAsync(
        string familyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, "list products for product family", cancellationToken);

        var envelopes = await ReadAsync<List<ProductEnvelope>>(response, cancellationToken)
                        ?? new List<ProductEnvelope>();

        var products = new List<ProductWire>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    /// <summary>GET /customers/lookup.json?reference=... — returns null when no customer matches.</summary>
    internal async Task<CustomerWire?> LookupCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer by reference", cancellationToken);
        var envelope = await ReadAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    /// <summary>POST /customers.json.</summary>
    internal async Task<CustomerWire> CreateCustomerAsync(
        CustomerAttributesWire attributes, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerBody { Customer = attributes };
        using var content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _httpClient.PostAsync("customers.json", content, cancellationToken);
        await EnsureSuccessAsync(response, "create customer", cancellationToken);

        var envelope = await ReadAsync<CustomerEnvelope>(response, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException((int)response.StatusCode, null,
                "Maxio create-customer response did not contain a customer.");
        }

        return envelope.Customer;
    }

    /// <summary>GET /customers/{customer_id}/subscriptions.json.</summary>
    internal async Task<IReadOnlyList<SubscriptionWire>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var envelopes = await ReadAsync<List<SubscriptionEnvelope>>(response, cancellationToken)
                        ?? new List<SubscriptionEnvelope>();

        var subscriptions = new List<SubscriptionWire>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    /// <summary>POST /subscriptions.json.</summary>
    internal async Task<SubscriptionWire> CreateSubscriptionAsync(
        CreateSubscriptionWire subscription, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionBody { Subscription = subscription };
        using var content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _httpClient.PostAsync("subscriptions.json", content, cancellationToken);
        await EnsureSuccessAsync(response, "create subscription", cancellationToken);

        var envelope = await ReadAsync<SubscriptionEnvelope>(response, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException((int)response.StatusCode, null,
                "Maxio create-subscription response did not contain a subscription.");
        }

        return envelope.Subscription;
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var rawBody = await SafeReadBodyAsync(response, cancellationToken);
        var detail = ExtractErrorDetail(rawBody);
        _logger.LogWarning(
            $"Maxio call failed while trying to {operation}. Status {(int)response.StatusCode}. Detail: {detail}");

        throw new MaxioApiException(
            (int)response.StatusCode,
            rawBody,
            $"Maxio request to {operation} failed with status {(int)response.StatusCode}. {detail}");
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractErrorDetail(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return "No response body.";
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<MaxioErrorEnvelope>(rawBody, JsonOptions);
            if (envelope?.Errors is { Count: > 0 })
            {
                return string.Join("; ", envelope.Errors);
            }
        }
        catch (JsonException)
        {
            // Not the list-shaped error envelope; fall through to raw body.
        }

        return rawBody!.Length > 500 ? rawBody[..500] : rawBody;
    }
}
