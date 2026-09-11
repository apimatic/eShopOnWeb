using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services.Maxio;

public interface IMaxioService
{
    Task<MaxioCustomerResponse?> GetCustomerByReferenceAsync(string reference);
    Task<MaxioCustomerResponse?> CreateCustomerAsync(string firstName, string lastName, string email, string reference);
    Task<MaxioSubscriptionResponse?> GetSubscriptionByReferenceAsync(string reference);
    Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(string productHandle, string customerReference, string reference);
    Task<MaxioProductResponse?> GetProductByHandleAsync(string handle);
    Task<List<MaxioSubscriptionResponse>?> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioService(HttpClient httpClient, IOptions<MaxioSettings> options)
    {
        _settings = options.Value;
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
    }

    private string BaseUrl => !string.IsNullOrWhiteSpace(_settings.BaseUrl)
        ? _settings.BaseUrl.TrimEnd('/')
        : $"https://{_settings.Subdomain}.chargify.com";

    public async Task<MaxioCustomerResponse?> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var url = $"{BaseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var resp = await _httpClient.GetAsync(url);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<MaxioCustomerResponse>();
        }
        catch (HttpRequestException) { return null; }
    }

    public async Task<MaxioCustomerResponse?> CreateCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        var url = $"{BaseUrl}/customers.json";
        var body = new { customer = new { first_name = firstName, last_name = lastName, email, reference } };
        var resp = await _httpClient.PostAsJsonAsync(url, body);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<MaxioCustomerResponse>();
    }

    public async Task<MaxioSubscriptionResponse?> GetSubscriptionByReferenceAsync(string reference)
    {
        try
        {
            var url = $"{BaseUrl}/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var resp = await _httpClient.GetAsync(url);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>();
        }
        catch (HttpRequestException) { return null; }
    }

    public async Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(string productHandle, string customerReference, string reference)
    {
        var url = $"{BaseUrl}/subscriptions.json";
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = reference,
                payment_collection_method = "remittance"
            }
        };
        var resp = await _httpClient.PostAsJsonAsync(url, body);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>();
    }

    public async Task<List<MaxioSubscriptionResponse>?> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var url = $"{BaseUrl}/customers/{customerId}/subscriptions.json";
            var resp = await _httpClient.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            var arr = await resp.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>();
            return arr;
        }
        catch { return null; }
    }

    public async Task<MaxioProductResponse?> GetProductByHandleAsync(string handle)
    {
        var url = $"{BaseUrl}/products/handle/{Uri.EscapeDataString(handle)}.json";
        var resp = await _httpClient.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<MaxioProductResponse>();
    }
}
