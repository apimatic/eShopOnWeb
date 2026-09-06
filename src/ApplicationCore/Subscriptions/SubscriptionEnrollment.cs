namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
public class SubscriptionEnrollment
{
    public required CustomerSubscription Subscription { get; init; }

    /// <summary>
    /// True when the subscriber was already enrolled in the plan and nothing new was created.
    /// Lets the API answer a repeated request with 200 instead of 201.
    /// </summary>
    public bool AlreadyExisted { get; init; }
}
