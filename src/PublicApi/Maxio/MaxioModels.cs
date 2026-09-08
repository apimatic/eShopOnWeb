using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A Maxio "product" (a.k.a. plan). JSON field names mirror the Maxio Advanced Billing API.
/// </summary>
public sealed class MaxioProduct
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    public bool? Taxable { get; set; }
}

/// <summary>
/// A Maxio customer. JSON field names mirror the Maxio Advanced Billing API.
/// </summary>
public sealed class MaxioCustomer
{
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Reference { get; set; }
}

/// <summary>
/// A Maxio subscription. JSON field names mirror the Maxio Advanced Billing API.
/// </summary>
public sealed class MaxioSubscription
{
    public long Id { get; set; }

    public string? State { get; set; }

    public string? Currency { get; set; }

    public MaxioProduct? Product { get; set; }

    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>The next date this subscription is billed.</summary>
    public DateTimeOffset? NextBillingAt => NextAssessmentAt ?? CurrentPeriodEndsAt;
}

internal sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}
