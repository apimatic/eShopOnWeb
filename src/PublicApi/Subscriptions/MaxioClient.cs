using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?include_archived=false";
        var responses = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, null, cancellationToken);
        var products = new List<MaxioProduct>(responses.Count);
        foreach (var response in responses)
            products.Add(response.Product);
        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Get, path, null, cancellationToken);
            return response.Customer;
        }
        catch (MaxioApiException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                Reference = reference,
                FirstName = firstName,
                LastName = lastName,
                Email = email
            }
        };

        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return response.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var responses = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken);
        var subscriptions = new List<MaxioSubscription>(responses.Count);
        foreach (var response in responses)
            subscriptions.Add(response.Subscription);
        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = "remittance"
            }
        };

        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return response.Subscription;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? requestBody, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (requestBody is not null)
            request.Content = JsonContent.Create(requestBody, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new MaxioApiException((int)response.StatusCode);

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<T>(content, JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException((int)response.StatusCode);
    }

    public static void ConfigureHttpClient(HttpClient client, MaxioOptions options)
    {
        options.ValidateForApiUse();
        client.BaseAddress = options.GetBaseAddress();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.Timeout = TimeSpan.FromSeconds(30);
    }
}
