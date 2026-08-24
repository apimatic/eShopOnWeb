using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// <see cref="IMaxioClient"/> implementation. Registered as a typed HttpClient;
/// the base address and Basic authentication header (API key as username, "X" as
/// password, per the Billing API authentication docs) are configured at registration.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The family can be addressed by its handle using the "handle:" prefix.
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(
            HttpMethod.Get, $"product_families/handle:{productFamilyHandle}/products.json", cancellationToken: cancellationToken);

        return envelopes?.Select(e => e.Product).Where(p => p is not null).Cast<MaxioProduct>().ToList()
            ?? new List<MaxioProduct>();
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioProductEnvelope>(
            HttpMethod.Get, $"products/handle/{productHandle}.json", allowNotFound: true, cancellationToken: cancellationToken);

        return envelope?.Product;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            allowNotFound: true, cancellationToken: cancellationToken);

        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest customer, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post, "customers.json",
            body: new MaxioCreateCustomerEnvelope { Customer = customer }, cancellationToken: cancellationToken);

        return envelope?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, "Maxio returned an empty customer payload.");
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get, $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            allowNotFound: true, cancellationToken: cancellationToken);

        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest subscription, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json",
            body: new MaxioCreateSubscriptionEnvelope { Subscription = subscription }, cancellationToken: cancellationToken);

        return envelope?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.OK, "Maxio returned an empty subscription payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get, $"customers/{customerId}/subscriptions.json", cancellationToken: cancellationToken);

        return envelopes?.Select(e => e.Subscription).Where(s => s is not null).Cast<MaxioSubscription>().ToList()
            ?? new List<MaxioSubscription>();
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativeUri, object? body = null,
        bool allowNotFound = false, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, relativeUri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, errorBody);
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }
}
