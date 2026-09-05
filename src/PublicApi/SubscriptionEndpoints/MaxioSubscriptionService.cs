using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioSubscriptionService
{
    Task<SubscriptionPlanDto[]> GetPlansAsync();
    Task<MaxioCustomerDto> GetOrCreateCustomerAsync(string userReference, string email, string firstName, string lastName);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<SubscriptionDto[]> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(HttpClient httpClient, IOptions<MaxioSettings> options, ILogger<MaxioSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<SubscriptionPlanDto[]> GetPlansAsync()
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/products.json";
            var request = CreateRequest(HttpMethod.Get, url);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var plans = new List<SubscriptionPlanDto>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                for (int i = 0; i < root.GetArrayLength(); i++)
                {
                    var item = root[i];
                    if (item.TryGetProperty("product", out var product))
                    {
                        var familyHandle = product.TryGetProperty("product_family", out var family) &&
                                           family.TryGetProperty("handle", out var handle)
                            ? handle.GetString()
                            : null;

                        if (familyHandle == _settings.ProductFamilyHandle)
                        {
                            plans.Add(new SubscriptionPlanDto
                            {
                                Id = product.GetProperty("id").GetInt32(),
                                Name = product.GetProperty("name").GetString()!,
                                Handle = product.GetProperty("handle").GetString()!,
                                Price = product.GetProperty("price_in_cents").GetInt64() / 100m,
                                Interval = product.GetProperty("interval").GetInt32(),
                                IntervalUnit = product.GetProperty("interval_unit").GetString()!
                            });
                        }
                    }
                }
            }

            return plans.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting plans from Maxio: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<MaxioCustomerDto> GetOrCreateCustomerAsync(string userReference, string email, string firstName, string lastName)
    {
        try
        {
            var existingCustomer = await TryGetCustomerByReferenceAsync(userReference);
            if (existingCustomer != null)
            {
                return existingCustomer;
            }

            return await CreateCustomerAsync(userReference, email, firstName, lastName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetOrCreateCustomerAsync for reference {Reference}", userReference);
            throw;
        }
    }

    private async Task<MaxioCustomerDto?> TryGetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var request = CreateRequest(HttpMethod.Get, url);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var customerEl = doc.RootElement.GetProperty("customer");

            return new MaxioCustomerDto
            {
                Id = customerEl.GetProperty("id").GetInt32(),
                Email = customerEl.GetProperty("email").GetString()!,
                FirstName = customerEl.GetProperty("first_name").GetString()!,
                LastName = customerEl.GetProperty("last_name").GetString()!,
                Reference = customerEl.GetProperty("reference").GetString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Customer lookup failed for reference, will create new");
            return null;
        }
    }

    private async Task<MaxioCustomerDto> CreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        var url = $"{_settings.GetBaseUrl()}/customers.json";

        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var request = CreateRequest(HttpMethod.Post, url, json);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var customerEl = doc.RootElement.GetProperty("customer");

        return new MaxioCustomerDto
        {
            Id = customerEl.GetProperty("id").GetInt32(),
            Email = customerEl.GetProperty("email").GetString()!,
            FirstName = customerEl.GetProperty("first_name").GetString()!,
            LastName = customerEl.GetProperty("last_name").GetString()!,
            Reference = customerEl.GetProperty("reference").GetString()
        };
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/subscriptions.json";

            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "automatic"
                }
            };

            var json = JsonSerializer.Serialize(payload);
            _logger.LogInformation("Creating subscription with payload: {Payload}", json);
            var request = CreateRequest(HttpMethod.Post, url, json);

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Maxio API error {StatusCode}: {Response}", response.StatusCode, content);
                throw new Exception($"Maxio API returned {response.StatusCode}: {content}");
            }

            return ParseSubscription(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer {CustomerId} product {ProductHandle}", customerId, productHandle);
            throw;
        }
    }

    public async Task<SubscriptionDto[]> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/customers/{customerId}/subscriptions.json";
            var request = CreateRequest(HttpMethod.Get, url);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("items", out var items))
                return Array.Empty<SubscriptionDto>();

            var subscriptions = new SubscriptionDto[items.GetArrayLength()];
            for (int i = 0; i < items.GetArrayLength(); i++)
            {
                subscriptions[i] = ParseSubscriptionFromResponse(items[i].GetProperty("subscription"));
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    private SubscriptionDto ParseSubscription(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var subEl = doc.RootElement.GetProperty("subscription");
        return ParseSubscriptionFromResponse(subEl);
    }

    private SubscriptionDto ParseSubscriptionFromResponse(JsonElement subEl)
    {
        return new SubscriptionDto
        {
            Id = subEl.GetProperty("id").GetInt32(),
            State = subEl.GetProperty("state").GetString()!,
            CustomerId = subEl.GetProperty("customer").GetProperty("id").GetInt32(),
            ProductName = subEl.TryGetProperty("product", out var prod) && prod.ValueKind != JsonValueKind.Null
                ? prod.GetProperty("name").GetString() ?? ""
                : "",
            CurrentPeriodEndsAt = subEl.TryGetProperty("current_period_ends_at", out var cpe) && cpe.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(cpe.GetString()!)
                : null,
            ActivatedAt = subEl.TryGetProperty("activated_at", out var aa) && aa.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(aa.GetString()!)
                : null,
            CanceledAt = subEl.TryGetProperty("canceled_at", out var ca) && ca.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(ca.GetString()!)
                : null
        };
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string? body = null)
    {
        var request = new HttpRequestMessage(method, url);

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        request.Headers.Add("Authorization", $"Basic {auth}");
        request.Headers.Add("Accept", "application/json");

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }
}

public class MaxioCustomerDto
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Reference { get; set; }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public int CustomerId { get; set; }
    public string ProductName { get; set; } = "";
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
}
