using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API. All routes, payloads and error shapes
/// follow the published Billing API reference. Authentication is HTTP Basic (api key + "x").
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private const string BasicAuthPassword = "x";
    private const string JsonMediaType = "application/json";
    private const int MaxPageSize = 200;
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, MaxioOptions options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Looks a customer up by its reference value (GET /customers/lookup.json). Returns
    /// <c>null</c> when no customer carries the reference.
    /// </summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var uri = BuildUri($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeEnvelopeAsync<MaxioCustomerEnvelope>(response, cancellationToken) is { Customer: { } customer }
            ? customer
            : null;
    }

    /// <summary>Creates a customer (POST /customers.json).</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(CustomerAttributes customer, CancellationToken cancellationToken)
    {
        var uri = BuildUri("customers.json");
        using var response = await SendAsync(
            () => CreateJsonRequest(HttpMethod.Post, uri, new Dictionary<string, object?> { ["customer"] = customer }),
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        return (await DeserializeEnvelopeAsync<MaxioCustomerEnvelope>(response, cancellationToken))!.Customer!;
    }

    /// <summary>
    /// Reads a product by API handle (GET /products/handle/{handle}.json). Returns <c>null</c>
    /// when the handle does not exist.
    /// </summary>
    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        var uri = BuildUri($"products/handle/{Uri.EscapeDataString(handle)}.json");
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeEnvelopeAsync<MaxioProductEnvelope>(response, cancellationToken) is { Product: { } product }
            ? product
            : null;
    }

    /// <summary>Lists every (non-archived) product on the site (GET /products.json, paged).</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        int page = 1;
        while (true)
        {
            var uri = BuildUri($"products.json?page={page}&per_page={MaxPageSize}");
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var pageItems = JsonSerializer.Deserialize<List<MaxioProductEnvelope>>(body, JsonOptions)
                ?? new List<MaxioProductEnvelope>();

            var mapped = pageItems.Where(x => x.Product is not null).Select(x => x.Product!).ToList();
            if (mapped.Count == 0)
            {
                break;
            }

            products.AddRange(mapped);

            if (pageItems.Count < MaxPageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    /// <summary>
    /// Creates a subscription (POST /subscriptions.json). Pass a <paramref name="uniquenessToken"/>
    /// so the Billing API duplicate-prevention guard rejects retries of the same logical request.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(SubscriptionAttributes subscription, string? uniquenessToken, CancellationToken cancellationToken)
    {
        var uri = BuildUri("subscriptions.json");
        var payload = new Dictionary<string, object?>
        {
            ["subscription"] = subscription
        };
        if (!string.IsNullOrWhiteSpace(uniquenessToken))
        {
            payload["uniqueness_token"] = uniquenessToken;
        }

        using var response = await SendAsync(() => CreateJsonRequest(HttpMethod.Post, uri, payload), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await DeserializeEnvelopeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken))!.Subscription!;
    }

    /// <summary>Lists all subscriptions that belong to a customer (GET /customers/{id}/subscriptions.json).</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var uri = BuildUri($"customers/{customerId}/subscriptions.json");
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var items = JsonSerializer.Deserialize<List<MaxioSubscriptionEnvelope>>(body, JsonOptions)
            ?? new List<MaxioSubscriptionEnvelope>();
        return items.Where(x => x.Subscription is not null).Select(x => x.Subscription!).ToList();
    }

    private Uri BuildUri(string relativePathAndQuery)
    {
        _options.EnsureConfigured();
        return new Uri(_options.GetApiBaseAddress(), relativePathAndQuery);
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, Uri uri, object? payload)
    {
        var request = new HttpRequestMessage(method, uri);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8, JsonMediaType);
        return request;
    }

    private static async Task<T?> DeserializeEnvelopeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:{BasicAuthPassword}"));
        int attempt = 1;
        while (true)
        {
            using var request = requestFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                bool transient = (int)response.StatusCode >= 500 || (int)response.StatusCode == 429;
                if (transient && attempt < MaxAttempts)
                {
                    response.Dispose();
                    await BackOffAsync(attempt, cancellationToken);
                    attempt++;
                    continue;
                }

                return response;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= MaxAttempts)
                {
                    _logger.LogWarning(ex, "Maxio request to {Uri} failed after {Attempts} attempts.", request.RequestUri, attempt);
                    throw;
                }

                await BackOffAsync(attempt, cancellationToken);
                attempt++;
            }
        }
    }

    private static Task BackOffAsync(int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
        return Task.Delay(delay, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException((int)response.StatusCode, ExtractErrorMessage(body));
    }

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "The Maxio Billing API returned an error with no additional detail.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors))
            {
                var parts = new List<string>();
                CollectErrorMessages(errors, parts);
                if (parts.Count > 0)
                {
                    return string.Join(" ", parts);
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to raw body.
        }

        return body.Length <= 500 ? body : body.Substring(0, 500);
    }

    private static void CollectErrorMessages(JsonElement errors, List<string> parts)
    {
        switch (errors.ValueKind)
        {
            case JsonValueKind.String:
                parts.Add(errors.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in errors.EnumerateArray())
                {
                    CollectErrorMessages(item, parts);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in errors.EnumerateObject())
                {
                    CollectErrorMessages(property.Value, parts);
                }
                break;
        }
    }
}
