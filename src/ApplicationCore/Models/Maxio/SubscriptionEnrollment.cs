using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

/// <summary>
/// The confirmed state of a Maxio subscription, as reported back to the shopper.
/// </summary>
public class SubscriptionEnrollment
{
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public string ProductHandle { get; set; } = default!;
    public string ProductName { get; set; } = default!;

    /// <summary>Maxio subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; set; } = default!;
    public int PriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}
