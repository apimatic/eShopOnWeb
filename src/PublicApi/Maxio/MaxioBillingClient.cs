using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API. The API contract is intentionally
/// kept here so callers never need to know Maxio wire names or authenticate directly.
/// </summary>
public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private const int MaxPageSize = 200;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioBillingClient> _logger;
    private readonly string _productFamilyHandle;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        var settings = options.Value;
        _productFamilyHandle = settings.ProductFamilyHandle;
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = settings.GetBaseUri();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:X")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        // Product-family handles are stable across Maxio reseeds; numeric IDs are not.
        var familyHandle = _productFamilyHandle;
        var products = new List<MaxioProductEnvelope>();
        for (var page = 1; ; page++)
        {
            var response = await SendAsync<List<MaxioProductEnvelope>>(
                HttpMethod.Get,
                $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?page={page}&per_page={MaxPageSize}",
                null,
                cancellationToken);

            products.AddRange(response);
            if (response.Count < MaxPageSize)
            {
                break;
            }
        }

        return products
            .Select(product => product.Product)
            .Where(product => product is not null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => new MaxioPlan(
                product!.Handle!,
                product.Name ?? product.Handle!,
                product.Description,
                product.PriceInCents,
                product.Interval,
                product.IntervalUnit ?? string.Empty,
                product.RequireCreditCard,
                product.Currency))
            .OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        FindAsync<MaxioCustomerResponse, MaxioCustomer>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            response => response.Customer is null ? null : new MaxioCustomer(response.Customer.Id, response.Customer.Reference ?? reference),
            cancellationToken);

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new CreateCustomerRequest(new CreateCustomer(firstName, lastName, email, reference)),
            cancellationToken);

        var customer = response.Customer ?? throw new InvalidOperationException("Maxio did not return the created customer.");
        return new MaxioCustomer(customer.Id, customer.Reference ?? reference);
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        FindAsync<MaxioSubscriptionResponse, MaxioSubscription>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            response => response.Subscription is null ? null : ToSubscription(response.Subscription),
            cancellationToken);

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string subscriptionReference, string planHandle, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            new CreateSubscriptionRequest(new CreateSubscription(planHandle, customerReference, subscriptionReference, "remittance")),
            cancellationToken);

        return response.Subscription is null
            ? throw new InvalidOperationException("Maxio did not return the created subscription.")
            : ToSubscription(response.Subscription);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var subscriptions = new List<MaxioSubscription>();
        for (var page = 1; ; page++)
        {
            var response = await SendAsync<List<MaxioSubscriptionEnvelope>>(
                HttpMethod.Get,
                $"customers/{customerId}/subscriptions.json?page={page}&per_page={MaxPageSize}",
                null,
                cancellationToken);
            subscriptions.AddRange(response.Where(item => item.Subscription is not null).Select(item => ToSubscription(item.Subscription!)));
            if (response.Count < MaxPageSize)
            {
                break;
            }
        }

        return subscriptions;
    }

    private async Task<T?> FindAsync<TResponse, T>(string relativeUrl, Func<TResponse, T?> mapper, CancellationToken cancellationToken)
        where TResponse : class
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var content = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        return content is null ? throw new InvalidOperationException("Maxio returned an empty response.") : mapper(content);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Maxio returned an empty response.");
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Consume the body so the connection can be reused, but do not expose provider details to callers.
            _ = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Maxio Billing API returned HTTP {StatusCode}.", (int)response.StatusCode);
            throw new MaxioApiException((int)response.StatusCode);
        }
    }

    private static MaxioSubscription ToSubscription(MaxioSubscriptionPayload subscription)
    {
        var product = subscription.Product ?? throw new InvalidOperationException("Maxio subscription response did not include its product.");
        var customer = subscription.Customer ?? throw new InvalidOperationException("Maxio subscription response did not include its customer.");
        return new MaxioSubscription(
            subscription.Id,
            subscription.Reference ?? string.Empty,
            subscription.State ?? string.Empty,
            product.Handle ?? string.Empty,
            product.Name ?? product.Handle ?? string.Empty,
            subscription.ProductPriceInCents,
            subscription.Currency ?? product.Currency,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            customer.Id);
    }

    private sealed record CreateCustomerRequest([property: JsonPropertyName("customer")] CreateCustomer Customer);
    private sealed record CreateCustomer(
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("reference")] string Reference);
    private sealed record CreateSubscriptionRequest([property: JsonPropertyName("subscription")] CreateSubscription Subscription);
    private sealed record CreateSubscription(
        [property: JsonPropertyName("product_handle")] string ProductHandle,
        [property: JsonPropertyName("customer_reference")] string CustomerReference,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod);

    private sealed class MaxioCustomerResponse { [JsonPropertyName("customer")] public MaxioCustomerPayload? Customer { get; set; } }
    private sealed class MaxioCustomerPayload
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("reference")] public string? Reference { get; set; }
    }
    private sealed class MaxioProductEnvelope { [JsonPropertyName("product")] public MaxioProductPayload? Product { get; set; } }
    private sealed class MaxioProductPayload
    {
        [JsonPropertyName("handle")] public string? Handle { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
        [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
        [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
    }
    private sealed class MaxioSubscriptionEnvelope { [JsonPropertyName("subscription")] public MaxioSubscriptionPayload? Subscription { get; set; } }
    private sealed class MaxioSubscriptionResponse { [JsonPropertyName("subscription")] public MaxioSubscriptionPayload? Subscription { get; set; } }
    private sealed class MaxioSubscriptionPayload
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("reference")] public string? Reference { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("product_price_in_cents")] public long ProductPriceInCents { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
        [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        [JsonPropertyName("product")] public MaxioProductPayload? Product { get; set; }
        [JsonPropertyName("customer")] public MaxioCustomerPayload? Customer { get; set; }
    }
}
