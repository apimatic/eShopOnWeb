using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private const int MaxAttempts = 3;
    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var encodedHandle = Uri.EscapeDataString(productFamilyHandle);
        var path = $"product_families/handle:{encodedHandle}/products.json?per_page=200";
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, body: null, notFoundReturnsDefault: false, cancellationToken);
        return UnwrapProducts(envelopes);
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        var encodedHandle = Uri.EscapeDataString(productHandle);
        var envelope = await SendAsync<MaxioProductEnvelope>(
            HttpMethod.Get,
            $"products/handle/{encodedHandle}.json",
            body: null,
            notFoundReturnsDefault: true,
            cancellationToken);
        return envelope?.Product;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, body: null, notFoundReturnsDefault: true, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerPayload customer, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            notFoundReturnsDefault: false,
            cancellationToken);

        return envelope?.Customer ?? throw new MaxioApiException(502, "Maxio returned an empty customer payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            body: null,
            notFoundReturnsDefault: false,
            cancellationToken);
        return UnwrapSubscriptions(envelopes);
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, body: null, notFoundReturnsDefault: true, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscriptionPayload subscription,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            notFoundReturnsDefault: false,
            cancellationToken);

        return envelope?.Subscription ?? throw new MaxioApiException(502, "Maxio returned an empty subscription payload.");
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        bool notFoundReturnsDefault,
        CancellationToken cancellationToken)
    {
        EnsureReady();

        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: MaxioJson.Options);
            }

            response = await _httpClient.SendAsync(request, cancellationToken);
            var status = (int)response.StatusCode;
            if (status is 429 or >= 500 && attempt < MaxAttempts)
            {
                _logger.LogWarning("Maxio {Method} {Path} returned {StatusCode}; retrying ({Attempt}/{Max}).",
                    method, path, status, attempt, MaxAttempts);
                var delayMs = 200 * (int)Math.Pow(2, attempt - 1);
                await Task.Delay(delayMs, cancellationToken);
                response.Dispose();
                continue;
            }

            break;
        }

        if (response is null)
        {
            throw new MaxioApiException(502, "No response received from Maxio.");
        }

        using (response)
        {
            if (notFoundReturnsDefault && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new MaxioApiException((int)response.StatusCode, FormatError((int)response.StatusCode, payload));
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return default;
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(MaxioJson.Options, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new MaxioApiException(502, $"Maxio returned an unexpected payload: {ex.Message}");
            }
        }
    }

    private void EnsureReady()
    {
        _options.Value.EnsureConfiguredForRequests();

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_options.Value.ResolveBaseUrl());
        }
    }

    private static string FormatError(int statusCode, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return $"Maxio request failed with HTTP {statusCode}.";
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(payload, MaxioJson.Options);
            if (parsed?.Errors is { Count: > 0 })
            {
                return $"Maxio request failed with HTTP {statusCode}: {string.Join("; ", parsed.Errors)}";
            }
        }
        catch (JsonException)
        {
            // Fall through to a truncated raw payload.
        }

        var trimmed = payload.Trim();
        if (trimmed.Length > 500)
        {
            trimmed = trimmed[..500];
        }

        return $"Maxio request failed with HTTP {statusCode}: {trimmed}";
    }

    private static IReadOnlyList<MaxioProduct> UnwrapProducts(List<MaxioProductEnvelope>? envelopes)
    {
        if (envelopes is null || envelopes.Count == 0)
        {
            return Array.Empty<MaxioProduct>();
        }

        var products = new List<MaxioProduct>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    private static IReadOnlyList<MaxioSubscription> UnwrapSubscriptions(List<MaxioSubscriptionEnvelope>? envelopes)
    {
        if (envelopes is null || envelopes.Count == 0)
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
}
