using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The product family can be addressed by its stable handle using the "handle:" prefix.
        var url = $"/product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        var wrappers = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, url, body: null, cancellationToken);
        var products = new List<MaxioProduct>();
        foreach (var wrapper in wrappers)
        {
            if (wrapper.Product != null)
            {
                products.Add(wrapper.Product);
            }
        }
        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var wrapper = await DeserializeAsync<MaxioCustomerResponse>(response, cancellationToken);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string email, string firstName, string lastName, string reference, CancellationToken cancellationToken = default)
    {
        var request = new CreateMaxioCustomerRequest
        {
            Customer = new CreateMaxioCustomer
            {
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Reference = reference
            }
        };

        var wrapper = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "/customers.json", request, cancellationToken);
        return wrapper?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, "Maxio returned an empty customer payload.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken cancellationToken = default)
    {
        var request = new CreateMaxioSubscriptionRequest
        {
            Subscription = new CreateMaxioSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                PaymentCollectionMethod = "remittance"
            }
        };

        var wrapper = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "/subscriptions.json", request, cancellationToken);
        return wrapper?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.Created, "Maxio returned an empty subscription payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var url = $"/customers/{customerId}/subscriptions.json";
        var wrappers = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get, url, body: null, cancellationToken);
        var subscriptions = new List<MaxioSubscription>();
        foreach (var wrapper in wrappers)
        {
            if (wrapper.Subscription != null)
            {
                subscriptions.Add(wrapper.Subscription);
            }
        }
        return subscriptions;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, _jsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken)
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response payload.");
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, _jsonOptions);
    }
}
