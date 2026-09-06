using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly string _authHeader;

    public MaxioSubscriptionService(IConfiguration configuration, ILogger<MaxioSubscriptionService> logger)
    {
        _logger = logger;
        var configSection = configuration.GetSection(MaxioConfiguration.ConfigSectionName);
        _config = configSection.Get<MaxioConfiguration>() ?? new MaxioConfiguration();

        if (string.IsNullOrEmpty(_config.ApiKey) || string.IsNullOrEmpty(_config.Subdomain))
        {
            throw new InvalidOperationException("Maxio configuration (ApiKey and Subdomain) is required");
        }

        _httpClient = new HttpClient();
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:"));
        _authHeader = $"Basic {credentials}";
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(_config.BaseUrl))
        {
            return _config.BaseUrl.TrimEnd('/');
        }
        return $"https://{_config.Subdomain}.chargify.com";
    }

    public async Task<List<SubscriptionPlanData>> ListSubscriptionPlansAsync()
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var url = $"{baseUrl}/products.json";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", _authHeader);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var products = new List<SubscriptionPlanData>();

            var rootElement = doc.RootElement;
            JsonElement itemsArray;

            if (rootElement.ValueKind == JsonValueKind.Array)
            {
                itemsArray = rootElement;
            }
            else if (rootElement.TryGetProperty("products", out var prop))
            {
                itemsArray = prop;
            }
            else
            {
                return products;
            }

            foreach (var item in itemsArray.EnumerateArray())
            {
                JsonElement productElement;

                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("product", out var prod))
                {
                    productElement = prod;
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    productElement = item;
                }
                else
                {
                    continue;
                }

                var plan = new SubscriptionPlanData();
                if (productElement.TryGetProperty("id", out var id))
                    plan.Id = id.GetInt32();
                if (productElement.TryGetProperty("handle", out var handle))
                    plan.Handle = handle.GetString();
                if (productElement.TryGetProperty("name", out var name))
                    plan.Name = name.GetString();
                if (productElement.TryGetProperty("description", out var desc))
                    plan.Description = desc.GetString();

                if (productElement.TryGetProperty("default_product_price_point_id", out var ppId))
                {
                    plan.DefaultPricePointId = ppId.GetInt32();
                }

                if (productElement.TryGetProperty("price_in_cents", out var priceInCents))
                    plan.PriceInCents = priceInCents.GetInt64();

                products.Add(plan);
            }

            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscription plans from Maxio");
            throw;
        }
    }

    public async Task<CustomerData> GetOrCreateCustomerAsync(string userId, string email, string firstName = "", string lastName = "")
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var url = $"{baseUrl}/customers.json";

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

            var jsonContent = JsonSerializer.Serialize(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", _authHeader);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            var customer = new CustomerData();
            if (doc.RootElement.TryGetProperty("customer", out var customerObj))
            {
                if (customerObj.TryGetProperty("id", out var id))
                    customer.Id = id.GetInt32();
                if (customerObj.TryGetProperty("email", out var emailProp))
                    customer.Email = emailProp.GetString();
                if (customerObj.TryGetProperty("reference", out var reference))
                    customer.Reference = reference.GetString();
            }

            _logger.LogInformation($"Customer created/retrieved with ID: {customer.Id}");
            return customer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/retrieving customer from Maxio");
            throw;
        }
    }

    public async Task<SubscriptionData> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var url = $"{baseUrl}/subscriptions.json";

            var payload = new
            {
                subscription = new
                {
                    product_handle = productHandle,
                    customer_id = customerId,
                    payment_collection_method = "remittance"
                }
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", _authHeader);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            var subscription = new SubscriptionData();
            if (doc.RootElement.TryGetProperty("subscription", out var subObj))
            {
                if (subObj.TryGetProperty("id", out var id))
                    subscription.Id = id.GetInt32();
                if (subObj.TryGetProperty("state", out var state))
                    subscription.State = state.GetString();
                if (subObj.TryGetProperty("current_period_started_at", out var started))
                    subscription.CurrentPeriodStartedAt = started.TryGetDateTime(out var startedTime) ? startedTime : null;
                if (subObj.TryGetProperty("current_period_ends_at", out var ends))
                    subscription.CurrentPeriodEndsAt = ends.TryGetDateTime(out var endsTime) ? endsTime : null;
                if (subObj.TryGetProperty("next_assessment_at", out var nextAssess))
                    subscription.NextAssessmentAt = nextAssess.TryGetDateTime(out var nextTime) ? nextTime : null;
                if (subObj.TryGetProperty("product", out var product))
                {
                    if (product.TryGetProperty("handle", out var handle))
                        subscription.ProductHandle = handle.GetString();
                    if (product.TryGetProperty("name", out var name))
                        subscription.ProductName = name.GetString();
                }
            }

            _logger.LogInformation($"Subscription created with ID: {subscription.Id}");
            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio");
            throw;
        }
    }

    public async Task<List<SubscriptionData>> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var url = $"{baseUrl}/customers/{customerId}/subscriptions.json";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", _authHeader);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var subscriptions = new List<SubscriptionData>();

            if (doc.RootElement.TryGetProperty("subscriptions", out var subsArray))
            {
                foreach (var sub in subsArray.EnumerateArray())
                {
                    var subscription = new SubscriptionData();
                    if (sub.TryGetProperty("id", out var id))
                        subscription.Id = id.GetInt32();
                    if (sub.TryGetProperty("state", out var state))
                        subscription.State = state.GetString();
                    if (sub.TryGetProperty("current_period_started_at", out var started))
                        subscription.CurrentPeriodStartedAt = started.TryGetDateTime(out var startedTime) ? startedTime : null;
                    if (sub.TryGetProperty("current_period_ends_at", out var ends))
                        subscription.CurrentPeriodEndsAt = ends.TryGetDateTime(out var endsTime) ? endsTime : null;
                    if (sub.TryGetProperty("next_assessment_at", out var nextAssess))
                        subscription.NextAssessmentAt = nextAssess.TryGetDateTime(out var nextTime) ? nextTime : null;
                    if (sub.TryGetProperty("balance_in_cents", out var balance))
                        subscription.BalanceInCents = balance.GetInt64();
                    if (sub.TryGetProperty("activated_at", out var activated))
                        subscription.ActivatedAt = activated.TryGetDateTime(out var activatedTime) ? activatedTime : null;
                    if (sub.TryGetProperty("created_at", out var created))
                        subscription.CreatedAt = created.TryGetDateTime(out var createdTime) ? createdTime : null;
                    if (sub.TryGetProperty("product", out var product))
                    {
                        if (product.TryGetProperty("handle", out var handle))
                            subscription.ProductHandle = handle.GetString();
                        if (product.TryGetProperty("name", out var name))
                            subscription.ProductName = name.GetString();
                    }

                    subscriptions.Add(subscription);
                }
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving customer subscriptions from Maxio");
            throw;
        }
    }
}

public class SubscriptionPlanData
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int DefaultPricePointId { get; set; }
}

public class CustomerData
{
    public int Id { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class SubscriptionData
{
    public int Id { get; set; }
    public string? State { get; set; }
    public DateTime? CurrentPeriodStartedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long BalanceInCents { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}
