namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic billing-component kind, used to validate that the configured
/// pay-as-you-go component is actually metered before usage is recorded against it.
/// </summary>
public enum ComponentKind
{
    Metered,
    QuantityBased,
    OnOff,
    PrepaidUsage,
    EventBased,
    Unknown
}
