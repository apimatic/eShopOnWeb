namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The operator-facing fulfilment lifecycle of an <see cref="Order"/>.
/// An order is created <see cref="AwaitingPayment"/>, becomes <see cref="PaymentAuthorized"/>
/// once a hold is placed on the shopper's funds, and is then either <see cref="Fulfilled"/>
/// (money captured) or <see cref="Cancelled"/> (hold released before fulfilment).
/// A fully-refunded, previously fulfilled order becomes <see cref="Refunded"/>.
/// </summary>
public enum OrderStatus
{
    AwaitingPayment = 0,
    PaymentAuthorized = 1,
    Fulfilled = 2,
    Cancelled = 3,
    Refunded = 4
}
