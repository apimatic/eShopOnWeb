using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioApiClient"/>, built strictly
/// against maxio-spec/openapi.yaml (Maxio Advanced Billing, OpenAPI 3.1):
/// - auth: HTTP basic, username = API key, password = "x" (spec securitySchemes.BasicAuth)
/// - JSON only, per the spec's primary content type
/// - error bodies per ./components/schemas/errors/*
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            throw new InvalidOperationException(
                $"Maxio API key is not configured. Set the '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)}' configuration value (e.g. via user-secrets from the MAXIO_API_KEY environment variable).");
        }
        if (string.IsNullOrWhiteSpace(opts.ResolveBaseUrl()))
        {
            throw new InvalidOperationException(
                $"Maxio base address is not configured. Set '{MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)}' (e.g. via user-secrets from the MAXIO_SITE_SUBDOMAIN environment variable) or '{MaxioOptions.SectionName}:{nameof(MaxioOptions.BaseUrl)}'.");
        }

        _httpClient.BaseAddress = new Uri(opts.ResolveBaseUrl());
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opts.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var products = await GetAsync<List<MaxioProductResponse>>(
            $"/product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json", cancellationToken);
        return products.Select(p => p.Product).ToList();
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<MaxioCustomerResponse>(
            $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken, notFoundReturnsNull: true);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post, "/customers.json", new MaxioCreateCustomerRequest { Customer = customer }, cancellationToken);
        return response.Customer;
    }

    public async Task<MaxioSubscription?> GetSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<MaxioSubscriptionResponse>(
            $"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken, notFoundReturnsNull: true);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post, "/subscriptions.json", new MaxioCreateSubscriptionRequest { Subscription = subscription }, cancellationToken);
        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"/customers/{customerId}/subscriptions.json", cancellationToken);
        return subscriptions.Select(s => s.Subscription).ToList();
    }

    public async Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<MaxioSubscriptionResponse>($"/subscriptions/{subscriptionId}.json", cancellationToken);
        return response.Subscription;
    }

    private async Task<T?> GetAsync<T>(string requestUri, CancellationToken cancellationToken, bool notFoundReturnsNull = false)
        where T : class
    {
        var response = await SendWithRetriesAsync(HttpMethod.Get, requestUri, requestBody: null, cancellationToken, notFoundReturnsNull);
        if (response.IsNullResult)
        {
            return null;
        }
        return JsonSerializer.Deserialize<T>(response.Body, JsonOptions)
               ?? throw new BillingException($"Maxio returned an empty response for {requestUri}", (int)response.StatusCode);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string requestUri, object requestBody, CancellationToken cancellationToken)
        where T : class
    {
        var response = await SendWithRetriesAsync(method, requestUri, requestBody, cancellationToken);
        return JsonSerializer.Deserialize<T>(response.Body, JsonOptions)
               ?? throw new BillingException($"Maxio returned an empty response for {requestUri}", (int)response.StatusCode);
    }

    private readonly record struct MaxioResponse(bool IsNullResult, HttpStatusCode StatusCode, string Body);

    private async Task<MaxioResponse> SendWithRetriesAsync(
        HttpMethod method, string requestUri, object? requestBody, CancellationToken cancellationToken, bool notFoundReturnsNull = false)
    {
        HttpRequestException? lastNetworkError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, requestUri);
                if (requestBody is not null)
                {
                    request.Content = JsonContent.Create(requestBody, options: JsonOptions);
                }

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return new MaxioResponse(false, response.StatusCode, body);
                }

                if (response.StatusCode == HttpStatusCode.NotFound && notFoundReturnsNull)
                {
                    return new MaxioResponse(true, response.StatusCode, body);
                }

                if (IsTransient(response.StatusCode) && attempt < MaxAttempts)
                {
                    _logger.LogWarning(
                        "Transient Maxio response {StatusCode} for {Method} {Uri} (attempt {Attempt}/{MaxAttempts}); retrying.",
                        (int)response.StatusCode, method, requestUri, attempt, MaxAttempts);
                    await Task.Delay(GetRetryDelay(attempt, response.Headers.RetryAfter), cancellationToken);
                    continue;
                }

                throw await CreateApiExceptionAsync(response.StatusCode, body, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                lastNetworkError = ex;
                _logger.LogWarning(ex, "Network error calling Maxio {Method} {Uri} (attempt {Attempt}/{MaxAttempts}); retrying.",
                    method, requestUri, attempt, MaxAttempts);
                await Task.Delay(GetRetryDelay(attempt, null), cancellationToken);
            }
        }

        throw new BillingException(
            $"Maxio did not respond after {MaxAttempts} attempts: {lastNetworkError?.Message}",
            errors: new[] { lastNetworkError?.Message ?? "network error" });
    }

    private static TimeSpan GetRetryDelay(int attempt, System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero && delta < TimeSpan.FromSeconds(30))
        {
            return delta;
        }
        return TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    private static async Task<BillingException> CreateApiExceptionAsync(HttpStatusCode statusCode, string body, CancellationToken cancellationToken)
    {
        var errors = ParseErrors(body);
        var detail = errors.Length > 0 ? string.Join("; ", errors) : body;
        return new BillingException(
            $"Maxio API request failed with status {(int)statusCode} ({statusCode}): {detail}",
            (int)statusCode,
            errors);
    }

    /// <summary>
    /// Error payloads per the spec's error schemas: either
    /// { "errors": [ ... ] } (Error-List-Response), { "errors": { field: [msgs] } }
    /// (Error-Array-Map-Response), or a bare array of strings.
    /// </summary>
    private static string[] ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                return root.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.ToString())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.Array => errors.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.ToString())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToArray(),
                    JsonValueKind.Object => errors.EnumerateObject()
                        .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                            ? p.Value.EnumerateArray().Select(e => $"{p.Name}: {e.GetString()}")
                            : new[] { $"{p.Name}: {p.Value.ToString()}" })
                        .ToArray(),
                    JsonValueKind.String => new[] { errors.GetString() ?? string.Empty },
                    _ => Array.Empty<string>()
                };
            }

            return new[] { body };
        }
        catch (JsonException)
        {
            return new[] { body };
        }
    }
}
