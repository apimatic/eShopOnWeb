using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(int perPage = 200, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"products.json?per_page={perPage}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        var parsed = Deserialize<List<MaxioProductResponse>>(body);
        return parsed?.Select(p => p.Product).ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"products/handle/{Uri.EscapeDataString(handle)}.json", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return Deserialize<MaxioProductResponse>(body)?.Product;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return Deserialize<MaxioCustomerResponse>(body)?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("customers.json", new { customer = request }, MaxioJson.Options, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return Deserialize<MaxioCustomerResponse>(body)?.Customer
            ?? throw new MaxioApiException((int)response.StatusCode, Array.Empty<string>(), "Maxio returned an empty customer payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"subscriptions.json?customer_id={customerId}&per_page=200", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        var parsed = Deserialize<List<MaxioSubscriptionResponse>>(body);
        return parsed?.Select(s => s.Subscription).ToList() ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", new { subscription = request }, MaxioJson.Options, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return Deserialize<MaxioSubscriptionResponse>(body)?.Subscription
            ?? throw new MaxioApiException((int)response.StatusCode, Array.Empty<string>(), "Maxio returned an empty subscription payload.");
    }

    private static T? Deserialize<T>(string body)
    {
        return JsonSerializer.Deserialize<T>(body, MaxioJson.Options);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var errors = ExtractErrors(body);
        var detail = errors.Count > 0 ? string.Join("; ", errors) : $"Maxio API returned {(int)response.StatusCode} {response.StatusCode}.";
        throw new MaxioApiException((int)response.StatusCode, errors, detail);
    }

    private static IReadOnlyList<string> ExtractErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return errorsElement.ValueKind switch
                {
                    JsonValueKind.Array => errorsElement.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .ToList(),
                    JsonValueKind.Object => errorsElement.EnumerateObject()
                        .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                            ? p.Value.EnumerateArray().Select(e => e.GetString() ?? "")
                            : new[] { p.Value.ToString() })
                        .Where(e => !string.IsNullOrEmpty(e))
                        .ToList(),
                    _ => new List<string>()
                };
            }
        }
        catch (JsonException)
        {
        }
        return Array.Empty<string>();
    }
}
