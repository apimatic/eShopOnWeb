using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioApiClient
{
    Task<List<ProductDto>> ListProductsAsync(string familyHandle);
    Task<CustomerDto?> FindCustomerByReferenceAsync(string reference);
    Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request);
    Task<SubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionRequest request);
    Task<List<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
        SetupHttpClient();
    }

    private void SetupHttpClient()
    {
        var baseUrl = _settings.GetApiUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<List<ProductDto>> ListProductsAsync(string familyHandle)
    {
        var url = $"/product_families/{familyHandle}/products.json?per_page=200";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to list products: {StatusCode}", response.StatusCode);
            throw new Exception($"Failed to list products: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");

        var products = new List<ProductDto>();
        foreach (var item in items.EnumerateArray())
        {
            var product = item.GetProperty("product");
            products.Add(new ProductDto
            {
                Id = product.GetProperty("id").GetInt32(),
                Handle = product.GetProperty("handle").GetString() ?? string.Empty,
                Name = product.GetProperty("name").GetString() ?? string.Empty,
                Description = product.TryGetProperty("description", out var desc) ? (desc.GetString() ?? string.Empty) : string.Empty,
                PriceInCents = product.GetProperty("price_in_cents").GetInt64(),
                Interval = product.GetProperty("interval").GetInt32(),
                IntervalUnit = product.GetProperty("interval_unit").GetString() ?? "month"
            });
        }

        return products;
    }

    public async Task<CustomerDto?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to find customer: {StatusCode}", response.StatusCode);
            throw new Exception($"Failed to find customer: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var customer = doc.RootElement.GetProperty("customer");

        return new CustomerDto
        {
            Id = customer.GetProperty("id").GetInt32(),
            FirstName = customer.GetProperty("first_name").GetString() ?? string.Empty,
            LastName = customer.GetProperty("last_name").GetString() ?? string.Empty,
            Email = customer.GetProperty("email").GetString() ?? string.Empty,
            Reference = customer.GetProperty("reference").GetString() ?? string.Empty
        };
    }

    public async Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request)
    {
        var url = "/customers.json";

        var payload = new
        {
            customer = new
            {
                request.FirstName,
                request.LastName,
                request.Email,
                request.Reference
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(url, content);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to create customer: {StatusCode} {Error}", response.StatusCode, errorContent);
            throw new Exception($"Failed to create customer: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var customer = doc.RootElement.GetProperty("customer");

        return new CustomerDto
        {
            Id = customer.GetProperty("id").GetInt32(),
            FirstName = customer.GetProperty("first_name").GetString() ?? string.Empty,
            LastName = customer.GetProperty("last_name").GetString() ?? string.Empty,
            Email = customer.GetProperty("email").GetString() ?? string.Empty,
            Reference = customer.GetProperty("reference").GetString() ?? string.Empty
        };
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionRequest request)
    {
        var url = "/subscriptions.json";

        var payload = new
        {
            subscription = new
            {
                request.ProductHandle,
                request.CustomerId,
                request.CustomerAttributes
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(url, content);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to create subscription: {StatusCode} {Error}", response.StatusCode, errorContent);
            throw new Exception($"Failed to create subscription: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var subscription = doc.RootElement.GetProperty("subscription");

        return ParseSubscriptionDto(subscription);
    }

    public async Task<List<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var url = $"/customers/{customerId}/subscriptions.json";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to list customer subscriptions: {StatusCode}", response.StatusCode);
            throw new Exception($"Failed to list customer subscriptions: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var subscriptions = new List<SubscriptionDto>();
        if (doc.RootElement.TryGetProperty("subscriptions", out var subs))
        {
            foreach (var sub in subs.EnumerateArray())
            {
                subscriptions.Add(ParseSubscriptionDto(sub));
            }
        }

        return subscriptions;
    }

    private static SubscriptionDto ParseSubscriptionDto(JsonElement element)
    {
        var dto = new SubscriptionDto
        {
            Id = element.GetProperty("id").GetInt32(),
            CustomerId = element.GetProperty("customer_id").GetInt32(),
            State = element.GetProperty("state").GetString() ?? string.Empty
        };

        if (element.TryGetProperty("product", out var product) && product.ValueKind != JsonValueKind.Null)
        {
            dto.ProductName = product.TryGetProperty("name", out var name) ? (name.GetString() ?? string.Empty) : string.Empty;
            dto.ProductHandle = product.TryGetProperty("handle", out var handle) ? (handle.GetString() ?? string.Empty) : string.Empty;
        }

        if (element.TryGetProperty("product_price_point", out var pricePoint) && pricePoint.ValueKind != JsonValueKind.Null)
        {
            if (pricePoint.TryGetProperty("price_in_cents", out var price))
            {
                dto.PriceInCents = price.GetInt64();
            }
        }

        if (element.TryGetProperty("next_billing_at", out var nextBilling) && nextBilling.ValueKind != JsonValueKind.Null)
        {
            if (DateTime.TryParse(nextBilling.GetString(), out var date))
            {
                dto.NextBillingAt = date;
            }
        }

        if (element.TryGetProperty("created_at", out var created))
        {
            if (DateTime.TryParse(created.GetString(), out var date))
            {
                dto.CreatedAt = date;
            }
        }

        return dto;
    }
}
