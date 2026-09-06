using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MaxioBillingService(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        // Configure default headers for Maxio API
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:x"))
        );
        _httpClient.BaseAddress = new Uri(settings.GetBaseUrl());
    }

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync(string productFamilyHandle)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var plans = new List<SubscriptionPlanDto>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("product", out var productJson))
                    {
                        var plan = ParseProduct(productJson);
                        // Filter by product family handle if needed
                        if (productJson.TryGetProperty("product_family", out var familyJson))
                        {
                            if (familyJson.TryGetProperty("handle", out var familyHandle))
                            {
                                if (familyHandle.GetString() == productFamilyHandle)
                                {
                                    plans.Add(plan);
                                }
                            }
                        }
                    }
                }
            }

            _logger.LogInformation($"Retrieved {plans.Count} subscription plans from Maxio");
            return plans;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"Error retrieving subscription plans: {ex.Message}");
            throw;
        }
    }

    public async Task<SubscriptionPlanDto?> GetPlanByHandleAsync(string handle)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("product", out var productJson))
                    {
                        if (productJson.TryGetProperty("handle", out var productHandle))
                        {
                            if (productHandle.GetString() == handle)
                            {
                                return ParseProduct(productJson);
                            }
                        }
                    }
                }
            }

            _logger.LogWarning($"Product with handle '{handle}' not found in Maxio");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"Error retrieving product by handle: {ex.Message}");
            throw;
        }
    }

    public async Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, string planHandle, string? customerReference = null)
    {
        try
        {
            customerReference ??= userId;

            // Check if customer exists, if not create one
            var customer = await FindOrCreateCustomerAsync(userId, customerReference);
            if (customer == null)
            {
                _logger.LogError($"Failed to create or find customer for user {userId}");
                return null;
            }

            // Create subscription
            var subscriptionPayload = new
            {
                subscription = new
                {
                    product_handle = planHandle,
                    customer_id = customer.Id,
                    payment_collection_method = "remittance"
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(subscriptionPayload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("/subscriptions.json", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error creating subscription: {response.StatusCode} - {errorContent}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("subscription", out var subscriptionJson))
            {
                var subscription = ParseSubscription(subscriptionJson);
                _logger.LogInformation($"Successfully created subscription {subscription.Id} for user {userId}");
                return subscription;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating subscription: {ex.Message}");
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(string customerReference)
    {
        try
        {
            // Find customer by reference
            var customer = await FindCustomerByReferenceAsync(customerReference);
            if (customer == null)
            {
                _logger.LogWarning($"Customer with reference '{customerReference}' not found");
                return new List<SubscriptionDto>();
            }

            var response = await _httpClient.GetAsync($"/customers/{customer.Id}/subscriptions.json");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Error retrieving subscriptions: {response.StatusCode}");
                return new List<SubscriptionDto>();
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var subscriptions = new List<SubscriptionDto>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    subscriptions.Add(ParseSubscription(element));
                }
            }

            _logger.LogInformation($"Retrieved {subscriptions.Count} subscriptions for customer {customerReference}");
            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error retrieving customer subscriptions: {ex.Message}");
            throw;
        }
    }

    private async Task<CustomerDto?> FindOrCreateCustomerAsync(string userId, string reference)
    {
        // First, try to find existing customer
        var existingCustomer = await FindCustomerByReferenceAsync(reference);
        if (existingCustomer != null)
        {
            return existingCustomer;
        }

        try
        {
            var customerPayload = new
            {
                customer = new
                {
                    first_name = "User",
                    last_name = userId.Substring(0, Math.Min(20, userId.Length)),
                    email = $"{userId}@eshop.local",
                    reference = reference
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(customerPayload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("/customers.json", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error creating customer: {response.StatusCode} - {errorContent}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("customer", out var customerJson))
            {
                return ParseCustomer(customerJson);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating customer: {ex.Message}");
            throw;
        }
    }

    private async Task<CustomerDto?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("customer", out var customerJson))
            {
                return ParseCustomer(customerJson);
            }

            return null;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error finding customer by reference: {ex.Message}");
            return null;
        }
    }

    private static SubscriptionPlanDto ParseProduct(JsonElement productJson)
    {
        return new SubscriptionPlanDto
        {
            Id = productJson.GetProperty("id").GetInt32(),
            Handle = productJson.GetProperty("handle").GetString() ?? string.Empty,
            Name = productJson.GetProperty("name").GetString() ?? string.Empty,
            PriceInCents = (decimal)(productJson.TryGetProperty("price_in_cents", out var price) ? price.GetInt64() : 0),
            Interval = productJson.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 1,
            IntervalUnit = productJson.TryGetProperty("interval_unit", out var unit) ? unit.GetString() ?? "month" : "month",
            Description = productJson.GetProperty("description").GetString() ?? string.Empty
        };
    }

    private static SubscriptionDto ParseSubscription(JsonElement subscriptionJson)
    {
        var dto = new SubscriptionDto
        {
            Id = subscriptionJson.GetProperty("id").GetInt32(),
            State = subscriptionJson.GetProperty("state").GetString() ?? string.Empty,
            CurrentPeriodEndsAt = ParseDateTime(subscriptionJson, "current_period_ends_at"),
            NextAssessmentAt = ParseDateTime(subscriptionJson, "next_assessment_at"),
            ActivatedAt = ParseDateTime(subscriptionJson, "activated_at"),
            CreatedAt = ParseDateTime(subscriptionJson, "created_at")
        };

        if (subscriptionJson.TryGetProperty("product_id", out var productId))
        {
            dto.ProductId = productId.GetInt32();
        }

        if (subscriptionJson.TryGetProperty("product_handle", out var productHandle))
        {
            dto.ProductHandle = productHandle.GetString();
        }

        if (subscriptionJson.TryGetProperty("customer", out var customerJson))
        {
            dto.Customer = ParseCustomer(customerJson);
        }

        if (subscriptionJson.TryGetProperty("product", out var productJson))
        {
            dto.Product = new ProductDto
            {
                Id = productJson.GetProperty("id").GetInt32(),
                Handle = productJson.GetProperty("handle").GetString() ?? string.Empty,
                Name = productJson.GetProperty("name").GetString() ?? string.Empty,
                PriceInCents = productJson.TryGetProperty("price_in_cents", out var price) ? price.GetInt64() : 0,
                Interval = productJson.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 1,
                IntervalUnit = productJson.TryGetProperty("interval_unit", out var unit) ? unit.GetString() ?? "month" : "month"
            };
        }

        return dto;
    }

    private static CustomerDto ParseCustomer(JsonElement customerJson)
    {
        return new CustomerDto
        {
            Id = customerJson.GetProperty("id").GetInt32(),
            Reference = customerJson.TryGetProperty("reference", out var reference) ? reference.GetString() ?? string.Empty : string.Empty,
            FirstName = customerJson.GetProperty("first_name").GetString() ?? string.Empty,
            LastName = customerJson.GetProperty("last_name").GetString() ?? string.Empty,
            Email = customerJson.GetProperty("email").GetString() ?? string.Empty
        };
    }

    private static DateTime? ParseDateTime(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var dateStr = value.GetString();
                if (DateTime.TryParse(dateStr, out var dt))
                {
                    return dt;
                }
            }
        }
        return null;
    }
}
