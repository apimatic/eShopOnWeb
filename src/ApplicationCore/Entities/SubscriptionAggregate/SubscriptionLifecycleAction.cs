namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The four UC4 lifecycle transitions exposed through one management surface.
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause = 0,
    Resume = 1,
    Cancel = 2,
    Reactivate = 3
}
