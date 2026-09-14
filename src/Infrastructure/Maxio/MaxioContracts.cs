using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire contracts for the Maxio Advanced Billing API. Field names follow the Maxio
// OpenAPI specification (maxio-spec/openapi.yaml) which is the authoritative contract;
// property names are mapped to the specification's snake_case JSON.

public class MaxioProductFamily
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

public class MaxioProduct
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public long? PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("request_credit_card")] public bool RequestCreditCard { get; set; }
    [JsonPropertyName("taxable")] public bool Taxable { get; set; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("default_product_price_point_id")] public int? DefaultProductPricePointId { get; set; }
    [JsonPropertyName("product_price_point_name")] public string? ProductPricePointName { get; set; }
    [JsonPropertyName("product_price_point_handle")] public string? ProductPricePointHandle { get; set; }
    [JsonPropertyName("product_family")] public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioCustomer
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("organization")] public string? Organization { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("zip")] public string? Zip { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
}

public class MaxioSubscription
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("balance_in_cents")] public long? BalanceInCents { get; set; }
    [JsonPropertyName("total_revenue_in_cents")] public long? TotalRevenueInCents { get; set; }
    [JsonPropertyName("product_price_in_cents")] public long? ProductPriceInCents { get; set; }
    [JsonPropertyName("product_version_number")] public int? ProductVersionNumber { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("current_period_started_at")] public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    [JsonPropertyName("trial_started_at")] public DateTimeOffset? TrialStartedAt { get; set; }
    [JsonPropertyName("trial_ended_at")] public DateTimeOffset? TrialEndedAt { get; set; }
    [JsonPropertyName("activated_at")] public DateTimeOffset? ActivatedAt { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("canceled_at")] public DateTimeOffset? CanceledAt { get; set; }
    [JsonPropertyName("cancel_at_end_of_period")] public bool? CancelAtEndOfPeriod { get; set; }
    [JsonPropertyName("payment_collection_method")] public string? PaymentCollectionMethod { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
    [JsonPropertyName("product_price_point_id")] public int? ProductPricePointId { get; set; }
}

public class MaxioProductEnvelope
{
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}

public class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
}

public class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; set; }
}

public class MaxioCreateCustomerEnvelope
{
    [JsonPropertyName("customer")] public MaxioCreateCustomer Customer { get; set; } = new();
}

public class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("organization")] public string? Organization { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
}

public class MaxioCreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")] public MaxioCreateSubscription Subscription { get; set; } = new();
}

public class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")] public string? ProductHandle { get; set; }
    [JsonPropertyName("product_id")] public int? ProductId { get; set; }
    [JsonPropertyName("customer_id")] public int? CustomerId { get; set; }
    [JsonPropertyName("customer_reference")] public string? CustomerReference { get; set; }
    [JsonPropertyName("payment_collection_method")] public string? PaymentCollectionMethod { get; set; }
}

public class MaxioErrorListResponse
{
    [JsonPropertyName("errors")] public IReadOnlyList<string>? Errors { get; set; }
}
