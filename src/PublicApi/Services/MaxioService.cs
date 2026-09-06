using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IRepository<MaxioCustomer> _maxioCustomerRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IAppLogger<MaxioService> _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public MaxioService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IRepository<MaxioCustomer> maxioCustomerRepository,
        IRepository<UserSubscription> userSubscriptionRepository,
        IAppLogger<MaxioService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _configuration = configuration;
        _maxioCustomerRepository = maxioCustomerRepository;
        _userSubscriptionRepository = userSubscriptionRepository;
        _logger = logger;

        _apiKey = configuration["Maxio:ApiKey"] ?? "";
        var subdomain = configuration["Maxio:Subdomain"] ?? "";
        var baseUrlOverride = configuration["Maxio:BaseUrl"];

        _baseUrl = baseUrlOverride ?? $"https://{subdomain}.chargify.com";

        var authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiKey}:x"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authHeader}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync()
    {
        try
        {
            var productFamilyHandle = _configuration["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
            var response = await _httpClient.GetAsync($"{_baseUrl}/product_families/{productFamilyHandle}.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            var plans = new List<SubscriptionPlanDto>();

            if (jsonDoc.RootElement.TryGetProperty("product_family", out var family) &&
                family.TryGetProperty("products", out var products))
            {
                foreach (var product in products.EnumerateArray())
                {
                    var productId = product.GetProperty("id").GetInt64();
                    var handle = product.GetProperty("handle").GetString() ?? "";
                    var name = product.GetProperty("name").GetString() ?? "";
                    var priceInCents = product.GetProperty("price_in_cents").GetInt32();
                    var interval = product.GetProperty("interval").GetInt32();
                    var intervalUnit = product.GetProperty("interval_unit").GetString() ?? "month";

                    plans.Add(new SubscriptionPlanDto
                    {
                        ProductId = productId,
                        Handle = handle,
                        Name = name,
                        PriceInCents = priceInCents,
                        Interval = interval,
                        IntervalUnit = intervalUnit
                    });
                }
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"Error fetching plans from Maxio: {ex.Message}");
            throw;
        }
    }

    public async Task<(long MaxioCustomerId, bool IsNew)> EnsureCustomerExistsAsync(
        string userId, string firstName, string lastName, string email)
    {
        try
        {
            var existing = await _maxioCustomerRepository.FirstOrDefaultAsync(
                new MaxioCustomerByUserIdSpec(userId));

            if (existing != null)
            {
                return (existing.MaxioCustomerId, false);
            }

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

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/customers.json", customerData);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            var customerId = jsonDoc.RootElement.GetProperty("customer").GetProperty("id").GetInt64();

            var maxioCustomer = new MaxioCustomer
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _maxioCustomerRepository.AddAsync(maxioCustomer);

            return (customerId, true);
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"Error ensuring Maxio customer exists: {ex.Message}");
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string userId, long maxioCustomerId, string productHandle)
    {
        try
        {
            var subscriptionData = new
            {
                subscription = new
                {
                    customer_id = maxioCustomerId,
                    product_handle = productHandle
                }
            };

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/subscriptions.json", subscriptionData);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            var sub = jsonDoc.RootElement.GetProperty("subscription");

            var subscriptionDto = ParseSubscription(sub);

            var subscriptionId = sub.GetProperty("id").GetInt64();
            var userSubscription = new UserSubscription
            {
                UserId = userId,
                MaxioSubscriptionId = subscriptionId,
                ProductHandle = sub.GetProperty("product_handle").GetString() ?? "",
                PlanName = sub.GetProperty("product_name").GetString() ?? "",
                PriceInCents = sub.GetProperty("price_in_cents").GetDecimal(),
                State = sub.GetProperty("state").GetString() ?? "",
                NextBillingDate = ParseDate(sub, "next_billing_date"),
                CurrentPeriodStartsAt = ParseDate(sub, "current_period_starts_at"),
                CurrentPeriodEndsAt = ParseDate(sub, "current_period_ends_at"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _userSubscriptionRepository.AddAsync(userSubscription);

            return subscriptionDto;
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"Error creating subscription: {ex.Message}");
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, long maxioCustomerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/customers/{maxioCustomerId}/subscriptions.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            var subscriptions = new List<SubscriptionDto>();

            if (jsonDoc.RootElement.TryGetProperty("subscriptions", out var subs))
            {
                foreach (var sub in subs.EnumerateArray())
                {
                    subscriptions.Add(ParseSubscription(sub));

                    var subscriptionId = sub.GetProperty("id").GetInt64();
                    var existing = await _userSubscriptionRepository.FirstOrDefaultAsync(
                        new UserSubscriptionByMaxioIdSpec(userId, subscriptionId));

                    if (existing == null)
                    {
                        var userSubscription = new UserSubscription
                        {
                            UserId = userId,
                            MaxioSubscriptionId = subscriptionId,
                            ProductHandle = sub.GetProperty("product_handle").GetString() ?? "",
                            PlanName = sub.GetProperty("product_name").GetString() ?? "",
                            PriceInCents = sub.GetProperty("price_in_cents").GetDecimal(),
                            State = sub.GetProperty("state").GetString() ?? "",
                            NextBillingDate = ParseDate(sub, "next_billing_date"),
                            CurrentPeriodStartsAt = ParseDate(sub, "current_period_starts_at"),
                            CurrentPeriodEndsAt = ParseDate(sub, "current_period_ends_at"),
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        await _userSubscriptionRepository.AddAsync(userSubscription);
                    }
                }
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"Error fetching user subscriptions: {ex.Message}");
            throw;
        }
    }

    private static SubscriptionDto ParseSubscription(JsonElement sub)
    {
        return new SubscriptionDto
        {
            SubscriptionId = sub.GetProperty("id").GetInt64(),
            ProductHandle = sub.GetProperty("product_handle").GetString() ?? "",
            PlanName = sub.GetProperty("product_name").GetString() ?? "",
            PriceInCents = sub.GetProperty("price_in_cents").GetDecimal(),
            State = sub.GetProperty("state").GetString() ?? "",
            NextBillingDate = ParseDate(sub, "next_billing_date"),
            CurrentPeriodStartsAt = ParseDate(sub, "current_period_starts_at"),
            CurrentPeriodEndsAt = ParseDate(sub, "current_period_ends_at")
        };
    }

    private static DateTime? ParseDate(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var dateElement) && dateElement.ValueKind != JsonValueKind.Null)
        {
            if (DateTime.TryParse(dateElement.GetString(), out var date))
            {
                return date;
            }
        }
        return null;
    }
}
