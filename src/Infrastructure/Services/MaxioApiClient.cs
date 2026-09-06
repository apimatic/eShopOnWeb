using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> options, IAppLogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;

        var baseUrl = _settings.GetApiUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<MaxioProductResponse?> GetProductAsync(string productHandle)
    {
        try
        {
            var url = $"/products/handle/{productHandle}.json";
            var request = CreateRequest(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to get product {productHandle}: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            var productJson = json.RootElement.GetProperty("product");

            return MapToProductResponse(productJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error getting product {productHandle}: {ex.Message}");
            return null;
        }
    }

    public async Task<List<MaxioProductResponse>> ListProductsByFamilyAsync(string familyHandle)
    {
        var products = new List<MaxioProductResponse>();

        try
        {
            var url = $"/product_families/handle:{familyHandle}/products.json";
            var request = CreateRequest(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to list products for family {familyHandle}: {response.StatusCode}");
                return products;
            }

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            var productsArray = json.RootElement.GetProperty("products");

            foreach (var productJson in productsArray.EnumerateArray())
            {
                var product = MapToProductResponse(productJson);
                if (product != null)
                {
                    products.Add(product);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error listing products for family {familyHandle}: {ex.Message}");
        }

        return products;
    }

    public async Task<MaxioCustomerResponse?> CreateOrGetCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        try
        {
            var requestBody = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = userId
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = "/customers.json";
            var request = CreateRequest(HttpMethod.Post, url);
            request.Content = content;

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to create customer {userId}: {response.StatusCode}");
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseContent);
            var customerJson = jsonDoc.RootElement.GetProperty("customer");

            return MapToCustomerResponse(customerJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error creating customer {userId}: {ex.Message}");
            return null;
        }
    }

    public async Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var requestBody = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "automatic"
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = "/subscriptions.json";
            var request = CreateRequest(HttpMethod.Post, url);
            request.Content = content;

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning($"Failed to create subscription for customer {customerId}, product {productHandle}: {response.StatusCode} - {errorContent}");
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseContent);
            var subscriptionJson = jsonDoc.RootElement.GetProperty("subscription");

            return MapToSubscriptionResponse(subscriptionJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error creating subscription for customer {customerId}: {ex.Message}");
            return null;
        }
    }

    public async Task<MaxioSubscriptionResponse?> GetSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var url = $"/subscriptions/{subscriptionId}.json";
            var request = CreateRequest(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to get subscription {subscriptionId}: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            var subscriptionJson = json.RootElement.GetProperty("subscription");

            return MapToSubscriptionResponse(subscriptionJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error getting subscription {subscriptionId}: {ex.Message}");
            return null;
        }
    }

    public async Task<List<MaxioSubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var subscriptions = new List<MaxioSubscriptionResponse>();

        try
        {
            var url = $"/customers/{customerId}/subscriptions.json";
            var request = CreateRequest(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to list subscriptions for customer {customerId}: {response.StatusCode}");
                return subscriptions;
            }

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            var subscriptionsArray = json.RootElement.GetProperty("subscriptions");

            foreach (var subJson in subscriptionsArray.EnumerateArray())
            {
                var subscription = MapToSubscriptionResponse(subJson);
                if (subscription != null)
                {
                    subscriptions.Add(subscription);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error listing subscriptions for customer {customerId}: {ex.Message}");
        }

        return subscriptions;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));
        request.Headers.Add("Accept", "application/json");
        return request;
    }

    private MaxioProductResponse? MapToProductResponse(JsonElement productJson)
    {
        if (!productJson.TryGetProperty("id", out var idProp) ||
            !productJson.TryGetProperty("name", out var nameProp) ||
            !productJson.TryGetProperty("handle", out var handleProp))
        {
            return null;
        }

        var priceInCents = 0L;
        if (productJson.TryGetProperty("price_in_cents", out var priceProp))
        {
            priceInCents = priceProp.GetInt64();
        }

        var interval = 1;
        if (productJson.TryGetProperty("interval", out var intervalProp))
        {
            interval = intervalProp.GetInt32();
        }

        var intervalUnit = "month";
        if (productJson.TryGetProperty("interval_unit", out var intervalUnitProp))
        {
            intervalUnit = intervalUnitProp.GetString() ?? "month";
        }

        var description = "";
        if (productJson.TryGetProperty("description", out var descProp))
        {
            description = descProp.GetString() ?? "";
        }

        return new MaxioProductResponse
        {
            Id = idProp.GetInt32(),
            Name = nameProp.GetString() ?? "",
            Handle = handleProp.GetString() ?? "",
            PriceInCents = priceInCents,
            Interval = interval,
            IntervalUnit = intervalUnit,
            Description = description
        };
    }

    private MaxioCustomerResponse MapToCustomerResponse(JsonElement customerJson)
    {
        return new MaxioCustomerResponse
        {
            Id = customerJson.GetProperty("id").GetInt32(),
            FirstName = customerJson.TryGetProperty("first_name", out var fnProp) ? fnProp.GetString() ?? "" : "",
            LastName = customerJson.TryGetProperty("last_name", out var lnProp) ? lnProp.GetString() ?? "" : "",
            Email = customerJson.TryGetProperty("email", out var emailProp) ? emailProp.GetString() ?? "" : ""
        };
    }

    private MaxioSubscriptionResponse MapToSubscriptionResponse(JsonElement subJson)
    {
        var product = null as MaxioProductResponse;
        if (subJson.TryGetProperty("product", out var productProp))
        {
            product = MapToProductResponse(productProp);
        }

        var nextAssessmentAt = null as DateTime?;
        if (subJson.TryGetProperty("next_assessment_at", out var nextProp) && nextProp.ValueKind != JsonValueKind.Null)
        {
            if (DateTime.TryParse(nextProp.GetString(), out var parsed))
            {
                nextAssessmentAt = parsed;
            }
        }

        var currentPeriodEndsAt = null as DateTime?;
        if (subJson.TryGetProperty("current_period_ends_at", out var endsProp) && endsProp.ValueKind != JsonValueKind.Null)
        {
            if (DateTime.TryParse(endsProp.GetString(), out var parsed))
            {
                currentPeriodEndsAt = parsed;
            }
        }

        return new MaxioSubscriptionResponse
        {
            Id = subJson.GetProperty("id").GetInt32(),
            CustomerId = subJson.TryGetProperty("customer_id", out var custProp) ? custProp.GetInt32() : 0,
            State = subJson.TryGetProperty("state", out var stateProp) ? stateProp.GetString() ?? "" : "",
            Product = product,
            CurrentPeriodEndsAt = currentPeriodEndsAt,
            NextAssessmentAt = nextAssessmentAt,
            CreatedAt = DateTime.TryParse(subJson.GetProperty("created_at").GetString(), out var created) ? created : DateTime.UtcNow,
            UpdatedAt = DateTime.TryParse(subJson.GetProperty("updated_at").GetString(), out var updated) ? updated : DateTime.UtcNow
        };
    }
}

