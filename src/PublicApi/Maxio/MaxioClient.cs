using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient : IMaxioClient
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The product_family_id path parameter accepts the family handle prefixed with "handle:".
        var items = await GetAsync<List<MaxioProductListItem>>(
            $"product_families/handle:{System.Uri.EscapeDataString(productFamilyHandle)}/products.json", cancellationToken);

        return items.Select(i => i.Product).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<MaxioCustomerEnvelope>(
            $"customers/lookup.json?reference={System.Uri.EscapeDataString(reference)}",
            cancellationToken,
            allowNotFound: true);

        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default)
    {
        var envelope = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>(
            "customers.json", new MaxioCreateCustomerRequest { Customer = customer }, cancellationToken);

        return envelope.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<MaxioSubscriptionEnvelope>(
            $"subscriptions/lookup.json?reference={System.Uri.EscapeDataString(reference)}",
            cancellationToken,
            allowNotFound: true);

        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes subscription, CancellationToken cancellationToken = default)
    {
        var envelope = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
            "subscriptions.json", new MaxioCreateSubscriptionRequest { Subscription = subscription }, cancellationToken);

        return envelope.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var items = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);

        return items.Select(i => i.Subscription).ToList();
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default!;
        }

        await EnsureSuccess(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken))!;
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(relativeUrl, body, JsonOptions, cancellationToken);

        await EnsureSuccess(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken))!;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, body);
        }
    }
}
