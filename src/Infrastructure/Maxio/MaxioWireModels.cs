using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire-format DTOs mirroring the Maxio Advanced Billing JSON contract, confirmed against
// the official developer portal (developers.maxio.com) and the live sandbox.

internal sealed class MaxioProductFamilyWire
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
}

internal sealed class MaxioProductWire
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public int PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
    [JsonPropertyName("taxable")] public bool Taxable { get; set; }
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("archived_at")] public string? ArchivedAt { get; set; }
    [JsonPropertyName("product_family")] public MaxioProductFamilyWire? ProductFamily { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")] public MaxioProductWire? Product { get; set; }
}

internal sealed class MaxioCustomerWire
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")] public MaxioCustomerWire? Customer { get; set; }
}

internal sealed class MaxioSubscriptionWire
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("product_price_in_cents")] public int ProductPriceInCents { get; set; }
    [JsonPropertyName("activated_at")] public string? ActivatedAt { get; set; }
    [JsonPropertyName("current_period_ends_at")] public string? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public string? NextAssessmentAt { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomerWire? Customer { get; set; }
    [JsonPropertyName("product")] public MaxioProductWire? Product { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")] public MaxioSubscriptionWire? Subscription { get; set; }
}

/// <summary>
/// Body for POST /subscriptions.json.
/// </summary>
internal sealed class CreateSubscriptionBody
{
    [JsonPropertyName("subscription")] public SubscriptionAttributes Subscription { get; set; } = new();
    [JsonPropertyName("uniqueness_token")] public string UniquenessToken { get; set; } = string.Empty;

    internal sealed class SubscriptionAttributes
    {
        [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
        [JsonPropertyName("customer_id")] public long CustomerId { get; set; }
        // 'remittance' avoids attempting an automatic charge at signup so the subscription
        // can be created without a stored payment method (payment method not required).
        [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; set; } = "remittance";
    }
}

/// <summary>
/// Body for POST /customers.json.
/// </summary>
internal sealed class CreateCustomerBody
{
    [JsonPropertyName("customer")] public CustomerAttributes Customer { get; set; } = new();
    [JsonPropertyName("uniqueness_token")] public string UniquenessToken { get; set; } = string.Empty;

    internal sealed class CustomerAttributes
    {
        [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("first_name")] public string? FirstName { get; set; }
        [JsonPropertyName("last_name")] public string? LastName { get; set; }
    }
}

/// <summary>
/// Error envelope returned by Advanced Billing. The "errors" member is polymorphic:
/// it is sometimes an array of strings and sometimes a single string.
/// </summary>
internal sealed class MaxioErrorEnvelope
{
    [JsonExtensionData] public IDictionary<string, object?>? AdditionalData { get; set; }
}
