using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var response = await SendAsync<List<MaxioProductListItem>>(HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200&include_archived=false",
            null, cancellationToken);
        return response?.ConvertAll(item => item.Product) ?? new List<MaxioProduct>();
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioProductResponse>(HttpMethod.Get,
            $"products/handle/{Uri.EscapeDataString(productHandle)}.json", null, cancellationToken, true);
        return response?.Product;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken, true);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json",
            new MaxioCustomerRequest { Customer = customer }, cancellationToken);
        return response!.Customer;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSiteResponse>(HttpMethod.Get, "site.json", null, cancellationToken);
        return response!.Site;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken, true);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes subscription, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json",
            new MaxioSubscriptionRequest { Subscription = subscription }, cancellationToken);
        return response!.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        return response?.ConvertAll(item => item.Subscription) ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Get,
            $"subscriptions/{subscriptionId}.json", null, cancellationToken, true);
        return response?.Subscription;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, path);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            return default;

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException((int)response.StatusCode, responseBody);
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }
}
