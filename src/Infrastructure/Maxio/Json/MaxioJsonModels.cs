using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Json;

// Wire-format DTOs mapping the subset of the Maxio Advanced Billing REST payloads this
// integration uses. Kept internal to the Infrastructure layer; the ApplicationCore models are
// what the rest of the app consumes.

internal sealed class MaxioProductFamily
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
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
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("product_family")] public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("payment_collection_method")] public string? PaymentCollectionMethod { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("product_price_in_cents")] public long? ProductPriceInCents { get; set; }
    [JsonPropertyName("current_period_started_at")] public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; set; }
}

// ----- Request payloads -----

internal sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")] public MaxioCreateCustomer Customer { get; set; } = new();
}

internal sealed class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")] public MaxioCreateSubscription Subscription { get; set; } = new();
}

internal sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
    [JsonPropertyName("customer_id")] public long CustomerId { get; set; }
    [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; set; } = "remittance";

    // Guards against duplicate creation on retries / concurrent double-clicks (see Duplicate Prevention).
    [JsonPropertyName("uniqueness_token")] public string UniquenessToken { get; set; } = string.Empty;
}

// ----- Error payload -----

internal sealed class MaxioErrorResponse
{
    // Maxio returns either {"errors": ["..."]} or {"errors": {"...": "..."}}; we only surface the array form.
    [JsonPropertyName("errors")] public List<string>? Errors { get; set; }
}
