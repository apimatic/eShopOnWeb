using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioClient"/> against a Maxio Advanced Billing site API.
/// Authentication is HTTP Basic over TLS: the site/user API key as the username and "X" as the
/// password (see https://docs.maxio.com/introduction/authentication).
/// </summary>
public class MaxioClient : IMaxioClient
{
    private const int MaxPageSize = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        var maxioOptions = options.Value;
        maxioOptions.Validate();

        _httpClient = httpClient;
        _options = maxioOptions;
        _logger = logger;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(maxioOptions.ResolveBaseUrl() + "/");
        }

        var credential = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{maxioOptions.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credential);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("customer").Deserialize<MaxioCustomer>(SerializerOptions);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken = default)
    {
        var payload = new { customer };
        var response = await _httpClient.PostAsync(
            "customers.json",
            new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json"),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("customer").Deserialize<MaxioCustomer>(SerializerOptions)
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty customer object.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var encodedFamily = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var items = await GetListAsync<ProductListEnvelope>(
                $"product_families/{encodedFamily}/products.json?page={page}&per_page={MaxPageSize}", cancellationToken);
            products.AddRange(items.Where(e => e.Product is not null).Select(e => e.Product!));

            if (items.Count < MaxPageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionInput input, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = input.ProductHandle,
                customer_id = input.CustomerId,
                // Subscribes without card capture / 3-DS: the customer is invoiced instead of auto-charged.
                payment_collection_method = _options.PaymentCollectionMethod
            },
            // Billing API duplicate prevention: identical submissions carrying this token are
            // rejected with 409 instead of creating a duplicate (docs: /about-the-api/duplicate-prevention).
            uniqueness_token = Guid.NewGuid()
        };

        var response = await _httpClient.PostAsync(
            "subscriptions.json",
            new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json"),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("subscription").Deserialize<MaxioSubscription>(SerializerOptions)
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty subscription object.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = new List<MaxioSubscription>();
        var page = 1;

        while (true)
        {
            var items = await GetListAsync<SubscriptionListEnvelope>(
                $"customers/{customerId}/subscriptions.json?page={page}&per_page={MaxPageSize}", cancellationToken);
            subscriptions.AddRange(items.Where(e => e.Subscription is not null).Select(e => e.Subscription!));

            if (items.Count < MaxPageSize)
            {
                break;
            }

            page++;
        }

        return subscriptions;
    }

    private async Task<List<T>> GetListAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);

        return JsonSerializer.Deserialize<List<T>>(body, SerializerOptions) ?? new List<T>();
    }

    // Billing API list endpoints wrap each item, e.g. [{"product": {...}}, ...]
    private sealed class ProductListEnvelope
    {
        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private sealed class SubscriptionListEnvelope
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new MaxioApiException((int)response.StatusCode, body);
    }
}
