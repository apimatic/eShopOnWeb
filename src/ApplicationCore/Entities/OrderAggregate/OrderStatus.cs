namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order is in its post-checkout lifecycle. Added so an operator can mark an order dispatched
/// or cancelled and so those transitions can be guarded (a cancelled order can never be dispatched).
/// </summary>
public enum OrderStatus
{
    /// <summary>Placed, not yet dispatched or cancelled.</summary>
    Pending = 0,

    /// <summary>Marked dispatched by an operator.</summary>
    Dispatched = 1,

    /// <summary>Cancelled by an operator.</summary>
    Cancelled = 2
}
