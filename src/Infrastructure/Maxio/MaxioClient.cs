using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Plain-HTTP client for the Maxio Advanced Billing API.
///
/// Contract points verified against a live Maxio sandbox:
/// - Host: https://{subdomain}.chargify.com (US environment); an explicitly
///   configured Maxio:BaseUrl is used verbatim when present.
/// - Auth: HTTP Basic, username = API key, password = "x".
/// - Requests/responses are JSON; single resources are wrapped in a singular
///   envelope ({"customer": {...}}, {"subscription": {...}}); collections are
///   arrays of the same envelopes.
/// - Customer lookup by reference: GET /customers.json?reference={r} → 200 with
///   the customer when found, 404 when not.
/// - Customer create: POST /customers.json with {customer:{first_name,last_name,email,reference}}.
/// - Subscription create: POST /subscriptions.json with
///   {subscription:{product_handle, customer_id, payment_collection_method:"invoice"}}.
///   With payment collection method invoice/remittance no stored card is needed.
/// - Subscription read/list: GET /subscriptions/{id}.json,
///   GET /subscriptions.json?customer_id={id}; DELETE /subscriptions/{id}.json cancels.
/// - Products: GET /product_families/lookup.json?handle={h} then
///   GET /product_families/{id}/products.json.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private string? _productFamilyId;

    public MaxioClient(HttpClient http, IOptions<MaxioSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
        EnsureConfigured();
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio:ApiKey is not configured. Set it from the MAXIO_API_KEY environment variable (e.g. via .NET user secrets).");
        }

        if (string.IsNullOrWhiteSpace(_settings.BaseUrl) && string.IsNullOrWhiteSpace(_settings.Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio:BaseUrl or Maxio:Subdomain must be configured. Set Maxio:Subdomain from the MAXIO_SITE_SUBDOMAIN environment variable.");
        }

        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Set it from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        // GET /customers.json?reference=X returns 200 with the customer (enveloped, as
        // array or single object) or 404 when there is no match.
        using var response = await _http.GetAsync($"customers.json?reference={Uri.EscapeDataString(reference)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, ct);
        var customers = await ReadEnvelopeCollectionAsync<MaxioCustomer>(response, "customer", ct);
        return customers.FirstOrDefault(c =>
            string.Equals(c.Reference, reference, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioNewCustomer customer, CancellationToken ct = default)
    {
        var body = new { customer = new { first_name = customer.FirstName, last_name = customer.LastName, email = customer.Email, reference = customer.Reference } };
        var envelope = await SendForEnvelopeAsync<MaxioCustomer>("customer", () => CreateJsonRequest(HttpMethod.Post, "customers.json", body), ct);
        return envelope;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"subscriptions.json?customer_id={customerId}", ct);
        await EnsureSuccessAsync(response, ct);
        return await ReadEnvelopeCollectionAsync<MaxioSubscription>(response, "subscription", ct);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken ct = default)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                payment_collection_method = "invoice"
            }
        };
        return await SendForEnvelopeAsync<MaxioSubscription>("subscription", () => CreateJsonRequest(HttpMethod.Post, "subscriptions.json", body), ct);
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(long subscriptionId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"subscriptions/{subscriptionId}.json", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, ct);
        return await ReadEnvelopeAsync<MaxioSubscription>(response, "subscription", ct);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(CancellationToken ct = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(ct);
        using var response = await _http.GetAsync($"product_families/{familyId}/products.json", ct);
        await EnsureSuccessAsync(response, ct);
        var products = await ReadEnvelopeCollectionAsync<MaxioProduct>(response, "product", ct);
        return products.Where(p => p.ArchivedAt == null).ToList();
    }

    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        if (_productFamilyId != null)
        {
            return _productFamilyId;
        }

        using var response = await _http.GetAsync($"product_families/lookup.json?handle={Uri.EscapeDataString(_settings.ProductFamilyHandle)}", ct);
        await EnsureSuccessAsync(response, ct);
        var family = await ReadEnvelopeAsync<ProductFamilyEnvelope>(response, "product_family", ct)
                     ?? throw new MaxioApiException((int)HttpStatusCode.NotFound,
                         string.Empty,
                         $"Maxio product family \"{_settings.ProductFamilyHandle}\" was not found on this site.");
        _productFamilyId = family.Id.ToString();
        return _productFamilyId;
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string url, object body)
    {
        return new HttpRequestMessage(method, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
    }

    private async Task<T> SendForEnvelopeAsync<T>(string envelopeName, Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        using var response = await _http.SendAsync(requestFactory(), ct);
        await EnsureSuccessAsync(response, ct);
        return await ReadEnvelopeAsync<T>(response, envelopeName, ct)
               ?? throw new MaxioApiException((int)response.StatusCode, await ReadBodyAsync(response, ct),
                   $"Maxio API returned an unexpected response without a \"{envelopeName}\" envelope.");
    }

    private static async Task<T?> ReadEnvelopeAsync<T>(HttpResponseMessage response, string envelopeName, CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty(envelopeName, out var element))
        {
            return default;
        }
        return element.Deserialize<T>(JsonOptions);
    }

    private static async Task<List<T>> ReadEnvelopeCollectionAsync<T>(HttpResponseMessage response, string envelopeName, CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var results = new List<T>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty(envelopeName, out var element))
                {
                    var value = element.Deserialize<T>(JsonOptions);
                    if (value != null)
                    {
                        results.Add(value);
                    }
                }
            }
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(envelopeName, out var single))
        {
            var value = single.Deserialize<T>(JsonOptions);
            if (value != null)
            {
                results.Add(value);
            }
        }
        return results;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await ReadBodyAsync(response, ct);
        throw new MaxioApiException((int)response.StatusCode, body);
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class ProductFamilyEnvelope
    {
        public long Id { get; set; }
    }
}
