namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
public class SubscribeResult
{
    public required CustomerSubscription Subscription { get; init; }

    /// <summary>
    /// True when the user already held this subscription and nothing new was created -
    /// the double-click / retry case.
    /// </summary>
    public bool AlreadySubscribed { get; init; }
}
