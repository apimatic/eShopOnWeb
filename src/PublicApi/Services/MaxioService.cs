using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync();
    Task<int?> GetOrCreateCustomerAsync(string userId, string email);
    Task<SubscriptionDto?> CreateSubscriptionAsync(int customerId, string productHandle, string planHandle);
    Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, MaxioConfiguration config, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        SetupHttpClient();
    }

    private void SetupHttpClient()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.BaseAddress = new Uri(_config.GetBaseUrl());
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products.json?filter[family_id]={await GetProductFamilyIdAsync()}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioProductsResponse>(content, options);

            var plans = new List<SubscriptionPlanDto>();
            if (result?.Products != null)
            {
                foreach (var product in result.Products)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id,
                        Handle = product.Handle,
                        Name = product.Name,
                        Price = product.DefaultPriceInCents / 100m,
                        Interval = product.IntervalUnit,
                        IntervalCount = product.Interval
                    });
                }
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscription plans from Maxio");
            throw;
        }
    }

    public async Task<int?> GetOrCreateCustomerAsync(string userId, string email)
    {
        try
        {
            var existingCustomer = await GetCustomerByReferenceAsync(userId);
            if (existingCustomer != null)
            {
                return existingCustomer.Id;
            }

            var createRequest = new
            {
                customer = new
                {
                    first_name = email.Split('@')[0],
                    email = email,
                    reference = userId
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(createRequest),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/customers.json", jsonContent);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioCustomerResponse>(content, options);

            return result?.Customer?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create customer in Maxio");
            throw;
        }
    }

    public async Task<SubscriptionDto?> CreateSubscriptionAsync(int customerId, string productHandle, string planHandle)
    {
        try
        {
            var productId = await GetProductIdByHandleAsync(productHandle);
            if (!productId.HasValue)
            {
                throw new InvalidOperationException($"Product handle '{productHandle}' not found");
            }

            var componentId = await GetMeteredComponentIdAsync(productId.Value);
            var createRequest = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_id = productId.Value,
                    product_price_point_id = await GetPricePointIdByHandleAsync(productId.Value, planHandle),
                    payment_collection_method = "automatic"
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(createRequest),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", jsonContent);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(content, options);

            if (result?.Subscription != null)
            {
                return new SubscriptionDto
                {
                    Id = result.Subscription.Id,
                    State = result.Subscription.State,
                    CreatedAt = result.Subscription.CreatedAt,
                    CurrentPeriodEndsAt = result.Subscription.CurrentPeriodEndsAt,
                    ProductHandle = result.Subscription.ProductHandle,
                    PlanHandle = planHandle
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription in Maxio");
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/subscriptions.json?customer_id={customerId}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioSubscriptionsResponse>(content, options);

            var subscriptions = new List<SubscriptionDto>();
            if (result?.Subscriptions != null)
            {
                foreach (var sub in result.Subscriptions)
                {
                    subscriptions.Add(new SubscriptionDto
                    {
                        Id = sub.Id,
                        State = sub.State,
                        CreatedAt = sub.CreatedAt,
                        CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                        ProductHandle = sub.ProductHandle,
                        PlanHandle = ""
                    });
                }
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get customer subscriptions from Maxio");
            throw;
        }
    }

    private async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers.json?reference={Uri.EscapeDataString(reference)}");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioCustomersResponse>(content, options);

            return result?.Customers?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check existing customer");
            return null;
        }
    }

    private async Task<int?> GetProductIdByHandleAsync(string handle)
    {
        try
        {
            var familyId = await GetProductFamilyIdAsync();
            var response = await _httpClient.GetAsync($"/products.json?filter[family_id]={familyId}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioProductsResponse>(content, options);

            var product = result?.Products?.FirstOrDefault(p => p.Handle == handle);
            return product?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get product ID by handle");
            throw;
        }
    }

    private async Task<int?> GetProductFamilyIdAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"/product_families.json?filter[handle]={_config.ProductFamilyHandle}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioProductFamiliesResponse>(content, options);

            return result?.ProductFamilies?.FirstOrDefault()?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get product family ID");
            throw;
        }
    }

    private async Task<int?> GetPricePointIdByHandleAsync(int productId, string planHandle)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products/{productId}/price_points.json?filter[handle]={planHandle}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioPricePointsResponse>(content, options);

            return result?.PricePoints?.FirstOrDefault()?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get price point ID by handle");
            throw;
        }
    }

    private async Task<int?> GetMeteredComponentIdAsync(int productId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products/{productId}/components.json?filter[kind]=metered_component");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioComponentsResponse>(content, options);

            return result?.Components?.FirstOrDefault()?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get metered component");
            return null;
        }
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Interval { get; set; } = string.Empty;
    public int IntervalCount { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
}

#region Maxio Response Models
internal class MaxioProductsResponse
{
    [JsonPropertyName("products")]
    public List<MaxioProduct>? Products { get; set; }
}

internal class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("default_price_in_cents")]
    public int DefaultPriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;
}

internal class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal class MaxioCustomersResponse
{
    [JsonPropertyName("customers")]
    public List<MaxioCustomer>? Customers { get; set; }
}

internal class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

internal class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal class MaxioSubscriptionsResponse
{
    [JsonPropertyName("subscriptions")]
    public List<MaxioSubscription>? Subscriptions { get; set; }
}

internal class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }
}

internal class MaxioProductFamiliesResponse
{
    [JsonPropertyName("product_families")]
    public List<MaxioProductFamily>? ProductFamilies { get; set; }
}

internal class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
}

internal class MaxioPricePointsResponse
{
    [JsonPropertyName("price_points")]
    public List<MaxioPricePoint>? PricePoints { get; set; }
}

internal class MaxioPricePoint
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
}

internal class MaxioComponentsResponse
{
    [JsonPropertyName("components")]
    public List<MaxioComponent>? Components { get; set; }
}

internal class MaxioComponent
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
}
#endregion
