using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Hand-written client built directly against maxio-spec/openapi.yaml (OpenAPI 3.1).
/// Auth: HTTP Basic, username = API key, password = "x" (spec securitySchemes.BasicAuth).
/// Paths, wrapper shapes and field names follow the spec; JSON is snake_case per the spec's schemas.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The spec allows "Either the product family's id or its handle prefixed with `handle:`".
        using var response = await _httpClient.GetAsync(
            $"product_families/handle:{UriEscape(productFamilyHandle)}/products.json?include_archived=false",
            cancellationToken);
        var wrappers = await ReadAsync<List<MaxioProductResponse>>(response, cancellationToken);
        return (wrappers ?? new()).Select(w => w.Product!).Where(p => p is not null).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={UriEscape(reference)}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        var wrapper = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            JsonOptions,
            cancellationToken);
        var wrapper = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return wrapper!.Customer!;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);
        var wrappers = await ReadAsync<List<MaxioSubscriptionResponse>>(response, cancellationToken);
        return (wrappers ?? new()).Select(w => w.Subscription!).Where(s => s is not null).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            JsonOptions,
            cancellationToken);
        var wrapper = await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken);
        return wrapper!.Subscription!;
    }

    private static string UriEscape(string value) => WebUtility.UrlEncode(value);

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, await ReadErrorsAsync(response, cancellationToken));
        }
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                // Error-List-Response: { "errors": ["..."] }
                var errorList = JsonSerializer.Deserialize<MaxioErrorListResponse>(body, JsonOptions);
                if (errorList?.Errors is { Count: > 0 })
                {
                    return errorList.Errors;
                }

                // Customer-Error-Response: { "errors": { "customer": "..." } } or a plain string body
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("errors", out var errors) &&
                    errors.ValueKind == JsonValueKind.Object)
                {
                    return errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}").ToList();
                }
            }
            catch (JsonException)
            {
                // fall through to raw body
            }
            return new[] { body };
        }
        return new[] { response.ReasonPhrase ?? "Unknown Maxio API error" };
    }
}
