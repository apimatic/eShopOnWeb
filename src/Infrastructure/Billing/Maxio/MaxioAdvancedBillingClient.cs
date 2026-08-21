using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing. Every path and payload is taken from
/// <c>maxio-spec/openapi.yaml</c> (customers, products, subscriptions).
/// </summary>
public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private const int MaxPageSize = 200;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(HttpClient httpClient, ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        // GET /product_families/{product_family_id}/products.json
        // product_family_id: "Either the product family's id or its handle prefixed with `handle:`"
        var products = new List<MaxioProduct>();
        var page = 1;
        while (true)
        {
            var path =
                $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?page={page}&per_page={MaxPageSize}";
            var pageItems = await GetAsync<List<MaxioProductResponse>>(path, cancellationToken)
                            ?? new List<MaxioProductResponse>();
            products.AddRange(pageItems.Select(item => item.Product).Where(p => p is not null)!);
            if (pageItems.Count < MaxPageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioProduct?> ReadProductByHandleAsync(
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        // GET /products/handle/{api_handle}.json
        var path = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        var response = await GetOptionalAsync<MaxioProductResponse>(path, cancellationToken);
        return response?.Product;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        // GET /customers/lookup.json?reference=
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetOptionalAsync<MaxioCustomerResponse>(path, cancellationToken);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default)
    {
        // POST /customers.json
        var body = new MaxioCreateCustomerRequest { Customer = customer };
        var response = await SendJsonAsync<MaxioCustomerResponse>(
            HttpMethod.Post, "customers.json", body, cancellationToken, expected: new[] { HttpStatusCode.OK, HttpStatusCode.Created });
        if (response?.Customer is null || response.Customer.Id is null)
        {
            throw new BillingGatewayException("Maxio did not return a customer after create.");
        }

        return response.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        // GET /subscriptions/lookup.json?reference=
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetOptionalAsync<MaxioSubscriptionResponse>(path, cancellationToken);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        // POST /subscriptions.json
        var body = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        var response = await SendJsonAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post, "subscriptions.json", body, cancellationToken, expected: new[] { HttpStatusCode.OK, HttpStatusCode.Created });
        if (response?.Subscription is null || response.Subscription.Id is null)
        {
            throw new BillingGatewayException("Maxio did not return a subscription after create.");
        }

        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        // GET /customers/{customer_id}/subscriptions.json
        var path = $"customers/{customerId}/subscriptions.json";
        var response = await GetAsync<List<MaxioSubscriptionResponse>>(path, cancellationToken)
                       ?? new List<MaxioSubscriptionResponse>();
        return response
            .Select(item => item.Subscription)
            .Where(subscription => subscription is not null)
            .Cast<MaxioSubscription>()
            .ToList();
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        using var response = await SendWithRetryAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadBodyAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetOptionalAsync<T>(string relativePath, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        using var response = await SendWithRetryAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadBodyAsync<T>(response, cancellationToken);
    }

    private async Task<T?> SendJsonAsync<T>(
        HttpMethod method,
        string relativePath,
        object body,
        CancellationToken cancellationToken,
        HttpStatusCode[] expected)
    {
        var json = JsonSerializer.Serialize(body, MaxioJson.Options);
        using var request = new HttpRequestMessage(method, relativePath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (Array.IndexOf(expected, response.StatusCode) < 0)
        {
            await ThrowOnErrorAsync(response, cancellationToken);
        }

        return await ReadBodyAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // GET/lookup only: POST bodies are not retried here so a timeout cannot double-create.
        var delays = new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(2000) };
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt <= delays.Length; attempt++)
        {
            response?.Dispose();
            try
            {
                response = await _httpClient.SendAsync(CloneGet(request), cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt == delays.Length)
                {
                    return response;
                }

                _logger.LogWarning(
                    "Transient Maxio response {StatusCode} on {Path}; retrying ({Attempt}/{Total}).",
                    (int)response.StatusCode, request.RequestUri, attempt + 1, delays.Length);
            }
            catch (HttpRequestException ex) when (attempt < delays.Length)
            {
                _logger.LogWarning(ex, "Transient Maxio transport error on {Path}; retrying.", request.RequestUri);
            }
            catch (TaskCanceledException ex) when (attempt < delays.Length && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Maxio request timed out on {Path}; retrying.", request.RequestUri);
            }

            await Task.Delay(delays[Math.Min(attempt, delays.Length - 1)], cancellationToken);
        }

        return response ?? throw new BillingGatewayException("Maxio request failed without a response.");
    }

    private static HttpRequestMessage CloneGet(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return clone;
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests
        || statusCode == HttpStatusCode.BadGateway
        || statusCode == HttpStatusCode.ServiceUnavailable
        || statusCode == HttpStatusCode.GatewayTimeout
        || (int)statusCode == 408;

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        await ThrowOnErrorAsync(response, cancellationToken);
    }

    private async Task ThrowOnErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var messages = ParseErrorMessages(body);
        var summary = messages.Count > 0
            ? string.Join(" ", messages)
            : $"Maxio Advanced Billing returned {(int)response.StatusCode}.";

        _logger.LogWarning("Maxio request failed with {StatusCode}: {Summary}", (int)response.StatusCode, summary);
        throw new BillingGatewayException(summary, (int)response.StatusCode);
    }

    private static List<string> ParseErrorMessages(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new List<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorListResponse>(body, MaxioJson.Options);
            return parsed?.Errors ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (stream.CanSeek && stream.Length == 0)
        {
            return default;
        }

        return await JsonSerializer.DeserializeAsync<T>(stream, MaxioJson.Options, cancellationToken);
    }
}
