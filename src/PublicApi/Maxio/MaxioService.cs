using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioService
{
    private readonly HttpClient _client;
    private readonly MaxioOptions _options;

    public MaxioService(HttpClient client, MaxioOptions options)
    {
        _client = client;
        _options = options;
        _client.BaseAddress = new Uri(DeriveBaseUrl());
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x")));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string DeriveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
            return _options.BaseUrl.TrimEnd('/');
        return $"https://{_options.Subdomain}.chargify.com";
    }

    public async Task<List<ProductPlan>> GetPlansAsync(CancellationToken ct = default)
    {
        var url = $"/products.json?product_family_handle={_options.ProductFamilyHandle}";
        var resp = await _client.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadAsStringAsync(ct);
        var arr = JsonSerializer.Deserialize<List<ProductWrapper>>(doc, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return arr?.Select(w => w.Product).ToList() ?? new();
    }

    public async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var doc = await resp.Content.ReadAsStringAsync(ct);
                var c = JsonSerializer.Deserialize<CustomerWrapper>(doc, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return c?.Customer;
            }
        }
        catch { }
        return null;
    }

    public async Task<Customer> CreateCustomerAsync(string email, string firstName, string lastName, string reference, CancellationToken ct = default)
    {
        var payload = new { customer = new { email, first_name = firstName, last_name = lastName, reference } };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        var resp = await _client.PostAsync("/customers.json", new StringContent(json, Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadAsStringAsync(ct);
        var c = JsonSerializer.Deserialize<CustomerWrapper>(doc, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return c!.Customer;
    }

    public async Task<List<Subscription>> GetSubscriptionsForCustomerAsync(int customerId, CancellationToken ct = default)
    {
        var resp = await _client.GetAsync($"/subscriptions.json?customer_id={customerId}", ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadAsStringAsync(ct);
        var arr = JsonSerializer.Deserialize<List<SubscriptionWrapper>>(doc, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return arr?.Select(s => s.Subscription).ToList() ?? new();
    }

    public async Task<Subscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                credit_card_attributes = new
                {
                    full_number = "4111111111111111",
                    first_name = "Test",
                    last_name = "User",
                    expiration_month = "12",
                    expiration_year = "2030",
                    cvv = "123"
                }
            }
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        var resp = await _client.PostAsync("/subscriptions.json", new StringContent(json, Encoding.UTF8, "application/json"), ct);
        // If no card needed, might succeed without; if 422 payment error, fall back not needed because we include card.
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadAsStringAsync(ct);
        var s = JsonSerializer.Deserialize<SubscriptionWrapper>(doc, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return s!.Subscription;
    }

    public class ProductPlan
    {
        public int Id { get; set; }
        public string Handle { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int PriceInCents { get; set; }
        public string IntervalUnit { get; set; } = string.Empty;
    }

    public class Customer
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }

    public class Subscription
    {
        public int Id { get; set; }
        public string State { get; set; } = string.Empty;
        public int? ProductId { get; set; }
        public string ProductHandle { get; set; } = string.Empty;
        public int CustomerId { get; set; }
        public string NextAssessmentAt { get; set; } = string.Empty;
        public int BalanceInCents { get; set; }
    }

    private class ProductWrapper { public ProductPlan Product { get; set; } = new(); }
    private class CustomerWrapper { public Customer Customer { get; set; } = new(); }
    private class SubscriptionWrapper { public Subscription Subscription { get; set; } = new(); }
}
