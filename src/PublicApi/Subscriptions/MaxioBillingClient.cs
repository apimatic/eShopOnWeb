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
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, string uniquenessToken, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string uniquenessToken, CancellationToken cancellationToken);
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString(_options.ProductFamilyHandle);
        var products = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/handle:{family}/products.json?per_page=200", cancellationToken);

        return products
            .Select(item => item.Product)
            .Where(product => product is not null && product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => ToPlan(product!))
            .OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return payload.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, string uniquenessToken, CancellationToken cancellationToken)
    {
        var payload = new { customer, uniqueness_token = uniquenessToken };
        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken)).Customer
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an incomplete customer response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return subscriptions.Select(item => item.Subscription).Where(subscription => subscription is not null).Cast<MaxioSubscription>().ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string uniquenessToken, CancellationToken cancellationToken)
    {
        var payload = new
        {
            // The subscription catalog is intentionally configured for invoice billing, so no card capture is needed at enrollment.
            subscription = new { customer_id = customerId, product_handle = planHandle, payment_collection_method = "invoice" },
            uniqueness_token = uniquenessToken
        };
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken)).Subscription
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an incomplete subscription response.");
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, $"Maxio returned HTTP {(int)response.StatusCode}: {error}");
        }
    }

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
    public HttpStatusCode StatusCode { get; }
}

public sealed class MaxioCustomerInput
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
}

public sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; init; }
}

public sealed class MaxioCustomer
{
    public long Id { get; init; }
    public string? Reference { get; init; }
}

public sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; init; }
}

public sealed class MaxioProduct
{
    public long Id { get; init; }
    public string? Handle { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }
}

public sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; init; }
}

public sealed class MaxioSubscription
{
    public long Id { get; init; }
    public string? State { get; init; }
    public long ProductPriceInCents { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public MaxioProduct? Product { get; init; }
}
