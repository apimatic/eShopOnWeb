using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

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
        SetupAuthHeader();
    }

    private void SetupAuthHeader()
    {
        var authString = $"{_config.ApiKey}:X";
        var authBytes = Encoding.ASCII.GetBytes(authString);
        var base64Auth = Convert.ToBase64String(authBytes);
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", base64Auth);
    }

    public async Task<SubscriptionPlanDto> GetPlanAsync(string planHandle)
    {
        try
        {
            var url = $"{_config.GetBaseUrl()}/products/{planHandle}.json";
            _logger.LogInformation("Fetching plan from Maxio: {Url}", url);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("product", out var productElement))
            {
                return ParseProductElement(productElement);
            }

            throw new InvalidOperationException("Invalid response from Maxio API");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching plan {PlanHandle} from Maxio", planHandle);
            throw;
        }
    }

    public async Task<IEnumerable<SubscriptionPlanDto>> GetPlansAsync()
    {
        try
        {
            var url = $"{_config.GetBaseUrl()}/product_families/{_config.ProductFamilyHandle}/products.json";
            _logger.LogInformation("Fetching plans from Maxio: {Url}", url);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var plans = new List<SubscriptionPlanDto>();

            if (root.TryGetProperty("products", out var productsElement) && productsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var productElement in productsElement.EnumerateArray())
                {
                    plans.Add(ParseProductElement(productElement));
                }
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching plans from Maxio");
            throw;
        }
    }

    public async Task<CustomerDto?> GetOrCreateCustomerAsync(string email, string userId)
    {
        try
        {
            var customer = await GetCustomerByEmailAsync(email);
            if (customer != null)
            {
                _logger.LogInformation("Customer {Email} already exists in Maxio with ID {CustomerId}", email, customer.Id);
                return customer;
            }

            _logger.LogInformation("Creating new customer in Maxio: {Email}", email);
            return await CreateCustomerAsync(email, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating customer {Email}", email);
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var url = $"{_config.GetBaseUrl()}/subscriptions.json";
            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "remittance"
                }
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogInformation("Creating subscription in Maxio for customer {CustomerId} on product {ProductHandle}", customerId, productHandle);

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("subscription", out var subscriptionElement))
            {
                return ParseSubscriptionElement(subscriptionElement);
            }

            throw new InvalidOperationException("Invalid response from Maxio API");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<SubscriptionDto?> GetSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var url = $"{_config.GetBaseUrl()}/subscriptions/{subscriptionId}.json";
            _logger.LogInformation("Fetching subscription from Maxio: {SubscriptionId}", subscriptionId);

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }
                response.EnsureSuccessStatusCode();
            }

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("subscription", out var subscriptionElement))
            {
                return ParseSubscriptionElement(subscriptionElement);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription {SubscriptionId} from Maxio", subscriptionId);
            throw;
        }
    }

    public async Task<IEnumerable<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var url = $"{_config.GetBaseUrl()}/customers/{customerId}/subscriptions.json";
            _logger.LogInformation("Fetching subscriptions from Maxio for customer {CustomerId}", customerId);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var subscriptions = new List<SubscriptionDto>();

            if (root.TryGetProperty("subscriptions", out var subscriptionsElement) && subscriptionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var subscriptionElement in subscriptionsElement.EnumerateArray())
                {
                    subscriptions.Add(ParseSubscriptionElement(subscriptionElement));
                }
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    private async Task<CustomerDto?> GetCustomerByEmailAsync(string email)
    {
        try
        {
            var url = $"{_config.GetBaseUrl()}/customers/lookup.json?email={Uri.EscapeDataString(email)}";
            _logger.LogInformation("Looking up customer by email: {Email}", email);

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }
                response.EnsureSuccessStatusCode();
            }

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customerElement))
            {
                return ParseCustomerElement(customerElement);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up customer by email: {Email}", email);
            throw;
        }
    }

    private async Task<CustomerDto> CreateCustomerAsync(string email, string userId)
    {
        try
        {
            var url = $"{_config.GetBaseUrl()}/customers.json";
            var nameParts = email.Split('@')[0].Split('.', StringSplitOptions.RemoveEmptyEntries);
            var firstName = nameParts.Length > 0 ? nameParts[0] : "User";
            var lastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : userId;

            var payload = new
            {
                customer = new
                {
                    email = email,
                    first_name = firstName,
                    last_name = lastName,
                    reference = userId
                }
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customerElement))
            {
                return ParseCustomerElement(customerElement);
            }

            throw new InvalidOperationException("Invalid response from Maxio API");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer in Maxio: {Email}", email);
            throw;
        }
    }

    private SubscriptionPlanDto ParseProductElement(JsonElement productElement)
    {
        return new SubscriptionPlanDto
        {
            Id = GetInt32(productElement, "id"),
            Handle = GetString(productElement, "handle"),
            Name = GetString(productElement, "name"),
            Description = GetString(productElement, "description"),
            Price = GetDecimal(productElement, "price_in_cents") / 100m,
            Interval = GetString(productElement, "interval"),
            IntervalUnit = GetInt32(productElement, "interval_unit")
        };
    }

    private SubscriptionDto ParseSubscriptionElement(JsonElement subscriptionElement)
    {
        return new SubscriptionDto
        {
            Id = GetInt32(subscriptionElement, "id"),
            CustomerId = GetInt32(subscriptionElement, "customer_id"),
            ProductId = GetInt32(subscriptionElement, "product_id"),
            ProductHandle = GetString(subscriptionElement, "product_handle"),
            State = GetString(subscriptionElement, "state"),
            CurrentPeriodAmountInCents = GetDecimal(subscriptionElement, "current_period_balance_in_cents"),
            CurrentPeriodEndsAt = GetDateTime(subscriptionElement, "current_period_ends_at"),
            NextBillingAt = GetDateTime(subscriptionElement, "next_billing_at"),
            ActivatedAt = GetDateTime(subscriptionElement, "activated_at"),
            CreatedAt = GetDateTime(subscriptionElement, "created_at")
        };
    }

    private CustomerDto ParseCustomerElement(JsonElement customerElement)
    {
        return new CustomerDto
        {
            Id = GetInt32(customerElement, "id"),
            Email = GetString(customerElement, "email"),
            FirstName = GetString(customerElement, "first_name"),
            LastName = GetString(customerElement, "last_name")
        };
    }

    private string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null)
        {
            return value.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private int GetInt32(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null)
        {
            return value.GetInt32();
        }
        return 0;
    }

    private decimal GetDecimal(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null)
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDecimal();
            }
        }
        return 0;
    }

    private DateTime? GetDateTime(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null)
        {
            if (value.TryGetDateTime(out var dt))
            {
                return dt;
            }
        }
        return null;
    }
}
