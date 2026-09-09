using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing (Billing API) REST API.
/// Authentication is HTTP Basic: API key as username, "x" as password.
/// Endpoint shapes follow the published Billing API reference.
/// </summary>
public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, CancellationToken cancellationToken = default);
}

public class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = _options.ResolveApiBaseAddress();
        }

        if (_httpClient.DefaultRequestHeaders.Authorization is null)
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        }

        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The product family can be addressed by its handle via the "handle:" prefix.
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
        var items = await ReadListAsync<MaxioProductResponse>(response, cancellationToken);

        return items
            .Select(i => i.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => p!)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await ToApiExceptionAsync(response, cancellationToken);
        }

        var payload = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return payload.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateCustomerBody { Customer = customer };
        var response = await SendAsync(() => NewJsonRequest(HttpMethod.Post, "customers.json", body), cancellationToken);
        var payload = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);

        return payload.Customer
            ?? throw new MaxioApiException("Maxio returned an empty customer payload.", (int)response.StatusCode, null);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
        var items = await ReadListAsync<MaxioSubscriptionResponse>(response, cancellationToken);

        return items
            .Select(i => i.Subscription)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateSubscriptionBody
        {
            Subscription = new MaxioSubscriptionInput
            {
                ProductHandle = productHandle,
                CustomerId = customerId
            }
        };

        var response = await SendAsync(() => NewJsonRequest(HttpMethod.Post, "subscriptions.json", body), cancellationToken);
        var payload = await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken);

        return payload.Subscription
            ?? throw new MaxioApiException("Maxio returned an empty subscription payload.", (int)response.StatusCode, null);
    }

    private static HttpRequestMessage NewJsonRequest(HttpMethod method, string path, object body)
    {
        return new HttpRequestMessage(method, path)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, SerializerOptions),
                Encoding.UTF8,
                "application/json")
        };
    }

    /// <summary>
    /// Sends a request, retrying idempotent (GET) calls a bounded number of times on
    /// transient failures (network errors, 5xx, 429).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.SendAsync(requestFactory(), cancellationToken);
                var retryable = attempt < maxAttempts
                    && (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                        || (int)response.StatusCode >= 500);

                if (!retryable)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw await ToApiExceptionAsync(response, cancellationToken);
                    }

                    return response;
                }
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                // fall through to retry
            }
            finally
            {
                if (response is not null && !response.IsSuccessStatusCode)
                {
                    response.Dispose();
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
        return payload ?? throw new MaxioApiException("Maxio returned an unexpected empty payload.", (int)response.StatusCode, null);
    }

    /// <summary>
    /// Deserializes a Maxio list endpoint. Maxio returns the wrapped shape
    /// {"items": [...]} when non-empty, but a bare JSON array ("[]") when empty;
    /// both shapes are accepted here.
    /// </summary>
    private static async Task<List<T>> ReadListAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var items = new List<T>();
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var item = element.Deserialize<T>(SerializerOptions);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Object
                 && document.RootElement.TryGetProperty("items", out var itemsElement)
                 && itemsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in itemsElement.EnumerateArray())
            {
                var item = element.Deserialize<T>(SerializerOptions);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    private static async Task<MaxioApiException> ToApiExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var excerpt = body is { Length: > 500 } ? body[..500] + "..." : body;
        return new MaxioApiException(
            $"Maxio Billing API request failed with status {(int)response.StatusCode} ({response.StatusCode}). Details: {excerpt}",
            (int)response.StatusCode,
            body);
    }
}
