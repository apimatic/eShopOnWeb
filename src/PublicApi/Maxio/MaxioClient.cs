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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed client for the operations defined by maxio-spec/openapi.yaml.
/// </summary>
public sealed class MaxioClient : IMaxioClient
{
    private const int PageSize = 200;
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        var settings = options.Value;
        _baseUri = settings.GetBaseUri();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio/1.0");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var response = await GetAsync<List<MaxioProductResponse>>(
                $"products.json?page={page}&per_page={PageSize}", "listProducts", false, cancellationToken);
            var pageProducts = response!.Select(item => item.Product).ToList();
            products.AddRange(pageProducts);
            if (pageProducts.Count < PageSize)
            {
                return products;
            }
        }
    }

    public async Task<MaxioProduct?> ReadProductByHandleAsync(string productHandle, CancellationToken cancellationToken)
    {
        var response = await GetAsync<MaxioProductResponse>(
            $"products/handle/{Uri.EscapeDataString(productHandle)}.json", "readProductByHandle", true, cancellationToken);
        return response?.Product;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetAsync<MaxioCustomerResponse>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", "readCustomerByReference", true, cancellationToken);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var response = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>(
            "customers.json", new MaxioCreateCustomerRequest(customer), "createCustomer", cancellationToken);
        return response.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json",
            "listCustomerSubscriptions", false, cancellationToken);
        return response!.Select(item => item.Subscription).ToList();
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetAsync<MaxioSubscriptionResponse>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", "findSubscription", true, cancellationToken);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        var response = await GetAsync<MaxioSubscriptionResponse>(
            $"subscriptions/{subscriptionId}.json", "readSubscription", true, cancellationToken);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        var response = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
            "subscriptions.json", new MaxioCreateSubscriptionRequest(subscription), "createSubscription", cancellationToken);
        return response.Subscription;
    }

    private async Task<T?> GetAsync<T>(string relativeUri, string operation, bool allowNotFound, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(relativeUri));
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                {
                    return default;
                }

                if (IsTransient(response.StatusCode) && attempt < maxAttempts)
                {
                    await DelayBeforeRetryAsync(response, attempt, cancellationToken);
                    continue;
                }

                return await ReadResponseAsync<T>(response, operation, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, operation, null, exception);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MaxioApiException(HttpStatusCode.GatewayTimeout, operation, null, exception);
            }
        }

        throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, operation);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string relativeUri,
        TRequest payload,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(relativeUri))
        {
            Content = JsonContent.Create(payload, options: _jsonOptions)
        };
        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return await ReadResponseAsync<TResponse>(response, operation, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, operation, null, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException(HttpStatusCode.GatewayTimeout, operation, null, exception);
        }
    }

    private async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, operation, ExtractError(errorBody));
        }

        try
        {
            var result = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
            return result ?? throw new MaxioApiException(response.StatusCode, operation, "Maxio returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, operation, "Maxio returned a response that does not match its OpenAPI schema.", exception);
        }
    }

    private Uri BuildUri(string relativeUri) =>
        new($"{_baseUri.ToString().TrimEnd('/')}/{relativeUri}", UriKind.Absolute);

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static async Task DelayBeforeRetryAsync(HttpResponseMessage response, int attempt, CancellationToken cancellationToken)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        var delay = retryAfter is { } value && value <= TimeSpan.FromSeconds(2)
            ? value
            : TimeSpan.FromMilliseconds(150 * attempt);
        await Task.Delay(delay, cancellationToken);
    }

    private static string? ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return null;
            }

            var text = errors.ValueKind == JsonValueKind.Array
                ? string.Join("; ", errors.EnumerateArray().Select(item => item.ToString()))
                : errors.ToString();
            return text.Length <= 500 ? text : text[..500];
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
