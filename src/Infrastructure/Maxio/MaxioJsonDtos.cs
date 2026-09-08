using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Raw wire DTOs. Property names are pinned with [JsonPropertyName] to the exact field names
// defined in maxio-spec (Advanced Billing uses snake_case). These mirror the request/response
// schemas Create-Customer-Request / Create-Subscription-Request / Customer-Response /
// Subscription-Response / Product-Response and are intentionally decoupled from the domain model.

internal sealed class CustomerRequest
{
    [JsonPropertyName("customer")] public CustomerRequestBody Customer { get; set; } = new();

    internal sealed class CustomerRequestBody
    {
        [JsonPropertyName("first_name")] public string? FirstName { get; set; }
        [JsonPropertyName("last_name")] public string? LastName { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("reference")] public string? Reference { get; set; }
    }
}

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")] public CustomerDto? Customer { get; set; }
}

internal sealed class CustomerDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
}

internal sealed class SubscriptionRequest
{
    [JsonPropertyName("subscription")] public SubscriptionRequestBody Subscription { get; set; } = new();

    internal sealed class SubscriptionRequestBody
    {
        [JsonPropertyName("product_handle")] public string? ProductHandle { get; set; }
        [JsonPropertyName("customer_id")] public long CustomerId { get; set; }
        [JsonPropertyName("reference")] public string? Reference { get; set; }
        [JsonPropertyName("payment_collection_method")] public string? PaymentCollectionMethod { get; set; }
    }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")] public SubscriptionDto? Subscription { get; set; }
}

internal sealed class SubscriptionDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("current_period_started_at")] public System.DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    [JsonPropertyName("current_period_ends_at")] public System.DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public System.DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("created_at")] public System.DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("activated_at")] public System.DateTimeOffset? ActivatedAt { get; set; }
    [JsonPropertyName("canceled_at")] public System.DateTimeOffset? CanceledAt { get; set; }
    [JsonPropertyName("customer")] public CustomerDto? Customer { get; set; }
    [JsonPropertyName("product")] public ProductDto? Product { get; set; }
}

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")] public ProductDto? Product { get; set; }
}

internal sealed class ProductDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public int PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("request_credit_card")] public bool RequestCreditCard { get; set; }
    [JsonPropertyName("taxable")] public bool Taxable { get; set; }
    [JsonPropertyName("archived_at")] public System.DateTimeOffset? ArchivedAt { get; set; }
    [JsonPropertyName("product_family")] public ProductFamilyDto? ProductFamily { get; set; }
}

internal sealed class ProductFamilyDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
}
