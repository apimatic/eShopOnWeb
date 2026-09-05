using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Server-side Maxio Advanced Billing integration. Maxio customer and subscription
/// references are deterministic external identifiers. With a persistent Identity
/// store, this mapping survives a process restart without a local subscription table.
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const string CustomerReferencePrefix = "eshoponweb-customer:";
    private const string SubscriptionReferencePrefix = "eshoponweb-subscription:";
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _productFamilyHandle = options.Value.ProductFamilyHandle;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        using var familyDocument = await GetDocumentAsync(
            $"product_families/handle:{Uri.EscapeDataString(_productFamilyHandle)}.json", cancellationToken);
        var family = familyDocument.RootElement.GetProperty("product_family");
        var familyId = family.GetProperty("id").GetInt64();

        using var plansDocument = await GetDocumentAsync($"product_families/{familyId}/products.json", cancellationToken);
        var plans = new List<SubscriptionPlanDto>();
        foreach (var item in plansDocument.RootElement.EnumerateArray())
        {
            var product = item.GetProperty("product");
            if (product.TryGetProperty("archived_at", out var archivedAt) && archivedAt.ValueKind != JsonValueKind.Null)
            {
                continue;
            }

            plans.Add(new SubscriptionPlanDto
            {
                Handle = product.GetProperty("handle").GetString() ?? string.Empty,
                Name = product.GetProperty("name").GetString() ?? string.Empty,
                Description = GetOptionalString(product, "description"),
                PriceInCents = product.GetProperty("price_in_cents").GetInt64(),
                Currency = GetOptionalString(product, "currency") ?? string.Empty,
                Interval = product.GetProperty("interval").GetInt32(),
                IntervalUnit = product.GetProperty("interval_unit").GetString() ?? string.Empty
            });
        }

        return plans.OrderBy(plan => plan.PriceInCents).ToList();
    }

    public async Task<SubscriptionDto?> FindSubscriptionAsync(string userId, string planHandle, CancellationToken cancellationToken)
    {
        var reference = SubscriptionReference(userId, planHandle);
        using var document = await TryGetDocumentAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return document is null ? null : ToSubscription(document.RootElement.GetProperty("subscription"));
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken)
    {
        var requestedPlan = (await GetPlansAsync(cancellationToken))
            .SingleOrDefault(plan => string.Equals(plan.Handle, planHandle, StringComparison.Ordinal));
        if (requestedPlan is null)
        {
            throw new ArgumentException("The requested plan is not available in the configured Maxio product family.", nameof(planHandle));
        }

        var existing = await FindSubscriptionAsync(userId, requestedPlan.Handle, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var customerId = await EnsureCustomerAsync(userId, email, cancellationToken);
        var reference = SubscriptionReference(userId, requestedPlan.Handle);
        var payload = new
        {
            subscription = new
            {
                product_handle = requestedPlan.Handle,
                customer_id = customerId,
                reference,
                // The demo plans do not require a payment profile. Remittance is the
                // documented collection method for a no-card signup on sites using
                // Relationship Invoicing.
                payment_collection_method = "remittance"
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            $"subscriptions.json?uniqueness_token={UniquenessToken(reference)}", payload, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var duplicate = await WaitForSubscriptionAsync(userId, requestedPlan.Handle, cancellationToken);
            if (duplicate is not null)
            {
                return duplicate;
            }
        }

        await EnsureSuccessAsync(response, "creating the subscription");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return ToSubscription(document.RootElement.GetProperty("subscription"));
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        using var document = await GetDocumentAsync($"customers/{customer.Value}/subscriptions.json", cancellationToken);
        return document.RootElement.EnumerateArray()
            .Select(item => ToSubscription(item.GetProperty("subscription")))
            .OrderByDescending(subscription => subscription.NextBillingAt)
            .ToList();
    }

    private async Task<long> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        var reference = CustomerReference(userId);
        var payload = new
        {
            customer = new
            {
                // eShopOnWeb has no profile-name fields. These required Maxio values do not
                // claim to be a legal name; the stable application user id remains the link.
                first_name = "eShopOnWeb",
                last_name = "Shopper",
                email,
                reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            $"customers.json?uniqueness_token={UniquenessToken(reference)}", payload, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var customerId = await WaitForCustomerAsync(userId, cancellationToken);
            if (customerId is not null)
            {
                return customerId.Value;
            }
        }

        await EnsureSuccessAsync(response, "creating the customer");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.GetProperty("customer").GetProperty("id").GetInt64();
    }

    private async Task<long?> FindCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        using var document = await TryGetDocumentAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(CustomerReference(userId))}", cancellationToken);
        return document is null ? null : document.RootElement.GetProperty("customer").GetProperty("id").GetInt64();
    }

    private async Task<long?> WaitForCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var customer = await FindCustomerAsync(userId, cancellationToken);
            if (customer is not null)
            {
                return customer;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
        return null;
    }

    private async Task<SubscriptionDto?> WaitForSubscriptionAsync(string userId, string planHandle, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var subscription = await FindSubscriptionAsync(userId, planHandle, cancellationToken);
            if (subscription is not null)
            {
                return subscription;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
        return null;
    }

    private async Task<JsonDocument> GetDocumentAsync(string relativeUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUri, cancellationToken);
        await EnsureSuccessAsync(response, "retrieving data");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    private async Task<JsonDocument?> TryGetDocumentAsync(string relativeUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, "retrieving data");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        _logger.LogWarning("Maxio failed while {Operation}. Status code: {StatusCode}", operation, (int)response.StatusCode);
        await response.Content.LoadIntoBufferAsync();
        throw new MaxioApiException(operation, (int)response.StatusCode);
    }

    private static SubscriptionDto ToSubscription(JsonElement subscription)
    {
        var product = subscription.TryGetProperty("product", out var productElement) && productElement.ValueKind != JsonValueKind.Null
            ? productElement
            : default;
        var nextAssessment = GetOptionalDateTimeOffset(subscription, "next_assessment_at")
            ?? GetOptionalDateTimeOffset(subscription, "current_period_ends_at");

        return new SubscriptionDto
        {
            Id = subscription.GetProperty("id").GetInt64(),
            State = GetOptionalString(subscription, "state") ?? string.Empty,
            PlanHandle = product.ValueKind == JsonValueKind.Undefined ? string.Empty : GetOptionalString(product, "handle") ?? string.Empty,
            PlanName = product.ValueKind == JsonValueKind.Undefined ? string.Empty : GetOptionalString(product, "name") ?? string.Empty,
            PriceInCents = GetOptionalInt64(subscription, "product_price_in_cents")
                ?? (product.ValueKind == JsonValueKind.Undefined ? 0 : GetOptionalInt64(product, "price_in_cents")) ?? 0,
            Currency = GetOptionalString(subscription, "currency") ?? (product.ValueKind == JsonValueKind.Undefined ? null : GetOptionalString(product, "currency")) ?? string.Empty,
            NextBillingAt = nextAssessment
        };
    }

    private static string CustomerReference(string userId) => CustomerReferencePrefix + userId;
    private static string SubscriptionReference(string userId, string planHandle) => $"{SubscriptionReferencePrefix}{userId}:{planHandle}";

    private static string UniquenessToken(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString()
            : null;

    private static long? GetOptionalInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetInt64()
            : null;

    private static DateTimeOffset? GetOptionalDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = GetOptionalString(element, propertyName);
        return value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date
            : null;
    }
}
