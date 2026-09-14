using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioApiClient : IMaxioApiClient
{
    private const string BasicAuthPassword = "x";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProductFamilyDto>> ListProductFamiliesAsync(CancellationToken cancellationToken)
    {
        var envelopes = await SendAsync<IReadOnlyList<ProductFamilyEnvelope>>(
            HttpMethod.Get, "product_families.json", allowNotFound: false, body: null, cancellationToken);
        var result = new List<ProductFamilyDto>(envelopes?.Count ?? 0);
        if (envelopes != null)
        {
            foreach (var envelope in envelopes)
            {
                result.Add(envelope.ProductFamily);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken)
    {
        var envelopes = await SendAsync<IReadOnlyList<ProductEnvelope>>(
            HttpMethod.Get, $"product_families/{productFamilyId}/products.json?per_page=200", allowNotFound: false, body: null, cancellationToken);
        var result = new List<MaxioProduct>(envelopes?.Count ?? 0);
        if (envelopes != null)
        {
            foreach (var envelope in envelopes)
            {
                result.Add(envelope.Product);
            }
        }

        return result;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<CustomerEnvelope>(
            HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", allowNotFound: true, body: null, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<CustomerEnvelope>(
            HttpMethod.Post, "customers.json", allowNotFound: false, body: request, cancellationToken);
        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await SendAsync<IReadOnlyList<SubscriptionEnvelope>>(
            HttpMethod.Get, $"customers/{customerId}/subscriptions.json", allowNotFound: false, body: null, cancellationToken);
        var result = new List<MaxioSubscription>(envelopes?.Count ?? 0);
        if (envelopes != null)
        {
            foreach (var envelope in envelopes)
            {
                result.Add(envelope.Subscription);
            }
        }

        return result;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", allowNotFound: false, body: request, cancellationToken);
        return envelope.Subscription;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, bool allowNotFound, object? body, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var request = new HttpRequestMessage(method, new Uri(options.ResolveBaseAddress(), path))
        {
            Headers =
            {
                Accept = { new MediaTypeWithQualityHeaderValue("application/json") }
            }
        };

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: 'Maxio:ApiKey' is missing. Set it from the MAXIO_API_KEY " +
                "environment variable (user-secrets) before calling this endpoint.");
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:{BasicAuthPassword}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio Advanced Billing returned HTTP {(int)statusCode} for {method} {path}: {body}",
                (int)response.StatusCode, method, path, responseBody);
        }

        if (response.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        }

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        throw new MaxioApiException(response.StatusCode, ParseErrors(responseBody), responseBody);
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
            return parsed?.Errors ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
