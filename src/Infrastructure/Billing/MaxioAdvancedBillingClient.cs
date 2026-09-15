using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;

    public MaxioAdvancedBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<IReadOnlyList<ProductDto>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        int page,
        int perPage,
        CancellationToken cancellationToken)
    {
        // Spec: product_family_id is "Either the product family's id or its handle prefixed with `handle:`"
        var familySegment = "handle:" + Uri.EscapeDataString(productFamilyIdOrHandle);
        var path = $"product_families/{familySegment}/products.json?page={page}&per_page={perPage}";
        var responses = await SendAsync<List<ProductResponse>>(HttpMethod.Get, path, null, cancellationToken);
        var products = new List<ProductDto>();
        if (responses == null)
        {
            return products;
        }

        foreach (var item in responses)
        {
            if (item.Product != null)
            {
                products.Add(item.Product);
            }
        }

        return products;
    }

    public async Task<CustomerDto?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<CustomerResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return response?.Customer;
    }

    public async Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var response = await SendAsync<CustomerResponse>(HttpMethod.Post, "customers.json", request, cancellationToken);
        if (response?.Customer == null)
        {
            throw new MaxioApiException(HttpStatusCode.OK, "Create customer returned an empty customer body.");
        }

        return response.Customer;
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var responses = await SendAsync<List<SubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken);
        var subscriptions = new List<SubscriptionDto>();
        if (responses == null)
        {
            return subscriptions;
        }

        foreach (var item in responses)
        {
            if (item.Subscription != null)
            {
                subscriptions.Add(item.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<SubscriptionDto?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return response?.Subscription;
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        if (response?.Subscription == null)
        {
            throw new MaxioApiException(HttpStatusCode.Created, "Create subscription returned an empty subscription body.");
        }

        return response.Subscription;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePathAndQuery,
        object? body,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        var options = _options.Value;
        var baseUrl = options.ResolveBaseUrl();
        var requestUri = new Uri($"{baseUrl}/{relativePathAndQuery}");

        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = CreateBasicAuth(options.ApiKey);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            await MaxioApiException.ThrowFromResponse(response, cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (stream == null || stream.CanSeek && stream.Length == 0)
        {
            return default;
        }

        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static AuthenticationHeaderValue CreateBasicAuth(string apiKey)
    {
        // Spec securitySchemes.BasicAuth: username is the API key, password is `x`.
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
        return new AuthenticationHeaderValue("Basic", token);
    }
}
