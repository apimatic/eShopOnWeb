namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle state of an <see cref="Order"/>. Added so the shop can notify the
/// shopper as the order moves from placed to dispatched or cancelled.
/// </summary>
public enum OrderStatus
{
    Submitted = 0,
    Dispatched = 1,
    Cancelled = 2
}
