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

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Small typed adapter for the Maxio operations used by subscriptions. Paths,
/// wrappers, Basic authentication, pagination and the server template follow
/// maxio-spec/openapi.yaml.
/// </summary>
public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private const int ProductsPerPage = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = _options.GetBaseUri();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var familyId = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");

        for (var page = 1; ; page++)
        {
            var pageProducts = await GetAsync<List<MaxioProductResponse>>(
                $"product_families/{familyId}/products.json?page={page}&per_page={ProductsPerPage}", cancellationToken);
            products.AddRange(pageProducts.Select(x => x.Product));
            if (pageProducts.Count < ProductsPerPage)
            {
                return products;
            }
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await GetAsync<MaxioCustomerResponse>(
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
            return response.Customer;
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
        => (await SendAsync<CreateCustomerRequest, MaxioCustomerResponse>(HttpMethod.Post, "customers.json", request, cancellationToken)).Customer;

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return response.Select(x => x.Subscription).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken)
        => (await SendAsync<CreateSubscriptionRequest, MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", request, cancellationToken)).Subscription;

    private async Task<TResponse> GetAsync<TResponse>(string relativeUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUri, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod method, string relativeUri, TRequest payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUri)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<TResponse> ReadResponseAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode);
        }

        var value = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
        return value ?? throw new InvalidOperationException("Maxio returned an empty response body.");
    }
}
