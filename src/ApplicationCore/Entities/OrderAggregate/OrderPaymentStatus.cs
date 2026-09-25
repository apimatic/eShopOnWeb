namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment / fulfilment state of an <see cref="Order"/>. An order is additive money-movement
/// state layered on top of the existing catalog/basket/order flow: it starts
/// <see cref="AwaitingPayment"/>, holds funds when <see cref="Authorized"/>, takes them at
/// fulfilment (<see cref="Paid"/>), and can be <see cref="Cancelled"/> before capture or
/// (partially) refunded after it.
/// </summary>
public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Paid = 2,
    Cancelled = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}
