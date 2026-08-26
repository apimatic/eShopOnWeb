using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Subscription as returned by the Maxio API (spec schema "Subscription"; serialized snake_case).
/// Only the fields this integration consumes are modeled.
/// </summary>
public class MaxioSubscription
{
    public long Id { get; set; }

    /// <summary>
    /// Spec schema "Subscription-State" (active, trialing, past_due, canceled, expired, ...).
    /// Modeled as a string so newly introduced states never break deserialization.
    /// </summary>
    public string? State { get; set; }

    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Reference { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// Spec schema "Subscription-Response": wraps a subscription in a top-level "subscription" property.
/// </summary>
public class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}
