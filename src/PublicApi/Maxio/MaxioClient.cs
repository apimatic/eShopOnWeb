using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<CustomerResponse?> GetCustomerByReferenceAsync(string reference, CancellationToken ct = default);
    Task<CustomerResponse> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct = default);
    Task<List<ProductResponse>> ListProductsAsync(string? familyHandle = null, CancellationToken ct = default);
    Task<SubscriptionResponse> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken ct = default);
    Task<List<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioClient(HttpClient http, MaxioSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    private string BaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return _settings.BaseUrl!.TrimEnd('/');
        var env = string.IsNullOrWhiteSpace(_settings.Subdomain) ? "cp-exp-2" : _settings.Subdomain;
        return $"https://{env}.chargify.com";
    }

    private void SetAuth()
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<CustomerResponse?> GetCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        SetAuth();
        var url = $"{BaseUrl()}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var resp = await _http.GetAsync(url, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<CustomerResponse>(JsonOptions, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<CustomerResponse> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        SetAuth();
        var url = $"{BaseUrl()}/customers.json";
        var body = new { customer = new { reference, email, first_name = firstName, last_name = lastName } };
        var resp = await _http.PostAsJsonAsync(url, body, JsonOptions, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CustomerResponse>(JsonOptions, ct))!;
    }

    public async Task<List<ProductResponse>> ListProductsAsync(string? familyHandle = null, CancellationToken ct = default)
    {
        SetAuth();
        var url = $"{BaseUrl()}/products.json";
        if (!string.IsNullOrWhiteSpace(familyHandle))
            url += $"?family={Uri.EscapeDataString(familyHandle)}";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var list = await resp.Content.ReadFromJsonAsync<List<ProductResponse>>(JsonOptions, ct);
        return list ?? new List<ProductResponse>();
    }

    public async Task<SubscriptionResponse> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken ct = default)
    {
        SetAuth();
        var url = $"{BaseUrl()}/subscriptions.json";
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                payment_collection_method = "automatic"
            }
        };
        var resp = await _http.PostAsJsonAsync(url, body, JsonOptions, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SubscriptionResponse>(JsonOptions, ct))!;
    }

    public async Task<List<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        SetAuth();
        var url = $"{BaseUrl()}/customers/{customerId}/subscriptions.json";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var list = await resp.Content.ReadFromJsonAsync<List<SubscriptionResponse>>(JsonOptions, ct);
        return list ?? new List<SubscriptionResponse>();
    }
}
