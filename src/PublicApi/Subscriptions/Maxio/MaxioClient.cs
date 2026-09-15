using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

/// <summary>
/// Minimal client for the operations used by subscriptions. Paths and payloads mirror maxio-spec/openapi.yaml.
/// </summary>
public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        const int pageSize = 200;

        for (var page = 1; ; page++)
        {
            var result = await SendAsync<List<MaxioProductResponse>>(
                HttpMethod.Get,
                $"product_families/handle:{Uri.EscapeDataString(_options.Value.ProductFamilyHandle)}/products.json?page={page}&per_page={pageSize}&include_archived=false",
                null,
                "list products",
                cancellationToken);

            var pageProducts = result ?? new List<MaxioProductResponse>();
            products.AddRange(pageProducts.Where(item => item.Product != null).Select(item => item.Product));
            if (pageProducts.Count < pageSize)
                break;
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var result = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            "find customer",
            cancellationToken,
            notFoundIsNull: true);
        return result?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var result = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCustomerRequest { Customer = customer },
            "create customer",
            cancellationToken);
        return result!.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var result = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            "find subscription",
            cancellationToken,
            notFoundIsNull: true);
        return result?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        var result = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioSubscriptionRequest { Subscription = subscription },
            "create subscription",
            cancellationToken);
        return result!.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var result = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            "list customer subscriptions",
            cancellationToken);
        return (result ?? new List<MaxioSubscriptionResponse>()).Where(item => item.Subscription != null).Select(item => item.Subscription).ToList();
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string operation,
        CancellationToken cancellationToken,
        bool notFoundIsNull = false)
    {
        _options.Value.Validate();

        using var request = new HttpRequestMessage(method, BuildUri(path));
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Value.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (notFoundIsNull && response.StatusCode == HttpStatusCode.NotFound)
            return default;
        if (!response.IsSuccessStatusCode)
            throw new MaxioApiException(response.StatusCode, operation);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private Uri BuildUri(string path)
    {
        var root = _options.Value.GetBaseAddress().ToString().TrimEnd('/');
        return new Uri($"{root}/{path}", UriKind.Absolute);
    }
}
