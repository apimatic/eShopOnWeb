namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic classification of a <see cref="BillingComponent"/>. Only
/// <see cref="Metered"/> components accept usage reports.
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
