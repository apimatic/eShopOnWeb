using System;
using System.Collections.Generic;
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

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;
    private readonly MaxioSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioClient(HttpClient httpClient, ILogger<MaxioClient> logger, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings.Value;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var baseUrl = _settings.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = $"https://{_settings.Subdomain}.chargify.com";
        }
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

        var authBytes = Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<MaxioProduct>> GetProductsAsync(string? productFamilyHandle = null, CancellationToken ct = default)
    {
        var url = "products.json?per_page=200";
        if (!string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            url += $"&product_family_id=handle:{productFamilyHandle}";
        }

        _logger.LogInformation("Maxio: GET {Url}", url);
        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);

        // Maxio returns a flat array of {product:{...}} objects
        var items = JsonSerializer.Deserialize<List<MaxioProductItem>>(content, _jsonOptions);
        var products = new List<MaxioProduct>();
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item.Product != null)
                    products.Add(item.Product);
            }
        }
        return products;
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken ct = default)
    {
        var products = await GetProductsAsync(ct: ct);
        return products.Find(p =>
            string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogInformation("Maxio: GET {Url}", url);
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);
            var wrapper = JsonSerializer.Deserialize<MaxioCustomerWrapper>(content, _jsonOptions);
            return wrapper?.Customer;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken ct = default)
    {
        var payload = new { customer };
        var body = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        _logger.LogInformation("Maxio: POST customers.json");
        var response = await _httpClient.PostAsync("customers.json", content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Maxio create customer failed ({response.StatusCode}): {responseBody}");
        }

        var wrapper = JsonSerializer.Deserialize<MaxioCustomerWrapper>(responseBody, _jsonOptions);
        if (wrapper?.Customer == null)
            throw new InvalidOperationException("Maxio returned empty customer response");

        return wrapper.Customer;
    }

    public async Task<MaxioPaymentProfile> CreatePaymentProfileAsync(MaxioPaymentProfileCreate profile, CancellationToken ct = default)
    {
        var payload = new { payment_profile = profile };
        var body = JsonSerializer.Serialize(payload, _jsonOptions);
        _logger.LogInformation("Maxio: POST payment_profiles.json payload: {Body}", body);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        _logger.LogInformation("Maxio: POST payment_profiles.json");
        var response = await _httpClient.PostAsync("payment_profiles.json", content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        _logger.LogInformation("Maxio payment profile response: {Status} {Body}", response.StatusCode, responseBody);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Maxio create payment profile failed ({response.StatusCode}): {responseBody}");
        }

        var wrapper = JsonSerializer.Deserialize<MaxioPaymentProfileWrapper>(responseBody, _jsonOptions);
        if (wrapper?.PaymentProfile == null)
            throw new InvalidOperationException("Maxio returned empty payment profile response");

        return wrapper.PaymentProfile;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, CancellationToken ct = default)
    {
        var payload = new { subscription };
        var body = JsonSerializer.Serialize(payload, _jsonOptions);
        _logger.LogInformation("Maxio: POST subscriptions.json payload: {Body}", body);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        _logger.LogInformation("Maxio: POST subscriptions.json");
        var response = await _httpClient.PostAsync("subscriptions.json", content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        _logger.LogInformation("Maxio subscription response: {Status} {Body}", response.StatusCode, responseBody);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Maxio create subscription failed ({response.StatusCode}): {responseBody}");
        }

        var wrapper = JsonSerializer.Deserialize<MaxioSubscriptionWrapper>(responseBody, _jsonOptions);
        if (wrapper?.Subscription == null)
            throw new InvalidOperationException("Maxio returned empty subscription response");

        return wrapper.Subscription;
    }

    public async Task<List<MaxioSubscription>> GetSubscriptionsByCustomerIdAsync(int customerId, CancellationToken ct = default)
    {
        var url = $"customers/{customerId}/subscriptions.json?per_page=200";
        _logger.LogInformation("Maxio: GET {Url}", url);
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new List<MaxioSubscription>();
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);

            // Maxio returns a flat array of {subscription:{...}} objects
            var items = JsonSerializer.Deserialize<List<MaxioSubscriptionItem>>(content, _jsonOptions);
            var subs = new List<MaxioSubscription>();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item.Subscription != null)
                        subs.Add(item.Subscription);
                }
            }
            return subs;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new List<MaxioSubscription>();
        }
    }

    // JSON wrapper types for deserialization

    private class MaxioProductItem
    {
        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private class MaxioCustomerWrapper
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private class MaxioPaymentProfileWrapper
    {
        [JsonPropertyName("payment_profile")]
        public MaxioPaymentProfile? PaymentProfile { get; set; }
    }

    private class MaxioSubscriptionWrapper
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }
    }

    private class MaxioSubscriptionItem
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }
    }
}
