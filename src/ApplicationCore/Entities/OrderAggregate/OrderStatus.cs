namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment / payment lifecycle state of an <see cref="Order"/>.
/// An order is created <see cref="AwaitingPayment"/>, moves to <see cref="PaymentAuthorized"/>
/// once funds are held, and then to a terminal-ish state depending on the operator action.
/// </summary>
public enum OrderStatus
{
    AwaitingPayment = 0,
    PaymentAuthorized = 1,
    Fulfilled = 2,
    Cancelled = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}
