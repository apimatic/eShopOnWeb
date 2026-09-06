using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSettings
{
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string ProductFamilyHandle { get; set; } = "";
    public string? BaseUrl { get; set; }
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioSettings> settings, IAppLogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        ConfigureHttpClient();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    private void ConfigureHttpClient()
    {
        if (string.IsNullOrEmpty(_settings.Subdomain) && string.IsNullOrEmpty(_settings.BaseUrl))
        {
            _logger.LogWarning("Maxio settings are not configured. Ensure Maxio:Subdomain or Maxio:BaseUrl is set.");
            return;
        }

        var baseUrl = _settings.BaseUrl ?? $"https://{_settings.Subdomain}.chargify.com";
        if (!string.IsNullOrEmpty(baseUrl))
        {
            _httpClient.BaseAddress = new Uri(baseUrl);
        }

        if (!string.IsNullOrEmpty(_settings.ApiKey))
        {
            var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authString);
        }
    }

    public async Task<SubscriptionPlan[]> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/product_families/handle:{_settings.ProductFamilyHandle}/products.json";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var plans = new List<SubscriptionPlan>();
            if (root.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("product", out var product))
                    {
                        var plan = ParseProductToSubscriptionPlan(product);
                        if (plan != null)
                            plans.Add(plan);
                    }
                }
            }

            _logger.LogInformation($"Retrieved {plans.Count} subscription plans from Maxio");
            return plans.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error retrieving subscription plans from Maxio: {ex.Message}");
            throw;
        }
    }

    public async Task<(int CustomerId, bool IsNew)> EnsureMaxioCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        try
        {
            var lookupUrl = $"/customers/lookup.json?reference={Uri.EscapeDataString(userId)}";
            var lookupResponse = await _httpClient.GetAsync(lookupUrl, cancellationToken);

            if (lookupResponse.IsSuccessStatusCode)
            {
                var content = await lookupResponse.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.TryGetProperty("customer", out var customer) && customer.TryGetProperty("id", out var idElement))
                {
                    var customerId = idElement.GetInt32();
                    _logger.LogInformation($"Found existing Maxio customer {customerId} for user {userId}");
                    return (customerId, false);
                }
            }

            var createRequest = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = userId
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(createRequest, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var createResponse = await _httpClient.PostAsync("/customers.json", jsonContent, cancellationToken);
            createResponse.EnsureSuccessStatusCode();

            var responseContent = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            using var responseDoc = JsonDocument.Parse(responseContent);
            var responseRoot = responseDoc.RootElement;

            if (responseRoot.TryGetProperty("customer", out var newCustomer) && newCustomer.TryGetProperty("id", out var newIdElement))
            {
                var customerId = newIdElement.GetInt32();
                _logger.LogInformation($"Created new Maxio customer {customerId} for user {userId}");
                return (customerId, true);
            }

            throw new Exception("Failed to parse customer response from Maxio");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error ensuring Maxio customer for user {userId}: {ex.Message}");
            throw;
        }
    }

    public async Task<UserSubscription> CreateSubscriptionAsync(int maxioCustomerId, int maxioProductId, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptionRequest = new
            {
                subscription = new
                {
                    customer_id = maxioCustomerId,
                    product_id = maxioProductId,
                    uniqueness_token = Guid.NewGuid().ToString("D")
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(subscriptionRequest, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", jsonContent, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogWarning($"Duplicate subscription attempt for customer {maxioCustomerId} and product {maxioProductId}");
            }

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("subscription", out var subscription))
            {
                var userSubscription = ParseSubscriptionResponse(subscription);
                _logger.LogInformation($"Created subscription {userSubscription.MaxioSubscriptionId} for customer {maxioCustomerId}");
                return userSubscription;
            }

            throw new Exception("Failed to parse subscription response from Maxio");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error creating subscription for customer {maxioCustomerId} and product {maxioProductId}: {ex.Message}");
            throw;
        }
    }

    public async Task<UserSubscription[]> GetCustomerSubscriptionsAsync(int maxioCustomerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/customers/{maxioCustomerId}/subscriptions.json";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var subscriptions = new List<UserSubscription>();
            if (root.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("subscription", out var subscription))
                    {
                        var userSubscription = ParseSubscriptionResponse(subscription);
                        subscriptions.Add(userSubscription);
                    }
                }
            }

            _logger.LogInformation($"Retrieved {subscriptions.Count} subscriptions for customer {maxioCustomerId}");
            return subscriptions.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error retrieving subscriptions for customer {maxioCustomerId}: {ex.Message}");
            throw;
        }
    }

    private SubscriptionPlan? ParseProductToSubscriptionPlan(JsonElement product)
    {
        var plan = new SubscriptionPlan();

        if (product.TryGetProperty("id", out var id))
            plan.MaxioProductId = id.GetInt32();

        if (product.TryGetProperty("handle", out var handle))
            plan.Handle = handle.GetString() ?? "";

        if (product.TryGetProperty("name", out var name))
            plan.Name = name.GetString() ?? "";

        if (product.TryGetProperty("description", out var desc))
            plan.Description = desc.GetString();

        if (product.TryGetProperty("price_in_cents", out var priceInCents))
            plan.Price = priceInCents.GetInt64() / 100m;

        if (product.TryGetProperty("interval_unit", out var intervalUnit))
            plan.Interval = intervalUnit.GetString();

        if (product.TryGetProperty("interval", out var interval))
            plan.IntervalCount = interval.GetInt32();

        if (product.TryGetProperty("product_family", out var family) && family.TryGetProperty("handle", out var familyHandle))
            plan.ProductFamilyHandle = familyHandle.GetString();

        return !string.IsNullOrEmpty(plan.Handle) && !string.IsNullOrEmpty(plan.Name) ? plan : null;
    }

    private UserSubscription ParseSubscriptionResponse(JsonElement subscription)
    {
        var userSub = new UserSubscription
        {
            UserId = "",
            State = subscription.TryGetProperty("state", out var state) ? (state.GetString() ?? "unknown") : "unknown",
            ProductName = "",
            SubscriptionHandle = ""
        };

        if (subscription.TryGetProperty("id", out var id))
            userSub.MaxioSubscriptionId = id.GetInt32();

        if (subscription.TryGetProperty("customer", out var customer) && customer.TryGetProperty("reference", out var reference))
            userSub.UserId = reference.GetString() ?? "";

        if (subscription.TryGetProperty("customer", out var cust) && cust.TryGetProperty("id", out var custId))
            userSub.MaxioCustomerId = custId.GetInt32();

        if (subscription.TryGetProperty("product", out var product) && product.TryGetProperty("name", out var prodName))
            userSub.ProductName = prodName.GetString() ?? "";

        if (subscription.TryGetProperty("product", out var prod2) && prod2.TryGetProperty("handle", out var prodHandle))
            userSub.SubscriptionHandle = prodHandle.GetString() ?? "";

        if (subscription.TryGetProperty("product_price_in_cents", out var price))
            userSub.MonthlyPrice = price.GetInt64() / 100m;

        if (subscription.TryGetProperty("current_period_ends_at", out var periodEnd) && periodEnd.GetString() is string periodEndStr && DateTime.TryParse(periodEndStr, out var periodEndDt))
            userSub.CurrentPeriodEndsAt = periodEndDt;

        if (subscription.TryGetProperty("next_assessment_at", out var nextAssess) && nextAssess.GetString() is string nextAssessStr && DateTime.TryParse(nextAssessStr, out var nextAssessDt))
            userSub.NextAssessmentAt = nextAssessDt;

        return userSub;
    }
}
