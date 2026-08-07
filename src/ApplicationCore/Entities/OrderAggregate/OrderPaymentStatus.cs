namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment lifecycle of an <see cref="Order"/>. Orders are created
/// <see cref="AwaitingPayment"/>, become <see cref="Paid"/> once PayPal captures
/// the payment, and <see cref="Refunded"/> after a full refund.
/// </summary>
public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Paid = 1,
    Refunded = 2
}
