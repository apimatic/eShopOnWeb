using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioClient(HttpClient httpClient, MaxioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    private void SetAuthHeader()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<MaxioCustomer?> LookupCustomerByEmailAsync(string email)
    {
        SetAuthHeader();
        var baseUrl = _settings.GetBaseUrl();

        try
        {
            var lookupUrl = $"{baseUrl}/customers/lookup.json?email={Uri.EscapeDataString(email)}";
            var response = await _httpClient.GetAsync(lookupUrl);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("customer", out var customerElem))
                {
                    return JsonSerializer.Deserialize<MaxioCustomer>(customerElem.GetRawText(), _jsonOptions);
                }
            }
            // Return null if customer not found (404) or any other error
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer?> CreateOrGetCustomerAsync(string email, string firstName, string lastName)
    {
        SetAuthHeader();
        var baseUrl = _settings.GetBaseUrl();

        try
        {
            // Try to lookup existing customer by email
            var lookupUrl = $"{baseUrl}/customers/lookup.json?email={Uri.EscapeDataString(email)}";
            var lookupResponse = await _httpClient.GetAsync(lookupUrl);

            if (lookupResponse.IsSuccessStatusCode)
            {
                var content = await lookupResponse.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("customer", out var customerElem))
                {
                    return JsonSerializer.Deserialize<MaxioCustomer>(customerElem.GetRawText(), _jsonOptions);
                }
            }

            // Customer doesn't exist, create new one
            var createUrl = $"{baseUrl}/customers.json";
            var createRequest = new
            {
                customer = new
                {
                    email,
                    first_name = firstName,
                    last_name = lastName
                }
            };

            var json = JsonSerializer.Serialize(createRequest);
            var content_request = new StringContent(json, Encoding.UTF8, "application/json");
            var createResponse = await _httpClient.PostAsync(createUrl, content_request);

            if (createResponse.IsSuccessStatusCode)
            {
                var responseContent = await createResponse.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(responseContent);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("customer", out var customerElem))
                {
                    return JsonSerializer.Deserialize<MaxioCustomer>(customerElem.GetRawText(), _jsonOptions);
                }
            }
            else
            {
                var errorContent = await createResponse.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to create customer: {createResponse.StatusCode} - {errorContent}");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Maxio API error: {ex.Message}", ex);
        }

        return null;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        SetAuthHeader();
        var baseUrl = _settings.GetBaseUrl();

        try
        {
            var url = $"{baseUrl}/subscriptions.json";
            var request = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "automatic"
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(responseContent);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("subscription", out var subscriptionElem))
                {
                    var subscription = JsonSerializer.Deserialize<MaxioSubscription>(subscriptionElem.GetRawText(), _jsonOptions);
                    if (subscription != null)
                        return subscription;
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to create subscription: {response.StatusCode} - {errorContent}");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Maxio API error: {ex.Message}", ex);
        }

        throw new InvalidOperationException("Failed to parse subscription response");
    }

    public async Task<IEnumerable<MaxioProduct>> GetProductsAsync(string productFamilyHandle)
    {
        SetAuthHeader();
        var baseUrl = _settings.GetBaseUrl();

        try
        {
            // First, get the product family to find its ID
            var familyUrl = $"{baseUrl}/product_families.json";
            var familiesResponse = await _httpClient.GetAsync(familyUrl);

            if (!familiesResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Failed to get product families: {familiesResponse.StatusCode}");
            }

            var familiesContent = await familiesResponse.Content.ReadAsStringAsync();
            int? familyId = null;

            using (var jsonDoc = JsonDocument.Parse(familiesContent))
            {
                var root = jsonDoc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var familyElem in root.EnumerateArray())
                    {
                        if (familyElem.TryGetProperty("product_family", out var pf))
                        {
                            if (pf.TryGetProperty("handle", out var handle) && handle.GetString() == productFamilyHandle)
                            {
                                if (pf.TryGetProperty("id", out var id))
                                {
                                    familyId = id.GetInt32();
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (!familyId.HasValue)
            {
                throw new InvalidOperationException($"Product family '{productFamilyHandle}' not found");
            }

            // Now get products for this family
            var productsUrl = $"{baseUrl}/product_families/{familyId}/products.json";
            var productsResponse = await _httpClient.GetAsync(productsUrl);

            if (!productsResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Failed to get products: {productsResponse.StatusCode}");
            }

            var productsContent = await productsResponse.Content.ReadAsStringAsync();
            var products = new List<MaxioProduct>();

            using (var jsonDoc = JsonDocument.Parse(productsContent))
            {
                var root = jsonDoc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var productElem in root.EnumerateArray())
                    {
                        if (productElem.TryGetProperty("product", out var prod))
                        {
                            var product = JsonSerializer.Deserialize<MaxioProduct>(prod.GetRawText(), _jsonOptions);
                            if (product != null)
                                products.Add(product);
                        }
                    }
                }
            }

            return products;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Maxio API error: {ex.Message}", ex);
        }
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string productHandle)
    {
        SetAuthHeader();
        var baseUrl = _settings.GetBaseUrl();

        try
        {
            var url = $"{baseUrl}/products/handle/{Uri.EscapeDataString(productHandle)}.json";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("product", out var productElem))
                {
                    return JsonSerializer.Deserialize<MaxioProduct>(productElem.GetRawText(), _jsonOptions);
                }
            }

            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<IEnumerable<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId)
    {
        SetAuthHeader();
        var baseUrl = _settings.GetBaseUrl();

        try
        {
            var url = $"{baseUrl}/customers/{customerId}/subscriptions.json";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Failed to get subscriptions: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var subscriptions = new List<MaxioSubscription>();

            using (var jsonDoc = JsonDocument.Parse(content))
            {
                var root = jsonDoc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var subElem in root.EnumerateArray())
                    {
                        if (subElem.TryGetProperty("subscription", out var sub))
                        {
                            var subscription = JsonSerializer.Deserialize<MaxioSubscription>(sub.GetRawText(), _jsonOptions);
                            if (subscription != null)
                                subscriptions.Add(subscription);
                        }
                    }
                }
            }

            return subscriptions;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Maxio API error: {ex.Message}", ex);
        }
    }
}
