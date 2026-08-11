namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of an order's payment. An order starts <see cref="AwaitingPayment"/>,
/// moves to <see cref="Authorized"/> when funds are held, <see cref="Captured"/> when
/// the money is actually taken at fulfilment, <see cref="Voided"/> when cancelled before
/// fulfilment, and <see cref="PartiallyRefunded"/> / <see cref="Refunded"/> after a return.
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 1,
    Authorized = 2,
    Captured = 3,
    Voided = 4,
    PartiallyRefunded = 5,
    Refunded = 6
}
