namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The subscription states reported by the billing system, and the rules this application
/// uses to decide whether a subscription still entitles the shopper to the plan.
/// </summary>
public static class SubscriptionStates
{
    public const string Pending = "pending";
    public const string Trialing = "trialing";
    public const string Assessing = "assessing";
    public const string Active = "active";
    public const string SoftFailure = "soft_failure";
    public const string PastDue = "past_due";
    public const string Paused = "paused";
    public const string AwaitingSignup = "awaiting_signup";
    public const string Canceled = "canceled";
    public const string Expired = "expired";
    public const string FailedToCreate = "failed_to_create";
    public const string OnHold = "on_hold";
    public const string Suspended = "suspended";
    public const string TrialEnded = "trial_ended";
    public const string Unpaid = "unpaid";

    /// <summary>
    /// States in which an enrollment is considered to still exist, i.e. subscribing the same
    /// shopper to the same plan again would be a duplicate rather than a new enrollment.
    /// Everything else is end-of-life, and the shopper is free to enroll again.
    /// </summary>
    public static bool IsLive(string? state) => state switch
    {
        Pending or Trialing or Assessing or Active or SoftFailure or PastDue or Paused or AwaitingSignup => true,
        _ => false
    };

    /// <summary>
    /// True when the subscription is live and not in a payment-problem state.
    /// </summary>
    public static bool IsHealthy(string? state) => state switch
    {
        Pending or Trialing or Assessing or Active or AwaitingSignup => true,
        _ => false
    };
}
