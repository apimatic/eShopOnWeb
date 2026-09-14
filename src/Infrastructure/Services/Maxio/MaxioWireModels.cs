using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

// Wire-format DTOs matching Maxio Advanced Billing's JSON shapes (snake_case). These are
// intentionally kept private to the Infrastructure implementation - callers only ever see
// the ApplicationCore.Interfaces MaxioPlan/MaxioCustomer/MaxioSubscription contract types.

internal class ProductFamilyRefDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

internal class ProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public ProductFamilyRefDto? ProductFamily { get; set; }
}

internal class ProductWrapperDto
{
    [JsonPropertyName("product")]
    public ProductDto? Product { get; set; }
}

internal class CustomerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

internal class CustomerWrapperDto
{
    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }
}

internal class CreateCustomerAttributesDto
{
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;
}

internal class CreateCustomerRequestDto
{
    [JsonPropertyName("customer")]
    public CreateCustomerAttributesDto Customer { get; set; } = new();
}

internal class SubscriptionProductDto
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long? PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int? Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }
}

internal class SubscriptionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product")]
    public SubscriptionProductDto? Product { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }
}

internal class SubscriptionWrapperDto
{
    [JsonPropertyName("subscription")]
    public SubscriptionDto? Subscription { get; set; }
}

internal class CreateSubscriptionAttributesDto
{
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// Sites with Relationship Invoicing enabled default new subscriptions to "automatic"
    /// payment collection, which requires a payment method up front even when the product
    /// itself does not (require_credit_card: false). "remittance" is the RI-architecture
    /// equivalent of "invoice" (bill later, no payment method required at signup) - see the
    /// Create Subscription payment_collection_method enum.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal class CreateSubscriptionRequestDto
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionAttributesDto Subscription { get; set; } = new();
}

internal class MaxioErrorsDto
{
    [JsonPropertyName("errors")]
    public object? Errors { get; set; }
}
