using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services;

// The Maxio Advanced Billing wire format. These types exist only to deserialize the provider's
// responses and serialize its requests; nothing outside this file's assembly sees them. Maxio
// wraps almost every payload in a single-property envelope, hence the *Envelope types.
//
// Money note: Maxio expresses product and subscription money as integer *cents*
// (price_in_cents), while component unit prices arrive as a decimal string in whole currency
// units ("0.01"). MaxioBillingClient normalizes both to decimal dollars.

internal sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
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
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamilyEnvelope
{
    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new MaxioCreateCustomer();
}

internal sealed class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool? CancelAtEndOfPeriod { get; set; }

    [JsonPropertyName("delayed_cancel_at")]
    public DateTimeOffset? DelayedCancelAt { get; set; }

    [JsonPropertyName("next_product_handle")]
    public string? NextProductHandle { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new MaxioCreateSubscription();
}

internal sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    /// <summary>
    /// Set to <c>remittance</c> so the subscription is invoiced rather than charged immediately.
    /// Without it Maxio rejects the enrollment with "No payment method was on file" even when the
    /// product has require_credit_card off, because the default automatic collection tries to
    /// settle the first invoice straight away.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class MaxioUpdateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioUpdateSubscription Subscription { get; set; } = new MaxioUpdateSubscription();
}

internal sealed class MaxioUpdateSubscription
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("product_change_delayed")]
    public bool? ProductChangeDelayed { get; set; }
}

internal sealed class MaxioMigrationRequest
{
    [JsonPropertyName("migration")]
    public MaxioMigration Migration { get; set; } = new MaxioMigration();
}

internal sealed class MaxioMigration
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }
}

internal sealed class MaxioMigrationPreviewEnvelope
{
    [JsonPropertyName("migration")]
    public MaxioMigrationPreview? Migration { get; set; }
}

internal sealed class MaxioMigrationPreview
{
    [JsonPropertyName("prorated_adjustment_in_cents")]
    public long ProratedAdjustmentInCents { get; set; }

    [JsonPropertyName("charge_in_cents")]
    public long ChargeInCents { get; set; }

    [JsonPropertyName("payment_due_in_cents")]
    public long PaymentDueInCents { get; set; }

    [JsonPropertyName("credit_applied_in_cents")]
    public long CreditAppliedInCents { get; set; }
}

internal sealed class MaxioComponentEnvelope
{
    [JsonPropertyName("component")]
    public MaxioComponent? Component { get; set; }
}

internal sealed class MaxioComponent
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    /// <summary>The kind discriminator. A metered component reports <c>metered_component</c>.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("pricing_scheme")]
    public string? PricingScheme { get; set; }

    /// <summary>A decimal string in whole currency units, e.g. <c>"0.01"</c>.</summary>
    [JsonPropertyName("unit_price")]
    public string? UnitPrice { get; set; }

    [JsonPropertyName("unit_name")]
    public string? UnitName { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }
}

internal sealed class MaxioSubscriptionComponentEnvelope
{
    [JsonPropertyName("component")]
    public MaxioSubscriptionComponent? Component { get; set; }
}

internal sealed class MaxioSubscriptionComponent
{
    [JsonPropertyName("component_id")]
    public int ComponentId { get; set; }

    [JsonPropertyName("component_handle")]
    public string? ComponentHandle { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>
    /// The period-to-date metered total. Maxio accumulates every recorded usage quantity here;
    /// allocated_quantity is a quantity-based concept and is null for metered components.
    /// </summary>
    [JsonPropertyName("unit_balance")]
    public decimal? UnitBalance { get; set; }
}

internal sealed class MaxioUsageEnvelope
{
    [JsonPropertyName("usage")]
    public MaxioUsage? Usage { get; set; }
}

internal sealed class MaxioUsage
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("memo")]
    public string? Memo { get; set; }

    [JsonPropertyName("component_id")]
    public int ComponentId { get; set; }

    [JsonPropertyName("component_handle")]
    public string? ComponentHandle { get; set; }

    [JsonPropertyName("subscription_id")]
    public int SubscriptionId { get; set; }
}

internal sealed class MaxioCreateUsageRequest
{
    [JsonPropertyName("usage")]
    public MaxioCreateUsage Usage { get; set; } = new MaxioCreateUsage();
}

internal sealed class MaxioCreateUsage
{
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("memo")]
    public string? Memo { get; set; }
}

internal sealed class MaxioCancellationRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCancellationOptions Subscription { get; set; } = new MaxioCancellationOptions();
}

internal sealed class MaxioCancellationOptions
{
    [JsonPropertyName("cancellation_message")]
    public string? CancellationMessage { get; set; }
}

internal sealed class MaxioHoldRequest
{
    [JsonPropertyName("hold")]
    public MaxioHoldOptions Hold { get; set; } = new MaxioHoldOptions();
}

internal sealed class MaxioHoldOptions
{
    [JsonPropertyName("automatically_resume_at")]
    public DateTimeOffset? AutomaticallyResumeAt { get; set; }
}

internal static class MaxioComponentKinds
{
    public const string Metered = "metered_component";
}
