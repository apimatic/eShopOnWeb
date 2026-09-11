using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference);
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName = "", string lastName = "");
    Task<MaxioSubscription?> LookupSubscriptionByReferenceAsync(string reference);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, int? customerId = null);
    Task<MaxioProduct?> GetProductByHandleAsync(string handle);
    Task<MaxioSubscription[]> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(IOptions<MaxioSettings> settings, ILogger<MaxioClient> logger, HttpClient client)
    {
        _settings = settings.Value;
        _logger = logger;
        _client = client;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
        }
        _logger.LogInformation("Maxio {Method} {Path}", method, path);
        var response = await _client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Maxio error {Status}: {Body}", response.StatusCode, error.Substring(0, Math.Min(error.Length, 500)));
        }
        return response;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference)
    {
        var resp = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var content = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<MaxioCustomer>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName = "", string lastName = "")
    {
        var payload = new { customer = new { reference, email, first_name = firstName, last_name = lastName } };
        var resp = await SendAsync(HttpMethod.Post, "customers.json", payload);
        resp.EnsureSuccessStatusCode();
        var content = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.TryGetProperty("customer", out var custEl))
        {
            return JsonSerializer.Deserialize<MaxioCustomer>(custEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        }
        return JsonSerializer.Deserialize<MaxioCustomer>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
    }

    public async Task<MaxioSubscription?> LookupSubscriptionByReferenceAsync(string reference)
    {
        var resp = await SendAsync(HttpMethod.Get, $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}");
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var content = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<MaxioSubscription>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, int? customerId = null)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = subscriptionReference
            }
        };
        if (customerId.HasValue) payload.subscription.GetType().GetProperty("customer_id")?.SetValue(payload.subscription, customerId.Value); // poor man's reflection; better to build dict
        // Build payload more explicitly via anonymous with optional
        var dict = new System.Collections.Generic.Dictionary<string, object>
        {
            ["subscription"] = new System.Collections.Generic.Dictionary<string, object>
            {
                ["product_handle"] = productHandle,
                ["customer_reference"] = customerReference,
                ["reference"] = subscriptionReference,
                ["defer_signup"] = true
            }
        };
        if (customerId.HasValue) ((System.Collections.Generic.Dictionary<string, object>)dict["subscription"])["customer_id"] = customerId.Value;
        var resp = await SendAsync(HttpMethod.Post, "subscriptions.json", dict);
        resp.EnsureSuccessStatusCode();
        var content = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.TryGetProperty("subscription", out var subEl))
        {
            return JsonSerializer.Deserialize<MaxioSubscription>(subEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        }
        return JsonSerializer.Deserialize<MaxioSubscription>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle)
    {
        var resp = await SendAsync(HttpMethod.Get, $"products/handle/{Uri.EscapeDataString(handle)}.json");
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var content = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.TryGetProperty("product", out var prodEl))
        {
            return JsonSerializer.Deserialize<MaxioProduct>(prodEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        }
        return JsonSerializer.Deserialize<MaxioProduct>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }

    public async Task<MaxioSubscription[]> GetCustomerSubscriptionsAsync(int customerId)
    {
        var resp = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json");
        resp.EnsureSuccessStatusCode();
        var content = await resp.Content.ReadAsStringAsync();
        // Response is array of subscriptions or wrapped; spec doesn't specify wrapper for this endpoint. Assume array.
        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return doc.RootElement.Deserialize<MaxioSubscription[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }) ?? Array.Empty<MaxioSubscription>();
        }
        if (doc.RootElement.TryGetProperty("subscriptions", out var subs))
        {
            return subs.Deserialize<MaxioSubscription[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }) ?? Array.Empty<MaxioSubscription>();
        }
        return Array.Empty<MaxioSubscription>();
    }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public string State { get; set; } = "";
    public string NextBillingAt { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public int CustomerId { get; set; }
    public string ProductPricePointHandle { get; set; } = "";
    // Additional fields for display
    public MaxioSubscriptionProduct? Product { get; set; }
    public MaxioSubscriptionPricePoint? PricePoint { get; set; }
}

public class MaxioSubscriptionProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
}

public class MaxioSubscriptionPricePoint
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PriceInCents { get; set; }
    public string Currency { get; set; } = "";
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}
