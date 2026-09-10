using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed <see cref="HttpClient"/> over the Maxio Advanced Billing REST API. The base
/// address and Basic-auth header are configured on the injected <see cref="HttpClient"/>
/// at registration time (see <c>MaxioServiceCollectionExtensions</c>).
/// </summary>
internal sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<ProductFamilyEnvelope>>(
            HttpMethod.Get, "product_families.json", content: null, cancellationToken);
        return Unwrap(envelopes, e => e.ProductFamily);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<ProductEnvelope>>(
            HttpMethod.Get, $"product_families/{productFamilyId}/products.json", content: null, cancellationToken);
        return Unwrap(envelopes, e => e.Product);
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        // A missing customer is a normal, expected outcome of lookup, not an error.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken);
        var envelope = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken = default)
    {
        var body = new CreateCustomerRequest { Customer = customer };
        var envelope = await SendAsync<CustomerEnvelope>(
            HttpMethod.Post, "customers.json", body, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, "POST", "customers.json", new[] { "Maxio returned an empty customer payload." });
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionRequest { Subscription = subscription };
        var envelope = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.OK, "POST", "subscriptions.json", new[] { "Maxio returned an empty subscription payload." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<SubscriptionEnvelope>>(
            HttpMethod.Get, $"customers/{customerId}/subscriptions.json", content: null, cancellationToken);
        return Unwrap(envelopes, e => e.Subscription);
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (content is not null)
        {
            request.Content = JsonContent.Create(content, options: SerializerOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, method, path, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, HttpMethod method, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await ExtractErrorsAsync(response, cancellationToken);
        _logger.LogError(
            "Maxio API {Method} {Path} returned {StatusCode}: {Errors}",
            method, path, (int)response.StatusCode, string.Join("; ", errors));
        throw new MaxioApiException(response.StatusCode, method.Method, path, errors);
    }

    private static async Task<IReadOnlyList<string>> ExtractErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        // Expected error shape is {"errors": ["..."]}. Tolerate other/object shapes by
        // falling back to a trimmed snippet of the raw body.
        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(raw, SerializerOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return parsed.Errors;
            }
        }
        catch (JsonException)
        {
            // fall through to raw snippet
        }

        var snippet = raw.Length > 500 ? raw[..500] : raw;
        return new[] { snippet };
    }

    private static IReadOnlyList<TItem> Unwrap<TEnvelope, TItem>(List<TEnvelope>? envelopes, Func<TEnvelope, TItem?> selector)
        where TItem : class
    {
        if (envelopes is null || envelopes.Count == 0)
        {
            return Array.Empty<TItem>();
        }

        var items = new List<TItem>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            var item = selector(envelope);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }
}
