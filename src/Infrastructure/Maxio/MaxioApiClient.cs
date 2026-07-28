using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed <see cref="HttpClient"/>-based implementation of <see cref="IMaxioApiClient"/>. The base
/// address and HTTP Basic authorization are configured on the injected client at registration
/// time (see <c>MaxioServiceCollectionExtensions</c>); this class owns request/response shaping.
/// </summary>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<ProductEnvelope>>(
            HttpMethod.Get, "products.json", body: null, "GET /products.json", cancellationToken)
            ?? new List<ProductEnvelope>();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
    }

    public async Task<MaxioCustomerDto?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        // A missing reference legitimately yields 404; treat that as "no customer" rather than an error.
        var envelope = await SendAsync<CustomerEnvelope>(
            HttpMethod.Get, path, body: null, "GET /customers/lookup.json", cancellationToken, treatNotFoundAsNull: true);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomerDto> CreateCustomerAsync(CreateCustomerDto customer, CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerEnvelope { Customer = customer };
        var envelope = await SendAsync<CustomerEnvelope>(
            HttpMethod.Post, "customers.json", request, "POST /customers.json", cancellationToken);

        if (envelope?.Customer is null)
        {
            throw new MaxioApiException(HttpStatusCode.OK, "POST /customers.json", "Response did not contain a customer.");
        }

        return envelope.Customer;
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionDto subscription, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionEnvelope { Subscription = subscription };
        var envelope = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", request, "POST /subscriptions.json", cancellationToken);

        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException(HttpStatusCode.OK, "POST /subscriptions.json", "Response did not contain a subscription.");
        }

        return envelope.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<SubscriptionEnvelope>>(
            HttpMethod.Get, path, body: null, $"GET /customers/{customerId}/subscriptions.json", cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    public async Task<string?> GetSiteCurrencyAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<SiteEnvelope>(
            HttpMethod.Get, "site.json", body: null, "GET /site.json", cancellationToken);
        return envelope?.Site?.Currency;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        string description,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Transport error calling Maxio: {Description}", description);
            throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, description, ex.Message, ex);
        }

        using (response)
        {
            if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Maxio call {Description} returned {StatusCode}. Body: {Body}",
                    description, (int)response.StatusCode, content);
                throw new MaxioApiException(response.StatusCode, description, content);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(content, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize Maxio response for {Description}. Body: {Body}", description, content);
                throw new MaxioApiException(response.StatusCode, description, content, ex);
            }
        }
    }
}
