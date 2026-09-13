using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<List<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken ct = default);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default);
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken ct = default);
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken ct = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, CancellationToken ct = default);
    Task<List<MaxioSubscription>> ListSubscriptionsByCustomerAsync(int customerId, CancellationToken ct = default);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUrl = _settings.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl))
        {
            baseUrl = $"https://{_settings.Subdomain}.chargify.com";
        }
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken ct = default)
    {
        var products = new List<MaxioProduct>();
        int page = 1;

        while (true)
        {
            var url = $"products.json?page={page}&per_page=200";
            _logger.LogDebug("Maxio GET {Url}", url);

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);
            var deserialized = JsonSerializer.Deserialize<List<MaxioProductResponse>>(content, s_jsonOptions);
            if (deserialized == null || deserialized.Count == 0)
                break;

            foreach (var item in deserialized)
            {
                if (item.Product.ProductFamily?.Handle == productFamilyHandle
                    && item.Product.ArchivedAt == null)
                {
                    products.Add(item.Product);
                }
            }

            if (deserialized.Count < 200)
                break;

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogDebug("Maxio GET {Url}", url);

        var response = await _httpClient.GetAsync(url, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var deserialized = JsonSerializer.Deserialize<MaxioCustomerResponse>(content, s_jsonOptions);
        return deserialized?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken ct = default)
    {
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var json = JsonSerializer.Serialize(request, s_jsonOptions);
        _logger.LogDebug("Maxio POST customers.json");

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("customers.json", content, ct);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(ct);
        var deserialized = JsonSerializer.Deserialize<MaxioCustomerResponse>(responseContent, s_jsonOptions);
        return deserialized!.Customer;
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken ct = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, ct);
        if (existing != null)
        {
            _logger.LogInformation("Found existing Maxio customer {Id} for reference {Reference}", existing.Id, reference);
            return existing;
        }

        _logger.LogInformation("Creating new Maxio customer for reference {Reference}", reference);
        return await CreateCustomerAsync(firstName, lastName, email, reference, ct);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, CancellationToken ct = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = "remittance"
            }
        };

        var json = JsonSerializer.Serialize(request, s_jsonOptions);
        _logger.LogDebug("Maxio POST subscriptions.json");

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("subscriptions.json", content, ct);

        var responseContent = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio subscription creation failed: {StatusCode} - {Content}", response.StatusCode, responseContent);
            throw new MaxioException(response.StatusCode, responseContent,
                $"Maxio subscription creation failed with status {response.StatusCode}: {responseContent}");
        }

        var deserialized = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(responseContent, s_jsonOptions);
        return deserialized!.Subscription;
    }

    public async Task<List<MaxioSubscription>> ListSubscriptionsByCustomerAsync(int customerId, CancellationToken ct = default)
    {
        var url = $"customers/{customerId}/subscriptions.json?per_page=200";
        _logger.LogDebug("Maxio GET {Url}", url);

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var deserialized = JsonSerializer.Deserialize<List<MaxioSubscriptionResponse>>(content, s_jsonOptions);

        var subscriptions = new List<MaxioSubscription>();
        if (deserialized != null)
        {
            foreach (var item in deserialized)
            {
                subscriptions.Add(item.Subscription);
            }
        }

        return subscriptions;
    }
}
