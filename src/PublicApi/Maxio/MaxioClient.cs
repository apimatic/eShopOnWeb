using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioClient"/> against the Maxio
/// Advanced Billing (Chargify) REST API. Authenticates with HTTP Basic
/// (API key as username, "x" as password) and tolerates Maxio's
/// concurrency-based throttling with bounded retries.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private const int MaxRetries = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var maxioOptions = options.Value;
        _httpClient.BaseAddress = maxioOptions.EffectiveBaseUrl();
        _httpClient.Timeout = TimeSpan.FromSeconds(100);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxioOptions.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<MaxioProductFamily?> GetProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"product_families/handle:{EscapeHandle(handle)}.json", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioProductFamilyEnvelope>(JsonOptions, cancellationToken);
        return envelope?.ProductFamily;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var response = await SendAsync(HttpMethod.Get,
                $"product_families/handle:{EscapeHandle(familyHandle)}/products.json?page={page}&per_page={perPage}",
                cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var batch = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(JsonOptions, cancellationToken)
                ?? new List<MaxioProductEnvelope>();
            products.AddRange(batch.Where(p => p.Product != null).Select(p => p.Product!));

            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products.Where(p => p.ArchivedAt is null).ToList();
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<MaxioSubscription>();
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(JsonOptions, cancellationToken)
            ?? new List<MaxioSubscriptionEnvelope>();
        return envelopes.Where(s => s.Subscription != null).Select(s => s.Subscription!).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty subscription response.", Array.Empty<string>());
        return envelope.Subscription
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty subscription response.", Array.Empty<string>());
    }

    /// <summary>
    /// Handles are interpolated into Maxio's "handle:xxx" path syntax, where the
    /// colon must stay literal; instead of URL-encoding, validate the handle
    /// against the safe character set Maxio allows.
    /// </summary>
    private static string EscapeHandle(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle) || !Regex.IsMatch(handle, "^[A-Za-z0-9._-]+$"))
        {
            throw new ArgumentException($"Invalid Maxio handle '{handle}'.", nameof(handle));
        }

        return handle;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string requestUri, CancellationToken cancellationToken)
    {
        return await SendAsync(method, requestUri, content: null, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string requestUri, object? content, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, requestUri);
            if (content != null)
            {
                request.Content = JsonContent.Create(content);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (IsTransientFailure(response) && attempt <= MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    "Maxio API returned {StatusCode} for {Method} {Uri} (attempt {Attempt}); retrying in {Delay}.",
                    (int)response.StatusCode, method, requestUri, attempt, delay);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            return response;
        }
    }

    private static bool IsTransientFailure(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        // Maxio throttles on concurrent calls (429) and may emit transient 5xx.
        return statusCode == 429 || statusCode == 502 || statusCode == 503 || statusCode == 504;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);
        var message = $"Maxio API request failed with status {(int)response.StatusCode} ({response.StatusCode}).";
        if (errors.Count > 0)
        {
            message += $" Errors: {string.Join("; ", errors)}.";
        }

        _logger.LogError("Maxio API failure: {Message}. Body: {Body}", message, body);
        throw new MaxioApiException((int)response.StatusCode, message, errors);
    }

    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            var error = JsonSerializer.Deserialize<MaxioApiError>(body, JsonOptions);
            return error?.Errors ?? new List<string> { body };
        }
        catch (JsonException)
        {
            return new List<string> { body };
        }
    }

    private sealed class MaxioProductFamilyEnvelope
    {
        [System.Text.Json.Serialization.JsonPropertyName("product_family")]
        public MaxioProductFamily? ProductFamily { get; set; }
    }

    private sealed class MaxioProductEnvelope
    {
        [System.Text.Json.Serialization.JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private sealed class MaxioCustomerEnvelope
    {
        [System.Text.Json.Serialization.JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private sealed class MaxioSubscriptionEnvelope
    {
        [System.Text.Json.Serialization.JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }
    }
}
