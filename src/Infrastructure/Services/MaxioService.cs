using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, IConfiguration configuration, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = new MaxioConfiguration();

        var section = configuration.GetSection("Maxio");
        _config.ApiKey = section["ApiKey"] ?? "";
        _config.Subdomain = section["Subdomain"] ?? "";
        _config.ProductFamilyHandle = section["ProductFamilyHandle"] ?? "";
        _config.BaseUrl = section["BaseUrl"];
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(_config.BaseUrl))
        {
            return _config.BaseUrl.TrimEnd('/');
        }
        return $"https://{_config.Subdomain}.maxio.com";
    }

    private string GetAuthHeader()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:"));
        return $"Basic {credentials}";
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync()
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var url = $"{baseUrl}/product_families/handle:{_config.ProductFamilyHandle}/products.json";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", GetAuthHeader());

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var root = doc.RootElement;

            var plans = new List<SubscriptionPlanDto>();

            if (root.TryGetProperty("items", out var itemsElement))
            {
                foreach (var item in itemsElement.EnumerateArray())
                {
                    if (item.TryGetProperty("product", out var productElement))
                    {
                        var plan = new SubscriptionPlanDto
                        {
                            Handle = productElement.GetProperty("handle").GetString() ?? "",
                            Name = productElement.GetProperty("name").GetString() ?? "",
                            Description = productElement.GetProperty("description").GetString() ?? "",
                            PriceInDollars = productElement.GetProperty("price_in_cents").GetInt64() / 100m
                        };
                        plans.Add(plan);
                    }
                }
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans from Maxio");
            throw;
        }
    }

    public async Task<int?> GetOrCreateMaxioCustomerAsync(string userId, string firstName, string lastName, string email)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var customerReference = $"eshop-{userId}";

            var createCustomerPayload = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = customerReference
                }
            };

            var jsonContent = System.Text.Json.JsonSerializer.Serialize(createCustomerPayload);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/customers.json")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", GetAuthHeader());

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create/get Maxio customer: {StatusCode}, {Content}", response.StatusCode, errorContent);
                if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity && errorContent.Contains("reference"))
                {
                    return await GetCustomerByReferenceAsync(customerReference);
                }
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var customerId = doc.RootElement.GetProperty("customer").GetProperty("id").GetInt32();
            return customerId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/getting Maxio customer");
            throw;
        }
    }

    private async Task<int?> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var url = $"{baseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", GetAuthHeader());

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var customerId = doc.RootElement.GetProperty("customer").GetProperty("id").GetInt32();
            return customerId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up Maxio customer by reference");
            return null;
        }
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(string userId, string firstName, string lastName, string email, string planHandle)
    {
        try
        {
            var customerId = await GetOrCreateMaxioCustomerAsync(userId, firstName, lastName, email);
            if (customerId == null)
            {
                throw new InvalidOperationException("Failed to create or get Maxio customer");
            }

            var baseUrl = GetBaseUrl();
            var createSubscriptionPayload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = planHandle,
                    payment_collection_method = "invoice"
                }
            };

            var jsonContent = System.Text.Json.JsonSerializer.Serialize(createSubscriptionPayload);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/subscriptions.json")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", GetAuthHeader());

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var subscriptionElement = doc.RootElement.GetProperty("subscription");

            var subscription = new MaxioSubscriptionDto
            {
                Id = subscriptionElement.GetProperty("id").GetInt32(),
                State = subscriptionElement.GetProperty("state").GetString() ?? "",
                PlanHandle = planHandle,
                PlanName = subscriptionElement.GetProperty("product").GetProperty("name").GetString() ?? ""
            };

            if (subscriptionElement.TryGetProperty("next_billing_at", out var nextBillingElement) &&
                nextBillingElement.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                if (DateTime.TryParse(nextBillingElement.GetString(), out var nextBilling))
                {
                    subscription.NextBillingDate = nextBilling;
                }
            }

            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Maxio subscription");
            throw;
        }
    }

    public async Task<List<MaxioSubscriptionDto>> GetCustomerSubscriptionsAsync(int maxioCustomerId)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var url = $"{baseUrl}/customers/{maxioCustomerId}/subscriptions.json";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", GetAuthHeader());

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var root = doc.RootElement;

            var subscriptions = new List<MaxioSubscriptionDto>();

            if (root.TryGetProperty("subscriptions", out var subscriptionsElement))
            {
                foreach (var sub in subscriptionsElement.EnumerateArray())
                {
                    var productHandle = "";
                    var productName = "";

                    if (sub.TryGetProperty("product", out var productElement))
                    {
                        productHandle = productElement.GetProperty("handle").GetString() ?? "";
                        productName = productElement.GetProperty("name").GetString() ?? "";
                    }

                    var subscription = new MaxioSubscriptionDto
                    {
                        Id = sub.GetProperty("id").GetInt32(),
                        State = sub.GetProperty("state").GetString() ?? "",
                        PlanHandle = productHandle,
                        PlanName = productName
                    };

                    if (sub.TryGetProperty("next_billing_at", out var nextBillingElement) &&
                        nextBillingElement.ValueKind != System.Text.Json.JsonValueKind.Null)
                    {
                        if (DateTime.TryParse(nextBillingElement.GetString(), out var nextBilling))
                        {
                            subscription.NextBillingDate = nextBilling;
                        }
                    }

                    subscriptions.Add(subscription);
                }
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Maxio customer subscriptions");
            throw;
        }
    }
}
