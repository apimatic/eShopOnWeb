using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

/// <inheritdoc cref="IMaxioBillingApi"/>
public sealed class MaxioBillingApi : IMaxioBillingApi
{
    private const int MaxRetries = 3;

    // Transient failures worth retrying (server hiccups and rate limiting).
    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes = new()
    {
        HttpStatusCode.TooManyRequests,          // 429
        HttpStatusCode.InternalServerError,      // 500
        HttpStatusCode.BadGateway,               // 502
        HttpStatusCode.ServiceUnavailable,       // 503
        HttpStatusCode.GatewayTimeout            // 504
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioBillingApi> _logger;

    public MaxioBillingApi(HttpClient httpClient, IOptions<MaxioBillingOptions> options,
        ILogger<MaxioBillingApi> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: an API key (Maxio:ApiKey / MAXIO_API_KEY) is required.");
        }

        _httpClient.BaseAddress = new Uri(value.ResolveBaseUrl(environment: null));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{value.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProductFamilyEnvelope>> ListProductFamiliesAsync(CancellationToken ct)
    {
        return await GetAsync<List<MaxioProductFamilyEnvelope>>("product_families.json", ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MaxioProductEnvelope>> ListProductsAsync(long productFamilyId, CancellationToken ct)
    {
        return await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/{productFamilyId}/products.json", ct).ConfigureAwait(false);
    }

    public async Task<MaxioCustomerEnvelope?> FindCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        return await GetAsync<MaxioCustomerEnvelope?>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", ct, allowNotFound: true).ConfigureAwait(false);
    }

    public async Task<MaxioCustomerEnvelope> CreateCustomerAsync(MaxioCreateCustomerEnvelope request, CancellationToken ct)
    {
        return await PostAsync<MaxioCreateCustomerEnvelope, MaxioCustomerEnvelope>("customers.json", request, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MaxioSubscriptionEnvelope>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken ct)
    {
        return await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", ct).ConfigureAwait(false);
    }

    public async Task<MaxioSubscriptionEnvelope> CreateSubscriptionAsync(MaxioCreateSubscriptionEnvelope request, CancellationToken ct)
    {
        return await PostAsync<MaxioCreateSubscriptionEnvelope, MaxioSubscriptionEnvelope>("subscriptions.json", request, ct)
            .ConfigureAwait(false);
    }

    private async Task<T> GetAsync<T>(string requestUri, CancellationToken ct, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default!;
        }

        await EnsureSuccessAsync(response, requestUri, ct).ConfigureAwait(false);
        return await ReadContentAsync<T>(response, ct).ConfigureAwait(false);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string requestUri, TRequest requestBody, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody, SerializeOptions), Encoding.UTF8, "application/json")
        };

        using var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, requestUri, ct).ConfigureAwait(false);
        return await ReadContentAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken ct)
    {
        int attempt = 0;
        while (true)
        {
            attempt++;

            // Each attempt needs its own request copy (request bodies are not replayable otherwise).
            var attemptRequest = attempt == 1
                ? request
                : await CloneAsync(request, ct).ConfigureAwait(false);

            var response = await _httpClient.SendAsync(attemptRequest, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (attempt >= MaxRetries || !RetryableStatusCodes.Contains(response.StatusCode))
            {
                return response;
            }

            var delay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
            if (response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter < TimeSpan.FromSeconds(30))
            {
                delay = retryAfter;
            }

            _logger.LogWarning(
                "Maxio API request '{Method} {Uri}' returned {StatusCode}; retrying ({Attempt}/{Max}) in {Delay}ms.",
                request.Method, request.RequestUri, (int)response.StatusCode, attempt, MaxRetries, delay.TotalMilliseconds);

            response.Dispose();
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            clone.Content = new ByteArrayContent(body);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string requestUri, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var errors = MaxioErrorParser.Parse(body);
        throw new MaxioApiException(response.StatusCode, requestUri, errors);
    }

    private static async Task<T> ReadContentAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(content, DeserializeOptions)!;
    }
}
