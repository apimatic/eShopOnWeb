using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<List<MaxioProduct>> GetProductsByFamilyAsync(string productFamilyHandle);
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference);
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<MaxioProduct>> GetProductsByFamilyAsync(string productFamilyHandle)
    {
        var response = await _httpClient.GetAsync("/products.json");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();

        var products = JsonSerializer.Deserialize<List<MaxioProductResponse>>(content, JsonOptions);
        var allProducts = products?.Select(p => p.Product).ToList() ?? new List<MaxioProduct>();

        return allProducts.Where(p =>
            string.Equals(p.ProductFamily?.Handle, productFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference)
    {
        var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await _httpClient.GetAsync(url);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MaxioCustomerResponse>(content, JsonOptions);
        return result?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        var requestBody = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference
            }
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/customers.json", content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MaxioCustomerResponse>(responseContent, JsonOptions);
        return result?.Customer ?? throw new InvalidOperationException("Failed to deserialize customer response");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        var requestBody = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                payment_collection_method = "remittance"
            }
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/subscriptions.json", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to create subscription. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, errorContent);
            throw new InvalidOperationException($"Failed to create subscription: {response.StatusCode} - {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(responseContent, JsonOptions);
        return result?.Subscription ?? throw new InvalidOperationException("Failed to deserialize subscription response");
    }

    public async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId)
    {
        var url = $"/customers/{customerId}/subscriptions.json";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var subscriptions = JsonSerializer.Deserialize<List<MaxioSubscriptionResponse>>(content, JsonOptions);
        return subscriptions?.Select(s => s.Subscription).ToList() ?? new List<MaxioSubscription>();
    }
}
