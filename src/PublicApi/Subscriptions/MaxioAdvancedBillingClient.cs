using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyCollection<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionInput subscription, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}

public sealed record MaxioPlan(string Handle, string Name, string? Description, int PriceInCents, int Interval, string IntervalUnit, string? Currency);
public sealed record MaxioCustomer(long Id, string Reference);
public sealed record MaxioCustomerInput(string Reference, string FirstName, string LastName, string Email);
public sealed record MaxioSubscription(long Id, string Reference, string State, DateTimeOffset? NextBillingAt, MaxioPlan Plan);
public sealed record MaxioSubscriptionInput(string Reference, string ProductHandle, long CustomerId, string UniquenessToken);

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation) : base($"Maxio {operation} failed with HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

/// <summary>Thin, verified HTTP adapter for Maxio Advanced Billing's JSON API.</summary>
public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public MaxioAdvancedBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = _options.GetBaseUri();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
    }

    public async Task<IReadOnlyCollection<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var family = await GetAsync<MaxioProductFamilyEnvelope>(
            $"product_families/{EscapeHandle(_options.ProductFamilyHandle)}.json", "read product family", cancellationToken);
        if (family.ProductFamily is null || !string.Equals(family.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "read product family");
        }

        var products = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/{family.ProductFamily.Id}/products.json", "list products", cancellationToken);

        return products
            .Where(x => x.Product is not null && x.Product.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Product.Handle))
            .Select(x => ToPlan(x.Product!))
            .ToArray();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, "find customer");
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, "find customer", cancellationToken);
        return envelope.Customer is null ? throw new MaxioApiException(HttpStatusCode.BadGateway, "find customer") : ToCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("customers.json", new
        {
            customer = new { reference = customer.Reference, first_name = customer.FirstName, last_name = customer.LastName, email = customer.Email }
        }, _jsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "create customer");
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, "create customer", cancellationToken);
        return envelope.Customer is null ? throw new MaxioApiException(HttpStatusCode.BadGateway, "create customer") : ToCustomer(envelope.Customer);
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, "find subscription");
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, "find subscription", cancellationToken);
        return envelope.Subscription is null ? throw new MaxioApiException(HttpStatusCode.BadGateway, "find subscription") : ToSubscription(envelope.Subscription);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionInput subscription, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", new
        {
            subscription = new
            {
                reference = subscription.Reference,
                product_handle = subscription.ProductHandle,
                customer_id = subscription.CustomerId,
                // The seeded plans deliberately allow enrollment without card capture. Remittance
                // is Maxio's documented collection method for that cardless signup scenario.
                payment_collection_method = "remittance",
                uniqueness_token = subscription.UniquenessToken
            }
        }, _jsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "create subscription");
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, "create subscription", cancellationToken);
        return envelope.Subscription is null ? throw new MaxioApiException(HttpStatusCode.BadGateway, "create subscription") : ToSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyCollection<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", "list customer subscriptions", cancellationToken);
        return subscriptions.Where(x => x.Subscription is not null).Select(x => ToSubscription(x.Subscription!)).ToArray();
    }

    private async Task<T> GetAsync<T>(string requestUri, string operation, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        await EnsureSuccessAsync(response, operation);
        return await ReadAsync<T>(response, operation, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new MaxioApiException(response.StatusCode, operation);
        }
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        using (response)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
            return value ?? throw new MaxioApiException(HttpStatusCode.BadGateway, operation);
        }
    }

    private static string EscapeHandle(string handle) => Uri.EscapeDataString("handle:" + handle);

    private static MaxioCustomer ToCustomer(MaxioCustomerDto customer) => new(customer.Id, customer.Reference ?? string.Empty);

    private static MaxioPlan ToPlan(MaxioProductDto product) => new(product.Handle!, product.Name ?? product.Handle!, product.Description,
        product.PriceInCents, product.Interval, product.IntervalUnit ?? "month", product.Currency);

    private static MaxioSubscription ToSubscription(MaxioSubscriptionDto subscription)
    {
        if (subscription.Product is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "read subscription product");
        }

        var plan = ToPlan(subscription.Product);
        return new MaxioSubscription(subscription.Id, subscription.Reference ?? string.Empty, subscription.State ?? "unknown",
            subscription.NextBillingAt ?? subscription.NextAssessmentAt, plan with { Currency = subscription.Currency ?? plan.Currency });
    }

    private sealed class MaxioProductFamilyEnvelope { [JsonPropertyName("product_family")] public MaxioProductFamilyDto? ProductFamily { get; init; } }
    private sealed class MaxioProductFamilyDto { public long Id { get; init; } public string? Handle { get; init; } }
    private sealed class MaxioProductEnvelope { public MaxioProductDto? Product { get; init; } }
    private sealed class MaxioProductDto
    {
        public string? Handle { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        [JsonPropertyName("price_in_cents")] public int PriceInCents { get; init; }
        public int Interval { get; init; }
        [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; init; }
        [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; init; }
        public string? Currency { get; init; }
    }
    private sealed class MaxioCustomerEnvelope { public MaxioCustomerDto? Customer { get; init; } }
    private sealed class MaxioCustomerDto { public long Id { get; init; } public string? Reference { get; init; } }
    private sealed class MaxioSubscriptionEnvelope { public MaxioSubscriptionDto? Subscription { get; init; } }
    private sealed class MaxioSubscriptionDto
    {
        public long Id { get; init; }
        public string? Reference { get; init; }
        public string? State { get; init; }
        [JsonPropertyName("next_billing_at")] public DateTimeOffset? NextBillingAt { get; init; }
        [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; init; }
        public string? Currency { get; init; }
        public MaxioProductDto? Product { get; init; }
    }
}
