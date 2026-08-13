namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. Orders start <see cref="Placed"/> and can move to
/// <see cref="Dispatched"/> or <see cref="Cancelled"/>; a dispatched order may still be cancelled.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
