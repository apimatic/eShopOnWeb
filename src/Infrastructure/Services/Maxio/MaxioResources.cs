using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

// Wire shapes for the Maxio Advanced Billing REST API, mirroring the operation schemas in
// maxio-spec/openapi.yaml. Every Maxio payload wraps its resource in a single named property, so
// each resource gets a matching envelope type. These are internal to the provider seam:
// MaxioBillingClient maps them onto the ApplicationCore subscription aggregate.
//
// Money units differ by resource and are preserved verbatim here:
//   * products, subscriptions and migration previews report integer minor units (`*_in_cents`);
//   * components report decimal major units as a string (`unit_price`).

internal class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

internal class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public int BalanceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool? CancelAtEndOfPeriod { get; set; }

    [JsonPropertyName("delayed_cancel_at")]
    public DateTimeOffset? DelayedCancelAt { get; set; }

    /// <summary>Set when a delayed product change is scheduled for the next renewal.</summary>
    [JsonPropertyName("next_product_handle")]
    public string? NextProductHandle { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal class MaxioComponentEnvelope
{
    [JsonPropertyName("component")]
    public MaxioComponent? Component { get; set; }
}

internal class MaxioComponent
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("pricing_scheme")]
    public string? PricingScheme { get; set; }

    /// <summary>Decimal major units as a string, e.g. "0.01" — components are not reported in minor units.</summary>
    [JsonPropertyName("unit_price")]
    public string? UnitPrice { get; set; }

    [JsonPropertyName("product_family_id")]
    public int ProductFamilyId { get; set; }
}

internal class MaxioSubscriptionComponentEnvelope
{
    [JsonPropertyName("component")]
    public MaxioSubscriptionComponent? Component { get; set; }
}

internal class MaxioSubscriptionComponent
{
    [JsonPropertyName("component_id")]
    public int ComponentId { get; set; }

    [JsonPropertyName("subscription_id")]
    public int SubscriptionId { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>The metered usage accrued in the current period. Never negative; Maxio floors it at zero.</summary>
    [JsonPropertyName("unit_balance")]
    public decimal? UnitBalance { get; set; }
}

internal class MaxioUsageEnvelope
{
    [JsonPropertyName("usage")]
    public MaxioUsage? Usage { get; set; }
}

internal class MaxioUsage
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("memo")]
    public string? Memo { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Maxio reports this as a number on create and as a decimal string on list.</summary>
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("component_id")]
    public int ComponentId { get; set; }

    [JsonPropertyName("component_handle")]
    public string? ComponentHandle { get; set; }

    [JsonPropertyName("subscription_id")]
    public int SubscriptionId { get; set; }
}

internal class MaxioMigrationPreviewEnvelope
{
    [JsonPropertyName("migration")]
    public MaxioMigrationPreview? Migration { get; set; }
}

internal class MaxioMigrationPreview
{
    [JsonPropertyName("prorated_adjustment_in_cents")]
    public int ProratedAdjustmentInCents { get; set; }

    [JsonPropertyName("charge_in_cents")]
    public int ChargeInCents { get; set; }

    [JsonPropertyName("payment_due_in_cents")]
    public int PaymentDueInCents { get; set; }

    [JsonPropertyName("credit_applied_in_cents")]
    public int CreditAppliedInCents { get; set; }
}

internal class MaxioErrorList
{
    [JsonPropertyName("errors")]
    public string[]? Errors { get; set; }
}
