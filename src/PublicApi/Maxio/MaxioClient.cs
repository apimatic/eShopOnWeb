using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Hand-written client for the Maxio Advanced Billing API built against the Maxio OpenAPI
/// specification (maxio-spec/openapi.yaml). Authentication uses HTTP Basic auth with the API key
/// as the username, as documented by the spec's request example.
/// </summary>
public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        string path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);

        var envelopes = await ReadAsync<List<MaxioProductEnvelope>>(response, cancellationToken) ?? new List<MaxioProductEnvelope>();
        return envelopes.Where(e => e.Product is not null).Select(e => e.Product!).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        string path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, path, cancellationToken);

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken)
    {
        string path = "customers.json";
        var body = new MaxioCreateCustomerRequest { Customer = customer };
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), System.Text.Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, null, "Maxio returned an empty customer payload.");
        return envelope.Customer
            ?? throw new MaxioApiException((int)response.StatusCode, null, "Maxio returned an empty customer payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        string path = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);

        var envelopes = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken) ?? new List<MaxioSubscriptionEnvelope>();
        return envelopes.Where(e => e.Subscription is not null).Select(e => e.Subscription!).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionInput subscription, CancellationToken cancellationToken)
    {
        string path = "subscriptions.json";
        var body = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), System.Text.Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);

        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, null, "Maxio returned an empty subscription payload.");
        return envelope.Subscription
            ?? throw new MaxioApiException((int)response.StatusCode, null, "Maxio returned an empty subscription payload.");
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? responseBody = null;
        try
        {
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read Maxio error response body for {Path}.", path);
        }

        int statusCode = (int)response.StatusCode;
        _logger.LogError("Maxio request '{Path}' failed with status {StatusCode}. Body: {Body}", path, statusCode, responseBody);
        throw new MaxioApiException(statusCode, responseBody, $"Maxio request '{path}' failed with HTTP {(int)response.StatusCode}.");
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
