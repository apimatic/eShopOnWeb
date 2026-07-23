namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The lifecycle transitions a subscription management surface can request.
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause = 0,
    Resume = 1,
    Cancel = 2,
    Reactivate = 3
}
