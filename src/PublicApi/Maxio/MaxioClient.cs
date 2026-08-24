using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The spec allows the product_family_id path segment to be the family handle prefixed with "handle:".
        using var response = await _httpClient.GetAsync($"product_families/handle:{productFamilyHandle}/products.json", cancellationToken);
        var wrappers = await ReadAsync<List<MaxioProductResponse>>(response, cancellationToken);
        return wrappers.Select(w => w.Product!).Where(p => p is not null).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={WebUtility.UrlEncode(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var wrapper = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("customers.json",
            new MaxioCreateCustomerRequest { Customer = customer }, SerializerOptions, cancellationToken);
        var wrapper = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return wrapper?.Customer ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty customer payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        var wrappers = await ReadAsync<List<MaxioSubscriptionResponse>>(response, cancellationToken);
        return wrappers.Select(w => w.Subscription!).Where(s => s is not null).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription }, SerializerOptions, cancellationToken);
        var wrapper = await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken);
        return wrapper?.Subscription ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty subscription payload.");
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, SummarizeErrors(errorBody));
        }

        var result = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response body.");
    }

    private static string SummarizeErrors(string body)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorListResponse>(body);
            if (parsed?.Errors is { Length: > 0 })
            {
                return string.Join("; ", parsed.Errors);
            }
        }
        catch (JsonException)
        {
            // Body wasn't the standard error shape; return it verbatim.
        }

        return body;
    }
}
