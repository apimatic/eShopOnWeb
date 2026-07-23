using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services;

// Wire shapes for the Maxio Advanced Billing REST API, mapped one-for-one from the OpenAPI
// specification under maxio-spec/. They stay internal to Infrastructure: nothing outside
// MaxioBillingClient sees them, so the rest of the application stays provider-agnostic.

internal class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

/// <summary>Product schema — the recurring plan. Prices are integer minor units.</summary>
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
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("archived_at")]
    public string? ArchivedAt { get; set; }
}

internal class MaxioProductFamilyEnvelope
{
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

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public string? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("activated_at")]
    public string? ActivatedAt { get; set; }

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool? CancelAtEndOfPeriod { get; set; }

    [JsonPropertyName("delayed_cancel_at")]
    public string? DelayedCancelAt { get; set; }

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

/// <summary>
/// Component schema. <c>unit_price</c> is a decimal string in whole currency units (e.g. "0.01"),
/// unlike product prices which are integer minor units.
/// </summary>
internal class MaxioComponent
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("pricing_scheme")]
    public string? PricingScheme { get; set; }

    [JsonPropertyName("unit_price")]
    public string? UnitPrice { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }
}

/// <summary>Subscription Component schema — carries the period-to-date <c>unit_balance</c>.</summary>
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

    [JsonPropertyName("component_handle")]
    public string? ComponentHandle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("unit_balance")]
    public decimal? UnitBalance { get; set; }
}

internal class MaxioUsageEnvelope
{
    [JsonPropertyName("usage")]
    public MaxioUsage? Usage { get; set; }
}

/// <summary>Usage schema. <c>quantity</c> may come back as a number or a string ("20.0").</summary>
internal class MaxioUsage
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("memo")]
    public string? Memo { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

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
    public long ProratedAdjustmentInCents { get; set; }

    [JsonPropertyName("charge_in_cents")]
    public long ChargeInCents { get; set; }

    [JsonPropertyName("payment_due_in_cents")]
    public long PaymentDueInCents { get; set; }

    [JsonPropertyName("credit_applied_in_cents")]
    public long CreditAppliedInCents { get; set; }
}

// --- request bodies -------------------------------------------------------------------------

internal class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new();
}

internal class MaxioCreateCustomer
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

internal class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

internal class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

internal class MaxioCreateUsageRequest
{
    [JsonPropertyName("usage")]
    public MaxioCreateUsage Usage { get; set; } = new();
}

internal class MaxioCreateUsage
{
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("memo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Memo { get; set; }
}

internal class MaxioMigrationRequest
{
    [JsonPropertyName("migration")]
    public MaxioMigration Migration { get; set; } = new();
}

internal class MaxioMigration
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("preserve_period")]
    public bool PreservePeriod { get; set; }

    [JsonPropertyName("include_coupons")]
    public bool IncludeCoupons { get; set; } = true;
}

internal class MaxioUpdateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioUpdateSubscription Subscription { get; set; } = new();
}

internal class MaxioUpdateSubscription
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("product_change_delayed")]
    public bool ProductChangeDelayed { get; set; }
}

internal class MaxioCancellationRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCancellationOptions Subscription { get; set; } = new();
}

internal class MaxioCancellationOptions
{
    [JsonPropertyName("cancellation_message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CancellationMessage { get; set; }
}

/// <summary>
/// Maxio reports failures as <c>{"errors": [...]}</c> or <c>{"errors": {"field": "message"}}</c>;
/// both shapes are read so the provider's own wording can be surfaced to the actor.
/// </summary>
internal class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public System.Text.Json.JsonElement Errors { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
