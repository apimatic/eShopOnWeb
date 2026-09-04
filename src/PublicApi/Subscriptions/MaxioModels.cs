using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("handle")] public string Handle { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public int PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string IntervalUnit { get; set; } = string.Empty;
    [JsonPropertyName("trial_interval")] public int? TrialInterval { get; set; }
    [JsonPropertyName("trial_interval_unit")] public string? TrialIntervalUnit { get; set; }
    [JsonPropertyName("initial_charge_in_cents")] public int? InitialChargeInCents { get; set; }
    [JsonPropertyName("expiration_interval_unit")] public string? ExpirationIntervalUnit { get; set; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("taxable")] public bool Taxable { get; set; }
    [JsonPropertyName("product_price_point_id")] public int? ProductPricePointId { get; set; }
    [JsonPropertyName("product_price_point_handle")] public string? ProductPricePointHandle { get; set; }
    [JsonPropertyName("product_family")] public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("handle")] public string Handle { get; set; } = string.Empty;
}

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")] public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string? Reference { get; set; }
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")] public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")] public int ProductPriceInCents { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("current_period_started_at")] public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}
