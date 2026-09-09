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

public class MaxioApiClient : IMaxioApiClient
{
    private const int MaxPageSize = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _options = options.Value;
        _httpClient = httpClient;

        _httpClient.BaseAddress = new Uri(ResolveBaseUrl(_options).TrimEnd('/') + "/");
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string ResolveBaseUrl(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl;
        }

        return $"https://{options.Subdomain}.chargify.com";
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductFamilyProductsAsync(
        string productFamilyHandle, CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var path = $"product_families/handle:{WebUtility.UrlEncode(productFamilyHandle)}/products.json" +
                       $"?page={page}&per_page={MaxPageSize}";
            var items = await GetJsonAsync<List<MaxioProductEnvelope>>(path, cancellationToken);

            if (items is null || items.Count == 0)
            {
                break;
            }

            products.AddRange(items.Select(i => i.Product!).Where(p => p is not null));

            if (items.Count < MaxPageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={WebUtility.UrlEncode(reference)}";
        var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content
            .ReadFromJsonAsync<MaxioCustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var envelope = await PostJsonAsync<MaxioCustomerEnvelope>("customers.json", request, cancellationToken);
        return envelope!.Customer!;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var envelope = await PostJsonAsync<MaxioSubscriptionEnvelope>("subscriptions.json", request, cancellationToken);
        return envelope!.Subscription!;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var items = await GetJsonAsync<List<MaxioSubscriptionEnvelope>>(path, cancellationToken);
        return (items ?? new List<MaxioSubscriptionEnvelope>())
            .Select(i => i.Subscription!)
            .Where(s => s is not null)
            .ToList();
    }

    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
    }

    private async Task<T?> PostJsonAsync<T>(string path, object request, CancellationToken cancellationToken) where T : class
    {
        var response = await _httpClient.PostAsJsonAsync(path, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(
            (int)response.StatusCode,
            body,
            $"Maxio Advanced Billing API returned {(int)response.StatusCode} ({response.ReasonPhrase}) for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}");
    }

    private class MaxioProductEnvelope
    {
        [System.Text.Json.Serialization.JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private class MaxioCustomerEnvelope
    {
        [System.Text.Json.Serialization.JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private class MaxioSubscriptionEnvelope
    {
        [System.Text.Json.Serialization.JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }
    }
}
