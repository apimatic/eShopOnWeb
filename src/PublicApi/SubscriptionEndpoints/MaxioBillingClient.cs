using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A deliberately small client for the Maxio OpenAPI operations used by subscriptions.
/// Request paths and JSON envelopes mirror maxio-spec/openapi.yaml.
/// </summary>
public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken cancellationToken);
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;

    public MaxioBillingClient(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        // GET /product_families/{product_family_id}/products.json; the contract permits handle:{handle}.
        using var document = await SendAsync(HttpMethod.Get,
            $"product_families/{Uri.EscapeDataString($"handle:{productFamilyHandle}")}/products.json?page=1&per_page=200",
            null, cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway);
        }

        return document.RootElement.EnumerateArray()
            .Select(item => ReadPlan(GetRequiredProperty(item, "product")))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return ReadCustomer(GetRequiredProperty(document.RootElement, "customer"));
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken cancellationToken)
    {
        var content = JsonSerializer.Serialize(new
        {
            customer = new
            {
                first_name = customer.FirstName,
                last_name = customer.LastName,
                email = customer.Email,
                reference = customer.Reference
            }
        });

        using var document = await SendAsync(HttpMethod.Post, "customers.json", content, cancellationToken);
        return ReadCustomer(GetRequiredProperty(document.RootElement, "customer"));
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway);
        }

        return document.RootElement.EnumerateArray()
            .Select(item => ReadSubscription(GetRequiredProperty(item, "subscription")))
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        var content = JsonSerializer.Serialize(new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                reference,
                // Collection-Method permits remittance and avoids raw card capture for these payment-method-optional plans.
                payment_collection_method = "remittance"
            }
        });

        using var document = await SendAsync(HttpMethod.Post, "subscriptions.json", content, cancellationToken);
        return ReadSubscription(GetRequiredProperty(document.RootElement, "subscription"));
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string relativeUri, string? json, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUri);
        if (json != null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Consume the body so connections can be reused, but do not surface provider detail to callers.
            await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode);
        }
    }

    private static MaxioPlan ReadPlan(JsonElement product) => new(
        GetRequiredInt64(product, "id"),
        GetRequiredString(product, "name"),
        GetOptionalString(product, "handle"),
        GetRequiredInt64(product, "price_in_cents"),
        GetRequiredInt32(product, "interval"),
        GetRequiredString(product, "interval_unit"),
        GetOptionalDateTimeOffset(product, "archived_at"));

    private static MaxioCustomer ReadCustomer(JsonElement customer) => new(
        GetRequiredInt64(customer, "id"),
        GetRequiredString(customer, "email"),
        GetOptionalString(customer, "reference"));

    private static MaxioSubscription ReadSubscription(JsonElement subscription)
    {
        var product = GetRequiredProperty(subscription, "product");
        return new MaxioSubscription(
            GetRequiredInt64(subscription, "id"),
            GetRequiredString(subscription, "state"),
            GetRequiredInt64(subscription, "product_price_in_cents"),
            GetOptionalDateTimeOffset(subscription, "next_assessment_at"),
            GetOptionalDateTimeOffset(subscription, "current_period_ends_at"),
            GetOptionalString(subscription, "reference"),
            GetRequiredString(product, "name"),
            GetOptionalString(product, "handle"));
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property
            : throw new MaxioApiException(HttpStatusCode.BadGateway);

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        GetOptionalString(element, propertyName) ?? throw new MaxioApiException(HttpStatusCode.BadGateway);

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString()
            : null;

    private static long GetRequiredInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : throw new MaxioApiException(HttpStatusCode.BadGateway);

    private static int GetRequiredInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : throw new MaxioApiException(HttpStatusCode.BadGateway);

    private static DateTimeOffset? GetOptionalDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = GetOptionalString(element, propertyName);
        return value != null && DateTimeOffset.TryParse(value, out var date) ? date : null;
    }
}

public sealed record MaxioPlan(long Id, string Name, string? Handle, long PriceInCents, int Interval, string IntervalUnit, DateTimeOffset? ArchivedAt);
public sealed record MaxioCustomer(long Id, string Email, string? Reference);
public sealed record MaxioCustomerCreate(string FirstName, string LastName, string Email, string Reference);
public sealed record MaxioSubscription(long Id, string State, long ProductPriceInCents, DateTimeOffset? NextAssessmentAt, DateTimeOffset? CurrentPeriodEndsAt, string? Reference, string ProductName, string? ProductHandle);
