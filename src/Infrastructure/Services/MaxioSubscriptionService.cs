using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly IRepository<UserMaxioCustomer> _userMaxioCustomerRepository;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private const string ApiVersion = "/api/v1";

    public MaxioSubscriptionService(
        HttpClient httpClient,
        MaxioConfiguration config,
        IRepository<UserMaxioCustomer> userMaxioCustomerRepository,
        ILogger<MaxioSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _userMaxioCustomerRepository = userMaxioCustomerRepository;
        _logger = logger;

        if (string.IsNullOrEmpty(_config.ApiKey) || string.IsNullOrEmpty(_config.Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete. Ensure MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, " +
                "and MAXIO_DEFAULT_PRODUCT_FAMILY are set in environment variables or appsettings.json");
        }

        var baseUrl = _config.BaseUrl ?? $"https://{_config.Subdomain}.chargify.com";
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.ApiKey}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<MaxioSubscriptionPlan[]> GetPlansAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{ApiVersion}/products.json");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var productsArray = doc.RootElement.GetProperty("products");

            var plans = new List<MaxioSubscriptionPlan>();

            foreach (var productElement in productsArray.EnumerateArray())
            {
                var product = productElement.GetProperty("product");
                var handle = product.GetProperty("handle").GetString();
                var familyHandle = product.GetProperty("product_family").GetProperty("handle").GetString();

                // Only include products from the configured family
                if (familyHandle == _config.ProductFamilyHandle)
                {
                    var pricePointsArray = product.GetProperty("price_points");
                    foreach (var ppElement in pricePointsArray.EnumerateArray())
                    {
                        var pricePoint = ppElement.GetProperty("price_point");
                        if (pricePoint.GetProperty("default").GetBoolean())
                        {
                            var plan = new MaxioSubscriptionPlan
                            {
                                Id = product.GetProperty("id").GetInt32(),
                                Handle = handle,
                                Name = product.GetProperty("name").GetString() ?? "",
                                Description = product.GetProperty("description").GetString() ?? "",
                                Price = (decimal)pricePoint.GetProperty("price").GetDouble(),
                                IntervalUnit = pricePoint.GetProperty("interval").GetInt32(),
                                IntervalUnitText = pricePoint.GetProperty("interval_unit").GetString(),
                            };
                            plan.PriceFormatted = $"${plan.Price:F2}";
                            plans.Add(plan);
                            break;
                        }
                    }
                }
            }

            return plans.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching plans from Maxio");
            throw;
        }
    }

    public async Task<MaxioCustomerSubscription> CreateSubscriptionAsync(string userId, string planHandle)
    {
        try
        {
            // Get or create Maxio customer
            var maxioCustomerId = await GetOrCreateMaxioCustomerAsync(userId);

            // Create subscription
            var subscriptionPayload = new
            {
                subscription = new
                {
                    customer_id = maxioCustomerId,
                    product_handle = planHandle,
                    payment_collection_method = "automatic"
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(subscriptionPayload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync($"{ApiVersion}/subscriptions.json", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Maxio subscription creation failed: {StatusCode} {Error}", response.StatusCode, errorContent);
                throw new InvalidOperationException($"Failed to create subscription: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var subscription = doc.RootElement.GetProperty("subscription");

            return new MaxioCustomerSubscription
            {
                Id = subscription.GetProperty("id").GetInt32(),
                CustomerId = maxioCustomerId,
                State = subscription.GetProperty("state").GetString() ?? "",
                ProductHandle = subscription.GetProperty("product").GetProperty("handle").GetString() ?? "",
                ProductName = subscription.GetProperty("product").GetProperty("name").GetString() ?? "",
                NextAssessmentAt = DateTime.Parse(subscription.GetProperty("next_assessment_at").GetString() ?? DateTime.UtcNow.ToString()),
                CreatedAt = DateTime.Parse(subscription.GetProperty("created_at").GetString() ?? DateTime.UtcNow.ToString()),
                UpdatedAt = DateTime.Parse(subscription.GetProperty("updated_at").GetString() ?? DateTime.UtcNow.ToString()),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId}", userId);
            throw;
        }
    }

    public async Task<MaxioCustomerSubscription[]> GetCustomerSubscriptionsAsync(string userId)
    {
        try
        {
            var spec = new UserMaxioCustomerByApplicationUserIdSpec(userId);
            var maxioCustomer = await _userMaxioCustomerRepository.FirstOrDefaultAsync(spec);

            if (maxioCustomer == null)
            {
                return Array.Empty<MaxioCustomerSubscription>();
            }

            var response = await _httpClient.GetAsync($"{ApiVersion}/customers/{maxioCustomer.MaxioCustomerId}/subscriptions.json");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("subscriptions", out var subscriptionsArray))
            {
                return Array.Empty<MaxioCustomerSubscription>();
            }

            var subscriptions = new List<MaxioCustomerSubscription>();

            foreach (var subElement in subscriptionsArray.EnumerateArray())
            {
                var sub = new MaxioCustomerSubscription
                {
                    Id = subElement.GetProperty("id").GetInt32(),
                    CustomerId = maxioCustomer.MaxioCustomerId,
                    State = subElement.GetProperty("state").GetString() ?? "",
                    ProductHandle = subElement.GetProperty("product").GetProperty("handle").GetString() ?? "",
                    ProductName = subElement.GetProperty("product").GetProperty("name").GetString() ?? "",
                    CreatedAt = DateTime.Parse(subElement.GetProperty("created_at").GetString() ?? DateTime.UtcNow.ToString()),
                    UpdatedAt = DateTime.Parse(subElement.GetProperty("updated_at").GetString() ?? DateTime.UtcNow.ToString()),
                };

                if (subElement.TryGetProperty("next_assessment_at", out var nextElement))
                {
                    var nextStr = nextElement.GetString();
                    if (nextStr != null)
                    {
                        sub.NextAssessmentAt = DateTime.Parse(nextStr);
                    }
                }

                subscriptions.Add(sub);
            }

            return subscriptions.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for user {UserId}", userId);
            throw;
        }
    }

    private async Task<int> GetOrCreateMaxioCustomerAsync(string userId)
    {
        // Check if customer mapping already exists
        var spec = new UserMaxioCustomerByApplicationUserIdSpec(userId);
        var existing = await _userMaxioCustomerRepository.FirstOrDefaultAsync(spec);

        if (existing != null)
        {
            return existing.MaxioCustomerId;
        }

        // Create new customer in Maxio
        var customerPayload = new
        {
            customer = new
            {
                reference = userId,
                first_name = userId.Split('@')[0] // Simple extraction from email/username
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(customerPayload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync($"{ApiVersion}/customers.json", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Maxio customer creation failed: {StatusCode} {Error}", response.StatusCode, errorContent);
            throw new InvalidOperationException($"Failed to create customer: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var customer = doc.RootElement.GetProperty("customer");
        var maxioCustomerId = customer.GetProperty("id").GetInt32();

        // Store the mapping
        var userMaxioCustomer = new UserMaxioCustomer
        {
            ApplicationUserId = userId,
            MaxioCustomerId = maxioCustomerId
        };

        await _userMaxioCustomerRepository.AddAsync(userMaxioCustomer);

        return maxioCustomerId;
    }
}
