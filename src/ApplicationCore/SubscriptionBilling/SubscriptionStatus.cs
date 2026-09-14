namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Describes which Maxio subscription states mean a subscription is still current
/// versus permanently ended. Maxio may add states over time, so the ended set is
/// intentionally conservative.
/// </summary>
public static class SubscriptionStatus
{
    public static bool IsEndedState(string state) => state switch
    {
        "canceled" or "expired" or "trial_ended" or "failed_to_create" => true,
        _ => false
    };
}
