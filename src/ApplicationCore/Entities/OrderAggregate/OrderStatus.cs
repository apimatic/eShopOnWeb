namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. eShopOnWeb historically had no notion of an order
/// progressing after checkout; this enables the dispatch/cancel transitions that drive shopper
/// notifications, without replacing the existing order/order-item model.
/// </summary>
public enum OrderStatus
{
    Submitted = 0,
    Dispatched = 1,
    Cancelled = 2
}
