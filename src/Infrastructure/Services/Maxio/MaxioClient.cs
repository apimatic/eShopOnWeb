using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing REST API. Authenticates with the site API key using
/// HTTP Basic auth (API key as username, literal "x" as password) as documented by Maxio.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;

        _httpClient.BaseAddress = new Uri(_settings.ResolveBaseAddress());
        string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        string url = $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}/products.json";
        string body = await SendAsync(() => _httpClient.GetAsync(url, cancellationToken), cancellationToken);

        var items = JsonSerializer.Deserialize<List<MaxioProductResponse>>(body, JsonOptions);
        return items?
            .Select(i => i.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Cast<MaxioProduct>()
            .ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        string url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        string body = await ReadBodyAsync(response, cancellationToken);
        var dto = JsonSerializer.Deserialize<MaxioCustomerResponse>(body, JsonOptions);
        return dto?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest { Customer = customer };
        var content = JsonContent.Create(request, options: JsonOptions);
        string body = await SendAsync(() => _httpClient.PostAsync("customers.json", content, cancellationToken), cancellationToken);

        var dto = JsonSerializer.Deserialize<MaxioCustomerResponse>(body, JsonOptions);
        return dto?.Customer ?? throw new MaxioApiException(200, "Maxio returned an empty customer payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        string url = $"customers/{customerId}/subscriptions.json";
        string body = await SendAsync(() => _httpClient.GetAsync(url, cancellationToken), cancellationToken);

        var items = JsonSerializer.Deserialize<List<MaxioSubscriptionResponse>>(body, JsonOptions);
        return items?
            .Select(i => i.Subscription)
            .Where(s => s is not null)
            .Cast<MaxioSubscription>()
            .ToList() ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                PaymentCollectionMethod = "remittance"
            }
        };

        var content = JsonContent.Create(request, options: JsonOptions);
        string body = await SendAsync(() => _httpClient.PostAsync("subscriptions.json", content, cancellationToken), cancellationToken);

        var dto = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(body, JsonOptions);
        return dto?.Subscription ?? throw new MaxioApiException(200, "Maxio returned an empty subscription payload.");
    }

    private static async Task<string> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        using var response = await send();
        return await ReadBodyAsync(response, cancellationToken);
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException((int)response.StatusCode, body);
        }

        return body;
    }
}
