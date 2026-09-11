using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioService
{
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference);
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<List<MaxioProduct>> ListProductsAsync(string? familyHandle = null);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string? reference = null, string? customerReference = null);
    Task<List<MaxioSubscription>> ListSubscriptionsForCustomerAsync(int customerId);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;

    public MaxioService(HttpClient http, MaxioSettings settings)
    {
        _http = http;
        _settings = settings;
        _http.BaseAddress = new Uri(GetBaseUrl());
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return _settings.BaseUrl.TrimEnd('/');

        var sub = _settings.Subdomain;
        if (string.IsNullOrWhiteSpace(sub)) sub = "subdomain";
        return $"https://{sub}.chargify.com";
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var resp = await _http.GetAsync(url);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        if (doc.TryGetProperty("customer", out var c))
            return ParseCustomer(c);
        return null;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        var payload = new
        {
            customer = new
            {
                reference,
                email,
                first_name = firstName,
                last_name = lastName
            }
        };
        var customerContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/customers.json", customerContent);
        resp.EnsureSuccessStatusCode();
        var docStr = await resp.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(docStr);
        return ParseCustomer(doc.GetProperty("customer"));
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing != null) return existing;
        return await CreateCustomerAsync(reference, email, firstName, lastName);
    }

    public async Task<List<MaxioProduct>> ListProductsAsync(string? familyHandle = null)
    {
        var handle = familyHandle ?? _settings.ProductFamilyHandle;
        var url = "/products.json?per_page=100";
        if (!string.IsNullOrWhiteSpace(handle))
            url += $"&filter[family_handle]={Uri.EscapeDataString(handle)}";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var arrStr = await resp.Content.ReadAsStringAsync();
        var arr = JsonSerializer.Deserialize<JsonElement>(arrStr);
        var list = new List<MaxioProduct>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty("product", out var p))
            {
                if (!string.IsNullOrWhiteSpace(handle))
                {
                    if (p.TryGetProperty("product_family", out var pf))
                    {
                        if (pf.TryGetProperty("handle", out var ph) && ph.GetString() == handle)
                            list.Add(ParseProduct(p));
                        else
                            continue;
                    }
                    else
                        continue;
                }
                else
                {
                    list.Add(ParseProduct(p));
                }
            }
        }
        return list;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string? reference = null, string? customerReference = null)
    {
        var payload = new
        {
            subscription = new Dictionary<string, object>
            {
                ["product_handle"] = productHandle,
                ["customer_id"] = customerId,
                ["reference"] = reference,
                ["customer_reference"] = customerReference
            }
        };
        // remove null values
        var subDict = (Dictionary<string, object>)payload.subscription;
        var keysToRemove = subDict.Where(k => k.Value == null || (k.Value is string s && s == null)).Select(k => k.Key).ToList();
        // Actually null values won't serialize if null; but for dictionary we can filter
        var cleanSub = subDict.Where(k => k.Value != null).ToDictionary(k => k.Key, k => k.Value);
        payload = new { subscription = cleanSub };

        var subContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/subscriptions.json", subContent);
        resp.EnsureSuccessStatusCode();
        var docStr = await resp.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(docStr);
        return ParseSubscription(doc.GetProperty("subscription"));
    }

    public async Task<List<MaxioSubscription>> ListSubscriptionsForCustomerAsync(int customerId)
    {
        var resp = await _http.GetAsync($"/customers/{customerId}/subscriptions.json");
        resp.EnsureSuccessStatusCode();
        var arrStr = await resp.Content.ReadAsStringAsync();
        var arr = JsonSerializer.Deserialize<JsonElement>(arrStr);
        var list = new List<MaxioSubscription>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty("subscription", out var s))
                list.Add(ParseSubscription(s));
            else if (item.TryGetProperty("id", out _))
                list.Add(ParseSubscription(item));
        }
        return list;
    }

    private static MaxioCustomer ParseCustomer(JsonElement el)
    {
        return new MaxioCustomer
        {
            Id = el.GetProperty("id").GetInt32(),
            Reference = el.TryGetProperty("reference", out var r) ? r.GetString() ?? "" : "",
            Email = el.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "",
            FirstName = el.TryGetProperty("first_name", out var f) ? f.GetString() ?? "" : "",
            LastName = el.TryGetProperty("last_name", out var l) ? l.GetString() ?? "" : "",
            CreatedAt = el.TryGetProperty("created_at", out var ca) ? ca.GetString() : null,
            UpdatedAt = el.TryGetProperty("updated_at", out var ua) ? ua.GetString() : null
        };
    }

    private static MaxioProduct ParseProduct(JsonElement el)
    {
        return new MaxioProduct
        {
            Id = el.GetProperty("id").GetInt32(),
            Handle = el.TryGetProperty("handle", out var h) ? h.GetString() ?? "" : "",
            Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            PriceInCents = el.TryGetProperty("price_in_cents", out var pc) ? pc.GetInt32() : 0,
            Interval = el.TryGetProperty("interval", out var i) ? i.GetInt32() : 0,
            IntervalUnit = el.TryGetProperty("interval_unit", out var iu) ? iu.GetString() ?? "" : "",
            Description = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
            AccountingCode = el.TryGetProperty("accounting_code", out var ac) ? ac.GetString() ?? "" : "",
            ExpirationInterval = el.TryGetProperty("expiration_interval", out var ei) ? ei.GetInt32() : 0,
            ExpirationIntervalUnit = el.TryGetProperty("expiration_interval_unit", out var eiu) ? eiu.GetString() ?? "" : "",
            Taxable = el.TryGetProperty("taxable", out var tx) && tx.GetBoolean(),
            RequireCreditCard = el.TryGetProperty("require_credit_card", out var rcc) ? rcc.GetBoolean() : false
        };
    }

    private static MaxioSubscription ParseSubscription(JsonElement el)
    {
        var sub = new MaxioSubscription
        {
            Id = el.GetProperty("id").GetInt32(),
            State = el.TryGetProperty("state", out var st) ? st.GetString() ?? "" : "",
            CreatedAt = el.TryGetProperty("created_at", out var ca) ? ca.GetString() : null,
            UpdatedAt = el.TryGetProperty("updated_at", out var ua) ? ua.GetString() : null,
            ActivatedAt = el.TryGetProperty("activated_at", out var aa) ? aa.GetString() : null,
            CurrentPeriodEndsAt = el.TryGetProperty("current_period_ends_at", out var cpe) ? cpe.GetString() : null,
            NextAssessmentAt = el.TryGetProperty("next_assessment_at", out var na) ? na.GetString() : null,
            BalanceInCents = el.TryGetProperty("balance_in_cents", out var bi) ? bi.GetInt32() : 0,
            TotalRevenueInCents = el.TryGetProperty("total_revenue_in_cents", out var tr) ? tr.GetInt32() : 0,
            ProductPriceInCents = el.TryGetProperty("product_price_in_cents", out var pp) ? pp.GetInt32() : 0,
            Reference = el.TryGetProperty("reference", out var rf) ? rf.GetString() ?? "" : "",
            CancelAtEndOfPeriod = el.TryGetProperty("cancel_at_end_of_period", out var caep) ? caep.GetBoolean() : false,
            CancellationMethod = el.TryGetProperty("cancellation_method", out var cm) ? cm.GetString() ?? "" : "",
            PaymentCollectionMethod = el.TryGetProperty("payment_collection_method", out var pcm) ? pcm.GetString() ?? "" : ""
        };
        if (el.TryGetProperty("customer", out var cust))
        {
            sub.Customer = new MaxioCustomer
            {
                Id = cust.TryGetProperty("id", out var cid) ? cid.GetInt32() : 0,
                Reference = cust.TryGetProperty("reference", out var cref) ? cref.GetString() ?? "" : "",
                Email = cust.TryGetProperty("email", out var cemail) ? cemail.GetString() ?? "" : "",
                FirstName = cust.TryGetProperty("first_name", out var cfn) ? cfn.GetString() ?? "" : "",
                LastName = cust.TryGetProperty("last_name", out var cln) ? cln.GetString() ?? "" : ""
            };
        }
        if (el.TryGetProperty("product", out var prod))
        {
            sub.Product = new MaxioProduct
            {
                Id = prod.TryGetProperty("id", out var pid) ? pid.GetInt32() : 0,
                Handle = prod.TryGetProperty("handle", out var ph) ? ph.GetString() ?? "" : "",
                Name = prod.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "",
                PriceInCents = prod.TryGetProperty("price_in_cents", out var pc) ? pc.GetInt32() : 0,
                Interval = prod.TryGetProperty("interval", out var pi) ? pi.GetInt32() : 0,
                IntervalUnit = prod.TryGetProperty("interval_unit", out var piu) ? piu.GetString() ?? "" : ""
            };
        }
        return sub;
    }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";
    public string Description { get; set; } = "";
    public string AccountingCode { get; set; } = "";
    public int ExpirationInterval { get; set; }
    public string ExpirationIntervalUnit { get; set; } = "";
    public bool Taxable { get; set; }
    public bool RequireCreditCard { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public int BalanceInCents { get; set; }
    public int TotalRevenueInCents { get; set; }
    public int ProductPriceInCents { get; set; }
    public string Reference { get; set; } = "";
    public bool CancelAtEndOfPeriod { get; set; }
    public string CancellationMethod { get; set; } = "";
    public string PaymentCollectionMethod { get; set; } = "";
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}
