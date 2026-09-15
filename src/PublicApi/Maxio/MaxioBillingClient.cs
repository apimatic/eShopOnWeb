using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioBillingClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var response = await SendAsync<List<MaxioProductEnvelope>>(
            HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200",
            body: null,
            cancellationToken);

        return response is null
            ? Array.Empty<MaxioProduct>()
            : response.Where(item => item.Product.ArchivedAt is null).Select(item => item.Product).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            body: null,
            cancellationToken,
            allowNotFound: true);

        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer request, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", new CreateMaxioCustomerRequest { Customer = request }, cancellationToken)
            ?? throw new InvalidOperationException("Maxio returned an empty customer response.");
        return response.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            body: null,
            cancellationToken,
            allowNotFound: true);

        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscription request, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", new CreateMaxioSubscriptionRequest { Subscription = request }, cancellationToken)
            ?? throw new InvalidOperationException("Maxio returned an empty subscription response.");
        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            body: null,
            cancellationToken);

        return response is null
            ? Array.Empty<MaxioSubscription>()
            : response.Select(item => item.Subscription).ToList();
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, responseBody);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Maxio returned an invalid JSON response.");
    }
}
