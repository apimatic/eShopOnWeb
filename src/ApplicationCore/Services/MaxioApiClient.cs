using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface IMaxioApiClient
{
    Task<SubscriptionPlanDto[]> GetProductFamilyProducts(string familyHandle);
    Task<MaxioCustomerDto?> GetOrCreateCustomer(string userId, string email, string firstName, string lastName);
    Task<SubscriptionResponseDto?> CreateSubscription(string customerReference, string productHandle);
    Task<SubscriptionResponseDto[]> GetCustomerSubscriptions(string customerReference);
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public MaxioApiClient(HttpClient httpClient, MaxioConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {EncodeBasicAuth(_config.ApiKey!, "x")}");
    }

    public async Task<SubscriptionPlanDto[]> GetProductFamilyProducts(string familyHandle)
    {
        var url = $"{GetBaseUrl()}/products.json?filter[product_family_handle]={familyHandle}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProductsResponse>(JsonOptions);
        return result?.Products?.Select(p => new SubscriptionPlanDto
        {
            Id = p.Product?.Id ?? 0,
            Handle = p.Product?.Handle,
            Name = p.Product?.Name,
            Description = p.Product?.Description,
            PriceInCents = p.Product?.PriceInCents ?? 0,
            Interval = p.Product?.Interval ?? 0,
            IntervalUnit = p.Product?.IntervalUnit
        }).ToArray() ?? Array.Empty<SubscriptionPlanDto>();
    }

    public async Task<MaxioCustomerDto?> GetOrCreateCustomer(string userId, string email, string firstName, string lastName)
    {
        try
        {
            var getUrl = $"{GetBaseUrl()}/customers/lookup.json?reference={userId}";
            var getResponse = await _httpClient.GetAsync(getUrl);

            if (getResponse.IsSuccessStatusCode)
            {
                var result = await getResponse.Content.ReadFromJsonAsync<CustomerLookupResponse>(JsonOptions);
                if (result?.Customer != null)
                {
                    return new MaxioCustomerDto
                    {
                        Id = result.Customer.Id,
                        Reference = result.Customer.Reference,
                        Email = result.Customer.Email,
                        FirstName = result.Customer.FirstName,
                        LastName = result.Customer.LastName
                    };
                }
            }
        }
        catch { }

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

        var content = new StringContent(
            JsonSerializer.Serialize(createRequest, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var createUrl = $"{GetBaseUrl()}/customers.json";
        var response = await _httpClient.PostAsync(createUrl, content);
        response.EnsureSuccessStatusCode();

        var result2 = await response.Content.ReadFromJsonAsync<CustomerResponse>(JsonOptions);
        return result2?.Customer != null ? new MaxioCustomerDto
        {
            Id = result2.Customer.Id,
            Reference = result2.Customer.Reference,
            Email = result2.Customer.Email,
            FirstName = result2.Customer.FirstName,
            LastName = result2.Customer.LastName
        } : null;
    }

    public async Task<SubscriptionResponseDto?> CreateSubscription(string customerReference, string productHandle)
    {
        var createRequest = new
        {
            subscription = new
            {
                customer_reference = customerReference,
                product_handle = productHandle
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(createRequest, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var url = $"{GetBaseUrl()}/subscriptions.json";
        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SubscriptionResponse>(JsonOptions);
        return result?.Subscription != null ? MapSubscription(result.Subscription) : null;
    }

    public async Task<SubscriptionResponseDto[]> GetCustomerSubscriptions(string customerReference)
    {
        var url = $"{GetBaseUrl()}/subscriptions.json?customer_reference={customerReference}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SubscriptionsResponse>(JsonOptions);
        return result?.Subscriptions?.Select(s => s.Subscription).Select(MapSubscription).ToArray()
            ?? Array.Empty<SubscriptionResponseDto>();
    }

    private static SubscriptionResponseDto MapSubscription(SubscriptionData subscription)
    {
        return new SubscriptionResponseDto
        {
            Id = subscription.Id,
            State = subscription.State,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            TrialStartedAt = subscription.TrialStartedAt,
            TrialEndedAt = subscription.TrialEndedAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt
        };
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(_config.BaseUrl))
        {
            return _config.BaseUrl.TrimEnd('/');
        }
        return $"https://{_config.Subdomain}.chargify.com";
    }

    private static string EncodeBasicAuth(string username, string password)
    {
        var credentials = $"{username}:{password}";
        var encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
        return encodedCredentials;
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class MaxioCustomerDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class SubscriptionResponseDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? TrialStartedAt { get; set; }
    public DateTime? TrialEndedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

#region API Response Models

internal class ProductsResponse
{
    [JsonPropertyName("products")]
    public ProductItem[]? Products { get; set; }
}

internal class ProductItem
{
    [JsonPropertyName("product")]
    public ProductData? Product { get; set; }
}

internal class ProductData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }
}

internal class CustomerLookupResponse
{
    [JsonPropertyName("customer")]
    public CustomerData? Customer { get; set; }
}

internal class CustomerResponse
{
    [JsonPropertyName("customer")]
    public CustomerData? Customer { get; set; }
}

internal class CustomerData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
}

internal class SubscriptionsResponse
{
    [JsonPropertyName("subscriptions")]
    public SubscriptionItem[]? Subscriptions { get; set; }
}

internal class SubscriptionItem
{
    [JsonPropertyName("subscription")]
    public SubscriptionData? Subscription { get; set; }
}

internal class SubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public SubscriptionData? Subscription { get; set; }
}

internal class SubscriptionData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("product")]
    public ProductData? Product { get; set; }

    [JsonPropertyName("current_period_starts_at")]
    public DateTime? CurrentPeriodStartsAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("trial_started_at")]
    public DateTime? TrialStartedAt { get; set; }

    [JsonPropertyName("trial_ended_at")]
    public DateTime? TrialEndedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

#endregion
