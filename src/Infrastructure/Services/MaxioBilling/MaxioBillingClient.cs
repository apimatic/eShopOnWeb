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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.MaxioBilling;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.MaxioBilling;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API.
///
/// Every operation here was verified against a live Maxio sandbox:
///   - Auth: HTTP Basic, API key as the username and the literal "x" as the password.
///   - GET  /products.json                          -> array of {"product": {...}}
///   - GET  /customers.json?reference={ref}         -> array of {"customer": {...}}  (filter verified correct)
    ///   - POST /customers.json                         -> {"customer": {...}}  (form-encoded body)
    ///   - POST /subscriptions.json                     -> {"subscription": {...}} (form-encoded body,
    ///                                                     payment_collection_method=remittance so no
    ///                                                     payment method is required at signup)
    ///   - GET  /subscriptions.json?customer_id={id}    -> array of {"subscription": {...}} (filter verified correct)
///   - GET  /subscriptions/{id}.json                -> {"subscription": {...}}
///
/// Note: POST bodies are sent form-encoded. The sandbox consistently returned
/// HTTP 500 with an empty body for JSON POSTs that include a "reference" field,
/// while the identical payloads sent as application/x-www-form-urlencoded succeed.
/// </summary>
public class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioBillingClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioBillingClient> logger)
    {
        settings.Validate();

        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_settings.ResolveBaseUrl() + "/");
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        if (_httpClient.DefaultRequestHeaders.Authorization is null)
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync("products.json", cancellationToken);
        if (document is null)
        {
            return new List<MaxioProduct>();
        }

        var products = new List<MaxioProduct>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var product = element.GetProperty("product");
            var familyHandle = product.GetProperty("product_family").GetProperty("handle").GetString();

            if (!string.Equals(familyHandle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip archived products; only sellable plans are exposed.
            if (product.TryGetProperty("archived_at", out var archivedAt) &&
                archivedAt.ValueKind == JsonValueKind.String)
            {
                continue;
            }

            products.Add(new MaxioProduct(
                Id: product.GetProperty("id").GetInt64(),
                Handle: product.GetProperty("handle").GetString() ?? string.Empty,
                Name: product.GetProperty("name").GetString() ?? string.Empty,
                PriceInCents: product.GetProperty("price_in_cents").GetInt32(),
                Interval: product.GetProperty("interval").GetInt32(),
                IntervalUnit: product.GetProperty("interval_unit").GetString() ?? "month",
                ProductFamilyHandle: familyHandle ?? string.Empty));
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        // Verified sandbox behavior:
        //   - no match      -> HTTP 404 with an empty body
        //   - single match  -> HTTP 200 with {"customer": {...}} (object wrapper)
        // Both shapes are handled defensively here.
        using var document = await GetJsonAsync($"customers.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (document is null)
        {
            return null;
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (document.RootElement.TryGetProperty("customer", out var wrapped))
            {
                return MapCustomer(wrapped);
            }

            return null;
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            return MapCustomer(element.GetProperty("customer"));
        }

        return null;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["customer[first_name]"] = customer.FirstName,
            ["customer[last_name]"] = customer.LastName,
            ["customer[email]"] = customer.Email,
            ["customer[organization]"] = customer.Organization,
            ["customer[reference]"] = customer.Reference
        };

        using var document = await PostFormAsync("customers.json", form, expectedStatus: System.Net.HttpStatusCode.Created, cancellationToken);
        return MapCustomer(document.RootElement.GetProperty("customer"));
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsByCustomerAsync(long customerId, CancellationToken cancellationToken = default)
    {
        // Verified sandbox behavior: unknown customer yields HTTP 404; a known
        // customer without subscriptions yields HTTP 200 with an empty array.
        using var document = await GetJsonAsync($"subscriptions.json?customer_id={customerId}", cancellationToken);
        if (document is null)
        {
            return new List<MaxioSubscription>();
        }

        var subscriptions = new List<MaxioSubscription>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            subscriptions.Add(MapSubscription(element.GetProperty("subscription")));
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["subscription[product_handle]"] = subscription.ProductHandle,
            ["subscription[customer_id]"] = subscription.CustomerId.ToString(),
            ["subscription[reference]"] = subscription.Reference,
            // Verified against the live sandbox: remittance collection enrolls the
            // subscription without requiring a stored payment method (no card capture / 3-DS).
            ["subscription[payment_collection_method]"] = "remittance"
        };

        using var document = await PostFormAsync("subscriptions.json", form, expectedStatus: System.Net.HttpStatusCode.Created, cancellationToken);
        return MapSubscription(document.RootElement.GetProperty("subscription"));
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        // Verified sandbox behavior: unknown id yields HTTP 404 with an empty body.
        using var document = await GetJsonAsync($"subscriptions/{subscriptionId}.json", cancellationToken);
        if (document is null)
        {
            return null;
        }

        if (!document.RootElement.TryGetProperty("subscription", out var subscription))
        {
            return null;
        }

        return MapSubscription(subscription);
    }

    /// <summary>
    /// Performs a GET and parses the JSON body. Returns null when the API responds
    /// with 404 (an expected "no match" outcome for lookup endpoints); throws
    /// MaxioApiException for any other non-success status.
    /// </summary>
    private async Task<JsonDocument?> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Maxio GET {Url} failed with {Status}: {Body}", relativeUrl, (int)response.StatusCode, body);
            throw new MaxioApiException((int)response.StatusCode, ParseErrors(body, (int)response.StatusCode));
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<JsonDocument> PostFormAsync(string relativeUrl, Dictionary<string, string> form, System.Net.HttpStatusCode expectedStatus, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(relativeUrl, content, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode != expectedStatus)
        {
            _logger.LogError("Maxio POST {Url} failed with {Status}: {Body}", relativeUrl, (int)response.StatusCode, body);
            throw new MaxioApiException((int)response.StatusCode, ParseErrors(body, (int)response.StatusCode));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new MaxioApiException((int)response.StatusCode, new[] { $"Maxio returned an empty response body for POST {relativeUrl}." });
        }

        return JsonDocument.Parse(body);
    }

    private static IReadOnlyList<string> ParseErrors(string body, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new[] { $"Maxio returned HTTP {statusCode} with an empty body." };
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var error in errors.EnumerateArray())
                {
                    list.Add(error.GetString() ?? string.Empty);
                }
                return list;
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }

        return new[] { body };
    }

    private static MaxioCustomer MapCustomer(JsonElement customer)
    {
        return new MaxioCustomer(
            Id: customer.GetProperty("id").GetInt64(),
            Email: GetStringOrNull(customer, "email"),
            FirstName: GetStringOrNull(customer, "first_name"),
            LastName: GetStringOrNull(customer, "last_name"),
            Organization: GetStringOrNull(customer, "organization"),
            Reference: GetStringOrNull(customer, "reference"));
    }

    private static MaxioSubscription MapSubscription(JsonElement subscription)
    {
        var product = subscription.GetProperty("product");

        return new MaxioSubscription(
            Id: subscription.GetProperty("id").GetInt64(),
            Reference: GetStringOrNull(subscription, "reference"),
            State: subscription.GetProperty("state").GetString() ?? string.Empty,
            ProductHandle: product.GetProperty("handle").GetString() ?? string.Empty,
            ProductName: product.GetProperty("name").GetString() ?? string.Empty,
            ProductPriceInCents: product.GetProperty("price_in_cents").GetInt32(),
            Currency: GetStringOrNull(subscription, "currency") ?? "USD",
            Interval: product.GetProperty("interval").GetInt32(),
            IntervalUnit: product.GetProperty("interval_unit").GetString() ?? "month",
            NextBillingDateUtc: GetDateTimeOrNull(subscription, "current_period_ends_at"),
            CustomerId: subscription.GetProperty("customer").GetProperty("id").GetInt64());
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static DateTime? GetDateTimeOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = value.GetString();
        if (string.IsNullOrWhiteSpace(raw) || !DateTimeOffset.TryParse(raw, out var parsed))
        {
            return null;
        }

        return parsed.UtcDateTime;
    }
}