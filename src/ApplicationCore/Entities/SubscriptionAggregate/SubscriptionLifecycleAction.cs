namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The lifecycle transitions a customer or administrator can request (UC4).
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause,
    Resume,

    /// <summary>Cancel straight away; the subscription stops billing immediately.</summary>
    Cancel,

    /// <summary>Schedule the cancellation for the end of the current billing period.</summary>
    CancelAtEndOfPeriod,
    Reactivate
}
