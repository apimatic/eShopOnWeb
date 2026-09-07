using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Linq;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService
{
    private readonly MaxioSettings _settings;
    private readonly AppIdentityDbContext _dbContext;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioSubscriptionService(
        IOptions<MaxioSettings> settings,
        AppIdentityDbContext dbContext,
        ILogger<MaxioSubscriptionService> logger,
        HttpClient httpClient)
    {
        _settings = settings.Value;
        _dbContext = dbContext;
        _logger = logger;
        _httpClient = httpClient;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    }

    private string GetAuthHeader()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        return $"Basic {credentials}";
    }

    public async Task<List<SubscriptionPlan>> GetSubscriptionPlansAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.GetBaseUrl()}/products.json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            var plans = new List<SubscriptionPlan>();
            if (root.TryGetProperty("products", out var productsArray))
            {
                foreach (var product in productsArray.EnumerateArray())
                {
                    if (product.TryGetProperty("product_family", out var family) &&
                        family.TryGetProperty("handle", out var familyHandle) &&
                        familyHandle.GetString() == _settings.ProductFamilyHandle)
                    {
                        var plan = new SubscriptionPlan
                        {
                            Handle = product.TryGetProperty("handle", out var h) ? h.GetString() ?? string.Empty : string.Empty,
                            Name = product.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                            Price = GetPriceFromProduct(product),
                            MaxioProductId = product.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                            LastSyncedAt = DateTime.UtcNow
                        };
                        plans.Add(plan);
                    }
                }
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans from Maxio");
            throw;
        }
    }

    public async Task<int> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        try
        {
            var existingSubscription = await _dbContext.UserSubscriptions
                .FirstOrDefaultAsync(us => us.UserId == userId);

            if (existingSubscription != null)
                return existingSubscription.MaxioCustomerId;

            var customerPayload = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = userId
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.GetBaseUrl()}/customers.json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));
            request.Content = new StringContent(
                JsonSerializer.Serialize(customerPayload, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customerObj) &&
                customerObj.TryGetProperty("id", out var customerId))
            {
                return customerId.GetInt32();
            }

            throw new InvalidOperationException("Failed to create Maxio customer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/retrieving customer in Maxio for user {UserId}", userId);
            throw;
        }
    }

    public async Task<UserSubscription> CreateSubscriptionAsync(
        string userId,
        int customerId,
        string productHandle,
        string email,
        string firstName,
        string lastName)
    {
        try
        {
            var subscriptionPayload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "automatic"
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.GetBaseUrl()}/subscriptions.json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));
            request.Content = new StringContent(
                JsonSerializer.Serialize(subscriptionPayload, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            if (!root.TryGetProperty("subscription", out var subscriptionObj))
                throw new InvalidOperationException("Failed to parse subscription response");

            var subscriptionId = subscriptionObj.TryGetProperty("id", out var id) ? id.GetInt32() : 0;
            var state = subscriptionObj.TryGetProperty("state", out var s) ? s.GetString() ?? "active" : "active";
            var activatedAt = subscriptionObj.TryGetProperty("activated_at", out var aa) ?
                DateTime.TryParse(aa.GetString(), out var parsedDate) ? parsedDate : DateTime.UtcNow : DateTime.UtcNow;
            var nextBillingAt = subscriptionObj.TryGetProperty("next_billing_at", out var nba) ?
                (DateTime.TryParse(nba.GetString(), out var billingDate) ? billingDate : (DateTime?)null) : null;

            var plan = await _dbContext.SubscriptionPlans
                .FirstOrDefaultAsync(sp => sp.Handle == productHandle);

            if (plan == null)
            {
                var plans = await GetSubscriptionPlansAsync();
                plan = plans.FirstOrDefault(p => p.Handle == productHandle);
                if (plan != null)
                {
                    _dbContext.SubscriptionPlans.Add(plan);
                    await _dbContext.SaveChangesAsync();
                }
            }

            var userSubscription = new UserSubscription
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                MaxioSubscriptionId = subscriptionId,
                SubscriptionPlanId = plan?.Id ?? 0,
                Status = state,
                NextBillingDate = nextBillingAt,
                StartDate = activatedAt,
                EndDate = null
            };

            _dbContext.UserSubscriptions.Add(userSubscription);
            await _dbContext.SaveChangesAsync();

            return userSubscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<UserSubscription>> GetUserSubscriptionsAsync(string userId)
    {
        try
        {
            var subscriptions = await _dbContext.UserSubscriptions
                .Where(us => us.UserId == userId)
                .ToListAsync();

            if (!subscriptions.Any())
                return new List<UserSubscription>();

            foreach (var subscription in subscriptions)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get,
                        $"{_settings.GetBaseUrl()}/subscriptions/{subscription.MaxioSubscriptionId}.json");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));

                    var response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    var jsonContent = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonContent);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("subscription", out var subObj))
                    {
                        subscription.Status = subObj.TryGetProperty("state", out var state) ?
                            state.GetString() ?? "unknown" : "unknown";
                        subscription.NextBillingDate = subObj.TryGetProperty("next_billing_at", out var nba) ?
                            (DateTime.TryParse(nba.GetString(), out var billingDate) ? billingDate : (DateTime?)null) : null;
                        subscription.EndDate = subObj.TryGetProperty("canceled_at", out var ca) ?
                            (DateTime.TryParse(ca.GetString(), out var cancelDate) ? cancelDate : (DateTime?)null) : null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error syncing subscription {SubscriptionId} from Maxio", subscription.MaxioSubscriptionId);
                }
            }

            _dbContext.UserSubscriptions.UpdateRange(subscriptions);
            await _dbContext.SaveChangesAsync();

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving subscriptions for user {UserId}", userId);
            throw;
        }
    }

    private decimal GetPriceFromProduct(JsonElement product)
    {
        if (product.TryGetProperty("default_price_in_cents", out var priceInCents))
        {
            return priceInCents.GetInt32() / 100m;
        }
        return 0;
    }
}


