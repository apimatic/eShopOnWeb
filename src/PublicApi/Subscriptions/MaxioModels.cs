using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

// These wire models intentionally mirror only fields in maxio-spec/openapi.yaml
// used by this integration's Product, Customer, and Subscription schemas.
internal sealed class MaxioProductEnvelope { public MaxioProduct? Product { get; set; } }
internal sealed class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class MaxioCustomerEnvelope { public MaxioCustomer? Customer { get; set; } }
internal sealed class MaxioCustomer
{
    public int Id { get; set; }
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope { public MaxioSubscription? Subscription { get; set; } }
internal sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string? Reference { get; set; }
    public MaxioProduct? Product { get; set; }
}
