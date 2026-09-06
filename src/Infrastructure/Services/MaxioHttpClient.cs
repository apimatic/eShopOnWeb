using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioHttpClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioHttpClient> _logger;

    public MaxioHttpClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioHttpClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        var baseUrl = _settings.BaseUrl ?? $"https://{_settings.SiteSubdomain}.chargify.com";
        _httpClient.BaseAddress = new Uri(baseUrl);

        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<MaxioProductDto> GetProductByHandleAsync(string productHandle)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products/handle/{productHandle}.json");
            await EnsureSuccessAsync(response, $"Failed to get product by handle: {productHandle}");

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var productElement = doc.RootElement.GetProperty("product");
            return ParseProductDto(productElement);
        }
        catch (MaxioApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product by handle: {ProductHandle}", productHandle);
            throw new MaxioApiException($"Failed to get product by handle: {productHandle}", ex);
        }
    }

    public async Task<List<MaxioProductDto>> ListProductsByFamilyHandleAsync(string familyHandle)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products.json?product_family_id={familyHandle}");
            await EnsureSuccessAsync(response, $"Failed to list products for family: {familyHandle}");

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var productsElement = doc.RootElement.GetProperty("products");

            var products = new List<MaxioProductDto>();
            foreach (var productElement in productsElement.EnumerateArray())
            {
                products.Add(ParseProductDto(productElement));
            }
            return products;
        }
        catch (MaxioApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products for family: {FamilyHandle}", familyHandle);
            throw new MaxioApiException($"Failed to list products for family: {familyHandle}", ex);
        }
    }

    public async Task<MaxioCustomerDto> CreateCustomerAsync(string email, string firstName, string lastName, string reference)
    {
        try
        {
            var payload = new
            {
                customer = new
                {
                    email = email,
                    first_name = firstName,
                    last_name = lastName,
                    reference = reference
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("/customers.json", content);

            if ((int)response.StatusCode == 422)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                if (errorJson.Contains("\"errors\""))
                {
                    _logger.LogWarning("Validation error creating customer: {Email}", email);
                    throw new MaxioCustomerCreationException(
                        $"Validation error creating customer: {email}",
                        422,
                        errorJson
                    );
                }
            }

            await EnsureSuccessAsync(response, $"Failed to create customer: {email}");

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var customerElement = doc.RootElement.GetProperty("customer");
            return ParseCustomerDto(customerElement);
        }
        catch (MaxioCustomerCreationException)
        {
            throw;
        }
        catch (MaxioApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer: {Email}", email);
            throw new MaxioCustomerCreationException($"Failed to create customer: {email}", ex);
        }
    }

    public async Task<MaxioCustomerDto?> GetCustomerAsync(int customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}.json");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            await EnsureSuccessAsync(response, $"Failed to get customer: {customerId}");

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var customerElement = doc.RootElement.GetProperty("customer");
            return ParseCustomerDto(customerElement);
        }
        catch (MaxioApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer: {CustomerId}", customerId);
            throw new MaxioApiException($"Failed to get customer: {customerId}", ex);
        }
    }

    public async Task<List<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
            await EnsureSuccessAsync(response, $"Failed to list subscriptions for customer: {customerId}");

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var subscriptionsElement = doc.RootElement.GetProperty("subscriptions");

            var subscriptions = new List<MaxioSubscriptionDto>();
            foreach (var subElement in subscriptionsElement.EnumerateArray())
            {
                subscriptions.Add(ParseSubscriptionDto(subElement));
            }
            return subscriptions;
        }
        catch (MaxioApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions for customer: {CustomerId}", customerId);
            throw new MaxioApiException($"Failed to list subscriptions for customer: {customerId}", ex);
        }
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, string paymentCollectionMethod = "automatic")
    {
        try
        {
            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = paymentCollectionMethod
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("/subscriptions.json", content);

            if ((int)response.StatusCode == 422)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Validation error creating subscription for customer: {CustomerId}", customerId);
                throw new MaxioSubscriptionCreationException(
                    $"Validation error creating subscription for customer: {customerId}",
                    422,
                    errorJson
                );
            }

            await EnsureSuccessAsync(response, $"Failed to create subscription for customer: {customerId}");

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var subscriptionElement = doc.RootElement.GetProperty("subscription");
            return ParseSubscriptionDto(subscriptionElement);
        }
        catch (MaxioSubscriptionCreationException)
        {
            throw;
        }
        catch (MaxioApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer: {CustomerId}", customerId);
            throw new MaxioSubscriptionCreationException($"Failed to create subscription for customer: {customerId}", ex);
        }
    }

    private MaxioProductDto ParseProductDto(JsonElement element)
    {
        return new MaxioProductDto
        {
            Id = element.GetProperty("id").GetInt32(),
            Name = element.GetProperty("name").GetString() ?? "",
            Handle = element.TryGetProperty("handle", out var handle) ? handle.GetString() : null,
            PriceInCents = element.GetProperty("price_in_cents").GetInt64(),
            Interval = element.GetProperty("interval").GetInt32(),
            IntervalUnit = element.GetProperty("interval_unit").GetString() ?? "month",
            RequireCreditCard = element.TryGetProperty("require_credit_card", out var requireCC) && requireCC.GetBoolean(),
            ProductFamilyId = element.TryGetProperty("product_family", out var family)
                ? family.GetProperty("id").GetInt32()
                : 0
        };
    }

    private MaxioCustomerDto ParseCustomerDto(JsonElement element)
    {
        return new MaxioCustomerDto
        {
            Id = element.GetProperty("id").GetInt32(),
            Email = element.GetProperty("email").GetString() ?? "",
            FirstName = element.GetProperty("first_name").GetString() ?? "",
            LastName = element.GetProperty("last_name").GetString() ?? "",
            Reference = element.TryGetProperty("reference", out var reference) ? reference.GetString() : null,
            CreatedAt = DateTime.Parse(element.GetProperty("created_at").GetString() ?? DateTime.UtcNow.ToString()),
            UpdatedAt = DateTime.Parse(element.GetProperty("updated_at").GetString() ?? DateTime.UtcNow.ToString())
        };
    }

    private MaxioSubscriptionDto ParseSubscriptionDto(JsonElement element)
    {
        var productElement = element.GetProperty("product");
        return new MaxioSubscriptionDto
        {
            Id = element.GetProperty("id").GetInt32(),
            State = element.GetProperty("state").GetString() ?? "unknown",
            CustomerId = element.GetProperty("customer_id").GetInt32(),
            ProductId = productElement.GetProperty("id").GetInt32(),
            ProductName = productElement.GetProperty("name").GetString() ?? "",
            ProductPriceInCents = productElement.GetProperty("price_in_cents").GetInt64(),
            CurrentPeriodEndsAt = DateTime.Parse(element.GetProperty("current_period_ends_at").GetString() ?? DateTime.UtcNow.ToString()),
            NextAssessmentAt = DateTime.Parse(element.GetProperty("next_assessment_at").GetString() ?? DateTime.UtcNow.ToString()),
            ActivatedAt = DateTime.Parse(element.GetProperty("activated_at").GetString() ?? DateTime.UtcNow.ToString()),
            CreatedAt = DateTime.Parse(element.GetProperty("created_at").GetString() ?? DateTime.UtcNow.ToString()),
            UpdatedAt = DateTime.Parse(element.GetProperty("updated_at").GetString() ?? DateTime.UtcNow.ToString())
        };
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string errorMessage)
    {
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("Maxio API error {StatusCode}: {ResponseBody}", response.StatusCode, responseBody);
            throw new MaxioApiException(errorMessage, (int)response.StatusCode, responseBody);
        }
    }
}
