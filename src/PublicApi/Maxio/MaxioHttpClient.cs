using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioHttpClient> _logger;
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public MaxioHttpClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioHttpClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUrl = string.IsNullOrEmpty(_settings.BaseUrl)
            ? $"https://{_settings.Subdomain}.chargify.com"
            : _settings.BaseUrl.TrimEnd('/');

        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        _logger.LogInformation("Looking up Maxio customer by reference: {Reference}", reference);
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            await MaxioErrorHandler.HandleErrorResponseAsync(response, _logger);
        var result = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(s_jsonOptions);
        return result?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request)
    {
        _logger.LogInformation("Creating Maxio customer: {Email}", request.Customer.Email);
        var response = await _httpClient.PostAsJsonAsync("customers.json", request, s_jsonOptions);
        if (!response.IsSuccessStatusCode)
            await MaxioErrorHandler.HandleErrorResponseAsync(response, _logger);
        var result = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(s_jsonOptions);
        return result!.Customer;
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string email, string firstName, string lastName, string reference)
    {
        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing != null)
        {
            _logger.LogInformation("Found existing Maxio customer {Id} for reference {Reference}", existing.Id, reference);
            return existing;
        }

        _logger.LogInformation("No customer found for reference {Reference}, creating new customer", reference);
        return await CreateCustomerAsync(new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Reference = reference
            }
        });
    }

    public async Task<List<MaxioProduct>> ListProductsByFamilyAsync(string familyHandle)
    {
        _logger.LogInformation("Listing Maxio products for family: {FamilyHandle}", familyHandle);
        var response = await _httpClient.GetAsync("products.json");
        if (!response.IsSuccessStatusCode)
            await MaxioErrorHandler.HandleErrorResponseAsync(response, _logger);
        var wrappers = await response.Content.ReadFromJsonAsync<List<MaxioProductResponse>>(s_jsonOptions);
        return wrappers?.Select(w => w.Product)
            .Where(p =>
                p.ProductFamily?.Handle == familyHandle &&
                p.ArchivedAt == null)
            .ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request)
    {
        _logger.LogInformation("Creating Maxio subscription for customer {CustomerId}, product {ProductHandle}",
            request.Subscription.CustomerId, request.Subscription.ProductHandle);
        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, s_jsonOptions);
        if (!response.IsSuccessStatusCode)
            await MaxioErrorHandler.HandleErrorResponseAsync(response, _logger);
        var result = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(s_jsonOptions);
        return result!.Subscription;
    }

    public async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        _logger.LogInformation("Listing Maxio subscriptions for customer {CustomerId}", customerId);
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json");
        if (!response.IsSuccessStatusCode)
            await MaxioErrorHandler.HandleErrorResponseAsync(response, _logger);
        var wrappers = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionListResponse>>(s_jsonOptions);
        return wrappers?.Select(w => w.Subscription).ToList() ?? new List<MaxioSubscription>();
    }
}
