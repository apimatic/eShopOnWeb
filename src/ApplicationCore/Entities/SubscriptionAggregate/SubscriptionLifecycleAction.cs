namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The lifecycle transitions a customer or an administrator may request on a subscription.
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause = 0,
    Resume = 1,
    Cancel = 2,
    Reactivate = 3
}
