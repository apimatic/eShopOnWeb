using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

// Wire shapes for the Maxio Advanced Billing REST API. Only the fields eShopOnWeb consumes are
// modelled; Maxio adds fields over time and unknown members are ignored on deserialization.

internal sealed class MaxioSiteEnvelope
{
    [JsonPropertyName("site")] public MaxioSite? Site { get; set; }
}

internal sealed class MaxioSite
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("subdomain")] public string? Subdomain { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("relationship_invoicing_enabled")] public bool RelationshipInvoicingEnabled { get; set; }
    [JsonPropertyName("test")] public bool Test { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
    [JsonPropertyName("initial_charge_in_cents")] public long? InitialChargeInCents { get; set; }
    [JsonPropertyName("trial_interval")] public int? TrialInterval { get; set; }
    [JsonPropertyName("trial_interval_unit")] public string? TrialIntervalUnit { get; set; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("taxable")] public bool Taxable { get; set; }
    [JsonPropertyName("product_family")] public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("organization")] public string? Organization { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")] public MaxioCreateCustomerAttributes Customer { get; set; } = new();
}

internal sealed class MaxioCreateCustomerAttributes
{
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("organization")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Organization { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("balance_in_cents")] public long BalanceInCents { get; set; }
    [JsonPropertyName("product_price_in_cents")] public long ProductPriceInCents { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("current_period_started_at")] public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("activated_at")] public DateTimeOffset? ActivatedAt { get; set; }
    [JsonPropertyName("canceled_at")] public DateTimeOffset? CanceledAt { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("payment_collection_method")] public string? PaymentCollectionMethod { get; set; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")] public MaxioCreateSubscriptionAttributes Subscription { get; set; } = new();

    /// <summary>
    /// Duplicate-prevention token. Maxio rejects a second request carrying the same token within
    /// 60 minutes with 409 Conflict. Note that it is a sibling of "subscription", not a member.
    /// </summary>
    [JsonPropertyName("uniqueness_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioCreateSubscriptionAttributes
{
    [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
    [JsonPropertyName("customer_id")] public long CustomerId { get; set; }

    [JsonPropertyName("payment_collection_method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaymentCollectionMethod { get; set; }
}
