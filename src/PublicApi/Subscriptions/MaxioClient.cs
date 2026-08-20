using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioClient : IMaxioClient
{
    private const int PageSize = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var settings = options.Value;
        _httpClient = httpClient;
        _httpClient.BaseAddress = settings.GetBaseUri();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var response = await GetAsync<List<MaxioProductResponse>>(
                $"products.json?page={page}&per_page={PageSize}&include_archived=false", cancellationToken);
            products.AddRange(response.Select(item => item.Product));
            if (response.Count < PageSize)
                return products;
        }
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetOptionalAsync<MaxioCustomerResponse>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft customer,
        string uniquenessToken, CancellationToken cancellationToken)
    {
        var request = new CreateMaxioCustomerRequest(
            new CreateMaxioCustomer(customer.FirstName, customer.LastName, customer.Email, customer.Reference),
            uniquenessToken);
        var response = await PostAsync<MaxioCustomerResponse>("customers.json", request, cancellationToken);
        return response.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference,
        CancellationToken cancellationToken)
    {
        var response = await GetOptionalAsync<MaxioSubscriptionResponse>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionDraft subscription,
        string uniquenessToken, CancellationToken cancellationToken)
    {
        var request = new CreateMaxioSubscriptionRequest(
            new CreateMaxioSubscription(subscription.ProductHandle, subscription.CustomerId,
                subscription.Reference, "remittance"),
            uniquenessToken);
        var response = await PostAsync<MaxioSubscriptionResponse>("subscriptions.json", request, cancellationToken);
        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken)
    {
        var response = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return response.Select(item => item.Subscription).ToList();
    }

    private async Task<T> GetAsync<T>(string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<T?> GetOptionalAsync<T>(string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string requestUri, object body, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response,
        CancellationToken cancellationToken) where T : class
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 4096)
                body = body[..4096];
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new MaxioDuplicateRequestException(body);
            throw new MaxioApiException(response.StatusCode, body);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response body.");
    }
}
