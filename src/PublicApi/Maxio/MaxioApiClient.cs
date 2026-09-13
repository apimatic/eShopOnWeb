using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        var baseUrl = !string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? _options.BaseUrl.TrimEnd('/')
            : $"https://{_options.Subdomain}.chargify.com";

        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        var authBytes = Encoding.ASCII.GetBytes($"{_options.ApiKey}:X");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ProductItem[]?> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var url = $"product_families/handle:{productFamilyHandle}/products.json";
        return await GetAsync<ProductItem[]>(url, cancellationToken);
    }

    public async Task<CustomerLookupResponse?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        return await GetAsync<CustomerLookupResponse>(url, cancellationToken);
    }

    public async Task<CreateCustomerResponse?> CreateCustomerAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new { customer = request };
        return await PostAsync<CreateCustomerResponse>("customers.json", payload, cancellationToken);
    }

    public async Task<MaxioCreateSubscriptionResponse?> CreateSubscriptionAsync(
        MaxioCreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new { subscription = request };
        return await PostAsync<MaxioCreateSubscriptionResponse>("subscriptions.json", payload, cancellationToken);
    }

    public async Task<SubscriptionDto[]?> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        var items = await GetAsync<SubscriptionListItem[]>(url, cancellationToken);
        return items?.Select(i => i.Subscription).Where(s => s != null).ToArray();
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        _logger.LogInformation("Maxio GET {Url}", url);
        var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio GET {Url} returned {StatusCode}: {Body}",
                url, response.StatusCode, body);
            return null;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private async Task<T?> PostAsync<T>(string url, object payload, CancellationToken cancellationToken) where T : class
    {
        _logger.LogInformation("Maxio POST {Url}", url);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio POST {Url} returned {StatusCode}: {Body}",
                url, response.StatusCode, body);
            return null;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }
}
