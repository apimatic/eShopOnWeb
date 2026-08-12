namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an <see cref="Order"/> as it moves through fulfilment. Additive to the original
/// eShopOnWeb model, which had no notion of an order being dispatched or cancelled.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
