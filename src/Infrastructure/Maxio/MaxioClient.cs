using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioClient"/> against the Maxio
/// Advanced Billing Billing API. Authenticates with HTTP Basic auth
/// (API key as username, "X" as password) over TLS, per Maxio docs.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        if (_options.IsConfigured())
        {
            _httpClient.BaseAddress = new Uri(_options.GetBaseAddress());
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
            if (_httpClient.DefaultRequestHeaders.Authorization is null)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
        }
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return JsonSerializer.Deserialize<CustomerResponse>(body, SerializerOptions)?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerInput customer, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("customers.json",
            ToJson(new CreateCustomerRequest { Customer = customer }), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return JsonSerializer.Deserialize<CustomerResponse>(body, SerializerOptions)?.Customer
               ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty customer payload.", Array.Empty<string>());
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        const int perPage = 200;
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var response = await _httpClient.GetAsync(
                $"product_families/{Uri.EscapeDataString("handle:" + productFamilyHandle)}/products.json?page={page}&per_page={perPage}",
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, body);

            var items = JsonSerializer.Deserialize<List<ProductResponse>>(body, SerializerOptions) ?? new List<ProductResponse>();
            products.AddRange(items.Where(i => i.Product is not null).Select(i => i.Product));

            if (items.Count < perPage || page >= 50)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string? uniquenessToken = null, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionInput
            {
                ProductHandle = productHandle,
                CustomerId = customerId
            },
            UniquenessToken = uniquenessToken
        };

        var response = await _httpClient.PostAsync("subscriptions.json", ToJson(request), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return JsonSerializer.Deserialize<SubscriptionResponse>(body, SerializerOptions)?.Subscription
               ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty subscription payload.", Array.Empty<string>());
    }

    public async Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"subscriptions/{subscriptionId}.json", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return JsonSerializer.Deserialize<SubscriptionResponse>(body, SerializerOptions)?.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json?per_page=200", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        var items = JsonSerializer.Deserialize<List<SubscriptionResponse>>(body, SerializerOptions) ?? new List<SubscriptionResponse>();
        return items.Where(i => i.Subscription is not null).Select(i => i.Subscription).ToList();
    }

    private static StringContent ToJson<T>(T payload)
        => new(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json");

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw MaxioApiException.FromResponse((int)response.StatusCode, body);
    }
}
