using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

// The wire shape of the Maxio Advanced Billing REST API. These types are internal on purpose: they
// are the provider's contract and must not leak past the billing client (§2.2). Maxio wraps most
// resources in a single-property envelope, which the *Envelope types model.

internal sealed class ProductFamilyEnvelope
{
    [JsonPropertyName("product_family")]
    public ProductFamilyResource? ProductFamily { get; set; }
}

internal sealed class ProductFamilyResource
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public ProductResource? Product { get; set; }
}

internal sealed class ProductResource
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Recurring price in cents. Maxio reports money in minor units on *_in_cents fields.</summary>
    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public ProductFamilyResource? ProductFamily { get; set; }
}

internal sealed class ComponentEnvelope
{
    [JsonPropertyName("component")]
    public ComponentResource? Component { get; set; }
}

internal sealed class ComponentResource
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    /// <summary>Component kind; "metered_component" is the only kind that accepts usage records.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("unit_name")]
    public string? UnitName { get; set; }

    /// <summary>
    /// Price per unit as a decimal string in major units (e.g. "0.01"), unlike the *_in_cents
    /// fields elsewhere in the API.
    /// </summary>
    [JsonPropertyName("unit_price")]
    public string? UnitPrice { get; set; }

    [JsonPropertyName("pricing_scheme")]
    public string? PricingScheme { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }
}

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerResource? Customer { get; set; }
}

internal sealed class CustomerResource
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionResource? Subscription { get; set; }
}

internal sealed class SubscriptionResource
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public int BalanceInCents { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public int ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool CancelAtEndOfPeriod { get; set; }

    [JsonPropertyName("delayed_cancel_at")]
    public DateTimeOffset? DelayedCancelAt { get; set; }

    /// <summary>Set when a plan change is scheduled for the next renewal.</summary>
    [JsonPropertyName("next_product_handle")]
    public string? NextProductHandle { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("product")]
    public ProductResource? Product { get; set; }

    [JsonPropertyName("customer")]
    public CustomerResource? Customer { get; set; }
}

internal sealed class UsageEnvelope
{
    [JsonPropertyName("usage")]
    public UsageResource? Usage { get; set; }
}

internal sealed class UsageResource
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("memo")]
    public string? Memo { get; set; }

    [JsonPropertyName("component_id")]
    public long ComponentId { get; set; }

    [JsonPropertyName("component_handle")]
    public string? ComponentHandle { get; set; }
}

internal sealed class SubscriptionComponentEnvelope
{
    [JsonPropertyName("component")]
    public SubscriptionComponentResource? Component { get; set; }
}

internal sealed class SubscriptionComponentResource
{
    [JsonPropertyName("component_id")]
    public long ComponentId { get; set; }

    [JsonPropertyName("component_handle")]
    public string? ComponentHandle { get; set; }

    /// <summary>Units accrued so far in the current billing period.</summary>
    [JsonPropertyName("unit_balance")]
    public int? UnitBalance { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }
}

/// <summary>
/// The bare confirmation some routes answer with instead of the affected resource — scheduling an
/// end-of-period cancellation is the one this integration hits.
/// </summary>
internal sealed class MaxioMessageResponse
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class MigrationPreviewEnvelope
{
    [JsonPropertyName("migration")]
    public MigrationPreviewResource? Migration { get; set; }
}

internal sealed class MigrationPreviewResource
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
