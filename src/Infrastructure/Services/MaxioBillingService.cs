using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioBillingService
{
    Task<SubscriptionPlan[]> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default);
    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string productHandle, CancellationToken ct = default);
    Task<MaxioSubscription[]> GetCustomerSubscriptionsAsync(string customerReference, CancellationToken ct = default);
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient http, MaxioSettings settings, ILogger<MaxioBillingService> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
        ConfigureClient();
    }

    private void ConfigureClient()
    {
        var baseUrl = !string.IsNullOrEmpty(_settings.BaseUrl)
            ? _settings.BaseUrl.TrimEnd('/')
            : $"https://{_settings.Subdomain}.chargify.com";
        _http.BaseAddress = new Uri(baseUrl);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
    }

    public async Task<SubscriptionPlan[]> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        var family = _settings.ProductFamilyHandle;
        var url = $"/product_families/handle:{family}/products.json?per_page=200";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var items = doc.RootElement.GetProperty("items");
        var plans = new List<SubscriptionPlan>();
        foreach (var item in items.EnumerateArray())
        {
            var p = item.GetProperty("product");
            plans.Add(new SubscriptionPlan
            {
                Id = p.GetProperty("id").GetInt32(),
                Name = p.GetProperty("name").GetString() ?? "",
                Handle = p.GetProperty("handle").GetString() ?? "",
                PriceInCents = p.GetProperty("price_in_cents").GetInt64(),
                Interval = p.GetProperty("interval").GetInt32(),
                IntervalUnit = p.GetProperty("interval_unit").GetString() ?? "month",
                RequiresCreditCard = p.TryGetProperty("require_credit_card", out var rc) && rc.GetBoolean(),
                Taxable = p.TryGetProperty("taxable", out var tx) && tx.GetBoolean()
            });
        }
        return plans.ToArray();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        try
        {
            var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var resp = await _http.GetAsync(url, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound || resp.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;
            resp.EnsureSuccessStatusCode();
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return ParseCustomer(doc.RootElement.GetProperty("customer"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FindCustomerByReference failed for {Reference}", reference);
            return null;
        }
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, ct);
        if (existing != null)
        {
            _logger.LogInformation("Customer already exists for reference {Reference} (id={Id})", reference, existing.Id);
            return existing;
        }

        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference,
                organization = $"{firstName} {lastName}",
                country = "US"
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/customers.json", content, ct);
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return ParseCustomer(doc.RootElement.GetProperty("customer"));
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string productHandle, CancellationToken ct = default)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/subscriptions.json", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Subscription creation failed: {Status} {Body}", resp.StatusCode, errBody);
            resp.EnsureSuccessStatusCode();
        }
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return ParseSubscription(doc.RootElement.GetProperty("subscription"));
    }

    public async Task<MaxioSubscription[]> GetCustomerSubscriptionsAsync(string customerReference, CancellationToken ct = default)
    {
        var customer = await FindCustomerByReferenceAsync(customerReference, ct);
        if (customer == null) return Array.Empty<MaxioSubscription>();
        var resp = await _http.GetAsync($"/customers/{customer.Id}/subscriptions.json", ct);
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var subs = doc.RootElement.GetProperty("subscriptions");
        var list = new List<MaxioSubscription>();
        foreach (var s in subs.EnumerateArray())
        {
            list.Add(ParseSubscription(s));
        }
        return list.ToArray();
    }

    private static MaxioCustomer ParseCustomer(JsonElement el)
    {
        return new MaxioCustomer
        {
            Id = el.GetProperty("id").GetInt32(),
            Reference = el.TryGetProperty("reference", out var r) ? r.GetString() : null,
            Email = el.GetProperty("email").GetString() ?? "",
            FirstName = el.TryGetProperty("first_name", out var fn) ? fn.GetString() : "",
            LastName = el.TryGetProperty("last_name", out var ln) ? ln.GetString() : ""
        };
    }

    private static MaxioSubscription ParseSubscription(JsonElement el)
    {
        JsonElement? prod = el.TryGetProperty("product", out var p) ? p : null;
        return new MaxioSubscription
        {
            Id = el.GetProperty("id").GetInt32(),
            State = el.GetProperty("state").GetString() ?? "",
            ProductName = (prod != null && prod.Value.TryGetProperty("name", out var pn)) ? pn.GetString() ?? "" : "",
            ProductHandle = (prod != null && prod.Value.TryGetProperty("handle", out var ph)) ? (ph.GetString() ?? "") : "",
            PriceInCents = el.TryGetProperty("product_price_in_cents", out var price) ? price.GetInt64() : 0,
            CurrentPeriodEndsAt = el.TryGetProperty("current_period_ends_at", out var cpe) && cpe.ValueKind != JsonValueKind.Null ? cpe.GetString() : null,
            NextAssessmentAt = el.TryGetProperty("next_assessment_at", out var na) && na.ValueKind != JsonValueKind.Null ? na.GetString() : null,
            ActivatedAt = el.TryGetProperty("activated_at", out var aa) && aa.ValueKind != JsonValueKind.Null ? aa.GetString() : null
        };
    }
}

public record SubscriptionPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool RequiresCreditCard { get; set; }
    public bool Taxable { get; set; }
}

public record MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}

public record MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public long PriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
}
