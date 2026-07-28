using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed <see cref="HttpClient"/> implementation of <see cref="IMaxioApiClient"/>. Auth,
/// base address and default headers are configured on the injected client (see
/// <see cref="MaxioServiceCollectionExtensions"/>).
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioApiClient> _logger;

    /// <summary>
    /// JSON options matching the Maxio wire format: snake_case property names and omission of
    /// null members on write (so partial create requests stay minimal).
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public MaxioApiClient(HttpClient httpClient, IAppLogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProductDto>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        // The path segment accepts the family handle prefixed with "handle:".
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        var envelopes = await SendAsync<List<ProductEnvelope>>(HttpMethod.Get, path, content: null, "listProductsForProductFamily", cancellationToken)
            ?? new List<ProductEnvelope>();

        var products = new List<ProductDto>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    public async Task<CustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        // The spec's lookup returns a single match; a missing reference yields 404.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "readCustomerByReference", cancellationToken);

        var envelope = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<CustomerDto> CreateCustomerAsync(CreateCustomerDto customer, CancellationToken cancellationToken = default)
    {
        var body = new CreateCustomerRequest(customer);
        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", body, "createCustomer", cancellationToken);

        return envelope?.Customer
            ?? throw new BillingException("Maxio returned an empty customer response for createCustomer.");
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<SubscriptionEnvelope>>(HttpMethod.Get, path, content: null, "listCustomerSubscriptions", cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        var subscriptions = new List<SubscriptionDto>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionDto subscription, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionRequest(subscription);
        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, "createSubscription", cancellationToken);

        return envelope?.Subscription
            ?? throw new BillingException("Maxio returned an empty subscription response for createSubscription.");
    }

    private async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string path, object? content, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (content is not null)
        {
            request.Content = JsonContent.Create(content, mediaType: null, JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Network error calling Maxio operation '{0}': {1}", operation, ex.Message);
            throw new BillingException($"Unable to reach the Maxio billing service ('{operation}').", ex);
        }

        try
        {
            await EnsureSuccessAsync(response, operation, cancellationToken);
            return await DeserializeAsync<TResponse>(response, cancellationToken);
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Maxio operation '{0}' returned {1}: {2}", operation, (int)response.StatusCode, body);
        throw MaxioApiException.FromResponse(response.StatusCode, operation, body);
    }

    private static async Task<TResponse?> DeserializeAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions, cancellationToken);
    }
}
