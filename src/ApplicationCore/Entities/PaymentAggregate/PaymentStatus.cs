namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an <see cref="OrderPayment"/>. An order starts <see cref="AwaitingPayment"/>,
/// moves to <see cref="Authorized"/> when funds are held, <see cref="Fulfilled"/> when the money is
/// captured, <see cref="Canceled"/> when a hold is released before fulfilment, and one of the refunded
/// states once captured money is returned.
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Fulfilled = 2,
    Canceled = 3,
    PartiallyRefunded = 4,
    Refunded = 5,
    Failed = 6
}
