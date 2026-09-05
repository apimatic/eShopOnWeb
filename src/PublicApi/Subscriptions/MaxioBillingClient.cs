using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer> FindOrCreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, CancellationToken cancellationToken);
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var family = "handle:" + _options.ProductFamilyHandle;
        using var request = CreateRequest(HttpMethod.Get, $"product_families/{Uri.EscapeDataString(family)}/products.json");
        using var response = await SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(JsonOptions, cancellationToken)
            ?? new List<ProductEnvelope>();
        return payload.Where(x => x.Product is not null).Select(x => x.Product!.ToPlan()).ToList();
    }

    public async Task<MaxioCustomer> FindOrCreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken cancellationToken)
    {
        var found = await FindCustomerByReferenceAsync(customer.Reference, cancellationToken);
        if (found is not null)
            return found;

        using var request = CreateRequest(HttpMethod.Post, "customers.json");
        request.Content = JsonContent.Create(new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        }, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
            return payload?.Customer?.ToCustomer()
                ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an invalid customer response.");
        }

        // The contract says customer references are unique. A duplicate response can only be a
        // competing request, so re-read it rather than creating a second customer.
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var concurrentCustomer = await FindCustomerByReferenceAsync(customer.Reference, cancellationToken);
            if (concurrentCustomer is not null)
                return concurrentCustomer;
        }

        await ThrowForFailureAsync(response, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"customers/{customerId}/subscriptions.json");
        using var response = await SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(JsonOptions, cancellationToken)
            ?? new List<SubscriptionEnvelope>();
        return payload.Where(x => x.Subscription is not null).Select(x => x.Subscription!.ToSubscription()).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "subscriptions.json");
        request.Content = JsonContent.Create(new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference,
                PaymentCollectionMethod = "remittance"
            }
        }, options: JsonOptions);

        using var response = await SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        return payload?.Subscription?.ToSubscription()
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an invalid subscription response.");
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            await ThrowForFailureAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return payload?.Customer?.ToCustomer()
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an invalid customer response.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        _options.ValidateForRequest();
        var request = new HttpRequestMessage(method, new Uri(_options.GetBaseUri(), relativePath));
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForFailureAsync(response, cancellationToken);
        }
        return response;
    }

    private static async Task ThrowForFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, body);
    }

    private sealed class ProductEnvelope { public Product? Product { get; init; } }
    private sealed class CustomerEnvelope { public Customer? Customer { get; init; } }
    private sealed class SubscriptionEnvelope { public Subscription? Subscription { get; init; } }
    private sealed class CreateCustomerRequest { public CreateCustomer Customer { get; init; } = new(); }
    private sealed class CreateSubscriptionRequest { public CreateSubscription Subscription { get; init; } = new(); }
    private sealed class CreateCustomer
    {
        [JsonPropertyName("first_name")] public string FirstName { get; init; } = string.Empty;
        [JsonPropertyName("last_name")] public string LastName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Reference { get; init; } = string.Empty;
    }
    private sealed class CreateSubscription
    {
        [JsonPropertyName("product_handle")] public string ProductHandle { get; init; } = string.Empty;
        [JsonPropertyName("customer_id")] public long CustomerId { get; init; }
        public string Reference { get; init; } = string.Empty;
        [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; init; } = string.Empty;
    }
    private sealed class Product
    {
        public long Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Handle { get; init; }
        [JsonPropertyName("price_in_cents")] public long PriceInCents { get; init; }
        public int Interval { get; init; }
        [JsonPropertyName("interval_unit")] public string IntervalUnit { get; init; } = string.Empty;
        [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; init; }
        [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; init; }
        public MaxioPlan ToPlan() => new(Id, Handle ?? string.Empty, Name, PriceInCents, Interval, IntervalUnit, RequireCreditCard, ArchivedAt);
    }
    private sealed class Customer
    {
        public long Id { get; init; }
        [JsonPropertyName("first_name")] public string FirstName { get; init; } = string.Empty;
        [JsonPropertyName("last_name")] public string LastName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? Reference { get; init; }
        public MaxioCustomer ToCustomer() => new(Id, FirstName, LastName, Email, Reference);
    }
    private sealed class Subscription
    {
        public long Id { get; init; }
        public string State { get; init; } = string.Empty;
        [JsonPropertyName("product_price_in_cents")] public long ProductPriceInCents { get; init; }
        [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
        [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; init; }
        public string? Reference { get; init; }
        public Product? Product { get; init; }
        public MaxioSubscription ToSubscription() => new(Id, State, ProductPriceInCents, CurrentPeriodEndsAt, NextAssessmentAt, Reference,
            Product is null ? null : Product.ToPlan());
    }
}

public sealed class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
}

public sealed record MaxioPlan(long Id, string Handle, string Name, long PriceInCents, int Interval, string IntervalUnit, bool RequireCreditCard, DateTimeOffset? ArchivedAt);
public sealed record MaxioCustomer(long Id, string FirstName, string LastName, string Email, string? Reference);
public sealed record MaxioCustomerCreate(string FirstName, string LastName, string Email, string Reference);
public sealed record MaxioSubscription(long Id, string State, long ProductPriceInCents, DateTimeOffset? CurrentPeriodEndsAt, DateTimeOffset? NextAssessmentAt, string? Reference, MaxioPlan? Plan);
public sealed record MaxioSubscriptionCreate(string ProductHandle, long CustomerId, string Reference);
