using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> GetOrCreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken);
}

public sealed record MaxioPlan(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit);
public sealed record MaxioCustomer(long Id, string Reference);
public sealed record MaxioCustomerInput(string Reference, string Email, string FirstName, string LastName);
public sealed record MaxioSubscription(long Id, string ProductHandle, string ProductName, long PriceInCents, string State, DateTimeOffset? NextBillingAt);

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode) : base($"Maxio returned HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

/// <summary>
/// Thin HTTP client for the verified Advanced Billing HTTP API. No numeric catalog IDs are persisted:
/// the configured product-family and product handles are resolved for every catalog request.
/// </summary>
public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = _options.GetBaseUri();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        using var family = await GetDocumentAsync($"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}.json", cancellationToken);
        var familyId = GetRequiredInt64(family.RootElement.GetProperty("product_family"), "id");

        using var products = await GetDocumentAsync($"product_families/{familyId}/products.json", cancellationToken);
        return products.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("product"))
            .Where(x => IsNull(x, "archived_at"))
            .Select(x => new MaxioPlan(
                GetRequiredString(x, "handle"),
                GetRequiredString(x, "name"),
                GetOptionalString(x, "description"),
                GetRequiredInt64(x, "price_in_cents"),
                GetRequiredInt32(x, "interval"),
                GetRequiredString(x, "interval_unit")))
            .OrderBy(x => x.PriceInCents)
            .ToList();
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken)
    {
        var existing = await TryGetCustomerByReferenceAsync(customer.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new
        {
            customer = new
            {
                first_name = customer.FirstName,
                last_name = customer.LastName,
                email = customer.Email,
                reference = customer.Reference
            }
        };

        try
        {
            using var created = await SendDocumentAsync(HttpMethod.Post, "customers.json", body, cancellationToken);
            return ReadCustomer(created.RootElement.GetProperty("customer"));
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The reference is unique in Maxio. A simultaneous request can create it between lookup and POST.
            var racedCustomer = await TryGetCustomerByReferenceAsync(customer.Reference, cancellationToken);
            if (racedCustomer is not null)
            {
                return racedCustomer;
            }

            throw;
        }
    }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        TryGetCustomerByReferenceAsync(reference, cancellationToken);

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var document = await GetDocumentAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        return document.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("subscription"))
            .Select(ReadSubscription)
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken)
    {
        // The configured plans are intentionally cardless. Maxio documents remittance collection for
        // subscriptions that are created without a payment profile; this avoids sending any card data.
        var body = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                payment_collection_method = "remittance"
            }
        };
        using var document = await SendDocumentAsync(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        return ReadSubscription(document.RootElement.GetProperty("subscription"));
    }

    private async Task<MaxioCustomer?> TryGetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            ThrowMaxioError(response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ReadCustomer(document.RootElement.GetProperty("customer"));
    }

    private async Task<JsonDocument> GetDocumentAsync(string path, CancellationToken cancellationToken)
    {
        return await SendDocumentAsync(HttpMethod.Get, path, null, cancellationToken);
    }

    private async Task<JsonDocument> SendDocumentAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ThrowMaxioError(response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private void ThrowMaxioError(HttpStatusCode statusCode)
    {
        _logger.LogWarning("Maxio Advanced Billing returned status code {StatusCode}", (int)statusCode);
        throw new MaxioApiException(statusCode);
    }

    private static MaxioCustomer ReadCustomer(JsonElement customer) => new(
        GetRequiredInt64(customer, "id"),
        GetRequiredString(customer, "reference"));

    private static MaxioSubscription ReadSubscription(JsonElement subscription)
    {
        var product = subscription.GetProperty("product");
        return new MaxioSubscription(
            GetRequiredInt64(subscription, "id"),
            GetRequiredString(product, "handle"),
            GetRequiredString(product, "name"),
            GetRequiredInt64(product, "price_in_cents"),
            GetRequiredString(subscription, "state"),
            // Current subscription responses provide next_assessment_at. Older response shapes use
            // next_billing_at, so accept it as a backwards-compatible fallback.
            GetOptionalDateTimeOffset(subscription, "next_assessment_at") ??
            GetOptionalDateTimeOffset(subscription, "next_billing_at"));
    }

    private static bool IsNull(JsonElement element, string propertyName) =>
        !element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null;

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        GetOptionalString(element, propertyName) ?? throw new InvalidOperationException($"Maxio response is missing {propertyName}.");

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static long GetRequiredInt64(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetInt64();

    private static int GetRequiredInt32(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetInt32();

    private static DateTimeOffset? GetOptionalDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = GetOptionalString(element, propertyName);
        return value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
