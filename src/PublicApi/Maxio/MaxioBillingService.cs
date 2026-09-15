using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Server-side adapter for the verified Maxio Advanced Billing HTTP API.
/// Customer references are deterministic eShop user IDs, so no local billing mapping is required.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private const string PlansCacheKey = "maxio-subscription-plans";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        HttpClient client,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<MaxioBillingService> logger)
    {
        _options = options.Value;
        _options.EnsureValid();
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        return await _cache.GetOrCreateAsync(PlansCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var family = await GetRequiredAsync<MaxioProductFamilyEnvelope>(
                $"product_families/{HandleIdentifier(_options.ProductFamilyHandle)}.json", cancellationToken);
            var products = await GetRequiredAsync<List<MaxioProductEnvelope>>(
                $"product_families/{family.ProductFamily.Id}/products.json", cancellationToken);

            return (IReadOnlyList<SubscriptionPlanDto>)products
                .Select(x => x.Product)
                .Where(x => x.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Handle))
                .Select(x => new SubscriptionPlanDto(
                    x.Handle!, x.Name ?? x.Handle!, x.Description, x.PriceInCents, x.Interval, x.IntervalUnit ?? "month"))
                .OrderBy(x => x.PriceInCents)
                .ToList();
        }) ?? Array.Empty<SubscriptionPlanDto>();
    }

    public async Task<SubscriptionEnrollment?> SubscribeAsync(Shopper shopper, string planHandle, CancellationToken cancellationToken)
    {
        var plan = (await GetPlansAsync(cancellationToken))
            .SingleOrDefault(x => string.Equals(x.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
            return null;

        var customerReference = CustomerReference(shopper.Id);
        var gate = CustomerLocks.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, customerReference, cancellationToken);
            var subscriptions = await GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = subscriptions.FirstOrDefault(x =>
                string.Equals(x.Product?.Handle, plan.Handle, StringComparison.Ordinal) && IsCurrentlyEnrolled(x.State));
            if (existing is not null)
                return new SubscriptionEnrollment(ToSubscriptionDto(existing, plan), false);

            // Maxio's request-level uniqueness token protects a retry or concurrent request across app instances.
            var body = new
            {
                subscription = new
                {
                    product_handle = plan.Handle,
                    customer_id = customer.Id,
                    payment_collection_method = "remittance"
                },
                uniqueness_token = SubscriptionUniquenessToken(shopper.Id, plan.Handle)
            };

            var response = await PostAsync("subscriptions.json", body, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                // The original submission may have completed. Re-read the authoritative customer record.
                subscriptions = await GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                existing = subscriptions.FirstOrDefault(x =>
                    string.Equals(x.Product?.Handle, plan.Handle, StringComparison.Ordinal) && IsCurrentlyEnrolled(x.State));
                if (existing is not null)
                    return new SubscriptionEnrollment(ToSubscriptionDto(existing, plan), false);
            }

            await EnsureSuccessAsync(response, cancellationToken);
            var created = await DeserializeRequiredAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
            return new SubscriptionEnrollment(ToSubscriptionDto(created.Subscription, plan), true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(Shopper shopper, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerByReferenceAsync(CustomerReference(shopper.Id), cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var subscriptions = await GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(x => string.Equals(x.Product?.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            .Select(x => ToSubscriptionDto(x, null))
            .OrderByDescending(x => x.NextBillingAt)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(Shopper shopper, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
            return existing;

        var body = new
        {
            customer = new
            {
                first_name = "eShop",
                last_name = "Shopper",
                email = shopper.Email,
                reference = customerReference
            }
        };

        var response = await PostAsync("customers.json", body, cancellationToken);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A second node may have created the uniquely-referenced customer between lookup and POST.
            existing = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (existing is not null)
                return existing;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return (await DeserializeRequiredAsync<MaxioCustomerEnvelope>(response, cancellationToken)).Customer;
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, cancellationToken);
        return (await DeserializeRequiredAsync<MaxioCustomerEnvelope>(response, cancellationToken)).Customer;
    }

    private async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var response = await GetRequiredAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return response.Select(x => x.Subscription).ToList();
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeRequiredAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> PostAsync<T>(string path, T body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        return await SendAsync(request, cancellationToken);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
        => SendAsync(new HttpRequestMessage(method, path), cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new MaxioApiException("Maxio is currently unavailable.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException("Maxio did not respond in time.", exception);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        _logger.LogWarning("Maxio request failed with HTTP {StatusCode}.", (int)response.StatusCode);
        // Consume the body so a connection can be reused, but never expose a billing-provider error to callers.
        _ = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException("Maxio could not process the request.", response.StatusCode);
    }

    private static async Task<T> DeserializeRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var model = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return model ?? throw new MaxioApiException("Maxio returned an empty response.");
    }

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, SubscriptionPlanDto? requestedPlan)
    {
        var product = subscription.Product;
        return new SubscriptionDto(
            subscription.Id,
            product?.Handle ?? requestedPlan?.Handle ?? "unknown",
            product?.Name ?? requestedPlan?.Name ?? "Unknown plan",
            subscription.ProductPriceInCents ?? product?.PriceInCents ?? requestedPlan?.PriceInCents ?? 0,
            subscription.State ?? "unknown",
            subscription.NextAssessmentAt);
    }

    private static bool IsCurrentlyEnrolled(string? state)
        => state is not null && state is not ("canceled" or "expired");

    private static string CustomerReference(string shopperId) => $"eshop-user-{shopperId}";

    private static string SubscriptionUniquenessToken(string shopperId, string planHandle)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"eshop-subscription|{shopperId}|{planHandle}")));

    private static string HandleIdentifier(string handle) => Uri.EscapeDataString($"handle:{handle}");

    private sealed record MaxioProductFamilyEnvelope([property: JsonPropertyName("product_family")] MaxioProductFamily ProductFamily);
    private sealed record MaxioProductEnvelope([property: JsonPropertyName("product")] MaxioProduct Product);
    private sealed record MaxioCustomerEnvelope([property: JsonPropertyName("customer")] MaxioCustomer Customer);
    private sealed record MaxioSubscriptionEnvelope([property: JsonPropertyName("subscription")] MaxioSubscription Subscription);

    private sealed record MaxioProductFamily(long Id, string? Handle);
    private sealed record MaxioCustomer(long Id);
    private sealed record MaxioProduct(
        long Id,
        string? Name,
        string? Handle,
        string? Description,
        [property: JsonPropertyName("price_in_cents")] int PriceInCents,
        int Interval,
        [property: JsonPropertyName("interval_unit")] string? IntervalUnit,
        [property: JsonPropertyName("archived_at")] DateTimeOffset? ArchivedAt,
        [property: JsonPropertyName("product_family")] MaxioProductFamily? ProductFamily);
    private sealed record MaxioSubscription(
        long Id,
        string? State,
        [property: JsonPropertyName("product_price_in_cents")] int? ProductPriceInCents,
        [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt,
        MaxioProduct? Product);
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(string message, Exception? innerException = null) : base(message, innerException) { }
    public MaxioApiException(string message, HttpStatusCode statusCode) : base(message) => StatusCode = statusCode;

    public HttpStatusCode? StatusCode { get; }
}
