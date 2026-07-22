namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The kind of a billable component, expressed in provider-agnostic terms.
/// Pay-as-you-go usage (UC2) may only be recorded against <see cref="Metered"/> components.
/// </summary>
public enum BillingComponentKind
{
    Unknown = 0,
    Metered,
    QuantityBased,
    OnOff,
    PrepaidUsage,
    EventBased
}
