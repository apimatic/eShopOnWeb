using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _maxioOptions;
    private readonly string? _environment;
    private bool _configured;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _maxioOptions = options.Value;
        _environment = configuration["MAXIO_ENVIRONMENT"];
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?page=1&per_page=200";
        var products = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, null, cancellationToken);
        var result = new List<MaxioProduct>();
        foreach (var item in products ?? new List<MaxioProductResponse>())
        {
            if (item.Product is not null)
            {
                result.Add(item.Product);
            }
        }

        return result;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };

        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", body, cancellationToken);
        return response?.Customer ?? throw new InvalidOperationException("Maxio returned no customer in its create response.");
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                reference,
                payment_collection_method = "remittance"
            }
        };

        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        return response?.Subscription ?? throw new InvalidOperationException("Maxio returned no subscription in its create response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        foreach (var item in response ?? new List<MaxioSubscriptionResponse>())
        {
            if (item.Subscription is not null)
            {
                subscriptions.Add(item.Subscription);
            }
        }

        return subscriptions;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        ConfigureClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException((int)response.StatusCode, responseBody);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Maxio returned an empty JSON response.");
    }

    private void ConfigureClient()
    {
        if (_configured)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_maxioOptions.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(_maxioOptions.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        }

        _httpClient.BaseAddress = _maxioOptions.ResolveBaseUrl(_environment);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_maxioOptions.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _configured = true;
    }
}
