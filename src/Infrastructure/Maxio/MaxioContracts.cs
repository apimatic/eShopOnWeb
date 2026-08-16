using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire DTOs mirroring the Maxio Advanced Billing (Chargify) JSON contract. Property names are
// PascalCase and mapped to Maxio's snake_case via JsonNamingPolicy.SnakeCaseLower configured on
// the serializer. Only the fields this integration consumes are modelled; unknown fields are ignored.

internal sealed class ProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class ProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class CustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

internal sealed class MaxioProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

internal sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public long? ProductPriceInCents { get; set; }
    public string? Currency { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

// Request bodies. These are serialized with the same snake_case policy.

internal sealed class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CustomerAttributes Customer { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class CustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public SubscriptionAttributes Subscription { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class SubscriptionAttributes
{
    public long CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>"remittance" so no payment profile / credit card is required at signup.</summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
