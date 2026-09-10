namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment/payment lifecycle state of an <see cref="Order"/>.
/// An order is created <see cref="AwaitingPayment"/>, becomes <see cref="Authorized"/> once the
/// buyer's funds are held at PayPal, <see cref="Fulfilled"/> once those funds are captured, or
/// <see cref="Cancelled"/> if the hold is released before fulfilment. Refund state is tracked on
/// the <see cref="Payment"/> itself so that a fulfilled order can still be (partly) refunded.
/// </summary>
public enum OrderStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Fulfilled = 2,
    Cancelled = 3
}
