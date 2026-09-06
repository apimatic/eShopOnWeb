using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey must be configured");
        }

        var baseUrl = _options.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);
        var authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authHeader}");
    }

    public async Task<MaxioProductDto[]> ListProductsAsync(string familyHandle)
    {
        try
        {
            var url = $"/products.json?product_family_handle={familyHandle}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var products = new List<MaxioProductDto>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var product = ParseProduct(element);
                    if (product != null)
                    {
                        products.Add(product);
                    }
                }
            }

            return products.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products from Maxio");
            throw;
        }
    }

    public async Task<MaxioCustomerDto> GetOrCreateCustomerAsync(string customerId, string email, string firstName, string lastName)
    {
        try
        {
            var existingCustomer = await GetCustomerByReferenceAsync(customerId);
            if (existingCustomer != null)
            {
                return existingCustomer;
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
        }

        var customer = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = customerId
            }
        };

        var json = JsonSerializer.Serialize(customer);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/customers.json", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        return ParseCustomerResponse(responseJson);
    }

    public async Task<MaxioCustomerDto> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var url = $"/customers.json?reference={Uri.EscapeDataString(reference)}";
            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new HttpRequestException("Customer not found", null, System.Net.HttpStatusCode.NotFound);
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var customer = ParseCustomer(doc.RootElement[0]);
                if (customer != null)
                {
                    return customer;
                }
            }

            throw new HttpRequestException("Customer not found", null, System.Net.HttpStatusCode.NotFound);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer from Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request)
    {
        try
        {
            var subscription = new { subscription = request };
            var json = JsonSerializer.Serialize(subscription);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            return ParseSubscriptionResponse(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscriptionDto[]> ListSubscriptionsAsync(string customerId)
    {
        try
        {
            var url = $"/subscriptions.json?customer_id={customerId}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var subscriptions = new List<MaxioSubscriptionDto>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var subscription = ParseSubscription(element);
                    if (subscription != null)
                    {
                        subscriptions.Add(subscription);
                    }
                }
            }

            return subscriptions.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions from Maxio");
            throw;
        }
    }

    private MaxioProductDto? ParseProduct(JsonElement element)
    {
        try
        {
            if (element.TryGetProperty("product", out var productElement))
            {
                return new MaxioProductDto
                {
                    Id = productElement.GetProperty("id").GetInt32(),
                    Name = productElement.GetProperty("name").GetString(),
                    Handle = productElement.GetProperty("handle").GetString(),
                    Description = productElement.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                    PriceInCents = productElement.GetProperty("price_in_cents").GetInt32(),
                    Interval = productElement.GetProperty("interval").GetInt32(),
                    IntervalUnit = productElement.GetProperty("interval_unit").GetString(),
                    RequiresCreditCard = productElement.GetProperty("require_credit_card").GetBoolean()
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing product");
        }

        return null;
    }

    private MaxioCustomerDto ParseCustomerResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var customerElement = doc.RootElement.GetProperty("customer");
        return ParseCustomer(customerElement);
    }

    private MaxioCustomerDto? ParseCustomer(JsonElement customerElement)
    {
        try
        {
            return new MaxioCustomerDto
            {
                Id = customerElement.GetProperty("id").GetInt32(),
                Email = customerElement.GetProperty("email").GetString(),
                FirstName = customerElement.GetProperty("first_name").GetString(),
                LastName = customerElement.GetProperty("last_name").GetString(),
                Reference = customerElement.TryGetProperty("reference", out var refElement) ? refElement.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing customer");
            return null;
        }
    }

    private MaxioSubscriptionDto ParseSubscriptionResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var subscriptionElement = doc.RootElement.GetProperty("subscription");
        return ParseSubscription(subscriptionElement);
    }

    private MaxioSubscriptionDto? ParseSubscription(JsonElement element)
    {
        try
        {
            if (element.TryGetProperty("subscription", out var subscriptionElement))
            {
                return new MaxioSubscriptionDto
                {
                    Id = subscriptionElement.GetProperty("id").GetInt32(),
                    State = subscriptionElement.GetProperty("state").GetString(),
                    CurrentPeriodEndsAt = subscriptionElement.TryGetProperty("current_period_ends_at", out var cpea) ? cpea.GetString() : null,
                    NextAssessmentAt = subscriptionElement.TryGetProperty("next_assessment_at", out var naa) ? naa.GetString() : null,
                    ActivatedAt = subscriptionElement.TryGetProperty("activated_at", out var aa) ? aa.GetString() : null,
                    CreatedAt = subscriptionElement.TryGetProperty("created_at", out var ca) ? ca.GetString() : null,
                    Product = ParseSubscriptionProduct(subscriptionElement),
                    Customer = ParseSubscriptionCustomer(subscriptionElement)
                };
            }
            else
            {
                return new MaxioSubscriptionDto
                {
                    Id = element.GetProperty("id").GetInt32(),
                    State = element.GetProperty("state").GetString(),
                    CurrentPeriodEndsAt = element.TryGetProperty("current_period_ends_at", out var cpea) ? cpea.GetString() : null,
                    NextAssessmentAt = element.TryGetProperty("next_assessment_at", out var naa) ? naa.GetString() : null,
                    ActivatedAt = element.TryGetProperty("activated_at", out var aa) ? aa.GetString() : null,
                    CreatedAt = element.TryGetProperty("created_at", out var ca) ? ca.GetString() : null,
                    Product = ParseSubscriptionProduct(element),
                    Customer = ParseSubscriptionCustomer(element)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing subscription");
        }

        return null;
    }

    private MaxioSubscriptionProductDto? ParseSubscriptionProduct(JsonElement element)
    {
        try
        {
            if (element.TryGetProperty("product", out var productElement))
            {
                return new MaxioSubscriptionProductDto
                {
                    Id = productElement.GetProperty("id").GetInt32(),
                    Name = productElement.GetProperty("name").GetString(),
                    Handle = productElement.GetProperty("handle").GetString(),
                    PriceInCents = productElement.GetProperty("price_in_cents").GetInt32(),
                    Interval = productElement.GetProperty("interval").GetInt32(),
                    IntervalUnit = productElement.GetProperty("interval_unit").GetString()
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing subscription product");
        }

        return null;
    }

    private MaxioSubscriptionCustomerDto? ParseSubscriptionCustomer(JsonElement element)
    {
        try
        {
            if (element.TryGetProperty("customer", out var customerElement))
            {
                return new MaxioSubscriptionCustomerDto
                {
                    Id = customerElement.GetProperty("id").GetInt32(),
                    Email = customerElement.GetProperty("email").GetString(),
                    FirstName = customerElement.GetProperty("first_name").GetString(),
                    LastName = customerElement.GetProperty("last_name").GetString()
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing subscription customer");
        }

        return null;
    }
}
