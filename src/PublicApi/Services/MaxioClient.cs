using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.MaxioDtos;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, MaxioOptions options)
    {
        _httpClient = httpClient;
        _options = options;

        var baseUrl = !string.IsNullOrEmpty(_options.BaseUrl)
            ? _options.BaseUrl.TrimEnd('/')
            : $"https://{_options.Subdomain}.chargify.com";

        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<MaxioProduct>> ListProductsAsync(int? productFamilyId = null)
    {
        var url = "products.json";
        if (productFamilyId.HasValue)
            url += $"?product_family_id={productFamilyId.Value}";

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<MaxioProductResponse>>(s_jsonOptions)
            is { } results
            ? results.Select(r => r.Product).ToList()
            : new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByEmailAsync(string email)
    {
        var response = await _httpClient.GetAsync($"customers.json?email={Uri.EscapeDataString(email)}");
        response.EnsureSuccessStatusCode();
        var customers = await response.Content.ReadFromJsonAsync<List<MaxioCustomerResponse>>(s_jsonOptions);
        return customers?.FirstOrDefault()?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string? reference)
    {
        var request = new MaxioCustomerRequest
        {
            Customer = new MaxioCustomerRequestData
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var response = await _httpClient.PostAsJsonAsync("customers.json", request, s_jsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Maxio create customer failed ({response.StatusCode}): {body}");

        var result = JsonSerializer.Deserialize<MaxioCustomerResponse>(body, s_jsonOptions);
        return result!.Customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId)
    {
        var request = new MaxioSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionRequestData
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = "remittance"
            }
        };

        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, s_jsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Maxio create subscription failed ({response.StatusCode}): {body}");

        var result = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(body, s_jsonOptions);
        return result!.Subscription;
    }

    public async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json");
        response.EnsureSuccessStatusCode();
        var results = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>(s_jsonOptions);
        return results?.Select(r => r.Subscription).ToList() ?? new List<MaxioSubscription>();
    }
}
