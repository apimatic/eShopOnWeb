using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A Maxio subscription, projected down to the fields eShopOnWeb needs to
/// confirm enrollment (plan, price, state and next billing date) back to the shopper.
/// </summary>
public class MaxioSubscription
{
    public long Id { get; init; }
    public required string State { get; init; }
    public required string PlanHandle { get; init; }
    public required string PlanName { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public required string IntervalUnit { get; init; }
    public DateTimeOffset? CurrentPeriodStartsAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    private static readonly string[] LiveStates =
    {
        "pending", "trialing", "assessing", "active", "soft_failure",
        "past_due", "suspended", "paused", "unpaid", "awaiting_signup"
    };

    /// <summary>
    /// True for any state that is not a terminal/end-of-life state. Used to decide
    /// whether an existing subscription to a plan should block creating a new one.
    /// </summary>
    public bool IsLive => Array.IndexOf(LiveStates, State) >= 0;
}
