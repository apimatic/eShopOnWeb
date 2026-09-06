using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IConfiguration configuration, ILogger<MaxioSubscriptionService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;

        var maxioConfig = configuration.GetSection("Maxio");
        var apiKey = maxioConfig["ApiKey"];
        var subdomain = maxioConfig["Subdomain"];
        var baseUrl = maxioConfig["BaseUrl"];
        var productFamilyHandle = maxioConfig["ProductFamilyHandle"];

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(subdomain) || string.IsNullOrEmpty(productFamilyHandle))
            throw new InvalidOperationException("Maxio configuration is incomplete. Check Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle settings.");

        _apiKey = apiKey;
        _productFamilyHandle = productFamilyHandle;

        if (!string.IsNullOrEmpty(baseUrl))
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }
        else
        {
            _baseUrl = $"https://{subdomain}.chargify.com";
        }

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{apiKey}:x"))}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync()
    {
        try
        {
            var url = $"{_baseUrl}/product_families/{_productFamilyHandle}/products.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var products = doc.RootElement.GetProperty("products");

            var plans = new List<SubscriptionPlanDto>();
            foreach (var product in products.EnumerateArray())
            {
                var handle = product.TryGetProperty("handle", out var h) ? h.GetString() : string.Empty;
                var name = product.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
                var id = product.TryGetProperty("id", out var i) ? i.GetInt32() : 0;
                var description = product.TryGetProperty("description", out var d) ? d.GetString() : string.Empty;

                plans.Add(new SubscriptionPlanDto
                {
                    Id = id,
                    Handle = handle,
                    Name = name,
                    Price = 0m,
                    Description = description,
                });
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching available plans from Maxio");
            throw;
        }
    }

    public async Task<CustomerSubscriptionDto?> CreateSubscriptionAsync(string userId, string userEmail, string firstName, string lastName, string planHandle)
    {
        try
        {
            var customer = await GetOrCreateCustomerAsync(userEmail, firstName, lastName);
            if (!customer.HasValue)
            {
                _logger.LogError("Failed to create or retrieve customer for email {Email}", userEmail);
                return null;
            }

            var (customerId, _) = customer.Value;
            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = planHandle
                }
            };

            var url = $"{_baseUrl}/subscriptions.json";
            var response = await _httpClient.PostAsJsonAsync(url, payload);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var subscription = doc.RootElement.GetProperty("subscription");

            var subscriptionId = subscription.TryGetProperty("id", out var id) ? id.GetInt32() : 0;
            var state = subscription.TryGetProperty("state", out var s) ? s.GetString() : "active";

            return new CustomerSubscriptionDto
            {
                Id = subscriptionId,
                CustomerId = customerId,
                UserId = userId,
                ProductHandle = planHandle,
                Status = state,
                Price = 0m,
                CreatedAt = DateTime.UtcNow,
                NextBillingDate = null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId} with plan {PlanHandle}", userId, planHandle);
            throw;
        }
    }

    public async Task<List<CustomerSubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        try
        {
            var subscriptions = new List<CustomerSubscriptionDto>();
            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for user {UserId}", userId);
            throw;
        }
    }

    private async Task<(int Id, string Email)?> GetOrCreateCustomerAsync(string email, string firstName, string lastName)
    {
        try
        {
            // Try to find existing customer
            var listUrl = $"{_baseUrl}/customers.json?email={Uri.EscapeDataString(email)}";
            var listResponse = await _httpClient.GetAsync(listUrl);
            if (listResponse.IsSuccessStatusCode)
            {
                var json = await listResponse.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("customers", out var customers))
                {
                    var customerArray = customers.EnumerateArray();
                    if (customerArray.MoveNext())
                    {
                        var customer = customerArray.Current;
                        var customerId = customer.TryGetProperty("id", out var id) ? id.GetInt32() : 0;
                        if (customerId > 0)
                        {
                            return (customerId, email);
                        }
                    }
                }
            }

            // Create new customer
            var payload = new
            {
                customer = new
                {
                    email = email,
                    first_name = firstName,
                    last_name = lastName
                }
            };

            var createUrl = $"{_baseUrl}/customers.json";
            var response = await _httpClient.PostAsJsonAsync(createUrl, payload);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var doc2 = JsonDocument.Parse(responseJson);
            var newCustomer = doc2.RootElement.GetProperty("customer");
            var newCustomerId = newCustomer.TryGetProperty("id", out var nid) ? nid.GetInt32() : 0;

            return (newCustomerId, email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating customer for email {Email}", email);
            throw;
        }
    }
}

internal class MaxioCustomer
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
}
