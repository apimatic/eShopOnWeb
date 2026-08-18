namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle stages an <see cref="Order"/> moves through. Notifications are sent to the
/// shopper as the order transitions between these states.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed by the shopper and is awaiting fulfilment.</summary>
    Placed = 0,

    /// <summary>An operator has marked the order as dispatched / on its way.</summary>
    Dispatched = 1,

    /// <summary>An operator has cancelled the order.</summary>
    Cancelled = 2
}
