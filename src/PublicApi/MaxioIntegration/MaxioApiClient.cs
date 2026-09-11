using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUrl = _settings.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = $"https://{_settings.Subdomain}.chargify.com";
        }
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProductDto>> GetProductsByFamilyAsync(string productFamilyHandle)
    {
        var response = await _httpClient.GetAsync($"product_families/{Uri.EscapeDataString(productFamilyHandle)}/products.json");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<ProductListResponse>(content, JsonOptions);
        return wrapper?.Items.Select(i => i.Product).ToList() ?? new List<MaxioProductDto>();
    }

    public async Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"customers.json?reference={Uri.EscapeDataString(reference)}");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to search customers by reference {Reference}: {StatusCode}", reference, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var wrapper = JsonSerializer.Deserialize<CustomerListResponse>(content, JsonOptions);
            return wrapper?.Items.FirstOrDefault()?.Customer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for customer with reference {Reference}", reference);
            return null;
        }
    }

    public async Task<MaxioCustomerDto> CreateCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference
            }
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("customers.json", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to create customer: {StatusCode} - {Content}", response.StatusCode, responseContent);
            response.EnsureSuccessStatusCode();
        }

        var wrapper = JsonSerializer.Deserialize<SingleCustomerResponse>(responseContent, JsonOptions);
        return wrapper?.Customer ?? throw new InvalidOperationException("Failed to deserialize customer response");
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = request.ProductHandle,
                customer_reference = request.CustomerReference,
                customer_id = request.CustomerId,
                customer_attributes = request.CustomerAttributes != null ? new
                {
                    first_name = request.CustomerAttributes.FirstName,
                    last_name = request.CustomerAttributes.LastName,
                    email = request.CustomerAttributes.Email
                } : null
            },
            uniqueness_token = Guid.NewGuid().ToString()
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("subscriptions.json", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to create subscription: {StatusCode} - {Content}", response.StatusCode, responseContent);
            response.EnsureSuccessStatusCode();
        }

        var wrapper = JsonSerializer.Deserialize<SingleSubscriptionResponse>(responseContent, JsonOptions);
        return wrapper?.Subscription ?? throw new InvalidOperationException("Failed to deserialize subscription response");
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> GetSubscriptionsByCustomerIdAsync(int maxioCustomerId)
    {
        var response = await _httpClient.GetAsync($"customers/{maxioCustomerId}/subscriptions.json");
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get subscriptions for customer {CustomerId}: {StatusCode}", maxioCustomerId, response.StatusCode);
            return new List<MaxioSubscriptionDto>();
        }

        var content = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<SubscriptionListResponse>(content, JsonOptions);
        return wrapper?.Items.Select(i => i.Subscription).ToList() ?? new List<MaxioSubscriptionDto>();
    }
}
