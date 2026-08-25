using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The spec allows the family path parameter to be "its handle prefixed with `handle:`".
        var products = await SendAsync<List<MaxioProductResponse>>(
            HttpMethod.Get,
            $"product_families/{Uri.EscapeDataString("handle:" + productFamilyHandle)}/products.json",
            body: null,
            cancellationToken);

        var result = new List<MaxioProduct>();
        foreach (var wrapper in products ?? new List<MaxioProductResponse>())
        {
            if (wrapper.Product is not null)
            {
                result.Add(wrapper.Product);
            }
        }
        return result;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var wrapper = await ReadAsAsync<MaxioCustomerResponse>(response, cancellationToken);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default)
    {
        var wrapper = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post, "customers.json", new MaxioCreateCustomerRequest(customer), cancellationToken);

        return wrapper?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, new[] { "Maxio returned an empty customer payload." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get, $"customers/{customerId}/subscriptions.json", body: null, cancellationToken);

        var result = new List<MaxioSubscription>();
        foreach (var wrapper in subscriptions ?? new List<MaxioSubscriptionResponse>())
        {
            if (wrapper.Subscription is not null)
            {
                result.Add(wrapper.Subscription);
            }
        }
        return result;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var wrapper = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post, "subscriptions.json", new MaxioCreateSubscriptionRequest(subscription), cancellationToken);

        return wrapper?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.OK, new[] { "Maxio returned an empty subscription payload." });
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadAsAsync<T>(response, cancellationToken);
    }

    private async Task<T?> ReadAsAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Maxio API returned {StatusCode} for {Method} {Path}: {Body}",
            (int)response.StatusCode, response.RequestMessage?.Method, response.RequestMessage?.RequestUri?.AbsolutePath, responseBody);

        IReadOnlyList<string> errors = new[] { response.ReasonPhrase ?? "Maxio API error" };
        try
        {
            var errorList = JsonSerializer.Deserialize<MaxioErrorListResponse>(responseBody, JsonOptions);
            if (errorList?.Errors is { Count: > 0 })
            {
                errors = errorList.Errors;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the reason phrase.
        }

        throw new MaxioApiException(response.StatusCode, errors, responseBody);
    }
}
