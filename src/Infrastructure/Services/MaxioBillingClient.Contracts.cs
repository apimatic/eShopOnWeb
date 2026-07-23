using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services;

// Wire shapes for the Maxio Advanced Billing API, named exactly as the OpenAPI specification
// defines them. They stay internal to this project: nothing outside the client sees a Maxio type.
// Property names are bound by the snake_case naming policy configured on the client.

internal class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public string? ArchivedAt { get; set; }
}

internal class MaxioComponentEnvelope
{
    [JsonPropertyName("component")]
    public MaxioComponent? Component { get; set; }
}

internal class MaxioComponent
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Kind { get; set; }
    public string? PricingScheme { get; set; }
    public string? UnitPrice { get; set; }
    public int ProductFamilyId { get; set; }
}

internal class MaxioSubscriptionComponentEnvelope
{
    [JsonPropertyName("component")]
    public MaxioSubscriptionComponent? Component { get; set; }
}

internal class MaxioSubscriptionComponent
{
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public string? Kind { get; set; }

    /// <summary>Units accumulated from usage so far this period.</summary>
    public decimal? UnitBalance { get; set; }
}

internal class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public System.DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public System.DateTimeOffset? DelayedCancelAt { get; set; }
    public string? NextProductHandle { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

internal class MaxioUsageEnvelope
{
    [JsonPropertyName("usage")]
    public MaxioUsage? Usage { get; set; }
}

internal class MaxioUsage
{
    public long Id { get; set; }
    public string? Memo { get; set; }
    public System.DateTimeOffset? CreatedAt { get; set; }
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public int SubscriptionId { get; set; }

    /// <summary>The specification types this as either an integer or a string, so it is read raw.</summary>
    public JsonElement Quantity { get; set; }
}

internal class MaxioMigrationPreviewEnvelope
{
    [JsonPropertyName("migration")]
    public MaxioMigrationPreview? Migration { get; set; }
}

internal class MaxioMigrationPreview
{
    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long PaymentDueInCents { get; set; }
    public long CreditAppliedInCents { get; set; }
}

internal class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new MaxioCreateCustomer();
}

internal class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new MaxioCreateSubscription();
}

internal class MaxioCreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>Omitted when unset so the provider applies the site's own default.</summary>
    public string? PaymentCollectionMethod { get; set; }
}

internal class MaxioUpdateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioUpdateSubscription Subscription { get; set; } = new MaxioUpdateSubscription();
}

internal class MaxioUpdateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Schedules the product change for the next renewal instead of applying it now.</summary>
    public bool ProductChangeDelayed { get; set; }
}

internal class MaxioCreateUsageRequest
{
    [JsonPropertyName("usage")]
    public MaxioCreateUsage Usage { get; set; } = new MaxioCreateUsage();
}

internal class MaxioCreateUsage
{
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
}

internal class MaxioMigrationRequest
{
    [JsonPropertyName("migration")]
    public MaxioMigration Migration { get; set; } = new MaxioMigration();
}

internal class MaxioMigration
{
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Keeps the billing period so the provider issues a prorated charge or credit.</summary>
    public bool PreservePeriod { get; set; }

    public bool IncludeTrial { get; set; }

    public bool IncludeInitialCharge { get; set; }
}

internal class MaxioCancellationRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCancellationOptions Subscription { get; set; } = new MaxioCancellationOptions();
}

internal class MaxioCancellationOptions
{
    public string? CancellationMessage { get; set; }
}

internal class MaxioProductListItem
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal class MaxioSubscriptionListItem
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal static class MaxioListExtensions
{
    public static IEnumerable<MaxioProduct> Products(this IEnumerable<MaxioProductListItem>? items)
    {
        if (items is null)
        {
            yield break;
        }

        foreach (var item in items)
        {
            if (item.Product is not null)
            {
                yield return item.Product;
            }
        }
    }

    public static IEnumerable<MaxioSubscription> Subscriptions(this IEnumerable<MaxioSubscriptionListItem>? items)
    {
        if (items is null)
        {
            yield break;
        }

        foreach (var item in items)
        {
            if (item.Subscription is not null)
            {
                yield return item.Subscription;
            }
        }
    }
}
