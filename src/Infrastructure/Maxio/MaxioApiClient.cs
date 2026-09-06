using System;
using System.Collections.Generic;
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
/// <see cref="IMaxioApiClient"/> over <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// The base address, HTTP Basic credentials, timeout and retry handler are applied by
/// <see cref="MaxioServiceCollectionExtensions.AddMaxioSubscriptions"/> when the typed client is
/// registered, so this class only deals with routes, payloads and error translation.
/// </remarks>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>
    /// Maxio speaks snake_case and returns offset-bearing ISO-8601 timestamps, which
    /// <see cref="DateTimeOffset"/> parses natively.
    /// </summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSiteEnvelope>(HttpMethod.Get, "site.json", body: null, cancellationToken);
        return envelope?.Site ?? throw new MaxioApiException(
            HttpMethod.Get, "site.json", HttpStatusCode.OK, new[] { "Response did not contain a 'site' object." }, rawBody: null);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // Maxio accepts either the numeric family id or the handle prefixed with "handle:" in this
        // path segment. Handles are stable across catalog re-seeds; numeric ids are not.
        var requestUri = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, requestUri, body: null, cancellationToken);

        var products = new List<MaxioProduct>();
        foreach (var envelope in envelopes ?? new List<MaxioProductEnvelope>())
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var requestUri = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, requestUri, body: null, cancellationToken, notFoundReturnsNull: true);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest { Customer = customer };
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return envelope?.Customer ?? throw new MaxioApiException(
            HttpMethod.Post, "customers.json", HttpStatusCode.OK, new[] { "Response did not contain a 'customer' object." }, rawBody: null);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var requestUri = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, requestUri, body: null, cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in envelopes ?? new List<MaxioSubscriptionEnvelope>())
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return envelope?.Subscription ?? throw new MaxioApiException(
            HttpMethod.Post, "subscriptions.json", HttpStatusCode.OK, new[] { "Response did not contain a 'subscription' object." }, rawBody: null);
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var requestUri = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, requestUri, body: null, cancellationToken, notFoundReturnsNull: true);
        return envelope?.Subscription;
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string requestUri,
        object? body,
        CancellationToken cancellationToken,
        bool notFoundReturnsNull = false)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: SerializerOptions);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (notFoundReturnsNull && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var errors = MaxioErrorReader.ReadErrors(errorBody);

            _logger.LogWarning(
                "Maxio {Method} {RequestUri} failed with {StatusCode}: {Errors}",
                method, requestUri, (int)response.StatusCode, string.Join("; ", errors));

            throw new MaxioApiException(method, requestUri, response.StatusCode, errors, MaxioErrorReader.Truncate(errorBody));
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return await JsonSerializer.DeserializeAsync<TResponse>(stream, SerializerOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(
                method, requestUri, response.StatusCode,
                new[] { $"Could not read the Maxio response body: {ex.Message}" }, rawBody: null);
        }
    }
}
