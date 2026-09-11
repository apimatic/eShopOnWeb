using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUrl = !string.IsNullOrEmpty(_settings.BaseUrl)
            ? _settings.BaseUrl.TrimEnd('/')
            : $"https://{_settings.Subdomain}.chargify.com";

        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        // Idempotent lookup by reference
        var lookupResponse = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        if (lookupResponse.IsSuccessStatusCode)
        {
            var result = await lookupResponse.Content.ReadFromJsonAsync<CustomerWrapper>(JsonOptions);
            if (result?.Customer != null)
            {
                var c = result.Customer;
                return new MaxioCustomer(c.Id, c.Reference ?? reference, c.Email, c.FirstName, c.LastName);
            }
        }

        // Create customer
        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference
            }
        };
        var createResponse = await 
_httpClient.PostAsJsonAsync("customers.json", payload, JsonOptions);
        if (!createResponse.IsSuccessStatusCode)
        {
            var err = await createResponse.Content.ReadAsStringAsync();
            _logger.LogError("Maxio create customer failed: {Status} {Body}", createResponse.StatusCode, err);
            throw new InvalidOperationException($"Failed to create Maxio customer: {createResponse.StatusCode}");
        }
        var created = await createResponse.Content.ReadFromJsonAsync<CustomerWrapper>(JsonOptions);
        var customer = created!.Customer;
        return new MaxioCustomer(customer.Id, customer.Reference ?? reference, customer.Email, customer.FirstName, customer.LastName);
    }

    public async Task<MaxioSubscription> SubscribeAsync(string customerReference, string productHandle)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference
            }
        };
        var response = await 
_httpClient.PostAsJsonAsync("subscriptions.json", payload, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Maxio subscribe failed: {Status} {Body}", response.StatusCode, err);
            throw new InvalidOperationException($"Failed to create Maxio subscription: {response.StatusCode}");
        }
        var sub = await response.Content.ReadFromJsonAsync<SubscriptionWrapper>(JsonOptions);
        var s = sub!.Subscription;
        return MapSubscription(s);
    }

    public async Task<List<MaxioSubscription>> ListSubscriptionsAsync(string customerReference)
    {
        // First find customer by reference to get id
        var lookup = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}");
        if (!lookup.IsSuccessStatusCode)
            return new List<MaxioSubscription>();
        var customerResp = await lookup.Content.ReadFromJsonAsync<CustomerWrapper>(JsonOptions);
        if (customerResp?.Customer == null)
            return new List<MaxioSubscription>();

        var subsResp = await _httpClient.GetAsync($"customers/{customerResp.Customer.Id}/subscriptions.json");
        if (!subsResp.IsSuccessStatusCode)
            return new List<MaxioSubscription>();
        var list = await subsResp.Content.ReadFromJsonAsync<SubscriptionsListResponse>(JsonOptions);
        var result = new List<MaxioSubscription>();
        if (list?.Items != null)
        {
            foreach (var item in list.Items)
                result.Add(MapSubscription(item.Subscription));
        }
        return result;
    }

    public async Task<List<MaxioPlan>> ListPlansAsync()
    {
        var familyHandle = $"handle:{_settings.ProductFamilyHandle}";
        var response = await _httpClient.GetAsync($"product_families/{familyHandle}/products.json?per_page=50");
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio list plans failed: {Status}", response.StatusCode);
            return new List<MaxioPlan>();
        }
        var items = await response.Content.ReadFromJsonAsync<List<ProductItem>>(JsonOptions);
        var plans = new List<MaxioPlan>();
        if (items != null)
        {
            foreach (var item in items)
            {
                var p = item.Product;
                plans.Add(new MaxioPlan(p.Id, p.Handle ?? string.Empty, p.Name ?? string.Empty, p.PriceInCents / 100m, p.Interval, p.IntervalUnit ?? "month"));
            }
        }
        return plans;
    }

    private static MaxioSubscription MapSubscription(SubscriptionData s)
    {
        return new MaxioSubscription(
            s.Id,
            s.State,
            s.Product?.Id ?? 0,
            s.Product?.Handle ?? string.Empty,
            s.Product?.Name ?? string.Empty,
            s.ProductPriceInCents / 100m,
            s.CurrentPeriodEndsAt,
            s.NextAssessmentAt
        );
    }

    private class CustomersListResponse
    {
        public List<CustomerItem> Items { get; set; } = new();
    }

    private class CustomerItem
    {
        public CustomerData Customer { get; set; } = new();
    }

    private class CustomerWrapper
    {
        public CustomerData Customer { get; set; } = new();
    }

    private class CustomerData
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }

    private class SubscriptionsListResponse
    {
        public List<SubscriptionItem> Items { get; set; } = new();
    }

    private class SubscriptionItem
    {
        public SubscriptionData Subscription { get; set; } = new();
    }

    private class SubscriptionWrapper
    {
        public SubscriptionData Subscription { get; set; } = new();
    }

    private class SubscriptionData
    {
        public int Id { get; set; }
        public string State { get; set; } = string.Empty;
        public ProductData? Product { get; set; }
        public int ProductPriceInCents { get; set; }
        public string? CurrentPeriodEndsAt { get; set; }
        public string? NextAssessmentAt { get; set; }
    }

    private class ProductData
    {
        public int Id { get; set; }
        public string? Handle { get; set; }
        public string? Name { get; set; }
        public int PriceInCents { get; set; }
        public int Interval { get; set; }
        public string? IntervalUnit { get; set; }
    }

    private class ProductsListResponse
    {
        public List<ProductItem> Items { get; set; } = new();
    }

    private class ProductItem
    {
        public ProductData Product { get; set; } = new();
    }
}
