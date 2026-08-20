using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Internal;

internal sealed class CustomerEnvelope
{
    public CustomerPayload? Customer { get; set; }
}

internal sealed class CustomerPayload
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class CreateCustomerRequestBody
{
    public CreateCustomerPayload Customer { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateCustomerPayload
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Organization { get; set; } = "eShopOnWeb";
}

internal sealed class ProductEnvelope
{
    public ProductPayload? Product { get; set; }
}

internal sealed class ProductPayload
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    public SubscriptionPayload? Subscription { get; set; }
}

internal sealed class SubscriptionPayload
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Reference { get; set; }
    public ProductPayload? Product { get; set; }
}

internal sealed class CreateSubscriptionRequestBody
{
    public CreateSubscriptionPayload Subscription { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateSubscriptionPayload
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// Remittance (Relationship Invoicing) enrolls without capturing a card. The seeded
    /// eShop plans are configured so a payment method is not required.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal sealed class ErrorListResponse
{
    public object? Errors { get; set; }
}
