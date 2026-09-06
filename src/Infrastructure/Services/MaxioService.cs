using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, MaxioSettings settings, IAppLogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<MaxioSubscriptionPlan[]> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching subscription plans from Maxio");

        var baseUrl = GetBaseUrl();
        var url = $"{baseUrl}/products.json?filter[product_family_id]={_settings.ProductFamilyHandle}&per_page=200";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeader(request);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(jsonContent);
        var products = doc.RootElement.GetProperty("products");

        var plans = new List<MaxioSubscriptionPlan>();
        foreach (var product in products.EnumerateArray())
        {
            var id = product.GetProperty("id").GetInt32();
            var handle = product.GetProperty("handle").GetString();
            var name = product.GetProperty("name").GetString();
            var priceInCents = product.GetProperty("price_in_cents").GetInt64();
            var description = product.TryGetProperty("description", out var descProp) ? descProp.GetString() : null;

            plans.Add(new MaxioSubscriptionPlan
            {
                Id = id,
                Handle = handle,
                Name = name,
                PriceInCents = (int)priceInCents,
                Description = description
            });
        }

        _logger.LogInformation($"Found {plans.Count} subscription plans");
        return plans.ToArray();
    }

    public async Task<MaxioCustomerResponse> GetOrCreateCustomerAsync(
        string userReference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Getting or creating customer with reference: {userReference}");

        var existing = await TryGetCustomerByReferenceAsync(userReference, cancellationToken);
        if (existing != null)
        {
            _logger.LogInformation($"Customer already exists: {existing.Id}");
            return existing;
        }

        return await CreateCustomerAsync(userReference, firstName, lastName, email, cancellationToken);
    }

    private async Task<MaxioCustomerResponse?> TryGetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var url = $"{baseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(jsonContent);
            var customer = doc.RootElement.GetProperty("customer");

            return new MaxioCustomerResponse
            {
                Id = customer.GetProperty("id").GetInt32(),
                Reference = customer.GetProperty("reference").GetString(),
                FirstName = customer.GetProperty("first_name").GetString(),
                LastName = customer.GetProperty("last_name").GetString(),
                Email = customer.GetProperty("email").GetString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error checking existing customer: {ex.Message}");
            return null;
        }
    }

    private async Task<MaxioCustomerResponse> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken)
    {
        var baseUrl = GetBaseUrl();
        var url = $"{baseUrl}/customers.json";

        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference
            }
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        AddAuthHeader(request);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(jsonContent);
        var customer = doc.RootElement.GetProperty("customer");

        _logger.LogInformation($"Created new customer: {customer.GetProperty("id")}");

        return new MaxioCustomerResponse
        {
            Id = customer.GetProperty("id").GetInt32(),
            Reference = customer.GetProperty("reference").GetString(),
            FirstName = customer.GetProperty("first_name").GetString(),
            LastName = customer.GetProperty("last_name").GetString(),
            Email = customer.GetProperty("email").GetString()
        };
    }

    public async Task<MaxioSubscriptionResponse> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Creating subscription for customer {customerId} with product {productHandle}");

        var baseUrl = GetBaseUrl();
        var url = $"{baseUrl}/subscriptions.json";

        var body = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle
            }
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        AddAuthHeader(request);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(jsonContent);
        var subscription = doc.RootElement.GetProperty("subscription");

        DateTime? nextBillingAt = null;
        if (subscription.TryGetProperty("next_billing_at", out var nextBillingProp) && nextBillingProp.ValueKind != JsonValueKind.Null)
        {
            nextBillingAt = DateTime.Parse(nextBillingProp.GetString()!);
        }

        var result = new MaxioSubscriptionResponse
        {
            Id = subscription.GetProperty("id").GetInt32(),
            CustomerId = subscription.GetProperty("customer_id").GetInt32(),
            ProductId = subscription.TryGetProperty("product_id", out var prodIdProp)
                && prodIdProp.ValueKind != JsonValueKind.Null
                ? prodIdProp.GetInt32()
                : 0,
            State = subscription.GetProperty("state").GetString(),
            NextBillingAt = nextBillingAt
        };

        _logger.LogInformation($"Created subscription {result.Id} in state {result.State}");
        return result;
    }

    public async Task<MaxioSubscription[]> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Fetching subscriptions for customer {customerId}");

        var baseUrl = GetBaseUrl();
        var url = $"{baseUrl}/customers/{customerId}/subscriptions.json?per_page=200";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeader(request);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(jsonContent);
        var subscriptions = doc.RootElement.GetProperty("subscriptions");

        var result = new List<MaxioSubscription>();
        foreach (var sub in subscriptions.EnumerateArray())
        {
            DateTime? nextBillingAt = null;
            if (sub.TryGetProperty("next_billing_at", out var nextBillingProp) && nextBillingProp.ValueKind != JsonValueKind.Null)
            {
                nextBillingAt = DateTime.Parse(nextBillingProp.GetString()!);
            }

            var productHandle = sub.TryGetProperty("product_handle", out var prodHandleProp)
                && prodHandleProp.ValueKind != JsonValueKind.Null
                ? prodHandleProp.GetString()
                : null;

            DateTime? currentPeriodEndsAt = null;
            if (sub.TryGetProperty("current_period_ends_at", out var periodProp) && periodProp.ValueKind != JsonValueKind.Null)
            {
                currentPeriodEndsAt = DateTime.Parse(periodProp.GetString()!);
            }

            result.Add(new MaxioSubscription
            {
                Id = sub.GetProperty("id").GetInt32(),
                State = sub.GetProperty("state").GetString(),
                CurrentPeriodEndsAt = currentPeriodEndsAt,
                NextBillingAt = nextBillingAt,
                ProductHandle = productHandle
            });
        }

        _logger.LogInformation($"Found {result.Count} subscriptions for customer");
        return result.ToArray();
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(_settings.BaseUrl))
        {
            return _settings.BaseUrl.TrimEnd('/');
        }

        return $"https://{_settings.Subdomain}.chargify.com";
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}
