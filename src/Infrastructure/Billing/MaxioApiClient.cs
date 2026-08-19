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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioApiClient> _logger;

    public MaxioApiClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        ConfigureClient();
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString(productFamilyHandle);
        return GetListAsync<MaxioProductEnvelope, MaxioProduct>(
            $"product_families/handle:{family}/products.json?per_page=200",
            envelope => envelope.Product,
            cancellationToken);
    }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var encoded = Uri.EscapeDataString(reference);
        return GetOptionalAsync<MaxioCustomerEnvelope, MaxioCustomer>(
            $"customers/lookup.json?reference={encoded}",
            envelope => envelope.Customer,
            cancellationToken);
    }

    public Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken)
    {
        return PostAsync<MaxioCreateCustomerEnvelope, MaxioCustomerEnvelope, MaxioCustomer>(
            "customers.json",
            new MaxioCreateCustomerEnvelope { Customer = customer },
            envelope => envelope.Customer,
            cancellationToken);
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var encoded = Uri.EscapeDataString(reference);
        return GetOptionalAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(
            $"subscriptions/lookup.json?reference={encoded}",
            envelope => envelope.Subscription,
            cancellationToken);
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        return GetListAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(
            $"customers/{customerId}/subscriptions.json",
            envelope => envelope.Subscription,
            cancellationToken);
    }

    public Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken)
    {
        return PostAsync<MaxioCreateSubscriptionEnvelope, MaxioSubscriptionEnvelope, MaxioSubscription>(
            "subscriptions.json",
            new MaxioCreateSubscriptionEnvelope { Subscription = subscription },
            envelope => envelope.Subscription,
            cancellationToken);
    }

    private void ConfigureClient()
    {
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = _options.ResolveBaseAddress();
        }

        if (_httpClient.DefaultRequestHeaders.Authorization is null &&
            !string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        if (_httpClient.DefaultRequestHeaders.Accept.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    private async Task<TItem> PostAsync<TRequest, TEnvelope, TItem>(
        string relativeUrl,
        TRequest body,
        Func<TEnvelope, TItem?> selector,
        CancellationToken cancellationToken)
        where TItem : class
    {
        var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, relativeUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            },
            cancellationToken);

        var payload = await ReadRequiredAsync<TEnvelope>(response, cancellationToken);
        return selector(payload) ?? throw new MaxioApiException(
            response.StatusCode,
            "Maxio returned an empty resource payload.");
    }

    private async Task<TItem?> GetOptionalAsync<TEnvelope, TItem>(
        string relativeUrl,
        Func<TEnvelope, TItem?> selector,
        CancellationToken cancellationToken)
        where TItem : class
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, relativeUrl),
            cancellationToken,
            allowNotFound: true);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadRequiredAsync<TEnvelope>(response, cancellationToken);
        return selector(payload);
    }

    private async Task<IReadOnlyList<TItem>> GetListAsync<TEnvelope, TItem>(
        string relativeUrl,
        Func<TEnvelope, TItem?> selector,
        CancellationToken cancellationToken)
        where TItem : class
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, relativeUrl),
            cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
        {
            return Array.Empty<TItem>();
        }

        var envelopes = JsonSerializer.Deserialize<List<TEnvelope>>(json, MaxioJson.SerializerOptions)
            ?? new List<TEnvelope>();

        var items = new List<TItem>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            var item = selector(envelope);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            response?.Dispose();
            using var request = requestFactory();
            response = await _httpClient.SendAsync(request, cancellationToken);

            if (IsSuccess(response.StatusCode) ||
                (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
            {
                return response;
            }

            if (attempt < maxAttempts && IsTransient(response.StatusCode))
            {
                _logger.LogInformation(
                    $"Maxio returned {(int)response.StatusCode} for {request.RequestUri}; retrying ({attempt}/{maxAttempts}).");
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = response.StatusCode;
            var reason = response.ReasonPhrase;
            response.Dispose();
            response = null;
            throw new MaxioApiException(
                statusCode,
                $"Maxio request failed with {(int)statusCode} {reason}: {TrimForLog(body)}");
        }

        throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio request failed after retries.");
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var payload = JsonSerializer.Deserialize<T>(json, MaxioJson.SerializerOptions);
        if (payload is null)
        {
            throw new MaxioApiException(response.StatusCode, "Maxio returned an empty JSON body.");
        }

        return payload;
    }

    private static bool IsSuccess(HttpStatusCode statusCode) =>
        (int)statusCode is >= 200 and <= 299;

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.RequestTimeout;

    private static string TrimForLog(string body) =>
        body.Length <= 500 ? body : body[..500];
}
