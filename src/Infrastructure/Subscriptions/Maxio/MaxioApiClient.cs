using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Default <see cref="IMaxioApiClient"/> over a configured <see cref="HttpClient"/> (base address and Basic
/// auth are set up by the DI registration). Handles JSON (de)serialization and turns non-success responses
/// into <see cref="MaxioApiException"/>.
/// </summary>
public sealed class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IAppLogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        // The product_family_id path segment accepts a handle prefixed with "handle:" per the spec.
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var wrappers = await ReadAsync<List<MaxioProductResponse>>(response, "list products in family", cancellationToken);

        var products = new List<MaxioProduct>();
        foreach (var wrapper in wrappers ?? new List<MaxioProductResponse>())
        {
            if (wrapper.Product is not null)
                products.Add(wrapper.Product);
        }
        return products;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        // The spec returns 404 when no customer matches the reference; that is an expected "not found", not an error.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var wrapper = await ReadAsync<MaxioCustomerResponse>(response, "lookup customer", cancellationToken);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerRequest { Customer = customer };
        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, SerializerOptions, cancellationToken);
        var wrapper = await ReadAsync<MaxioCustomerResponse>(response, "create customer", cancellationToken);

        return wrapper?.Customer
            ?? throw new MaxioApiException(response.StatusCode, "create customer", new[] { "response body did not contain a customer" });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var wrappers = await ReadAsync<List<MaxioSubscriptionResponse>>(response, "list customer subscriptions", cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        foreach (var wrapper in wrappers ?? new List<MaxioSubscriptionResponse>())
        {
            if (wrapper.Subscription is not null)
                subscriptions.Add(wrapper.Subscription);
        }
        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest { Subscription = subscription };
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, SerializerOptions, cancellationToken);
        var wrapper = await ReadAsync<MaxioSubscriptionResponse>(response, "create subscription", cancellationToken);

        return wrapper?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, "create subscription", new[] { "response body did not contain a subscription" });
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errors = MaxioApiException.ParseErrors(body);
            _logger.LogWarning("Maxio {Operation} returned {StatusCode}: {Errors}",
                operation, (int)response.StatusCode, string.Join("; ", errors));
            throw new MaxioApiException(response.StatusCode, operation, errors);
        }

        if (string.IsNullOrWhiteSpace(body))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(body, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode, operation, new[] { $"could not parse response: {ex.Message}" });
        }
    }
}
