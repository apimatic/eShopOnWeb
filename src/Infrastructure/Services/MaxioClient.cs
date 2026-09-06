using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        SetupHttpClient();
    }

    private void SetupHttpClient()
    {
        var baseUrl = _settings.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
    }

    public async Task<List<ProductDto>> GetProductsByFamilyHandleAsync(string familyHandle)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products.json?family_handle={familyHandle}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            var products = new List<ProductDto>();

            if (jsonDoc.RootElement.TryGetProperty("products", out var productsArray))
            {
                foreach (var productElement in productsArray.EnumerateArray())
                {
                    if (productElement.TryGetProperty("product", out var productObj))
                    {
                        var product = new ProductDto
                        {
                            Id = productObj.GetProperty("id").GetInt32(),
                            Name = productObj.GetProperty("name").GetString() ?? "",
                            Handle = productObj.GetProperty("handle").GetString() ?? "",
                            PriceInCents = productObj.GetProperty("price_in_cents").GetInt64(),
                            Interval = productObj.GetProperty("interval").GetInt32(),
                            IntervalUnit = productObj.GetProperty("interval_unit").GetString() ?? "month",
                            Description = productObj.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                            RequireCreditCard = productObj.TryGetProperty("require_credit_card", out var rcc) ? rcc.GetBoolean() : false
                        };
                        products.Add(product);
                    }
                }
            }

            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products for family: {FamilyHandle}", familyHandle);
            throw;
        }
    }

    public async Task<CustomerDto> GetOrCreateCustomerAsync(string userEmail, string firstName, string lastName, string? reference = null)
    {
        reference ??= userEmail;

        try
        {
            var existing = await GetCustomerByReferenceAsync(reference);
            if (existing != null)
            {
                return existing;
            }
        }
        catch
        {
        }

        return await CreateCustomerAsync(new CreateCustomerRequest
        {
            FirstName = firstName,
            LastName = lastName,
            Email = userEmail,
            Reference = reference
        });
    }

    private async Task<CustomerDto?> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={reference}");
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);

            if (jsonDoc.RootElement.TryGetProperty("customer", out var customerObj))
            {
                return ParseCustomerDto(customerObj);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer by reference: {Reference}", reference);
            return null;
        }
    }

    private async Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request)
    {
        try
        {
            var requestBody = JsonSerializer.Serialize(new { customer = request });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/customers.json", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseContent);

            if (jsonDoc.RootElement.TryGetProperty("customer", out var customerObj))
            {
                return ParseCustomerDto(customerObj);
            }

            throw new InvalidOperationException("Invalid response from Maxio when creating customer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer: {Email}", request.Email);
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var requestBody = JsonSerializer.Serialize(new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle
                }
            });

            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/subscriptions.json", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseContent);

            if (jsonDoc.RootElement.TryGetProperty("subscription", out var subscriptionObj))
            {
                return ParseSubscriptionDto(subscriptionObj);
            }

            throw new InvalidOperationException("Invalid response from Maxio when creating subscription");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer {CustomerId}", customerId);
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
            var jsonDoc = JsonDocument.Parse(content);
            var subscriptions = new List<SubscriptionDto>();

            if (jsonDoc.RootElement.TryGetProperty("subscriptions", out var subscriptionsArray))
            {
                foreach (var subElement in subscriptionsArray.EnumerateArray())
                {
                    subscriptions.Add(ParseSubscriptionDto(subElement));
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

    private static CustomerDto ParseCustomerDto(JsonElement customerObj)
    {
        return new CustomerDto
        {
            Id = customerObj.GetProperty("id").GetInt32(),
            FirstName = customerObj.GetProperty("first_name").GetString() ?? "",
            LastName = customerObj.GetProperty("last_name").GetString() ?? "",
            Email = customerObj.GetProperty("email").GetString() ?? "",
            Reference = customerObj.TryGetProperty("reference", out var @ref) ? @ref.GetString() : null,
            CreatedAt = customerObj.GetProperty("created_at").GetDateTime(),
            UpdatedAt = customerObj.TryGetProperty("updated_at", out var updated) ? updated.GetDateTime() : null
        };
    }

    private static SubscriptionDto ParseSubscriptionDto(JsonElement subObj)
    {
        var state = subObj.TryGetProperty("state", out var stateObj) ? stateObj.GetString() : "unknown";
        var nextBillingAt = DateTime.MinValue;
        if (subObj.TryGetProperty("next_billing_at", out var nextBilling))
        {
            if (nextBilling.ValueKind == JsonValueKind.String && DateTime.TryParse(nextBilling.GetString(), out var dt))
            {
                nextBillingAt = dt;
            }
        }

        var product = new ProductDto { Handle = "", Name = "", PriceInCents = 0, Id = 0, Interval = 0, IntervalUnit = "month" };
        if (subObj.TryGetProperty("product", out var productObj))
        {
            product = new ProductDto
            {
                Id = productObj.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                Name = productObj.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Handle = productObj.TryGetProperty("handle", out var handle) ? handle.GetString() ?? "" : "",
                PriceInCents = productObj.TryGetProperty("price_in_cents", out var price) ? price.GetInt64() : 0,
                Interval = productObj.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 0,
                IntervalUnit = productObj.TryGetProperty("interval_unit", out var unit) ? unit.GetString() ?? "month" : "month"
            };
        }

        return new SubscriptionDto
        {
            Id = subObj.GetProperty("id").GetInt32(),
            CustomerId = subObj.GetProperty("customer_id").GetInt32(),
            ProductHandle = subObj.TryGetProperty("product_handle", out var ph) ? ph.GetString() ?? "" : "",
            State = state ?? "unknown",
            NextBillingAt = nextBillingAt,
            CreatedAt = subObj.GetProperty("created_at").GetDateTime(),
            UpdatedAt = subObj.TryGetProperty("updated_at", out var upd) ? upd.GetDateTime() : null,
            Product = product
        };
    }
}

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public string? Description { get; set; }
    public bool RequireCreditCard { get; set; }

    public decimal Price => PriceInCents / 100m;
}

public class CustomerDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string State { get; set; } = "";
    public DateTime NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public ProductDto Product { get; set; } = new();
}

public class CreateCustomerRequest
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Reference { get; set; }
}
