namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Result of enrolling a user in a subscription plan.
/// </summary>
public class SubscriptionEnrollment
{
    public SubscriptionSummary Subscription { get; set; } = new();
    public bool AlreadySubscribed { get; set; }
}
