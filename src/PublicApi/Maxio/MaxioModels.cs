using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Wire-level models for the Maxio Advanced Billing (Chargify) JSON API.
// All property names are mapped with the snake_case naming policy below;
// envelope objects ("customer", "subscription", ...) use explicit tags.

internal class MaxioSnakeCase
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

internal class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = null!;
}

public class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
}

internal class MaxioProductFamilyEnvelope
{
    [JsonPropertyName("product_family")]
    public MaxioProductFamily ProductFamily { get; set; } = null!;
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = null!;
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? Currency { get; set; }
    public int ProductPriceInCents { get; set; }
    public int BalanceInCents { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

internal class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = null!;
}

internal class MaxioSubscriptionListEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}