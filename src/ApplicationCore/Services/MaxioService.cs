using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        var baseUrl = string.IsNullOrEmpty(_settings.BaseUrl)
            ? $"https://{_settings.Subdomain}.chargify.com"
            : _settings.BaseUrl;

        _httpClient.BaseAddress = new Uri(baseUrl);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<MaxioProduct[]> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Maxio API key is not configured. Please set MAXIO_API_KEY environment variable.");
                return Array.Empty<MaxioProduct>();
            }

            var response = await _httpClient.GetAsync("/products.json", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Maxio API returned status {StatusCode}: {Content}", response.StatusCode, errorContent);
                return Array.Empty<MaxioProduct>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            var products = new List<MaxioProduct>();
            if (doc.RootElement.TryGetProperty("products", out var productsArray))
            {
                foreach (var productEl in productsArray.EnumerateArray())
                {
                    var product = ParseProduct(productEl);
                    // Filter by product family handle
                    if (productEl.TryGetProperty("product_family", out var familyEl) &&
                        familyEl.TryGetProperty("handle", out var handleEl))
                    {
                        var familyHandle = handleEl.GetString() ?? "";
                        if (familyHandle == _settings.ProductFamilyHandle)
                        {
                            products.Add(product);
                        }
                    }
                }
            }

            return products.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching plans from Maxio");
            return Array.Empty<MaxioProduct>();
        }
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        // Try to find existing customer by reference
        var existing = await GetCustomerByReferenceAsync(userId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        // Create new customer
        return await CreateCustomerAsync(userId, email, firstName, lastName, cancellationToken);
    }

    private async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("customer", out var customerEl))
            {
                return ParseCustomer(customerEl);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error looking up customer by reference");
            return null;
        }
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = userId
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/customers.json", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("customer", out var customerEl))
        {
            return ParseCustomer(customerEl);
        }

        throw new InvalidOperationException("Failed to parse customer response");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                payment_collection_method = "invoice"
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/subscriptions.json", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("subscription", out var subscriptionEl))
        {
            return ParseSubscription(subscriptionEl);
        }

        throw new InvalidOperationException("Failed to parse subscription response");
    }

    public async Task<MaxioSubscription[]> GetSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/subscriptions.json?customer_id={customerId}&state=active,past_due,pending", cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            var subscriptions = new List<MaxioSubscription>();
            if (doc.RootElement.TryGetProperty("subscriptions", out var subsArray))
            {
                foreach (var subEl in subsArray.EnumerateArray())
                {
                    subscriptions.Add(ParseSubscription(subEl));
                }
            }

            return subscriptions.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions from Maxio");
            throw;
        }
    }

    private MaxioProduct ParseProduct(JsonElement el)
    {
        return new MaxioProduct
        {
            Id = el.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            Name = el.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Handle = el.TryGetProperty("handle", out var handle) ? handle.GetString() ?? "" : "",
            Description = el.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
            PriceInCents = el.TryGetProperty("price_in_cents", out var price) ? price.GetInt32() : 0,
            Interval = el.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 1,
            IntervalUnit = el.TryGetProperty("interval_unit", out var unit) ? unit.GetString() ?? "" : ""
        };
    }

    private MaxioCustomer ParseCustomer(JsonElement el)
    {
        return new MaxioCustomer
        {
            Id = el.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            FirstName = el.TryGetProperty("first_name", out var firstName) ? firstName.GetString() ?? "" : "",
            LastName = el.TryGetProperty("last_name", out var lastName) ? lastName.GetString() ?? "" : "",
            Email = el.TryGetProperty("email", out var email) ? email.GetString() ?? "" : "",
            Reference = el.TryGetProperty("reference", out var reference) ? reference.GetString() ?? "" : "",
            CreatedAt = el.TryGetProperty("created_at", out var created) ? created.GetDateTime() : DateTime.UtcNow
        };
    }

    private MaxioSubscription ParseSubscription(JsonElement el)
    {
        var product = el.TryGetProperty("product", out var productEl) ? ParseProduct(productEl) : null;

        return new MaxioSubscription
        {
            Id = el.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            CustomerId = el.TryGetProperty("customer_id", out var customerId) ? customerId.GetInt32() : 0,
            ProductId = el.TryGetProperty("product_id", out var productId) ? productId.GetInt32() : 0,
            State = el.TryGetProperty("state", out var state) ? state.GetString() ?? "" : "",
            CurrentPeriodEndsAt = el.TryGetProperty("current_period_ends_at", out var ends) ? ends.GetDateTime() : DateTime.UtcNow,
            NextAssessmentAt = el.TryGetProperty("next_assessment_at", out var next) ? next.GetDateTime() : null,
            ActivatedAt = el.TryGetProperty("activated_at", out var activated) ? activated.GetDateTime() : DateTime.UtcNow,
            CreatedAt = el.TryGetProperty("created_at", out var created) ? created.GetDateTime() : DateTime.UtcNow,
            Product = product
        };
    }
}
