using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<MaxioSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _apiKey = configuration["Maxio:ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey is not configured");
        var subdomain = configuration["Maxio:Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain is not configured");
        var baseUrlOverride = configuration["Maxio:BaseUrl"];

        if (!string.IsNullOrEmpty(baseUrlOverride))
        {
            _baseUrl = baseUrlOverride.TrimEnd('/');
        }
        else
        {
            _baseUrl = $"https://{subdomain}.chargify.com";
        }

        SetupHttpClient();
    }

    private void SetupHttpClient()
    {
        var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiKey}:x"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authString}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<MaxioProduct[]> GetProductsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/products.json");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();

            using var jsonDoc = JsonDocument.Parse(content);
            var productsArray = jsonDoc.RootElement;

            var products = new List<MaxioProduct>();
            foreach (var productElement in productsArray.EnumerateArray())
            {
                var product = new MaxioProduct
                {
                    Id = productElement.GetProperty("id").GetInt32(),
                    Handle = productElement.GetProperty("handle").GetString() ?? "",
                    Name = productElement.GetProperty("name").GetString() ?? "",
                    Description = productElement.GetProperty("description").GetString() ?? "",
                    Price = productElement.TryGetProperty("price_in_cents", out var priceElement)
                        ? priceElement.GetInt32() / 100m
                        : 0
                };
                products.Add(product);
            }

            return products.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting products from Maxio");
            throw;
        }
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string userId, string email)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(userId)}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(content);
                var customerElement = jsonDoc.RootElement.GetProperty("customer");

                return new MaxioCustomer
                {
                    Id = customerElement.GetProperty("id").GetInt32().ToString(),
                    Email = customerElement.GetProperty("email").GetString() ?? "",
                    FirstName = customerElement.GetProperty("first_name").GetString() ?? "",
                    LastName = customerElement.GetProperty("last_name").GetString()
                };
            }

            return await CreateCustomerAsync(userId, email);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return await CreateCustomerAsync(userId, email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating customer in Maxio");
            throw;
        }
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(string userId, string email)
    {
        try
        {
            var firstNamePart = email.Split('@')[0];
            var payload = new
            {
                customer = new
                {
                    first_name = firstNamePart,
                    last_name = "Subscriber",
                    email = email,
                    reference = userId
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/customers.json", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(responseContent);
            var customerElement = jsonDoc.RootElement.GetProperty("customer");

            return new MaxioCustomer
            {
                Id = customerElement.GetProperty("id").GetInt32().ToString(),
                Email = customerElement.GetProperty("email").GetString() ?? "",
                FirstName = customerElement.GetProperty("first_name").GetString() ?? "",
                LastName = customerElement.GetProperty("last_name").GetString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer in Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string maxioCustomerId, int maxioProductId)
    {
        try
        {
            var payload = new
            {
                subscription = new
                {
                    customer_id = int.Parse(maxioCustomerId),
                    product_id = maxioProductId
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/subscriptions.json", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(responseContent);
            var subscriptionElement = jsonDoc.RootElement.GetProperty("subscription");

            return ParseSubscriptionElement(subscriptionElement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio for customer {CustomerId}", maxioCustomerId);
            throw;
        }
    }

    public async Task<MaxioSubscription[]> GetCustomerSubscriptionsAsync(string maxioCustomerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/customers/{maxioCustomerId}/subscriptions.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(content);
            var subscriptionsArray = jsonDoc.RootElement;

            var subscriptions = new List<MaxioSubscription>();
            foreach (var subscriptionElement in subscriptionsArray.EnumerateArray())
            {
                subscriptions.Add(ParseSubscriptionElement(subscriptionElement));
            }

            return subscriptions.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions from Maxio for customer {CustomerId}", maxioCustomerId);
            throw;
        }
    }

    private MaxioSubscription ParseSubscriptionElement(JsonElement element)
    {
        var productHandle = "";
        if (element.TryGetProperty("product_handle", out var handleElement))
        {
            productHandle = handleElement.GetString() ?? "";
        }

        var currentPrice = 0m;
        if (element.TryGetProperty("monthly_recurring_revenue", out var mrrElement))
        {
            currentPrice = mrrElement.GetDecimal();
        }

        return new MaxioSubscription
        {
            Id = element.GetProperty("id").GetInt32(),
            CustomerId = element.GetProperty("customer_id").GetInt32().ToString(),
            ProductId = element.GetProperty("product_id").GetInt32(),
            ProductHandle = productHandle,
            State = element.GetProperty("state").GetString() ?? "",
            CurrentPrice = currentPrice,
            NextBillingDate = element.TryGetProperty("next_billing_at", out var nextBillingElement) &&
                             !nextBillingElement.ValueKind.Equals(JsonValueKind.Null)
                ? DateTime.Parse(nextBillingElement.GetString() ?? "")
                : null,
            CreatedAt = DateTime.Parse(element.GetProperty("created_at").GetString() ?? ""),
            UpdatedAt = DateTime.Parse(element.GetProperty("updated_at").GetString() ?? "")
        };
    }
}
