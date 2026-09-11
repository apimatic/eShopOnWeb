using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.eShopWeb;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _client;
    private readonly MaxioSettings _settings;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public MaxioBillingService(HttpClient client, IOptions<MaxioSettings> options)
    {
        _client = client;
        _settings = options.Value;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));
    }

    public async Task<MaxioCustomer?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        var reference = userId;
        // Try lookup by reference
        try
        {
            var lookup = await _client.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            if (lookup.IsSuccessStatusCode)
            {
                var doc = await JsonDocument.ParseAsync(await lookup.Content.ReadAsStreamAsync());
                var cust = doc.RootElement.GetProperty("customer");
                return new MaxioCustomer
                {
                    Id = cust.GetProperty("id").GetInt32(),
                    Reference = cust.GetProperty("reference").GetString() ?? reference,
                    Email = cust.GetProperty("email").GetString() ?? email,
                    FirstName = cust.GetProperty("first_name").GetString() ?? firstName,
                    LastName = cust.GetProperty("last_name").GetString() ?? lastName
                };
            }
        }
        catch { /* fall through to create */ }

        var body = new { customer = new { first_name = firstName, last_name = lastName, email = email, reference = reference } };
        var response = await _client.PostAsync("/customers.json", new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var respDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var c = respDoc.RootElement.GetProperty("customer");
        return new MaxioCustomer
        {
            Id = c.GetProperty("id").GetInt32(),
            Reference = c.GetProperty("reference").GetString() ?? reference,
            Email = c.GetProperty("email").GetString() ?? email,
            FirstName = c.GetProperty("first_name").GetString() ?? firstName,
            LastName = c.GetProperty("last_name").GetString() ?? lastName
        };
    }

    public async Task<SubscriptionInfo?> SubscribeAsync(string userId, string planHandle)
    {
        // Ensure customer
        // For simplicity use a fixed demo profile; in real app get from token claims
        var customer = await GetOrCreateCustomerAsync(userId, $"{userId}@example.com", "Shopper", "User");
        if (customer == null) return null;

        // Deferred billing (future date) avoids card capture in sandbox when require_credit_card is true
        var nextBilling = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:sszzz");
        var body = new
        {
            subscription = new
            {
                product_handle = planHandle,
                customer_reference = customer.Reference,
                next_billing_at = nextBilling
            }
        };

        var response = await _client.PostAsync("/subscriptions.json", new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var respDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var sub = respDoc.RootElement.GetProperty("subscription");
        return new SubscriptionInfo
        {
            Id = sub.GetProperty("id").GetInt32(),
            State = sub.GetProperty("state").GetString() ?? "",
            ProductHandle = sub.GetProperty("product").GetProperty("handle").GetString() ?? planHandle,
            ProductName = sub.GetProperty("product").GetProperty("name").GetString() ?? planHandle,
            PriceInCents = sub.GetProperty("product_price_in_cents").GetInt32(),
            NextBillingAt = sub.GetProperty("current_period_ends_at").GetString() ?? nextBilling,
            CustomerReference = sub.GetProperty("customer").GetProperty("reference").GetString() ?? customer.Reference,
            CustomerId = sub.GetProperty("customer").GetProperty("id").GetInt32()
        };
    }

    public async Task<List<SubscriptionInfo>> GetMySubscriptionsAsync(string userId)
    {
        var reference = userId;
        var response = await _client.GetAsync($"/subscriptions.json?customer_reference={Uri.EscapeDataString(reference)}");
        if (!response.IsSuccessStatusCode) return new List<SubscriptionInfo>();
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var results = new List<SubscriptionInfo>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("subscription", out var sub))
                    results.Add(MapSubscription(sub));
            }
        }
        else if (doc.RootElement.TryGetProperty("subscriptions", out var subsArray))
        {
            foreach (var item in subsArray.EnumerateArray())
            {
                if (item.TryGetProperty("subscription", out var sub))
                    results.Add(MapSubscription(sub));
            }
        }
        else if (doc.RootElement.TryGetProperty("subscription", out var singleSub))
        {
            results.Add(MapSubscription(singleSub));
        }
        return results;
    }

    public async Task<List<PlanInfo>> GetPlansAsync()
    {
        var familyHandle = _settings.ProductFamilyHandle;
        // First get family id by handle via lookup or list; use list with filter not available directly.
        // We'll fetch products under family by filtering manually from /products.json with family handle not directly supported.
        // Alternative: call /product_families.json and find handle.
        var famResp = await _client.GetAsync("/product_families.json");
        int familyId = 0;
        if (famResp.IsSuccessStatusCode)
        {
            var famDoc = await JsonDocument.ParseAsync(await famResp.Content.ReadAsStreamAsync());
            if (famDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in famDoc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("product_family", out var pf))
                    {
                        if (pf.GetProperty("handle").GetString() == familyHandle)
                        {
                            familyId = pf.GetProperty("id").GetInt32();
                            break;
                        }
                    }
                }
            }
        }
        if (familyId == 0) return new List<PlanInfo>();

        var prodResp = await _client.GetAsync($"/products.json?product_family_id={familyId}");
        var plans = new List<PlanInfo>();
        if (!prodResp.IsSuccessStatusCode) return plans;
        var prodDoc = await JsonDocument.ParseAsync(await prodResp.Content.ReadAsStreamAsync());
        if (prodDoc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in prodDoc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("product", out var p))
                {
                    plans.Add(new PlanInfo
                    {
                        Id = p.GetProperty("id").GetInt32(),
                        Handle = p.GetProperty("handle").GetString() ?? "",
                        Name = p.GetProperty("name").GetString() ?? "",
                        PriceInCents = p.GetProperty("price_in_cents").GetInt32(),
                        IntervalUnit = p.GetProperty("interval_unit").GetString() ?? ""
                    });
                }
            }
        }
        return plans;
    }

    private static SubscriptionInfo MapSubscription(JsonElement sub)
    {
        return new SubscriptionInfo
        {
            Id = sub.GetProperty("id").GetInt32(),
            State = sub.GetProperty("state").GetString() ?? "",
            ProductHandle = sub.TryGetProperty("product", out var prod) ? prod.GetProperty("handle").GetString() ?? "" : "",
            ProductName = sub.TryGetProperty("product", out var prod2) ? prod2.GetProperty("name").GetString() ?? "" : "",
            PriceInCents = sub.GetProperty("product_price_in_cents").GetInt32(),
            NextBillingAt = sub.GetProperty("current_period_ends_at").GetString() ?? "",
            CustomerReference = sub.TryGetProperty("customer", out var cust) ? cust.GetProperty("reference").GetString() ?? "" : "",
            CustomerId = sub.TryGetProperty("customer", out var cust2) ? cust2.GetProperty("id").GetInt32() : 0
        };
    }
}
