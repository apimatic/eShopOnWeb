namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The four lifecycle transitions offered by UC4's single management surface.
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause = 0,
    Resume = 1,
    Cancel = 2,
    Reactivate = 3
}
