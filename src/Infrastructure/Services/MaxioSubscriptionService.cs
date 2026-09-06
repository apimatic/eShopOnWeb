using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(HttpClient httpClient, MaxioConfiguration config, IAppLogger<MaxioSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync()
    {
        try
        {
            var url = $"{GetBaseUrl()}/product_families/handle:{_config.ProductFamilyHandle}/products.json";
            _logger.LogInformation($"Fetching subscription plans from {url}");

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            using var doc = JsonDocument.Parse(content);
            var plans = new List<SubscriptionPlanDto>();

            var itemsElement = doc.RootElement.GetProperty("items");
            foreach (var item in itemsElement.EnumerateArray())
            {
                var product = item.GetProperty("product");
                plans.Add(new SubscriptionPlanDto
                {
                    Id = product.GetProperty("id").GetInt32(),
                    Handle = product.GetProperty("handle").GetString() ?? string.Empty,
                    Name = product.GetProperty("name").GetString() ?? string.Empty,
                    Description = product.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                    PriceInCents = product.GetProperty("price_in_cents").GetInt64(),
                    Interval = product.GetProperty("interval").GetInt32(),
                    IntervalUnit = product.GetProperty("interval_unit").GetString() ?? string.Empty,
                });
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error fetching subscription plans: {ex.Message}");
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string userEmail, string userName, string productHandle)
    {
        try
        {
            _logger.LogInformation($"Creating subscription for {userEmail} with product {productHandle}");

            // Ensure customer exists (idempotent via reference)
            var customerId = await GetOrCreateCustomerAsync(userEmail, userName);

            // Create subscription
            var url = $"{GetBaseUrl()}/subscriptions.json";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            AddAuthHeader(request);

            var subscriptionPayload = new
            {
                subscription = new
                {
                    product_handle = productHandle,
                    customer_id = customerId
                }
            };

            request.Content = new StringContent(
                JsonSerializer.Serialize(subscriptionPayload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            using var doc = JsonDocument.Parse(content);
            var subscription = doc.RootElement.GetProperty("subscription");

            return ParseSubscriptionDto(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error creating subscription: {ex.Message}");
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userEmail)
    {
        try
        {
            _logger.LogInformation($"Fetching subscriptions for {userEmail}");

            // Get customer ID by reference
            var customerId = await GetCustomerIdByReferenceAsync(userEmail);
            if (customerId == null)
            {
                return new List<SubscriptionDto>();
            }

            var url = $"{GetBaseUrl()}/customers/{customerId}/subscriptions.json";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            using var doc = JsonDocument.Parse(content);
            var subscriptions = new List<SubscriptionDto>();

            var itemsElement = doc.RootElement.GetProperty("subscriptions");
            foreach (var item in itemsElement.EnumerateArray())
            {
                subscriptions.Add(ParseSubscriptionDto(item));
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error fetching user subscriptions: {ex.Message}");
            throw;
        }
    }

    private async Task<int> GetOrCreateCustomerAsync(string email, string name)
    {
        // Try to get existing customer by reference
        var existingId = await GetCustomerIdByReferenceAsync(email);
        if (existingId.HasValue)
        {
            return existingId.Value;
        }

        // Create new customer
        var url = $"{GetBaseUrl()}/customers.json";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        AddAuthHeader(request);

        var nameParts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : "Customer";
        var lastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : "";

        var customerPayload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = email
            }
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(customerPayload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var customer = doc.RootElement.GetProperty("customer");

        return customer.GetProperty("id").GetInt32();
    }

    private async Task<int?> GetCustomerIdByReferenceAsync(string reference)
    {
        try
        {
            // List customers and search by reference
            var url = $"{GetBaseUrl()}/customers.json";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            var customersElement = doc.RootElement.GetProperty("customers");
            foreach (var customer in customersElement.EnumerateArray())
            {
                if (customer.TryGetProperty("reference", out var refProp) &&
                    refProp.GetString() == reference)
                {
                    return customer.GetProperty("id").GetInt32();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error looking up customer by reference: {ex.Message}");
            return null;
        }
    }

    private SubscriptionDto ParseSubscriptionDto(JsonElement subscription)
    {
        var product = subscription.GetProperty("product");

        return new SubscriptionDto
        {
            Id = subscription.GetProperty("id").GetInt32(),
            State = subscription.GetProperty("state").GetString() ?? string.Empty,
            ProductId = product.GetProperty("id").GetInt32(),
            ProductHandle = product.GetProperty("handle").GetString() ?? string.Empty,
            ProductName = product.GetProperty("name").GetString() ?? string.Empty,
            ProductPriceInCents = product.GetProperty("price_in_cents").GetInt64(),
            CustomerId = subscription.GetProperty("customer_id").GetInt32(),
            CurrentPeriodEndsAt = subscription.TryGetProperty("current_period_ends_at", out var cpe) ? cpe.GetString() : null,
            NextAssessmentAt = subscription.TryGetProperty("next_assessment_at", out var naa) ? naa.GetString() : null,
            ActivatedAt = subscription.TryGetProperty("activated_at", out var aa) ? aa.GetString() : null,
        };
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.ApiKey}:X"));
        request.Headers.Add("Authorization", $"Basic {credentials}");
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(_config.BaseUrl))
        {
            return _config.BaseUrl.TrimEnd('/');
        }

        return $"https://{_config.Subdomain}.chargify.com".TrimEnd('/');
    }
}
