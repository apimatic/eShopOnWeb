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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly string _baseUrl;
    private readonly string _authHeader;

    public MaxioBillingService(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_settings.ApiKey) || string.IsNullOrEmpty(_settings.Subdomain))
        {
            throw new InvalidOperationException("Maxio API Key and Subdomain are required");
        }

        if (!string.IsNullOrEmpty(_settings.BaseUrl))
        {
            _baseUrl = _settings.BaseUrl.TrimEnd('/');
        }
        else
        {
            _baseUrl = $"https://{_settings.Subdomain}.maxio.com";
        }

        var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:x"));
        _authHeader = $"Basic {authString}";
    }

    public async Task<List<MaxioProduct>> GetAvailablePlansAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/products.json");
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(_authHeader);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            var products = new List<MaxioProduct>();
            if (doc.RootElement.TryGetProperty("products", out var productsArray))
            {
                foreach (var item in productsArray.EnumerateArray())
                {
                    var product = new MaxioProduct
                    {
                        Id = item.GetProperty("id").GetInt32(),
                        Name = item.GetProperty("name").GetString() ?? string.Empty,
                        Handle = item.GetProperty("handle").GetString() ?? string.Empty,
                        Description = item.TryGetProperty("description", out var desc)
                            ? desc.GetString() ?? string.Empty
                            : string.Empty,
                        PriceInCents = item.TryGetProperty("price_in_cents", out var price)
                            ? price.GetInt32() / 100m
                            : null
                    };
                    products.Add(product);
                }
            }

            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available plans from Maxio");
            throw;
        }
    }

    public async Task<MaxioCustomer?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        try
        {
            var existingCustomer = await FindCustomerByReferenceAsync(userId);
            if (existingCustomer != null)
            {
                return existingCustomer;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/customers.json");
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(_authHeader);

            var customerData = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = userId
                }
            };

            var json = JsonSerializer.Serialize(customerData);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("customer", out var customerElement))
            {
                return new MaxioCustomer
                {
                    Id = customerElement.GetProperty("id").GetInt32(),
                    FirstName = customerElement.GetProperty("first_name").GetString() ?? string.Empty,
                    LastName = customerElement.GetProperty("last_name").GetString() ?? string.Empty,
                    Email = customerElement.GetProperty("email").GetString() ?? string.Empty,
                    Reference = customerElement.TryGetProperty("reference", out var @ref)
                        ? @ref.GetString()
                        : null
                };
            }

            return null;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogWarning("Customer with reference {UserId} might already exist", userId);
            return await FindCustomerByReferenceAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create customer in Maxio for userId {UserId}", userId);
            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/customers.json?reference={Uri.EscapeDataString(reference)}");
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(_authHeader);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("customers", out var customersArray))
            {
                foreach (var item in customersArray.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id))
                    {
                        return new MaxioCustomer
                        {
                            Id = id.GetInt32(),
                            FirstName = item.GetProperty("first_name").GetString() ?? string.Empty,
                            LastName = item.GetProperty("last_name").GetString() ?? string.Empty,
                            Email = item.GetProperty("email").GetString() ?? string.Empty,
                            Reference = item.TryGetProperty("reference", out var @ref)
                                ? @ref.GetString()
                                : null
                        };
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find customer by reference {Reference}", reference);
            return null;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/subscriptions.json");
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(_authHeader);

            var subscriptionData = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "automatic"
                }
            };

            var json = JsonSerializer.Serialize(subscriptionData);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("subscription", out var subElement))
            {
                return ParseSubscription(subElement);
            }

            throw new InvalidOperationException("Failed to parse subscription response from Maxio");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription in Maxio for customerId {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/customers/{customerId}/subscriptions.json");
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(_authHeader);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            var subscriptions = new List<MaxioSubscription>();
            if (doc.RootElement.TryGetProperty("subscriptions", out var subsArray))
            {
                foreach (var item in subsArray.EnumerateArray())
                {
                    subscriptions.Add(ParseSubscription(item));
                }
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get customer subscriptions from Maxio for customerId {CustomerId}", customerId);
            throw;
        }
    }

    private static MaxioSubscription ParseSubscription(JsonElement subElement)
    {
        return new MaxioSubscription
        {
            Id = subElement.GetProperty("id").GetInt32(),
            CustomerId = subElement.GetProperty("customer_id").GetInt32(),
            ProductId = subElement.GetProperty("product_id").GetInt32(),
            ProductHandle = int.TryParse(subElement.TryGetProperty("product_handle", out var ph)
                ? ph.GetString() : "0", out var handle) ? handle : 0,
            State = subElement.GetProperty("state").GetString() ?? string.Empty,
            CurrentPeriodStartsAt = subElement.TryGetProperty("current_period_starts_at", out var start)
                ? DateTime.Parse(start.GetString() ?? DateTime.UtcNow.ToString())
                : null,
            CurrentPeriodEndsAt = subElement.TryGetProperty("current_period_ends_at", out var end)
                ? DateTime.Parse(end.GetString() ?? DateTime.UtcNow.ToString())
                : null,
            NextAssessmentAt = subElement.TryGetProperty("next_assessment_at", out var next)
                ? DateTime.Parse(next.GetString() ?? DateTime.UtcNow.ToString())
                : null,
            TotalRecurringCustom = subElement.TryGetProperty("total_recurring_custom_dollars", out var total)
                ? decimal.Parse(total.GetString() ?? "0")
                : null
        };
    }
}
