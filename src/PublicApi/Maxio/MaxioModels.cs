using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Wire models for the Maxio Advanced Billing JSON API.
// Maxio serializes dates with a timezone offset, so they are read as DateTimeOffset.

public class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

public class MaxioSubscriptionCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public int BalanceInCents { get; set; }
    public int ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public MaxioSubscriptionCustomer Customer { get; set; } = new();
    public MaxioProduct Product { get; set; } = new();
}

// Envelope objects — Maxio wraps single-resource payloads: { "customer": {...} }, { "subscription": {...} }.

public class MaxioCustomerEnvelope
{
    public MaxioCustomer Customer { get; set; } = new();
}

public class MaxioProductEnvelope
{
    public MaxioProduct Product { get; set; } = new();
}

public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public class MaxioErrorEnvelope
{
    public string[] Errors { get; set; } = System.Array.Empty<string>();
}

// Request bodies (snake_case on the wire).

public class MaxioCreateCustomerBody
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new();
}

public class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioCreateSubscriptionBody
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

public class MaxioCreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }

    /// <summary>
    /// "remittance" lets a subscription be created without a payment profile on file
    /// (the merchant collects payment outside Maxio). The seeded plans do not require
    /// a card, and this demo flow deliberately captures no payment data.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

public static class MaxioJson
{
    public static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}