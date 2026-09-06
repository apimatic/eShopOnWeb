namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
public class SubscribeResult
{
    public required CustomerSubscription Subscription { get; init; }

    /// <summary>
    /// True when the subscriber was already enrolled and the existing subscription was
    /// returned instead of a new one being created (double click, retry, replayed request).
    /// </summary>
    public bool AlreadySubscribed { get; init; }
}
