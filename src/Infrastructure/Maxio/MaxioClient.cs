using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _http;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioClient> _logger;
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient http, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(string productFamilyHandle, CancellationToken ct = default)
    {
        var url = $"/product_families/handle:{productFamilyHandle}/products.json";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(content);

        var products = new List<MaxioProductDto>();

        // Maxio returns a flat array: [{"product": {...}}, ...]
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("product", out var productEl))
                {
                    var product = JsonSerializer.Deserialize<MaxioProductDto>(productEl.GetRawText(), s_jsonOptions);
                    if (product != null)
                        products.Add(product);
                }
            }
        }
        // Some endpoints wrap in {"items": [...]}
        else if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("product", out var productEl))
                {
                    var product = JsonSerializer.Deserialize<MaxioProductDto>(productEl.GetRawText(), s_jsonOptions);
                    if (product != null)
                        products.Add(product);
                }
            }
        }
        return products;
    }

    public async Task<MaxioCustomerDto?> LookupCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(content);

        if (doc.RootElement.TryGetProperty("customer", out var customerEl))
        {
            return JsonSerializer.Deserialize<MaxioCustomerDto>(customerEl.GetRawText(), s_jsonOptions);
        }
        return null;
    }

    public async Task<MaxioCustomerDto> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken ct = default)
    {
        var url = "/customers.json";
        var wrapper = new MaxioCustomerCreateWrapper { Customer = request };
        var response = await _http.PostAsJsonAsync(url, wrapper, s_jsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(content);

        if (doc.RootElement.TryGetProperty("customer", out var customerEl))
        {
            return JsonSerializer.Deserialize<MaxioCustomerDto>(customerEl.GetRawText(), s_jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize Maxio customer response.");
        }
        throw new InvalidOperationException("Maxio customer response did not contain a customer object.");
    }

    public async Task<MaxioPaymentProfileDto> CreatePaymentProfileAsync(int customerId, MaxioCreatePaymentProfileRequest request, CancellationToken ct = default)
    {
        var url = "/payment_profiles.json";
        var payload = new { payment_profile = new { customer_id = customerId, first_name = request.FirstName, last_name = request.LastName, card_number = request.CardNumber, expiration_month = request.ExpirationMonth, expiration_year = request.ExpirationYear, billing_address = request.BillingAddress, billing_city = request.BillingCity, billing_state = request.BillingState, billing_zip = request.BillingZip, billing_country = request.BillingCountry } };
        var response = await _http.PostAsJsonAsync(url, payload, s_jsonOptions, ct);

        var content = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("Maxio CreatePaymentProfile response {StatusCode}: {Content}", response.StatusCode, content.Length > 500 ? content[..500] : content);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio CreatePaymentProfile failed: {StatusCode} {Content}", response.StatusCode, content);
            throw new InvalidOperationException($"Maxio payment profile creation failed with status {response.StatusCode}: {content}");
        }

        using var doc = JsonDocument.Parse(content);

        if (doc.RootElement.TryGetProperty("payment_profile", out var ppEl))
        {
            return JsonSerializer.Deserialize<MaxioPaymentProfileDto>(ppEl.GetRawText(), s_jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize Maxio payment profile response.");
        }
        throw new InvalidOperationException("Maxio payment profile response did not contain a payment_profile object.");
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken ct = default)
    {
        var url = "/subscriptions.json";
        var wrapper = new MaxioSubscriptionCreateWrapper { Subscription = request };
        var response = await _http.PostAsJsonAsync(url, wrapper, s_jsonOptions, ct);

        var content = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("Maxio CreateSubscription response {StatusCode}: {Content}", response.StatusCode, content.Length > 500 ? content[..500] : content);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio CreateSubscription failed: {StatusCode} {Content}", response.StatusCode, content);
            throw new InvalidOperationException($"Maxio subscription creation failed with status {response.StatusCode}: {content}");
        }

        using var doc = JsonDocument.Parse(content);

        if (doc.RootElement.TryGetProperty("subscription", out var subEl))
        {
            return JsonSerializer.Deserialize<MaxioSubscriptionDto>(subEl.GetRawText(), s_jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize Maxio subscription response.");
        }
        throw new InvalidOperationException("Maxio subscription response did not contain a subscription object.");
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        var url = $"/customers/{customerId}/subscriptions.json";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(content);

        var subscriptions = new List<MaxioSubscriptionDto>();

        // Flat array: [{"subscription": {...}}, ...]
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var sub in doc.RootElement.EnumerateArray())
            {
                if (sub.TryGetProperty("subscription", out var subEl))
                {
                    var subscription = JsonSerializer.Deserialize<MaxioSubscriptionDto>(subEl.GetRawText(), s_jsonOptions);
                    if (subscription != null)
                        subscriptions.Add(subscription);
                }
            }
        }
        // Wrapped: {"subscriptions": [...]}
        else if (doc.RootElement.TryGetProperty("subscriptions", out var subs))
        {
            foreach (var sub in subs.EnumerateArray())
            {
                var subscription = JsonSerializer.Deserialize<MaxioSubscriptionDto>(sub.GetRawText(), s_jsonOptions);
                if (subscription != null)
                    subscriptions.Add(subscription);
            }
        }
        return subscriptions;
    }
}
