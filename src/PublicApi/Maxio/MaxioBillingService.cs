using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioBillingService
{
    Task<List<SubscriptionPlanDto>> GetPlansAsync();
    Task<SubscriptionDto?> GetSubscriptionAsync(string customerReference);
    Task<List<SubscriptionDto>> GetMySubscriptionsAsync(string userId);
    Task<SubscriptionResultDto> SubscribeAsync(string userId, string userEmail, string planHandle);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PricePoint { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
}

public class SubscriptionResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public SubscriptionDto? Subscription { get; set; }
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _http;
    private readonly MaxioOptions _opts;

    public MaxioBillingService(IOptions<MaxioOptions> opts)
    {
        _opts = opts.Value;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_opts.ApiKey}:")));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string BaseUrl => !string.IsNullOrWhiteSpace(_opts.BaseUrl)
        ? _opts.BaseUrl.TrimEnd('/')
        : $"https://{_opts.Subdomain}.chargify.com";

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync()
    {
        // Fetch product family by handle, then products/components inside it
        var family = await _http.GetFromJsonAsync<JsonElement>($"{BaseUrl}/api/v1/product_families/handle/{_opts.ProductFamilyHandle}.json");
        var products = new List<SubscriptionPlanDto>();
        if (family.TryGetProperty("product_family", out var pf) && pf.TryGetProperty("products", out var prods))
        {
            foreach (var p in prods.EnumerateArray())
            {
                var handle = p.GetProperty("handle").GetString() ?? "";
                var name = p.GetProperty("name").GetString() ?? "";
                var price = 0m;
                if (p.TryGetProperty("product_price_points", out var pp) && pp.GetArrayLength() > 0)
                {
                    var first = pp[0];
                    if (first.TryGetProperty("price_in_cents", out var cents))
                        price = cents.GetInt32() / 100m;
                }
                products.Add(new SubscriptionPlanDto { Id = p.GetProperty("id").GetInt32(), Handle = handle, Name = name, Price = price, PricePoint = handle });
            }
        }
        return products;
    }

    public async Task<SubscriptionDto?> GetSubscriptionAsync(string customerReference)
    {
        // List subscriptions by customer reference using customer lookup then subscriptions
        var subs = await _http.GetFromJsonAsync<JsonElement>($"{BaseUrl}/api/v1/subscriptions.json?reference={customerReference}");
        if (subs.TryGetProperty("subscriptions", out var arr) && arr.GetArrayLength() > 0)
        {
            var first = arr[0];
            return new SubscriptionDto
            {
                Id = first.GetProperty("id").GetInt32(),
                State = first.GetProperty("state").GetString() ?? "",
                PlanHandle = first.GetProperty("product_handle").GetString() ?? "",
                Amount = first.GetProperty("amount_in_cents").GetInt32() / 100m,
                NextBillingDate = first.TryGetProperty("next_billing_at", out var nb) && nb.ValueKind != JsonValueKind.Null ? nb.GetDateTime() : null,
                CustomerReference = customerReference
            };
        }
        return null;
    }

    public async Task<List<SubscriptionDto>> GetMySubscriptionsAsync(string userId)
    {
        // Idempotent mapping: use userId as Maxio customer reference
        var result = new List<SubscriptionDto>();
        var sub = await GetSubscriptionAsync(userId);
        if (sub != null) result.Add(sub);
        return result;
    }

    public async Task<SubscriptionResultDto> SubscribeAsync(string userId, string userEmail, string planHandle)
    {
        try
        {
            // Ensure customer exists idempotently by reference = userId / email
            var customerPayload = new { customer = new { email = userEmail, reference = userId } };
            var customerResponse = await _http.PostAsJsonAsync($"{BaseUrl}/api/v1/customers.json", customerPayload);
            if (!customerResponse.IsSuccessStatusCode)
            {
                // If customer exists (422/409), try to read by reference
                var existing = await _http.GetFromJsonAsync<JsonElement>($"{BaseUrl}/api/v1/customers/lookup.json?reference={userId}");
                if (existing.TryGetProperty("customer", out _)) { /* ok */ }
            }

            // Find product handle -> product id if needed; use handle for subscription
            // Create subscription
            var subPayload = new { subscription = new { product_handle = planHandle, customer_reference = userId } };
            var subResponse = await _http.PostAsJsonAsync($"{BaseUrl}/api/v1/subscriptions.json", subPayload);
            if (!subResponse.IsSuccessStatusCode)
            {
                var err = await subResponse.Content.ReadAsStringAsync();
                return new SubscriptionResultDto { Success = false, Message = $"Subscription failed: {err}" };
            }
            var subDoc = await subResponse.Content.ReadFromJsonAsync<JsonElement>();
            if (subDoc.TryGetProperty("subscription", out var s))
            {
                return new SubscriptionResultDto
                {
                    Success = true,
                    Message = "Subscribed successfully",
                    Subscription = new SubscriptionDto
                    {
                        Id = s.GetProperty("id").GetInt32(),
                        State = s.GetProperty("state").GetString() ?? "",
                        PlanHandle = s.GetProperty("product_handle").GetString() ?? planHandle,
                        Amount = s.TryGetProperty("amount_in_cents", out var a) ? a.GetInt32() / 100m : 0,
                        NextBillingDate = s.TryGetProperty("next_billing_at", out var n) && n.ValueKind != JsonValueKind.Null ? n.GetDateTime() : null,
                        CustomerReference = userId
                    }
                };
            }
            return new SubscriptionResultDto { Success = true, Message = "Subscribed" };
        }
        catch (Exception ex)
        {
            return new SubscriptionResultDto { Success = false, Message = ex.Message };
        }
    }
}
