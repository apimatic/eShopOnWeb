namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks where an order sits in the (additive) PayPal payment lifecycle.
/// An order is created <see cref="AwaitingPayment"/>, becomes <see cref="Paid"/> once a
/// PayPal capture completes, and <see cref="Refunded"/> after a full refund.
/// </summary>
public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Paid = 1,
    Refunded = 2
}
