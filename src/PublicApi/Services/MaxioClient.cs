using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioClient
{
    private readonly HttpClient _http;
    private readonly MaxioOptions _opts;

    public MaxioClient(IHttpClientFactory factory, IOptions<MaxioOptions> opts)
    {
        _opts = opts.Value;
        _http = factory.CreateClient("Maxio");
        _http.BaseAddress = new Uri(BaseUrl());
        var cred = Convert.ToBase64String(Encoding.ASCII.GetBytes(_opts.ApiKey + ":"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", cred);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string BaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_opts.BaseUrl))
            return _opts.BaseUrl!.TrimEnd('/');
        return $"https://{_opts.Subdomain}.chargify.com";
    }

    public async Task<JsonElement> GetProductsAsync(string? handle = null)
    {
        var url = "/products.json" + (handle != null ? $"?handle={handle}" : "");
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
    }

    public async Task<JsonElement> GetSubscriptionsAsync(int? customerId = null)
    {
        var url = "/subscriptions.json" + (customerId.HasValue ? $"?customer_id={customerId}" : "");
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
    }

    public async Task<JsonElement> GetCustomersAsync(string? reference = null, string? email = null)
    {
        var q = new List<string>();
        if (reference != null) q.Add($"reference={Uri.EscapeDataString(reference)}");
        if (email != null) q.Add($"email={Uri.EscapeDataString(email)}");
        var url = "/customers.json" + (q.Any() ? "?" + string.Join("&", q) : "");
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
    }

    public async Task<JsonElement> CreateCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        var payload = new Dictionary<string, object>
        {
            ["customer"] = new Dictionary<string, object>
            {
                ["first_name"] = firstName,
                ["last_name"] = lastName,
                ["email"] = email,
                ["reference"] = reference
            }
        };
        var resp = await _http.PostAsync("/customers.json", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
    }

    public async Task<JsonElement> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        var payload = new Dictionary<string, object>
        {
            ["subscription"] = new Dictionary<string, object>
            {
                ["product_handle"] = productHandle,
                ["customer_id"] = customerId,
                ["payment_collection_method"] = "remittance"
            }
        };
        var resp = await _http.PostAsync("/subscriptions.json", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
    }

    public async Task<JsonElement> GetProductFamilyAsync(string handle)
    {
        var resp = await _http.GetAsync($"/product_families.json?handle={handle}");
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
    }
}


