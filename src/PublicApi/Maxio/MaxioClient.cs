using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns an error response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public MaxioApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Thin typed client over the Maxio Advanced Billing JSON API
/// (verified against https://developers.maxio.com and the live sandbox).
/// </summary>
public interface IMaxioClient
{
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken ct = default);
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken ct = default);
    Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string familyHandle, CancellationToken ct = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string paymentCollectionMethod, CancellationToken ct = default);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;

    public MaxioClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<MaxioSettings> settings)
    {
        _settings = settings.Value;
        _http = httpClient;

        _http.BaseAddress = new Uri(_settings.ResolveBaseUrl() + "/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, ct);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken ct = default)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };
        var response = await SendAsync(HttpMethod.Post, "customers.json", body, ct);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response);
        return envelope!.Customer;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string familyHandle, CancellationToken ct = default)
    {
        // Numeric ids are reassigned on re-seed; handles are stable, so the
        // family is always resolved by handle, never by id.
        // product_families.json returns [{"product_family": {...}}, ...]
        var familyList = await ReadAsync<List<MaxioProductFamilyEnvelope>>(
            await SendAsync(HttpMethod.Get, "product_families.json", null, ct));
        var match = familyList?.FirstOrDefault(f => f.ProductFamily?.Handle == familyHandle)?.ProductFamily;
        if (match == null)
        {
            throw new MaxioApiException((int)HttpStatusCode.NotFound, $"Maxio product family '{familyHandle}' was not found.");
        }

        var products = await ReadAsync<List<MaxioProductEnvelope>>(
            await SendAsync(HttpMethod.Get, $"product_families/{match.Id}/products.json", null, ct));
        return products?.Where(p => p.Product != null).Select(p => p.Product!).ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string paymentCollectionMethod, CancellationToken ct = default)
    {
        var body = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                payment_collection_method = paymentCollectionMethod
            }
        };
        var response = await SendAsync(HttpMethod.Post, "subscriptions.json", body, ct);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response);
        return envelope!.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null, ct);
        var list = await ReadAsync<List<MaxioSubscriptionListEnvelope>>(response);
        return list?.Where(s => s.Subscription != null).Select(s => s.Subscription!).ToList() ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"subscriptions/{subscriptionId}.json", null, ct);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response);
        return envelope?.Subscription;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, MaxioSnakeCase.Options), Encoding.UTF8, "application/json");
        }

        var response = await _http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            // 404 is a valid "resource does not exist" result for the read
            // paths; ReadAsync maps it to null. Anything else is an error.
            return response;
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        throw new MaxioApiException((int)response.StatusCode, $"Maxio API returned {(int)response.StatusCode}: {ExtractErrors(content)}");
    }

    /// <summary>
    /// Reads and deserializes a successful response. For 404 responses the
    /// caller receives null (i.e. "resource does not exist").
    /// </summary>
    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (typeof(T) == typeof(string))
        {
            return (T)(object)content;
        }

        return JsonSerializer.Deserialize<T>(content, MaxioSnakeCase.Options);
    }

    private static string ExtractErrors(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind == JsonValueKind.Array
                    ? string.Join("; ", errors.EnumerateArray().Select(e => e.GetString()))
                    : errors.ToString();
            }
        }
        catch (JsonException)
        {
        }

        return string.IsNullOrWhiteSpace(content) ? "(no body)" : content;
    }
}