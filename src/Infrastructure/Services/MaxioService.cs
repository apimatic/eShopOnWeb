using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, IOptions<MaxioSettings> settings, IAppLogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUrl = _settings.GetApiBaseUrl();
        _httpClient.BaseAddress = new Uri($"{baseUrl}/");

        var authString = $"{_settings.ApiKey}:x";
        var authBytes = Encoding.ASCII.GetBytes(authString);
        var authBase64 = Convert.ToBase64String(authBytes);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authBase64}");
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("products.json");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var products = doc.RootElement.GetProperty("products");

            var plans = new List<SubscriptionPlanDto>();

            foreach (var product in products.EnumerateArray())
            {
                var familyHandle = product.TryGetProperty("product_family", out var familyElement)
                    && familyElement.TryGetProperty("handle", out var handleElement)
                    ? handleElement.GetString() ?? string.Empty
                    : string.Empty;

                if (familyHandle == _settings.ProductFamilyHandle)
                {
                    var plan = new SubscriptionPlanDto
                    {
                        Id = product.GetProperty("id").GetInt32(),
                        Handle = product.GetProperty("handle").GetString() ?? string.Empty,
                        Name = product.GetProperty("name").GetString() ?? string.Empty,
                        PricingScheme = product.GetProperty("pricing_scheme").GetString() ?? string.Empty,
                    };

                    if (product.TryGetProperty("price_in_cents", out var priceElement) && priceElement.ValueKind != JsonValueKind.Null)
                    {
                        plan.Price = priceElement.GetDecimal() / 100;
                    }

                    if (product.TryGetProperty("trial_period_days", out var trialElement) && trialElement.ValueKind != JsonValueKind.Null)
                    {
                        plan.TrialDays = trialElement.GetInt32();
                    }

                    plans.Add(plan);
                }
            }

            _logger.LogInformation("Retrieved {0} subscription plans from Maxio", plans.Count);
            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error retrieving subscription plans: {0}", ex.Message);
            throw;
        }
    }

    public async Task<int> GetOrCreateMaxioCustomerAsync(string userId, string email)
    {
        try
        {
            var customerData = new
            {
                customer = new
                {
                    email = email,
                    first_name = "User",
                    last_name = userId.Substring(0, Math.Min(20, userId.Length)),
                    reference = userId,
                }
            };

            var content = JsonContent.Create(customerData);
            var response = await _httpClient.PostAsync("customers.json", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Error creating customer: {0} - {1}", response.StatusCode, errorContent);
                throw new InvalidOperationException($"Failed to create Maxio customer: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var customerId = doc.RootElement.GetProperty("customer").GetProperty("id").GetInt32();

            _logger.LogInformation("Created/retrieved Maxio customer {0} for user {1}", customerId, userId);
            return customerId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error in GetOrCreateMaxioCustomerAsync: {0}", ex.Message);
            throw;
        }
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(int maxioCustomerId, string productHandle)
    {
        try
        {
            var products = await GetSubscriptionPlansAsync();
            var product = products.FirstOrDefault(p => p.Handle == productHandle);

            if (product == null)
            {
                throw new ArgumentException($"Product with handle '{productHandle}' not found");
            }

            var subscriptionData = new
            {
                subscription = new
                {
                    product_handle = productHandle,
                    customer_id = maxioCustomerId,
                    payment_collection_method = "automatic",
                }
            };

            var content = JsonContent.Create(subscriptionData);
            var response = await _httpClient.PostAsync("subscriptions.json", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Error creating subscription: {0} - {1}", response.StatusCode, errorContent);
                throw new InvalidOperationException($"Failed to create subscription: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var subscription = doc.RootElement.GetProperty("subscription");

            var dto = new MaxioSubscriptionDto
            {
                Id = subscription.GetProperty("id").GetInt32(),
                ProductId = subscription.GetProperty("product_id").GetInt32(),
                CustomerId = subscription.GetProperty("customer_id").GetInt32(),
                State = subscription.GetProperty("state").GetString() ?? string.Empty,
            };

            if (subscription.TryGetProperty("current_period_starts_at", out var startsElement) && startsElement.ValueKind != JsonValueKind.Null)
            {
                if (DateTime.TryParse(startsElement.GetString(), out var starts))
                {
                    dto.CurrentPeriodStartsAt = starts;
                }
            }

            if (subscription.TryGetProperty("current_period_ends_at", out var endsElement) && endsElement.ValueKind != JsonValueKind.Null)
            {
                if (DateTime.TryParse(endsElement.GetString(), out var ends))
                {
                    dto.CurrentPeriodEndsAt = ends;
                }
            }

            if (subscription.TryGetProperty("next_assessment_at", out var nextElement) && nextElement.ValueKind != JsonValueKind.Null)
            {
                if (DateTime.TryParse(nextElement.GetString(), out var next))
                {
                    dto.NextAssessmentAt = next;
                }
            }

            _logger.LogInformation("Created subscription {0} for customer {1}", dto.Id, maxioCustomerId);
            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error creating subscription: {0}", ex.Message);
            throw;
        }
    }

    public async Task<List<MaxioSubscriptionDto>> GetCustomerSubscriptionsAsync(int maxioCustomerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"customers/{maxioCustomerId}/subscriptions.json");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var subscriptions = doc.RootElement.GetProperty("subscriptions");

            var dtos = new List<MaxioSubscriptionDto>();

            foreach (var subscription in subscriptions.EnumerateArray())
            {
                var dto = new MaxioSubscriptionDto
                {
                    Id = subscription.GetProperty("id").GetInt32(),
                    ProductId = subscription.GetProperty("product_id").GetInt32(),
                    CustomerId = subscription.GetProperty("customer_id").GetInt32(),
                    State = subscription.GetProperty("state").GetString() ?? string.Empty,
                };

                if (subscription.TryGetProperty("current_period_starts_at", out var startsElement) && startsElement.ValueKind != JsonValueKind.Null)
                {
                    if (DateTime.TryParse(startsElement.GetString(), out var starts))
                    {
                        dto.CurrentPeriodStartsAt = starts;
                    }
                }

                if (subscription.TryGetProperty("current_period_ends_at", out var endsElement) && endsElement.ValueKind != JsonValueKind.Null)
                {
                    if (DateTime.TryParse(endsElement.GetString(), out var ends))
                    {
                        dto.CurrentPeriodEndsAt = ends;
                    }
                }

                if (subscription.TryGetProperty("next_assessment_at", out var nextElement) && nextElement.ValueKind != JsonValueKind.Null)
                {
                    if (DateTime.TryParse(nextElement.GetString(), out var next))
                    {
                        dto.NextAssessmentAt = next;
                    }
                }

                dtos.Add(dto);
            }

            _logger.LogInformation("Retrieved {0} subscriptions for customer {1}", dtos.Count, maxioCustomerId);
            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error retrieving customer subscriptions: {0}", ex.Message);
            throw;
        }
    }
}
