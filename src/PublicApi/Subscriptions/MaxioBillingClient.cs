using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioBillingClient : IMaxioBillingClient
{
    private const int PageSize = 200;
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, MaxioOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        _options.Validate();
        var products = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var path = $"product_families/{Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}")}/products.json?page={page}&per_page={PageSize}&include_archived=false";
            var pageProducts = await GetAsync<List<MaxioProductEnvelope>>(path, cancellationToken);
            products.AddRange(pageProducts.Select(item => item.Product));
            if (pageProducts.Count < PageSize)
                return products;
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetOptionalAsync<MaxioCustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        var request = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };
        var response = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return response.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetOptionalAsync<MaxioSubscriptionEnvelope>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken)
    {
        var request = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = subscriptionReference,
                payment_collection_method = "remittance"
            }
        };
        var response = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var response = await GetAsync<List<MaxioSubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return response.Select(item => item.Subscription).ToArray();
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        return await SendAsync<T>(HttpMethod.Get, path, null, cancellationToken);
    }

    private async Task<T?> GetOptionalAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await GetAsync<T>(path, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        _options.Validate();
        var baseUri = _options.GetBaseUri();
        using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var basicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicCredentials);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new MaxioApiException(response.StatusCode);

        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        return value ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string? message = null)
        : base(message ?? $"Maxio returned HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
