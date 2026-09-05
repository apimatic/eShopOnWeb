using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        SetupAuthenticationHeaders();
    }

    private void SetupAuthenticationHeaders()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
        _httpClient.BaseAddress = new Uri(_settings.GetBaseUrl());
    }

    public async Task<MaxioProductDto?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products/handle/{productHandle}.json", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to get product {productHandle}: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("product", out var productElem))
            {
                return MapJsonToProductDto(productElem);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting product {productHandle}");
            return null;
        }
    }

    public async Task<IEnumerable<MaxioProductDto>> ListProductsByFamilyHandleAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProductDto>();

        try
        {
            var page = 1;
            bool hasMore = true;

            while (hasMore)
            {
                var response = await _httpClient.GetAsync(
                    $"/product_families/handle:{familyHandle}/products.json?page={page}&per_page=200",
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Failed to list products for family {familyHandle}: {response.StatusCode}");
                    break;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var items = root.GetProperty("items");
                if (items.GetArrayLength() == 0)
                {
                    hasMore = false;
                }
                else
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.TryGetProperty("product", out var productElem))
                        {
                            products.Add(MapJsonToProductDto(productElem));
                        }
                    }

                    page++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error listing products for family {familyHandle}");
        }

        return products;
    }

    public async Task<MaxioCustomerDto?> GetOrCreateCustomerAsync(string customerId, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        // First try to get existing customer by reference (idempotent)
        var existing = await GetCustomerByReferenceAsync(customerId, cancellationToken);
        if (existing != null)
        {
            _logger.LogInformation($"Customer {customerId} already exists in Maxio as ID {existing.Id}");
            return existing;
        }

        // Create new customer
        try
        {
            var requestBody = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = customerId
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/customers.json", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError($"Failed to create customer {customerId}: {response.StatusCode} - {errorContent}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customerElem))
            {
                return MapJsonToCustomerDto(customerElem);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating customer {customerId}");
            return null;
        }
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var requestBody = new
        {
            subscription = new
            {
                customer_id = request.CustomerId,
                product_handle = request.ProductHandle,
                product_id = request.ProductId
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/subscriptions.json", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError($"Failed to create subscription for customer {request.CustomerId}: {response.StatusCode} - {errorContent}");
            throw new InvalidOperationException($"Failed to create subscription: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("subscription", out var subscriptionElem))
        {
            return MapJsonToSubscriptionDto(subscriptionElem);
        }

        throw new InvalidOperationException("Invalid subscription response from Maxio");
    }

    public async Task<MaxioSubscriptionDto?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/subscriptions/{subscriptionId}.json", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to get subscription {subscriptionId}: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("subscription", out var subscriptionElem))
            {
                return MapJsonToSubscriptionDto(subscriptionElem);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting subscription {subscriptionId}");
            return null;
        }
    }

    public async Task<IEnumerable<MaxioSubscriptionDto>> ListSubscriptionsByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = new List<MaxioSubscriptionDto>();

        try
        {
            var page = 1;
            bool hasMore = true;

            while (hasMore)
            {
                var response = await _httpClient.GetAsync(
                    $"/customers/{customerId}/subscriptions.json?page={page}&per_page=200",
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Failed to list subscriptions for customer {customerId}: {response.StatusCode}");
                    break;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("subscriptions", out var subsElem))
                {
                    var itemCount = subsElem.GetArrayLength();
                    if (itemCount == 0)
                    {
                        hasMore = false;
                    }
                    else
                    {
                        foreach (var sub in subsElem.EnumerateArray())
                        {
                            subscriptions.Add(MapJsonToSubscriptionDto(sub));
                        }

                        page++;
                    }
                }
                else
                {
                    hasMore = false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error listing subscriptions for customer {customerId}");
        }

        return subscriptions;
    }

    private async Task<MaxioCustomerDto?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to lookup customer {reference}: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customerElem))
            {
                return MapJsonToCustomerDto(customerElem);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error looking up customer {reference}");
            return null;
        }
    }

    private static MaxioProductDto MapJsonToProductDto(JsonElement elem)
    {
        return new MaxioProductDto
        {
            Id = elem.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            Handle = elem.TryGetProperty("handle", out var handle) ? handle.GetString() : null,
            Name = elem.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Description = elem.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            PriceInCents = elem.TryGetProperty("price_in_cents", out var price) ? price.GetInt64() : 0,
            Interval = elem.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 1,
            IntervalUnit = elem.TryGetProperty("interval_unit", out var unit) ? unit.GetString() : "month",
            RequiresCreditCard = elem.TryGetProperty("require_credit_card", out var req) && req.GetBoolean()
        };
    }

    private static MaxioCustomerDto MapJsonToCustomerDto(JsonElement elem)
    {
        return new MaxioCustomerDto
        {
            Id = elem.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            Email = elem.TryGetProperty("email", out var email) ? email.GetString() ?? "" : "",
            FirstName = elem.TryGetProperty("first_name", out var firstName) ? firstName.GetString() ?? "" : "",
            LastName = elem.TryGetProperty("last_name", out var lastName) ? lastName.GetString() ?? "" : "",
            Reference = elem.TryGetProperty("reference", out var reference) ? reference.GetString() : null
        };
    }

    private static MaxioSubscriptionDto MapJsonToSubscriptionDto(JsonElement elem)
    {
        return new MaxioSubscriptionDto
        {
            Id = elem.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            CustomerId = elem.TryGetProperty("customer_id", out var customerId) ? customerId.GetInt32() : 0,
            ProductId = elem.TryGetProperty("product_id", out var productId) ? productId.GetInt32() : 0,
            State = elem.TryGetProperty("state", out var state) ? state.GetString() ?? "active" : "active",
            CurrentPeriodStartsAt = elem.TryGetProperty("current_period_starts_at", out var startAt) && startAt.ValueKind != JsonValueKind.Null ? startAt.GetDateTime() : null,
            CurrentPeriodEndsAt = elem.TryGetProperty("current_period_ends_at", out var endAt) && endAt.ValueKind != JsonValueKind.Null ? endAt.GetDateTime() : null,
            CreatedAt = elem.TryGetProperty("created_at", out var createdAt) ? createdAt.GetDateTime() : DateTime.UtcNow,
            UpdatedAt = elem.TryGetProperty("updated_at", out var updatedAt) ? updatedAt.GetDateTime() : DateTime.UtcNow
        };
    }
}
