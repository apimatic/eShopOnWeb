using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A subscription record as returned by Maxio Advanced Billing.
/// The billing system is the source of truth for state, price and billing dates.
/// </summary>
public class MaxioSubscription
{
    public long Id { get; set; }

    /// <summary>
    /// Billing system state, one of the documented subscription states
    /// (see <see cref="MaxioSubscriptionStates"/>).
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>External id provided by this app for the subscription itself.</summary>
    public string? Reference { get; set; }

    public long? ProductId { get; set; }

    public string? ProductHandle { get; set; }

    public string? ProductName { get; set; }

    /// <summary>Current recurring price for the subscription, in integer cents.</summary>
    public int? PriceInCents { get; set; }

    public string? Currency { get; set; }

    /// <summary>When the next regularly scheduled charge will occur.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// True for states that no longer occupy the shopper's slot on a plan,
    /// i.e. a fresh subscription may be created after reaching one of them.
    /// </summary>
    public bool IsTerminated => State switch
    {
        MaxioSubscriptionStates.Canceled => true,
        MaxioSubscriptionStates.Expired => true,
        MaxioSubscriptionStates.FailedToCreate => true,
        MaxioSubscriptionStates.TrialEnded => true,
        _ => false
    };
}

/// <summary>
/// Documented subscription states of the Maxio Advanced Billing API.
/// </summary>
public static class MaxioSubscriptionStates
{
    public const string Pending = "pending";
    public const string FailedToCreate = "failed_to_create";
    public const string Trialing = "trialing";
    public const string Assessing = "assessing";
    public const string Active = "active";
    public const string SoftFailure = "soft_failure";
    public const string PastDue = "past_due";
    public const string Suspended = "suspended";
    public const string Canceled = "canceled";
    public const string Expired = "expired";
    public const string Paused = "paused";
    public const string Unpaid = "unpaid";
    public const string TrialEnded = "trial_ended";
    public const string OnHold = "on_hold";
    public const string AwaitingSignup = "awaiting_signup";
}
