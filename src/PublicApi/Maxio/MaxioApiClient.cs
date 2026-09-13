using System;
using System.Collections.Generic;
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

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var baseUrl = settings.Value.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = $"https://{settings.Value.Subdomain}.chargify.com";
        }
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.Value.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken ct = default)
    {
        var raw = await GetRawAsync("products.json", ct);
        var items = JsonSerializer.Deserialize<List<ProductWrapper>>(raw, _jsonOptions);
        return items?.Select(w => w.Product).ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByEmailAsync(string email, CancellationToken ct = default)
    {
        try
        {
            var encoded = Uri.EscapeDataString(email);
            var raw = await GetRawAsync($"customers.json?email={encoded}", ct);
            var items = JsonSerializer.Deserialize<List<CustomerWrapper>>(raw, _jsonOptions);
            return items?.Select(w => w.Customer).FirstOrDefault();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to search for customer by email {Email}", email);
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreateRequest request, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { customer = request }, _jsonOptions);
        var raw = await PostRawAsync("customers.json", body, ct);
        var wrapper = JsonSerializer.Deserialize<CustomerWrapper>(raw, _jsonOptions);
        return wrapper!.Customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreateRequest request, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { subscription = request }, _jsonOptions);
        var raw = await PostRawAsync("subscriptions.json", body, ct);
        var wrapper = JsonSerializer.Deserialize<SubscriptionWrapper>(raw, _jsonOptions);
        return wrapper!.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsByCustomerIdAsync(int customerId, CancellationToken ct = default)
    {
        var raw = await GetRawAsync($"customers/{customerId}/subscriptions.json", ct);
        var items = JsonSerializer.Deserialize<List<SubscriptionWrapper>>(raw, _jsonOptions);
        return items?.Select(w => w.Subscription).ToList() ?? new List<MaxioSubscription>();
    }

    private async Task<string> GetRawAsync(string path, CancellationToken ct)
    {
        _logger.LogDebug("Maxio API GET {Path}", path);
        using var response = await _httpClient.GetAsync(path, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio API error {StatusCode}: {Response}", response.StatusCode, content);
            throw new HttpRequestException($"Maxio API returned {(int)response.StatusCode}: {content}");
        }
        return content;
    }

    private async Task<string> PostRawAsync(string path, string json, CancellationToken ct)
    {
        _logger.LogDebug("Maxio API POST {Path}", path);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(path, content, ct);
        var responseContent = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio API error {StatusCode}: {Response}", response.StatusCode, responseContent);
            throw new HttpRequestException($"Maxio API returned {(int)response.StatusCode}: {responseContent}");
        }
        return responseContent;
    }

    // Wrapper types matching Maxio's actual JSON envelope
    private class ProductWrapper
    {
        [JsonPropertyName("product")]
        public MaxioProduct Product { get; set; } = null!;
    }

    private class CustomerWrapper
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer Customer { get; set; } = null!;
    }

    private class SubscriptionWrapper
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription Subscription { get; set; } = null!;
    }
}
