using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(
        HttpClient httpClient,
        IOptions<MaxioConfiguration> config,
        ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;

        var baseUrl = ResolveBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_config.BaseUrl))
        {
            return _config.BaseUrl.TrimEnd('/') + "/";
        }

        return $"https://{_config.Subdomain}.chargify.com/";
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(string productFamilyHandle)
    {
        var products = new List<MaxioProduct>();
        int page = 1;
        const int perPage = 50;

        while (true)
        {
            var url = $"products.json?page={page}&per_page={perPage}";
            _logger.LogDebug("Listing plans from Maxio: {Url}", url);

            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessOrThrow(response);

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<MaxioProductResponse>>(content, JsonOptions);

            if (items == null || items.Count == 0)
                break;

            foreach (var item in items)
            {
                if (item.Product?.ProductFamily?.Handle == productFamilyHandle)
                    products.Add(item.Product);
            }

            if (items.Count < perPage)
                break;

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogDebug("Looking up customer in Maxio: {Url}", url);

        var response = await _httpClient.GetAsync(url);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessOrThrow(response);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MaxioCustomerResponse>(content, JsonOptions);
        return result?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        var url = "customers.json";
        _logger.LogDebug("Creating customer in Maxio: {Reference}", reference);

        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomerBody
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogWarning("Customer with reference {Reference} already exists, attempting lookup", reference);
            var existing = await FindCustomerByReferenceAsync(reference);
            if (existing != null)
                return existing;

            var errorContent = await response.Content.ReadAsStringAsync();
            throw new MaxioApiException($"Failed to create customer: {errorContent}");
        }

        await EnsureSuccessOrThrow(response);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MaxioCustomerResponse>(responseContent, JsonOptions);
        return result!.Customer;
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing != null)
        {
            _logger.LogInformation("Found existing Maxio customer for reference {Reference}", reference);
            return existing;
        }

        _logger.LogInformation("Creating new Maxio customer for reference {Reference}", reference);
        return await CreateCustomerAsync(firstName, lastName, email, reference);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference)
    {
        var url = "subscriptions.json";
        _logger.LogDebug("Creating subscription in Maxio: product={Product}, customer={Customer}", productHandle, customerReference);

        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionBody
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                PaymentCollectionMethod = "remittance"
            }
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);
        await EnsureSuccessOrThrow(response);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(responseContent, JsonOptions);
        return result!.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsByCustomerReferenceAsync(string customerReference)
    {
        var customer = await FindCustomerByReferenceAsync(customerReference);
        if (customer == null)
            return Array.Empty<MaxioSubscription>();

        var url = $"customers/{customer.Id}/subscriptions.json";
        _logger.LogDebug("Listing subscriptions for customer {CustomerId}", customer.Id);

        var response = await _httpClient.GetAsync(url);
        await EnsureSuccessOrThrow(response);

        var content = await response.Content.ReadAsStringAsync();
        var items = JsonSerializer.Deserialize<List<MaxioSubscriptionResponse>>(content, JsonOptions);

        var subscriptions = new List<MaxioSubscription>();
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item.Subscription != null)
                    subscriptions.Add(item.Subscription);
            }
        }

        return subscriptions;
    }

    private async Task EnsureSuccessOrThrow(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        _logger.LogError("Maxio API error {StatusCode}: {Body}", response.StatusCode, body);

        string message;
        try
        {
            var error = JsonSerializer.Deserialize<MaxioErrorResponse>(body, JsonOptions);
            message = error?.Errors?.Length > 0
                ? string.Join("; ", error.Errors)
                : body;
        }
        catch
        {
            message = body;
        }

        throw new MaxioApiException($"Maxio API returned {(int)response.StatusCode}: {message}");
    }
}

public class MaxioApiException : Exception
{
    public MaxioApiException(string message) : base(message) { }
}
