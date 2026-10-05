using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing API.
/// Authentication is HTTP Basic: the API key as the username and "X" as the password.
/// List endpoints return bare JSON arrays; single-resource endpoints wrap the
/// resource in an envelope object ({ "customer": {...} }, { "subscription": {...} }).
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var maxioOptions = options.Value;
        _options = maxioOptions;

        if (!maxioOptions.IsConfigured)
        {
            throw new MaxioApiException(
                "Maxio integration is not configured. Supply Maxio:ApiKey, Maxio:Subdomain and " +
                "Maxio:ProductFamilyHandle (e.g. via user-secrets or environment variables).");
        }

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(maxioOptions.GetEffectiveBaseUrl());
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{maxioOptions.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string familyHandle, CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var batch = await GetJsonAsync<List<ProductEnvelope>>(
                $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?page={page}&per_page={perPage}",
                cancellationToken);
            if (batch is null || batch.Count == 0)
            {
                break;
            }

            products.AddRange(batch.Where(b => b.Value is not null).Select(b => b.Value!));

            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get,
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content
            .ReadFromJsonAsync<CustomerEnvelope>(_jsonOptions, cancellationToken);
        return envelope?.Value;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomerBody customer, CancellationToken cancellationToken = default)
    {
        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "customers.json")
            {
                Content = JsonContent.Create(new MaxioCreateCustomerRequest { Customer = customer }, options: _jsonOptions)
            },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content
            .ReadFromJsonAsync<CustomerEnvelope>(_jsonOptions, cancellationToken);
        return envelope?.Value
            ?? throw new MaxioApiException("Maxio returned an empty customer payload.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string productHandle, int customerId, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionBody
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = _options.PaymentCollectionMethod
            }
        };

        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
            {
                Content = JsonContent.Create(body, options: _jsonOptions)
            },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content
            .ReadFromJsonAsync<SubscriptionEnvelope>(_jsonOptions, cancellationToken);
        return envelope?.Value
            ?? throw new MaxioApiException("Maxio returned an empty subscription payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = new List<MaxioSubscription>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var batch = await GetJsonAsync<List<SubscriptionEnvelope>>(
                $"customers/{customerId}/subscriptions.json?page={page}&per_page={perPage}",
                cancellationToken);
            if (batch is null || batch.Count == 0)
            {
                break;
            }

            subscriptions.AddRange(batch.Where(b => b.Value is not null).Select(b => b.Value!));

            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        return subscriptions;
    }

    private async Task<T?> GetJsonAsync<T>(string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, requestUri), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
    }

    // Maxio limits concurrency per site and responds with 429 when throttled
    // (see the Billing API docs, "Error Handling & Rate Limiting"). Retry a small
    // number of times with backoff, honouring Retry-After when present.
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            using var request = requestFactory();
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxRetryAttempts)
            {
                await Task.Delay(DefaultRetryDelay * attempt, cancellationToken);
                continue;
            }

            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests ||
                            (int)response.StatusCode >= 500;

            if (!retryable || attempt == MaxRetryAttempts)
            {
                return response;
            }

            var delay = DefaultRetryDelay * (1 << (attempt - 1));
            if (response.Headers.RetryAfter?.Delta is { } delta)
            {
                delay = delta;
            }

            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        List<string> errors = new();
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(content))
            {
                // Error bodies vary by endpoint: { "errors": ["..."] },
                // { "errors": { "customer": "..." } } or { "error": "..." }.
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.TryGetProperty("errors", out var errorsProperty))
                {
                    if (errorsProperty.ValueKind == JsonValueKind.Array)
                    {
                        errors.AddRange(errorsProperty.EnumerateArray()
                            .Where(e => e.ValueKind == JsonValueKind.String)
                            .Select(e => e.GetString()!));
                    }
                    else if (errorsProperty.ValueKind == JsonValueKind.Object)
                    {
                        errors.AddRange(errorsProperty.EnumerateObject()
                            .Where(p => p.Value.ValueKind == JsonValueKind.String)
                            .Select(p => $"{p.Name}: {p.Value.GetString()}"));
                    }
                }
                else if (document.RootElement.TryGetProperty("error", out var errorProperty) &&
                         errorProperty.ValueKind == JsonValueKind.String)
                {
                    errors.Add(errorProperty.GetString()!);
                }

                if (errors.Count == 0)
                {
                    errors.Add(content);
                }
            }
        }
        catch (JsonException)
        {
            // non-JSON error body - fall through with generic message
        }

        throw new MaxioApiException(
            $"Maxio API request failed with status {(int)response.StatusCode} {response.StatusCode}.",
            (int)response.StatusCode,
            errors);
    }

    private sealed class CustomerEnvelope
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Value { get; set; }
    }

    private sealed class ProductEnvelope
    {
        [JsonPropertyName("product")]
        public MaxioProduct? Value { get; set; }
    }

    private sealed class SubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription? Value { get; set; }
    }
}