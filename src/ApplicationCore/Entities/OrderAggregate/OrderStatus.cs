namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment lifecycle state of an <see cref="Order"/>. Before this feature the app had no
/// notion of an order being dispatched or cancelled; these states drive the SMS notifications.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
