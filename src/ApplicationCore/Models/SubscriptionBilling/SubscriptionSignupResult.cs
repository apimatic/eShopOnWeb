namespace Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;

public enum SubscriptionSignupOutcome
{
    /// <summary>A new subscription was created in the billing system.</summary>
    Created,

    /// <summary>
    /// The user already has a live subscription on the requested plan;
    /// the existing subscription is returned instead of creating a duplicate.
    /// </summary>
    AlreadySubscribed
}

/// <summary>
/// Result of a signup request.
/// </summary>
public class SubscriptionSignupResult
{
    public SubscriptionSignupOutcome Outcome { get; set; }

    public UserSubscription Subscription { get; set; } = new UserSubscription();
}