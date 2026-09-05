using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Mirrors the relevant fields of the Maxio "Subscription" schema (maxio-spec/components/schemas/Subscription.yaml).
/// </summary>
public class MaxioSubscription
{
    public long Id { get; set; }

    /// <summary>
    /// One of the values enumerated in maxio-spec/components/schemas/Subscription-State.yaml
    /// (e.g. active, trialing, past_due, canceled, ...).
    /// </summary>
    public string State { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; set; } = new();
}
