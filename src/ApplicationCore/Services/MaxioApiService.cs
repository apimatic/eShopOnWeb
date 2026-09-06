using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioApiService : IMaxioApiService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioApiService> _logger;

    public MaxioApiService(HttpClient httpClient, IOptions<MaxioConfiguration> options, ILogger<MaxioApiService> logger)
    {
        _httpClient = httpClient;
        _config = options.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_config.ApiKey) || string.IsNullOrEmpty(_config.Subdomain))
        {
            throw new InvalidOperationException("Maxio configuration (ApiKey and Subdomain) must be provided");
        }

        SetupHttpClient();
    }

    private void SetupHttpClient()
    {
        var baseUrl = _config.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl))
        {
            baseUrl = $"https://{_config.Subdomain}.chargify.com";
        }

        _httpClient.BaseAddress = new Uri(baseUrl);

        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<MaxioSubscriptionPlan>> ListSubscriptionPlansAsync()
    {
        try
        {
            _logger.LogInformation("Fetching subscription plans for product family: {ProductFamily}", _config.ProductFamilyHandle);

            var response = await _httpClient.GetAsync($"/products.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var products = JsonSerializer.Deserialize<JsonElement>(content);

            var plans = new List<MaxioSubscriptionPlan>();

            if (products.ValueKind == JsonValueKind.Array)
            {
                foreach (var product in products.EnumerateArray())
                {
                    var p = product.GetProperty("product");
                    var family = p.TryGetProperty("product_family", out var fam) ? fam : default;

                    if (family.ValueKind == JsonValueKind.Object &&
                        family.TryGetProperty("handle", out var familyHandle) &&
                        familyHandle.GetString() == _config.ProductFamilyHandle)
                    {
                        var plan = new MaxioSubscriptionPlan
                        {
                            Id = p.GetProperty("id").GetInt32(),
                            Handle = p.GetProperty("handle").GetString() ?? "",
                            Name = p.GetProperty("name").GetString() ?? "",
                            PriceInCents = p.GetProperty("price_in_cents").GetInt64(),
                            Interval = p.GetProperty("interval").GetInt32(),
                            IntervalUnit = p.TryGetProperty("interval_unit", out var unit)
                                ? unit.GetString() ?? "month"
                                : "month",
                            Description = p.TryGetProperty("description", out var desc)
                                ? desc.GetString() ?? ""
                                : ""
                        };
                        plans.Add(plan);
                    }
                }
            }

            _logger.LogInformation("Found {Count} subscription plans", plans.Count);
            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans");
            throw;
        }
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string userReference, string firstName, string lastName, string email)
    {
        try
        {
            _logger.LogInformation("Looking up customer by reference: {Reference}", userReference);

            try
            {
                var lookupResponse = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(userReference)}");
                if (lookupResponse.IsSuccessStatusCode)
                {
                    var content = await lookupResponse.Content.ReadAsStringAsync();
                    var customerObj = JsonSerializer.Deserialize<JsonElement>(content);

                    if (customerObj.TryGetProperty("customer", out var customer))
                    {
                        return MapCustomer(customer);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Customer lookup failed, will create new customer");
            }

            _logger.LogInformation("Creating new customer with reference: {Reference}", userReference);
            return await CreateCustomerAsync(userReference, firstName, lastName, email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetOrCreateCustomerAsync");
            throw;
        }
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        var requestBody = new
        {
            customer = new
            {
                reference,
                first_name = firstName,
                last_name = lastName,
                email,
                country = "US"
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/customers.json", content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var customerObj = JsonSerializer.Deserialize<JsonElement>(responseContent);

        if (customerObj.TryGetProperty("customer", out var customer))
        {
            return MapCustomer(customer);
        }

        throw new InvalidOperationException("Failed to create customer - no customer in response");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            _logger.LogInformation("Creating subscription for customer {CustomerId} on product {ProductHandle}", customerId, productHandle);

            var requestBody = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "remittance"
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var subscriptionObj = JsonSerializer.Deserialize<JsonElement>(responseContent);

            if (subscriptionObj.TryGetProperty("subscription", out var subscription))
            {
                return MapSubscription(subscription);
            }

            throw new InvalidOperationException("Failed to create subscription - no subscription in response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            throw;
        }
    }

    public async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            _logger.LogInformation("Fetching subscriptions for customer {CustomerId}", customerId);

            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var subscriptions = JsonSerializer.Deserialize<JsonElement>(content);

            var result = new List<MaxioSubscription>();

            if (subscriptions.ValueKind == JsonValueKind.Array)
            {
                foreach (var sub in subscriptions.EnumerateArray())
                {
                    result.Add(MapSubscription(sub));
                }
            }

            _logger.LogInformation("Found {Count} subscriptions for customer", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching customer subscriptions");
            throw;
        }
    }

    private MaxioCustomer MapCustomer(JsonElement customer)
    {
        return new MaxioCustomer
        {
            Id = customer.GetProperty("id").GetInt32(),
            Reference = customer.TryGetProperty("reference", out var ref_) ? ref_.GetString() ?? "" : "",
            FirstName = customer.TryGetProperty("first_name", out var first) ? first.GetString() ?? "" : "",
            LastName = customer.TryGetProperty("last_name", out var last) ? last.GetString() ?? "" : "",
            Email = customer.TryGetProperty("email", out var email) ? email.GetString() ?? "" : "",
            Address = customer.TryGetProperty("address", out var addr) ? addr.GetString() : null,
            City = customer.TryGetProperty("city", out var city) ? city.GetString() : null,
            State = customer.TryGetProperty("state", out var state) ? state.GetString() : null,
            Zip = customer.TryGetProperty("zip", out var zip) ? zip.GetString() : null,
            Country = customer.TryGetProperty("country", out var country) ? country.GetString() : null
        };
    }

    private MaxioSubscription MapSubscription(JsonElement subscription)
    {
        var sub = new MaxioSubscription
        {
            Id = subscription.GetProperty("id").GetInt32(),
            State = subscription.TryGetProperty("state", out var state) ? state.GetString() ?? "" : "",
            ProductPriceInCents = subscription.TryGetProperty("product_price_in_cents", out var price)
                ? price.GetInt64()
                : 0,
            CurrentPeriodEndsAt = subscription.TryGetProperty("current_period_ends_at", out var ends)
                ? ends.GetString()
                : null,
            NextAssessmentAt = subscription.TryGetProperty("next_assessment_at", out var next)
                ? next.GetString()
                : null,
            ActivatedAt = subscription.TryGetProperty("activated_at", out var activated)
                ? activated.GetString()
                : null,
            CreatedAt = subscription.TryGetProperty("created_at", out var created)
                ? created.GetString()
                : null
        };

        if (subscription.TryGetProperty("product", out var product))
        {
            sub.Product = new MaxioSubscriptionProduct
            {
                Id = product.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                Handle = product.TryGetProperty("handle", out var handle) ? handle.GetString() ?? "" : "",
                Name = product.TryGetProperty("name", out var name) ? name.GetString() ?? "" : ""
            };
        }

        if (subscription.TryGetProperty("customer", out var customer))
        {
            sub.Customer = new MaxioSubscriptionCustomer
            {
                Id = customer.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                FirstName = customer.TryGetProperty("first_name", out var first) ? first.GetString() ?? "" : "",
                LastName = customer.TryGetProperty("last_name", out var last) ? last.GetString() ?? "" : "",
                Email = customer.TryGetProperty("email", out var email) ? email.GetString() ?? "" : ""
            };
        }

        return sub;
    }
}
