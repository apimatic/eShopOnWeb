namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment lifecycle state of an <see cref="Order"/>.
/// An order is created <see cref="AwaitingPayment"/>, becomes <see cref="Paid"/> once a
/// PayPal capture succeeds, and becomes <see cref="Refunded"/> once that capture is fully refunded.
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 0,
    Paid = 1,
    Refunded = 2
}
