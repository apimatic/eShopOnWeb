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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient : IMaxioClient
{
    private const int MaxPageSize = 200;
    private const int MaxRetryAttempts = 3;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var maxioOptions = options.Value;
        maxioOptions.Validate();

        _httpClient.BaseAddress = new Uri(maxioOptions.ResolveBaseUrl() + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{maxioOptions.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default)
    {
        var families = new List<MaxioProductFamily>();
        await ForEachPageAsync(async (page, ct) =>
            await GetArrayAsync<MaxioProductFamilyEnvelope>($"product_families.json?page={page}&per_page={MaxPageSize}", ct),
            families, e => e.ProductFamily!, cancellationToken);
        return families;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        await ForEachPageAsync(async (page, ct) =>
            await GetArrayAsync<MaxioProductEnvelope>($"products.json?page={page}&per_page={MaxPageSize}", ct),
            products, e => e.Product!, cancellationToken);
        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            retryOnServerError: true, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope!.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest customer, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "customers.json")
        {
            Content = JsonContent.Create(new { customer }, options: SerializerOptions)
        }, retryOnServerError: false, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope!.Customer!;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest subscription, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
        {
            Content = JsonContent.Create(new { subscription }, options: SerializerOptions)
        }, retryOnServerError: false, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(SerializerOptions, cancellationToken);
        return envelope!.Subscription!;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = new List<MaxioSubscription>();
        await ForEachPageAsync(async (page, ct) =>
            await GetArrayAsync<MaxioSubscriptionEnvelope>($"customers/{customerId}/subscriptions.json?page={page}&per_page={MaxPageSize}", ct),
            subscriptions, e => e.Subscription!, cancellationToken);
        return subscriptions;
    }

    private async Task ForEachPageAsync<TEnvelope, TResult>(
        Func<int, CancellationToken, Task<IReadOnlyList<TEnvelope>>> fetchPage,
        List<TResult> results,
        Func<TEnvelope, TResult> unwrap,
        CancellationToken cancellationToken)
    {
        for (var page = 1; ; page++)
        {
            var envelopes = await fetchPage(page, cancellationToken);
            results.AddRange(envelopes.Select(unwrap));

            if (envelopes.Count < MaxPageSize)
            {
                break;
            }
        }
    }

    private async Task<IReadOnlyList<T>> GetArrayAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, requestUri),
            retryOnServerError: true, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var items = await response.Content.ReadFromJsonAsync<T[]>(SerializerOptions, cancellationToken);
        return items ?? Array.Empty<T>();
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> createRequest,
        bool retryOnServerError,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            response = await _httpClient.SendAsync(createRequest(), cancellationToken);
            if (IsRetryable(response.StatusCode, retryOnServerError) && attempt < MaxRetryAttempts)
            {
                _logger.LogWarning(
                    "Maxio API returned {StatusCode}; retrying attempt {Attempt}/{MaxRetryAttempts}.",
                    (int)response.StatusCode, attempt, MaxRetryAttempts);
                response.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                continue;
            }
            break;
        }

        return response!;
    }

    private static bool IsRetryable(HttpStatusCode statusCode, bool retryOnServerError)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (!retryOnServerError)
        {
            return false;
        }

        var code = (int)statusCode;
        return code is 502 or 503 or 504;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);

        _logger.LogError(
            "Maxio API request to {Uri} failed with status {StatusCode}: {Errors}",
            response.RequestMessage?.RequestUri, (int)response.StatusCode, string.Join("; ", errors));

        throw new MaxioApiException(
            (int)response.StatusCode,
            errors,
            $"Maxio API request failed with status {(int)response.StatusCode} ({response.StatusCode}).");
    }

    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return new[] { body };
            }

            return errorsElement.ValueKind switch
            {
                JsonValueKind.Array => errorsElement.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText())
                    .ToList(),
                JsonValueKind.Object => errorsElement.EnumerateObject()
                    .SelectMany(property => property.Value.ValueKind == JsonValueKind.Array
                        ? property.Value.EnumerateArray().Select(e => $"{property.Name}: {e.GetString()}")
                        : new[] { $"{property.Name}: {property.Value.GetRawText()}" })
                    .ToList(),
                JsonValueKind.String => new[] { errorsElement.GetString()! },
                _ => new[] { errorsElement.GetRawText() }
            };
        }
        catch (JsonException)
        {
            return new[] { body };
        }
    }

    private sealed class MaxioProductFamilyEnvelope
    {
        public MaxioProductFamily? ProductFamily { get; set; }
    }

    private sealed class MaxioProductEnvelope
    {
        public MaxioProduct? Product { get; set; }
    }

    private sealed class MaxioCustomerEnvelope
    {
        public MaxioCustomer? Customer { get; set; }
    }

    private sealed class MaxioSubscriptionEnvelope
    {
        public MaxioSubscription? Subscription { get; set; }
    }
}
