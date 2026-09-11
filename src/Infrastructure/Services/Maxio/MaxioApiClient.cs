using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioApiClient(HttpClient httpClient, MaxioOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public static void ConfigureHttpClient(HttpClient client, MaxioOptions options)
    {
        var baseUrl = options.ResolveBaseUrl();
        client.BaseAddress = new Uri(baseUrl + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(string productFamilyHandle)
    {
        var url = $"product_families/handle:{productFamilyHandle}/products.json";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var products = JsonSerializer.Deserialize<List<ProductListItem>>(content, JsonOptions)
            ?? new List<ProductListItem>();

        var plans = new List<SubscriptionPlan>();
        foreach (var item in products)
        {
            var p = item.Product;
            plans.Add(new SubscriptionPlan
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                PriceInCents = p.PriceInCents,
                IntervalUnit = p.IntervalUnit,
                Interval = p.Interval,
                RequireCreditCard = p.RequireCreditCard,
                ProductFamilyHandle = p.ProductFamily?.Handle ?? productFamilyHandle
            });
        }
        return plans;
    }

    public async Task<int?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<CustomerWrapper>(content, JsonOptions);
        return wrapper?.Customer?.Id;
    }

    public async Task<int> CreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        var request = new
        {
            Customer = new
            {
                First_name = firstName,
                Last_name = lastName,
                Email = email,
                Reference = reference
            }
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("customers.json", content);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Maxio customer creation failed ({response.StatusCode}): {errorBody}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<CustomerWrapper>(responseBody, JsonOptions);
        return wrapper?.Customer?.Id
            ?? throw new InvalidOperationException("Maxio customer created but no ID returned.");
    }

    public async Task<UserSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        var request = new
        {
            Subscription = new
            {
                Product_handle = productHandle,
                Customer_id = customerId,
                Payment_collection_method = "remittance"
            }
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("subscriptions.json", content);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Maxio subscription creation failed ({response.StatusCode}): {errorBody}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<SubscriptionWrapper>(responseBody, JsonOptions);
        return MapSubscription(wrapper?.Subscription
            ?? throw new InvalidOperationException("Maxio subscription created but no data returned."));
    }

    public async Task<IReadOnlyList<UserSubscription>> GetSubscriptionsForCustomerAsync(int customerId)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var items = JsonSerializer.Deserialize<List<SubscriptionListItem>>(content, JsonOptions)
            ?? new List<SubscriptionListItem>();

        var subscriptions = new List<UserSubscription>();
        foreach (var item in items)
        {
            subscriptions.Add(MapSubscription(item.Subscription));
        }
        return subscriptions;
    }

    private static UserSubscription MapSubscription(MaxioSubscription sub)
    {
        return new UserSubscription
        {
            Id = sub.Id,
            State = sub.State,
            PlanName = sub.Product?.Name ?? string.Empty,
            PlanHandle = sub.Product?.Handle ?? string.Empty,
            PriceInCents = sub.Product?.PriceInCents ?? 0,
            NextBillingAt = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CanceledAt = sub.CanceledAt,
            ExpiresAt = sub.ExpiresAt,
            CustomerId = sub.Customer?.Id ?? sub.CustomerId ?? 0,
            ProductId = sub.Product?.Id ?? sub.ProductId ?? 0
        };
    }

    #region JSON DTOs

    private class ProductListItem
    {
        [JsonPropertyName("product")]
        public MaxioProduct Product { get; set; } = new();
    }

    private class MaxioProduct
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("handle")]
        public string Handle { get; set; } = string.Empty;
        [JsonPropertyName("price_in_cents")]
        public int PriceInCents { get; set; }
        [JsonPropertyName("interval")]
        public int Interval { get; set; }
        [JsonPropertyName("interval_unit")]
        public string IntervalUnit { get; set; } = string.Empty;
        [JsonPropertyName("require_credit_card")]
        public bool RequireCreditCard { get; set; }
        [JsonPropertyName("product_family")]
        public MaxioProductFamily? ProductFamily { get; set; }
    }

    private class MaxioProductFamily
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("handle")]
        public string Handle { get; set; } = string.Empty;
    }

    private class CustomerWrapper
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private class MaxioCustomer
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;
        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }
    }

    private class SubscriptionListWrapper
    {
        [JsonPropertyName("items")]
        public List<SubscriptionListItem> Items { get; set; } = new();
    }

    private class SubscriptionListItem
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription Subscription { get; set; } = new();
    }

    private class SubscriptionWrapper
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription Subscription { get; set; } = new();
    }

    private class MaxioSubscription
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;
        [JsonPropertyName("customer_id")]
        public int? CustomerId { get; set; }
        [JsonPropertyName("product_id")]
        public int? ProductId { get; set; }
        [JsonPropertyName("next_assessment_at")]
        public DateTime? NextAssessmentAt { get; set; }
        [JsonPropertyName("activated_at")]
        public DateTime? ActivatedAt { get; set; }
        [JsonPropertyName("canceled_at")]
        public DateTime? CanceledAt { get; set; }
        [JsonPropertyName("expires_at")]
        public DateTime? ExpiresAt { get; set; }
        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    #endregion
}
