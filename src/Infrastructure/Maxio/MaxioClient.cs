using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioClient"/> against the Maxio Advanced
/// Billing REST API. Authentication is HTTP Basic where the username is the site
/// API key and the password is the literal "x" (per the vendor contract).
/// </summary>
public class MaxioClient : IMaxioClient
{
    private const string PasswordSentinel = "x";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient, MaxioSettings settings)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(settings.ResolveBaseUrl() + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:{PasswordSentinel}")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadOrThrowAsync<WrappedMaxioCustomer>(response, cancellationToken);
        return payload?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest customer, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("customers.json", new WrappedMaxioCustomerRequest { Customer = customer }, JsonOptions, cancellationToken);
        var payload = await ReadOrThrowAsync<WrappedMaxioCustomer>(response, cancellationToken);
        return payload!.Customer!;
    }

    public async Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("product_families.json", cancellationToken);
        var families = await ReadOrThrowAsync<List<WrappedMaxioProductFamily>>(response, cancellationToken);
        return families?
            .Where(f => string.Equals(f.ProductFamily?.Handle, handle, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.ProductFamily!)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(int productFamilyId, CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        const int pageSize = 100;
        int page = 1;

        while (true)
        {
            var response = await _httpClient.GetAsync(
                $"product_families/{productFamilyId}/products.json?page={page}&per_page={pageSize}", cancellationToken);
            var payload = await ReadOrThrowAsync<List<WrappedMaxioProduct>>(response, cancellationToken);

            if (payload is null || payload.Count == 0)
            {
                break;
            }

            products.AddRange(payload.Select(p => p.Product!));
            if (payload.Count < pageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadOrThrowAsync<WrappedMaxioSubscription>(response, cancellationToken);
        return payload?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "subscriptions.json",
            new WrappedMaxioSubscriptionRequest { Subscription = request },
            JsonOptions,
            cancellationToken);
        var payload = await ReadOrThrowAsync<WrappedMaxioSubscription>(response, cancellationToken);
        return payload!.Subscription!;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        var payload = await ReadOrThrowAsync<List<WrappedMaxioSubscription>>(response, cancellationToken);
        return payload?.Select(s => s.Subscription!).ToList() ?? new List<MaxioSubscription>();
    }

    private static async Task<T?> ReadOrThrowAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new MaxioApiException((int)response.StatusCode, body, $"Maxio returned an unexpected payload: {ex.Message}");
            }
        }

        throw new MaxioApiException(
            (int)response.StatusCode,
            body,
            $"Maxio API request to '{response.RequestMessage?.RequestUri}' failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).");
    }

    private class WrappedMaxioCustomerRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("customer")]
        public CreateMaxioCustomerRequest? Customer { get; set; }
    }

    private class WrappedMaxioCustomer
    {
        [System.Text.Json.Serialization.JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private class WrappedMaxioProductFamily
    {
        [System.Text.Json.Serialization.JsonPropertyName("product_family")]
        public MaxioProductFamily? ProductFamily { get; set; }
    }

    private class WrappedMaxioProduct
    {
        [System.Text.Json.Serialization.JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private class WrappedMaxioSubscriptionRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("subscription")]
        public CreateMaxioSubscriptionRequest? Subscription { get; set; }
    }

    private class WrappedMaxioSubscription
    {
        [System.Text.Json.Serialization.JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }
    }
}