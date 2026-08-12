namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle state of an <see cref="Order"/>. Added additively to the existing order model
/// so operators can dispatch or cancel an order and shoppers can be notified as it moves.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
