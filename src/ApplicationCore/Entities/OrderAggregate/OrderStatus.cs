namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment / payment lifecycle of an <see cref="Order"/>. An order starts awaiting payment,
/// moves to authorized once funds are held, to fulfilled once the money is captured, and then
/// possibly to refunded. It is cancelled if the hold is released before fulfilment.
/// </summary>
public enum OrderStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Fulfilled = 2,
    Cancelled = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}
