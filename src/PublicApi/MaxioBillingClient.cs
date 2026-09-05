using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string reference, CancellationToken cancellationToken);
}

public sealed record MaxioPlan(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit);
public sealed record MaxioCustomer(long Id, string Reference);
public sealed record MaxioSubscription(long Id, string? Reference, string State, string PlanHandle, string PlanName, long PriceInCents, string IntervalUnit, int Interval, DateTimeOffset? NextBillingAt);

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation) : base($"Maxio {operation} failed with HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

/// <summary>Small HTTP client for only the Maxio Billing API operations required by subscriptions.</summary>
public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        using var response = await _httpClient.GetAsync($"product_families/{family}/products.json?per_page=200", cancellationToken);
        await EnsureSuccessAsync(response, "list plans", cancellationToken);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Maxio returned an empty plans response.");

        return document.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("product"))
            .Where(product => product.GetPropertyOrNull("archived_at")?.ValueKind == JsonValueKind.Null)
            .Select(product => new MaxioPlan(
                product.GetRequiredString("handle"), product.GetRequiredString("name"), product.GetOptionalString("description"),
                product.GetRequiredInt64("price_in_cents"), product.GetRequiredInt32("interval"), product.GetRequiredString("interval_unit")))
            .ToList();
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null) return existing;

        var request = new
        {
            customer = new { first_name = firstName, last_name = lastName, email, reference },
            uniqueness_token = StableToken($"customer:{reference}")
        };
        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Maxio returned an empty customer response.");
            return ReadCustomer(document.RootElement.GetProperty("customer"));
        }

        // A simultaneous request may have created the reference first. Lookup is authoritative in either the 409 or validation-conflict case.
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            var racedCustomer = await FindCustomerAsync(reference, cancellationToken);
            if (racedCustomer is not null) return racedCustomer;
        }

        await EnsureSuccessAsync(response, "create customer", cancellationToken);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Maxio returned an empty subscriptions response.");
        return document.RootElement.EnumerateArray().Select(item => ReadSubscription(item.GetProperty("subscription"))).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        var request = new
        {
            // Invoice collection permits enrollment without accepting raw payment data; Maxio remains the system of record for the resulting receivable.
            subscription = new { customer_id = customerId, product_handle = planHandle, reference, payment_collection_method = "invoice", net_terms = "30" },
            uniqueness_token = StableToken($"subscription:{reference}")
        };
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Maxio returned an empty subscription response.");
            return ReadSubscription(document.RootElement.GetProperty("subscription"));
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = (await ListCustomerSubscriptionsAsync(customerId, cancellationToken))
                .SingleOrDefault(subscription => string.Equals(subscription.Reference, reference, StringComparison.Ordinal));
            if (existing is not null) return existing;
        }

        await EnsureSuccessAsync(response, "create subscription", cancellationToken);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, "find customer", cancellationToken);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Maxio returned an empty customer response.");
        return ReadCustomer(document.RootElement.GetProperty("customer"));
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Maxio {Operation} returned HTTP {StatusCode}. Response: {MaxioError}", operation, (int)response.StatusCode, errorBody);
        throw new MaxioApiException(response.StatusCode, operation);
    }

    private static MaxioCustomer ReadCustomer(JsonElement customer) => new(customer.GetRequiredInt64("id"), customer.GetRequiredString("reference"));

    private static MaxioSubscription ReadSubscription(JsonElement subscription)
    {
        var product = subscription.GetProperty("product");
        return new MaxioSubscription(
            subscription.GetRequiredInt64("id"), subscription.GetOptionalString("reference"), subscription.GetRequiredString("state"),
            product.GetRequiredString("handle"), product.GetRequiredString("name"), subscription.GetRequiredInt64("product_price_in_cents"),
            product.GetRequiredString("interval_unit"), product.GetRequiredInt32("interval"),
            subscription.GetDateTimeOffsetOrNull("next_assessment_at") ?? subscription.GetDateTimeOffsetOrNull("current_period_ends_at"));
    }

    private static string StableToken(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32];
}

internal static class JsonElementExtensions
{
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value : null;

    public static string GetRequiredString(this JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetString() ?? throw new InvalidOperationException($"Maxio response '{propertyName}' was null.");

    public static string? GetOptionalString(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    public static long GetRequiredInt64(this JsonElement element, string propertyName) => element.GetProperty(propertyName).GetInt64();

    public static int GetRequiredInt32(this JsonElement element, string propertyName) => element.GetProperty(propertyName).GetInt32();

    public static DateTimeOffset? GetDateTimeOffsetOrNull(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetDateTimeOffset() : null;
}
