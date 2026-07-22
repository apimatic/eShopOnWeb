namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// The lifecycle transitions a customer or admin can request on an existing subscription (UC4).
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause,
    Resume,
    Cancel,
    Reactivate
}
