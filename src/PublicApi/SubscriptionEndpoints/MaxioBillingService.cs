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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Small HTTP client over the documented Maxio Billing API endpoints. Maxio remains the source of billing truth.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly AppIdentityDbContext _identityDbContext;
    private readonly MaxioOptions _options;
    private readonly SubscriptionEnrollmentCoordinator _coordinator;

    public MaxioBillingService(HttpClient httpClient, AppIdentityDbContext identityDbContext,
        IOptions<MaxioOptions> options, SubscriptionEnrollmentCoordinator coordinator)
    {
        _httpClient = httpClient;
        _identityDbContext = identityDbContext;
        _options = options.Value;
        _coordinator = coordinator;
    }

    public async Task<IReadOnlyList<SubscriptionPlanResponse>> ListPlansAsync(CancellationToken cancellationToken)
    {
        _options.Validate();
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var response = await GetRequiredAsync<List<ProductEnvelope>>($"product_families/{family}/products.json?per_page=200", cancellationToken);
        return response
            .Select(item => item.Product)
            .Where(product => product is not null && product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => new SubscriptionPlanResponse(product!.Handle!, product.Name ?? product.Handle!, product.Description,
                product.PriceInCents, product.Interval, product.IntervalUnit ?? "month", product.RequireCreditCard))
            .OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> ListSubscriptionsAsync(MaxioShopper shopper, CancellationToken cancellationToken)
    {
        _options.Validate();
        var customer = await FindCustomerAsync(CustomerReference(shopper.UserId), cancellationToken);
        if (customer is null)
            return [];

        return (await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
            .Select(ToResponse)
            .OrderByDescending(subscription => subscription.NextBillingAt)
            .ToList();
    }

    public async Task<SubscriptionResponse> SubscribeAsync(MaxioShopper shopper, string planHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(plan => string.Equals(plan.Handle, planHandle, StringComparison.Ordinal)))
            throw new MaxioApiException(HttpStatusCode.UnprocessableEntity, "The requested plan is not available in the configured product family.");

        using var enrollmentLock = await _coordinator.AcquireAsync(shopper.UserId, planHandle, cancellationToken);
        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var existing = await FindExistingEnrollmentAsync(shopper.UserId, planHandle, customer.Id, cancellationToken);
        if (existing is not null)
            return ToResponse(existing);

        var reference = SubscriptionReference(shopper.UserId, planHandle);
        var byReference = await FindSubscriptionAsync(reference, cancellationToken);
        if (byReference is not null)
        {
            await SaveLinkAsync(shopper.UserId, planHandle, customer.Id, byReference.Id, cancellationToken);
            return ToResponse(byReference);
        }

        try
        {
            var created = await PostRequiredAsync<SubscriptionResponseEnvelope>("subscriptions.json", new
            {
                subscription = new
                {
                    product_handle = planHandle,
                    customer_id = customer.Id,
                    // Invoice collection enrolls the seeded no-card plans without collecting raw payment data.
                    payment_collection_method = "invoice",
                    reference
                }
            }, cancellationToken);

            if (created.Subscription is null)
                throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an invalid subscription response.");

            await SaveLinkAsync(shopper.UserId, planHandle, customer.Id, created.Subscription.Id, cancellationToken);
            return ToResponse(created.Subscription);
        }
        catch (MaxioApiException exception) when ((int)exception.StatusCode >= 500)
        {
            // A network/proxy failure may occur after Maxio created the subscription. Re-read the stable reference before surfacing failure.
            var recovered = await FindSubscriptionAsync(reference, cancellationToken);
            if (recovered is not null)
            {
                await SaveLinkAsync(shopper.UserId, planHandle, customer.Id, recovered.Id, cancellationToken);
                return ToResponse(recovered);
            }
            throw;
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(MaxioShopper shopper, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(shopper.UserId);
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
            return existing;

        try
        {
            var created = await PostRequiredAsync<CustomerResponse>("customers.json", new
            {
                customer = new
                {
                    first_name = shopper.FirstName,
                    last_name = shopper.LastName,
                    email = shopper.Email,
                    reference
                }
            }, cancellationToken);
            return created.Customer ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an invalid customer response.");
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The customer reference is unique in Maxio; another concurrent request may have created it.
            var concurrentCustomer = await FindCustomerAsync(reference, cancellationToken);
            if (concurrentCustomer is null)
                throw;
            return concurrentCustomer;
        }
    }

    private async Task<MaxioSubscription?> FindExistingEnrollmentAsync(string userId, string planHandle, int customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        var link = await _identityDbContext.MaxioSubscriptionLinks
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == planHandle, cancellationToken);

        if (link?.MaxioSubscriptionId is int linkedId)
        {
            var linkedSubscription = subscriptions.SingleOrDefault(subscription => subscription.Id == linkedId);
            if (linkedSubscription is not null && IsLive(linkedSubscription.State))
                return linkedSubscription;
        }

        var matchingLiveSubscription = subscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.Ordinal) && IsLive(subscription.State));
        if (matchingLiveSubscription is not null)
            await SaveLinkAsync(userId, planHandle, customerId, matchingLiveSubscription.Id, cancellationToken);

        return matchingLiveSubscription;
    }

    private async Task SaveLinkAsync(string userId, string planHandle, int customerId, int subscriptionId, CancellationToken cancellationToken)
    {
        var link = await _identityDbContext.MaxioSubscriptionLinks
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == planHandle, cancellationToken);
        if (link is null)
        {
            link = new MaxioSubscriptionLink { UserId = userId, ProductHandle = planHandle };
            _identityDbContext.MaxioSubscriptionLinks.Add(link);
        }

        link.MaxioCustomerId = customerId;
        link.MaxioSubscriptionId = subscriptionId;
        await _identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        return (await ReadRequiredAsync<CustomerResponse>(response, cancellationToken)).Customer;
    }

    private async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        return (await ReadRequiredAsync<SubscriptionResponseEnvelope>(response, cancellationToken)).Subscription;
    }

    private async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await GetRequiredAsync<List<SubscriptionResponseEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return response.Select(item => item.Subscription).Where(subscription => subscription is not null).Cast<MaxioSubscription>().ToList();
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadRequiredAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostRequiredAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        return await ReadRequiredAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new MaxioApiException(response.StatusCode, $"Maxio request failed with HTTP {(int)response.StatusCode}.");

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty response.");
    }

    private static bool IsLive(string? state) => state is "active" or "trialing" or "pending" or "assessing" or "past_due" or "awaiting_signup";
    private static string CustomerReference(string userId) => $"eshop-user-{userId}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-sub-{userId}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(planHandle))).Substring(0, 16)}";

    private static SubscriptionResponse ToResponse(MaxioSubscription subscription) => new(subscription.Id,
        subscription.Product?.Handle ?? string.Empty, subscription.Product?.Name ?? "Subscription",
        subscription.ProductPriceInCents, subscription.State ?? "unknown",
        subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt);

    private sealed class ProductEnvelope { [JsonPropertyName("product")] public MaxioProduct? Product { get; init; } }
    private sealed class SubscriptionResponseEnvelope { [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; init; } }
    private sealed class CustomerResponse { [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; init; } }
    private sealed class MaxioCustomer { [JsonPropertyName("id")] public int Id { get; init; } }
    private sealed class MaxioProduct
    {
        [JsonPropertyName("handle")] public string? Handle { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("price_in_cents")] public long PriceInCents { get; init; }
        [JsonPropertyName("interval")] public int Interval { get; init; }
        [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; init; }
        [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; init; }
        [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; init; }
    }
    private sealed class MaxioSubscription
    {
        [JsonPropertyName("id")] public int Id { get; init; }
        [JsonPropertyName("state")] public string? State { get; init; }
        [JsonPropertyName("product_price_in_cents")] public long ProductPriceInCents { get; init; }
        [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
        [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; init; }
        [JsonPropertyName("product")] public MaxioProduct? Product { get; init; }
    }
}
