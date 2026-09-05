using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = default!;
    public string PlanHandle { get; set; } = default!;
    public string PlanName { get; set; } = default!;
    public long PriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}

public class MaxioSubscribeResult
{
    public MaxioSubscription Subscription { get; set; } = default!;

    /// <summary>
    /// False when an existing, non-terminal subscription for this customer/plan was returned
    /// instead of creating a new one (e.g. a double-click resubmission of the same request).
    /// </summary>
    public bool WasNewlyCreated { get; set; }
}
