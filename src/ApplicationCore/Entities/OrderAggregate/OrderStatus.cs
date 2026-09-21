namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment/payment lifecycle state of an <see cref="Order"/>.
/// An order is created <see cref="AwaitingPayment"/>; a successful authorization moves it to
/// <see cref="Authorized"/> (funds held, not taken); fulfilment captures and moves it to
/// <see cref="Fulfilled"/>; a cancel before fulfilment voids the hold (<see cref="Cancelled"/>);
/// refunds after fulfilment move it to <see cref="PartiallyRefunded"/> or <see cref="Refunded"/>.
/// </summary>
public enum OrderStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Fulfilled = 2,
    PartiallyRefunded = 3,
    Refunded = 4,
    Cancelled = 5
}
