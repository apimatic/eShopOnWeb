namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order sits in its fulfilment lifecycle. Added so the shop has a notion of an
/// order having been dispatched or cancelled, which the SMS notifications hang off.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
