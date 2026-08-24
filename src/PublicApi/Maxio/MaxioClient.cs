using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// HttpClient-based implementation of <see cref="IMaxioClient"/>. Base address and Basic
/// authentication (API key as username, "x" as password, per the spec's securitySchemes)
/// are configured on the typed HttpClient at registration time.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default)
    {
        var wrappers = await SendAsync<List<MaxioProductFamilyResponse>>(
            new HttpRequestMessage(HttpMethod.Get, "product_families.json"), cancellationToken) ?? new();
        return wrappers.Where(w => w.ProductFamily != null).Select(w => w.ProductFamily!).ToList();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default)
    {
        var wrappers = await SendAsync<List<MaxioProductResponse>>(
            new HttpRequestMessage(HttpMethod.Get, $"product_families/{productFamilyId}/products.json"), cancellationToken) ?? new();
        return wrappers.Where(w => w.Product != null).Select(w => w.Product!).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        var wrapper = await SendAsync<MaxioCustomerResponse>(request, cancellationToken, notFoundReturnsNull: true);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        var body = new CreateMaxioCustomerRequest
        {
            Customer = new CreateMaxioCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };
        var wrapper = await SendAsync<MaxioCustomerResponse>(PostJson("customers.json", body), cancellationToken);
        return wrapper?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, "Create Customer response did not contain a customer object.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var wrappers = await SendAsync<List<MaxioSubscriptionResponse>>(
            new HttpRequestMessage(HttpMethod.Get, $"customers/{customerId}/subscriptions.json"), cancellationToken) ?? new();
        return wrappers.Where(w => w.Subscription != null).Select(w => w.Subscription!).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string? reference = null, CancellationToken cancellationToken = default)
    {
        var body = new CreateMaxioSubscriptionRequest
        {
            Subscription = new CreateMaxioSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                PaymentCollectionMethod = "remittance"
            }
        };
        var wrapper = await SendAsync<MaxioSubscriptionResponse>(PostJson("subscriptions.json", body), cancellationToken);
        return wrapper?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.Created, "Create Subscription response did not contain a subscription object.");
    }

    private static HttpRequestMessage PostJson<T>(string relativeUri, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, relativeUri);
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken, bool notFoundReturnsNull = false)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (notFoundReturnsNull && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, responseBody);
        }

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
    }
}
