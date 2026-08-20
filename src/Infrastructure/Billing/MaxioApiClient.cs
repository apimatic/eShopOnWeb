using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// HTTP client for Maxio Advanced Billing (Chargify) REST API.
/// Authentication is HTTP Basic over TLS with the API key as username and "X" as password.
/// See https://developers.maxio.com/introduction/authentication
/// </summary>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    private const int MaxRetries = 3;
    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var familyKey = $"handle:{Uri.EscapeDataString(productFamilyHandle)}";
        var products = new List<MaxioProduct>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var path = $"product_families/{familyKey}/products.json?page={page}&per_page={perPage}";
            var envelopes = await GetAsync<List<MaxioProductEnvelope>>(path, cancellationToken, allowNotFound: false);
            if (envelopes is null || envelopes.Count == 0)
            {
                break;
            }

            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (envelopes.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetAsync<MaxioCustomerEnvelope>(path, cancellationToken, allowNotFound: true);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, string uniquenessToken, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var body = new MaxioCreateCustomerRequest
        {
            Customer = customer,
            UniquenessToken = uniquenessToken
        };
        var envelope = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>("customers.json", body, cancellationToken);
        if (envelope.Customer is null)
        {
            throw new BillingException(502, "Maxio created a customer but returned an empty payload.");
        }

        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken, allowNotFound: true);
        if (envelopes is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetAsync<MaxioSubscriptionEnvelope>(path, cancellationToken, allowNotFound: true);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, string uniquenessToken, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = subscription,
            UniquenessToken = uniquenessToken
        };
        var envelope = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>("subscriptions.json", body, cancellationToken);
        if (envelope.Subscription is null)
        {
            throw new BillingException(502, "Maxio created a subscription but returned an empty payload.");
        }

        return envelope.Subscription;
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken, bool allowNotFound)
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, relativePath),
            cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
        {
            return default;
        }

        EnsureSuccess(response, payload, relativePath);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(payload, MaxioJson.SerializerOptions);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string relativePath, TRequest body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var response = await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, relativePath);
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return request;
            },
            cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload, relativePath);
        var result = JsonSerializer.Deserialize<TResponse>(payload, MaxioJson.SerializerOptions);
        if (result is null)
        {
            throw new BillingException(502, $"Maxio returned an empty JSON body for POST {relativePath}.");
        }

        return result;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        EnsureAuthorizationHeader();
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            response?.Dispose();
            using var request = requestFactory();
            response = await _httpClient.SendAsync(request, cancellationToken);
            if (!ShouldRetry(response.StatusCode) || attempt == MaxRetries)
            {
                return response;
            }

            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfter && retryAfter > TimeSpan.Zero)
            {
                delay = retryAfter;
            }

            _logger.LogWarning(
                "Maxio returned {StatusCode} for {Method} {Uri}; retrying in {Delay} (attempt {Attempt}/{Max}).",
                (int)response.StatusCode,
                request.Method,
                request.RequestUri,
                delay,
                attempt + 1,
                MaxRetries);
            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    private void EnsureAuthorizationHeader()
    {
        if (_httpClient.DefaultRequestHeaders.Authorization is not null)
        {
            return;
        }

        var apiKey = _options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private void EnsureConfigured()
    {
        if (_options.Value.IsConfigured)
        {
            return;
        }

        throw new BillingException(503,
            "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle.");
    }

    private static bool ShouldRetry(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests
        || statusCode == HttpStatusCode.RequestTimeout
        || (int)statusCode >= 500;

    private static void EnsureSuccess(HttpResponseMessage response, string payload, string path)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = FormatMaxioError(payload, path, (int)response.StatusCode);
        var status = response.StatusCode switch
        {
            HttpStatusCode.Conflict => 409,
            HttpStatusCode.UnprocessableEntity => 400,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => 502,
            HttpStatusCode.TooManyRequests => 503,
            _ when (int)response.StatusCode >= 500 => 502,
            _ => 502
        };

        throw new BillingException(status, message);
    }

    internal static string FormatMaxioError(string payload, string path, int statusCode)
    {
        var detail = TryReadErrorDetail(payload);
        if (string.IsNullOrWhiteSpace(detail))
        {
            return $"Maxio request to {path} failed with HTTP {statusCode}.";
        }

        return $"Maxio request to {path} failed with HTTP {statusCode}: {detail}";
    }

    internal static string? TryReadErrorDetail(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return payload.Length > 500 ? payload[..500] : payload;
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in errors.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(item.GetString() ?? string.Empty);
                    }
                    else
                    {
                        parts.Add(item.ToString());
                    }
                }

                return string.Join("; ", parts);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var property in errors.EnumerateObject())
                {
                    parts.Add($"{property.Name}: {property.Value}");
                }

                return string.Join("; ", parts);
            }

            return errors.ToString();
        }
        catch (JsonException)
        {
            return payload.Length > 500 ? payload[..500] : payload;
        }
    }
}
