using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _http;
    private readonly MaxioOptions _opts;
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioService(IHttpClientFactory factory, IOptions<MaxioOptions> options)
    {
        _opts = options.Value;
        _http = factory.CreateClient("maxio");
    }

    private string BaseUrl => DeriveBaseUrl(_opts);

    private static string DeriveBaseUrl(MaxioOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
            return opts.BaseUrl.TrimEnd('/');
        var env = (opts.Environment ?? "US").ToUpperInvariant();
        var host = env == "EU" ? $"{opts.Subdomain}.ebilling.maxio.com" : $"{opts.Subdomain}.chargify.com";
        return $"https://{host}";
    }

    public async Task<List<PlanDto>> GetPlansAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/products.json", ct);
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var plans = new List<PlanDto>();
        var familyHandle = _opts.ProductFamilyHandle;
        if (doc?.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var plan = MapProduct(el);
                if (string.Equals(plan.FamilyHandle, familyHandle, StringComparison.OrdinalIgnoreCase))
                    plans.Add(plan);
            }
        }
        else if (doc?.RootElement.TryGetProperty("products", out var arr) == true && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                var plan = MapProduct(el);
                if (string.Equals(plan.FamilyHandle, familyHandle, StringComparison.OrdinalIgnoreCase))
                    plans.Add(plan);
            }
        }
        return plans;
    }

    public async Task<CustomerDto?> GetOrCreateCustomerAsync(string userId, string email, string? firstName = null, string? lastName = null, CancellationToken ct = default)
    {
        // Idempotent: lookup by reference = userId
        try
        {
            var lookup = await _http.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(userId)}", ct);
            if (lookup.IsSuccessStatusCode)
            {
                var doc = await lookup.Content.ReadFromJsonAsync<JsonDocument>(ct);
                if (doc != null)
                    return MapCustomer(doc.RootElement);
            }
        }
        catch { /* ignore lookup errors */ }

        var body = new
        {
            customer = new
            {
                email,
                first_name = firstName ?? "User",
                last_name = lastName ?? "Customer",
                reference = userId
            }
        };
        var post = await _http.PostAsJsonAsync("/customers.json", body, JsonOpts, ct);
        if (post.IsSuccessStatusCode || post.StatusCode == System.Net.HttpStatusCode.Created)
        {
            var doc = await post.Content.ReadFromJsonAsync<JsonDocument>(ct);
            if (doc != null) return MapCustomer(doc.RootElement);
        }
        // If already exists (409/422), try lookup again
        try
        {
            var lookup2 = await _http.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(userId)}", ct);
            if (lookup2.IsSuccessStatusCode)
            {
                var doc = await lookup2.Content.ReadFromJsonAsync<JsonDocument>(ct);
                if (doc != null) return MapCustomer(doc.RootElement);
            }
        }
        catch { }
        return null;
    }

    public async Task<List<SubscriptionDto>> GetSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        var customer = await GetOrCreateCustomerAsync(userId, $"{userId}@eshop.local", ct: ct);
        if (customer == null) return new List<SubscriptionDto>();
        var resp = await _http.GetAsync($"/customers/{customer.Id}/subscriptions.json", ct);
        if (!resp.IsSuccessStatusCode) return new List<SubscriptionDto>();
        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var subs = new List<SubscriptionDto>();
        if (doc?.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in doc.RootElement.EnumerateArray())
                subs.Add(MapSubscription(el));
        }
        return subs;
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string productHandle, CancellationToken ct = default)
    {
        // Idempotent: if user already has active subscription to this product handle, return it
        var existing = await GetSubscriptionsAsync(userId, ct);
        var found = existing.FirstOrDefault(s => s.ProductHandle == productHandle);
        if (found != null) return found;

        var customer = await GetOrCreateCustomerAsync(userId, $"{userId}@eshop.local", ct: ct);
        if (customer == null) throw new InvalidOperationException("Customer not found or created.");

        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customer.Id
            }
        };
        var resp = await _http.PostAsJsonAsync("/subscriptions.json", body, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc == null) throw new InvalidOperationException("Subscription response empty.");
        // Response may wrap in "subscription" key
        if (doc.RootElement.TryGetProperty("subscription", out var subEl))
            return MapSubscription(subEl);
        return MapSubscription(doc.RootElement);
    }

    private static PlanDto MapProduct(JsonElement el)
    {
        // Try wrapped form "product" or direct
        var root = el;
        if (el.TryGetProperty("product", out var p)) root = p;
        return new PlanDto
        {
            Id = root.GetProperty("id").GetInt32(),
            Name = root.GetProperty("name").GetString() ?? "",
            Handle = root.GetProperty("handle").GetString() ?? "",
            Price = root.TryGetProperty("price_in_cents", out var pc) ? pc.GetInt32() / 100m : 0,
            PriceUnit = "month",
            FamilyHandle = root.TryGetProperty("product_family", out var fam) && fam.TryGetProperty("handle", out var fh) ? fh.GetString() ?? "" : ""
        };
    }

    private static CustomerDto MapCustomer(JsonElement el)
    {
        var root = el;
        if (el.TryGetProperty("customer", out var c)) root = c;
        return new CustomerDto
        {
            Id = root.GetProperty("id").GetInt32(),
            Email = root.GetProperty("email").GetString() ?? "",
            Reference = root.TryGetProperty("reference", out var r) ? (r.GetString() ?? "") : "",
            FirstName = root.TryGetProperty("first_name", out var fn) ? fn.GetString() : null,
            LastName = root.TryGetProperty("last_name", out var ln) ? ln.GetString() : null
        };
    }

    private static SubscriptionDto MapSubscription(JsonElement el)
    {
        var root = el;
        if (el.TryGetProperty("subscription", out var s)) root = s;
        return new SubscriptionDto
        {
            Id = root.GetProperty("id").GetInt32(),
            State = root.GetProperty("state").GetString() ?? "",
            ProductHandle = root.TryGetProperty("product", out var prod) && prod.TryGetProperty("handle", out var ph) ? ph.GetString() ?? "" : "",
            ProductName = root.TryGetProperty("product", out var prodN) && prodN.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "",
            Price = root.TryGetProperty("product_price_in_cents", out var ppc) ? ppc.GetInt32() / 100m : 0,
            NextBillingDate = root.TryGetProperty("current_period_ends_at", out var cpe) && cpe.ValueKind == JsonValueKind.String ? DateTime.TryParse(cpe.GetString(), out var d) ? d : (DateTime?)null : null,
            ActivatedAt = root.TryGetProperty("activated_at", out var act) && act.ValueKind == JsonValueKind.String ? DateTime.TryParse(act.GetString(), out var da) ? da : (DateTime?)null : null
        };
    }
}
