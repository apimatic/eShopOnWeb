using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _productFamilyHandle;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<SubscriptionPlan> _planRepository;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(
        HttpClient httpClient,
        IConfiguration configuration,
        IRepository<Subscription> subscriptionRepository,
        IRepository<SubscriptionPlan> planRepository,
        ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _subscriptionRepository = subscriptionRepository;
        _planRepository = planRepository;

        _apiKey = configuration["Maxio:ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey is not configured");
        var subdomain = configuration["Maxio:Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain is not configured");
        _productFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured");

        var baseUrl = configuration["Maxio:BaseUrl"];
        if (!string.IsNullOrEmpty(baseUrl))
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }
        else
        {
            _baseUrl = $"https://{subdomain}.chargify.com";
        }

        var authHeader = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_apiKey}:x"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authHeader}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<List<SubscriptionPlan>> GetAvailablePlansAsync()
    {
        try
        {
            var url = $"{_baseUrl}/products.json";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to get products: {response.StatusCode}");
                return new List<SubscriptionPlan>();
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var plans = new List<SubscriptionPlan>();
            if (root.TryGetProperty("products", out var productsElement))
            {
                foreach (var product in productsElement.EnumerateArray())
                {
                    var plan = new SubscriptionPlan
                    {
                        MaxioPlanId = product.GetProperty("id").GetInt32(),
                        Name = product.GetProperty("name").GetString() ?? "",
                        Handle = product.GetProperty("handle").GetString() ?? "",
                        Description = product.TryGetProperty("description", out var desc) ? (desc.GetString() ?? "") : "",
                        PriceInCents = product.GetProperty("price_in_cents").GetDecimal(),
                        Currency = "USD",
                        Interval = product.GetProperty("interval_unit").GetString() ?? "",
                        IntervalUnit = product.GetProperty("interval").GetInt32()
                    };
                    plans.Add(plan);
                }
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error getting available plans: {ex.Message}");
            return new List<SubscriptionPlan>();
        }
    }

    public async Task<Subscription> CreateSubscriptionAsync(string userId, string userEmail, string planHandle)
    {
        try
        {
            await EnsureCustomerExistsAsync(userId, userEmail);

            var customerId = await GetCustomerIdAsync(userEmail);
            if (string.IsNullOrEmpty(customerId))
            {
                throw new InvalidOperationException($"Could not create or retrieve customer for {userEmail}");
            }

            var subscriptionRequest = new
            {
                subscription = new
                {
                    product_handle = planHandle,
                    customer_id = int.Parse(customerId),
                    payment_collection_method = "remittance"
                }
            };

            var url = $"{_baseUrl}/subscriptions.json";
            var content = JsonContent.Create(subscriptionRequest);
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning($"Failed to create subscription: {response.StatusCode} - {errorContent}");
                throw new InvalidOperationException($"Failed to create subscription: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;
            var subscription = root.GetProperty("subscription");

            decimal priceInCents = 0;
            if (subscription.TryGetProperty("current_period_ends_at", out var priceElement))
            {
                try
                {
                    priceInCents = priceElement.GetDecimal();
                }
                catch
                {
                    // use default if parsing fails
                }
            }

            var dbSubscription = new Subscription(
                userId,
                subscription.GetProperty("id").GetInt32(),
                subscription.GetProperty("customer_id").GetString() ?? "",
                planHandle,
                subscription.GetProperty("state").GetString() ?? "",
                subscription.TryGetProperty("next_billing_at", out var nextBillingElement) &&
                    !string.IsNullOrEmpty(nextBillingElement.GetString())
                    ? DateTime.Parse(nextBillingElement.GetString() ?? "")
                    : (DateTime?)null,
                priceInCents
            );

            await _subscriptionRepository.AddAsync(dbSubscription);

            return dbSubscription;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error creating subscription: {ex.Message}");
            throw;
        }
    }

    public async Task<List<Subscription>> GetUserSubscriptionsAsync(string userId)
    {
        try
        {
            var subscriptions = await _subscriptionRepository.ListAsync();
            return subscriptions.Where(s => s.UserId == userId).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error getting user subscriptions: {ex.Message}");
            return new List<Subscription>();
        }
    }

    public async Task<bool> EnsureCustomerExistsAsync(string userId, string userEmail)
    {
        try
        {
            var customerId = await GetCustomerIdAsync(userEmail);
            if (!string.IsNullOrEmpty(customerId))
            {
                return true;
            }

            var customerRequest = new
            {
                customer = new
                {
                    first_name = userEmail.Split('@')[0],
                    last_name = "User",
                    email = userEmail,
                    reference = userId
                }
            };

            var url = $"{_baseUrl}/customers.json";
            var content = JsonContent.Create(customerRequest);
            var response = await _httpClient.PostAsync(url, content);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error ensuring customer exists: {ex.Message}");
            return false;
        }
    }

    private async Task<string?> GetCustomerIdAsync(string userEmail)
    {
        try
        {
            var url = $"{_baseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(userEmail)}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;
            var customer = root.GetProperty("customer");
            return customer.GetProperty("id").GetInt32().ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error looking up customer: {ex.Message}");
            return null;
        }
    }
}
