using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API. Endpoints, parameters and
/// payloads follow maxio-spec/openapi.yaml. Auth is HTTP Basic (API key as username,
/// "x" as password) per the spec's BasicAuth security scheme; the scheme is applied
/// when the typed client is registered.
/// </summary>
public class MaxioHttpClient
{
    private readonly HttpClient _httpClient;

    public MaxioHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// GET /product_families/{product_family_id}/products.json (listProductsForProductFamily).
    /// The path parameter accepts a family handle prefixed with "handle:".
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{productFamilyHandle}/products.json";
        var products = await SendAsync<List<MaxioProductResponse>>(
            HttpMethod.Get, path, body: null, cancellationToken);
        var result = new List<MaxioProduct>();
        foreach (var wrapper in products ?? new List<MaxioProductResponse>())
        {
            if (wrapper.Product != null)
            {
                result.Add(wrapper.Product);
            }
        }
        return result;
    }

    /// <summary>
    /// GET /customers/lookup.json?reference=... (readCustomerByReference).
    /// Returns null when no customer exists for the reference (404).
    /// </summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={WebUtility.UrlEncode(reference)}";
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Get, path, body: null, cancellationToken, allowNotFound: true);
        return response?.Customer;
    }

    /// <summary>
    /// POST /customers.json (createCustomer).
    /// </summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post, "customers.json", new MaxioCreateCustomerRequest { Customer = customer },
            cancellationToken);
        return response!.Customer!;
    }

    /// <summary>
    /// POST /subscriptions.json (createSubscription).
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post, "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription }, cancellationToken);
        return response!.Subscription!;
    }

    /// <summary>
    /// GET /customers/{customer_id}/subscriptions.json (listCustomerSubscriptions).
    /// </summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var subscriptions = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get, path, body: null, cancellationToken);
        var result = new List<MaxioSubscription>();
        foreach (var wrapper in subscriptions ?? new List<MaxioSubscriptionResponse>())
        {
            if (wrapper.Subscription != null)
            {
                result.Add(wrapper.Subscription);
            }
        }
        return result;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null)
        {
            request.Content = JsonContent.Create(body);
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

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }
}
