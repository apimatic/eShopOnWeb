namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle state of an <see cref="Order"/>. Orders start out <see cref="Placed"/> and can
/// move to <see cref="Dispatched"/> or <see cref="Cancelled"/> by an operator. This is an additive
/// concept layered onto the existing order model to support shipment notifications.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
